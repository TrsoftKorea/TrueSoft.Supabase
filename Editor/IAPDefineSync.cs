using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;

namespace TrueBase.Editor
{
    /// <summary>
    /// IAP 관련 심볼을 전역 Scripting Define으로 자동 미러링합니다.
    /// <para>게임 코드와 샘플의 <c>#if</c> 가드가 프로젝트별 추가 설정 없이 동작하게 하려는 것입니다.
    /// IAP asmdef의 versionDefine은 <b>그 어셈블리 안에서만</b> 유효해서, asmdef가 없는 코드
    /// (<c>Assembly-CSharp</c>로 들어가는 게임 코드·<c>Samples~</c>)에는 닿지 않습니다.
    /// 그래서 타입 존재 여부로 같은 사실을 다시 판정해 전역에 뿌립니다.</para>
    /// </summary>
    [InitializeOnLoad]
    internal static class IAPDefineSync
    {
        // (전역에 뿌릴 심볼, 그 조건이 참일 때만 존재하는 타입).
        // 어셈블리 한정 이름으로 찾으므로 버전 문자열을 파싱할 필요가 없다.
        private static readonly (string Symbol, string ProbeType)[] Mirrored =
        {
            // IAP 어셈블리가 컴파일됐는가 = com.unity.purchasing 5.0 이상이 있는가.
            ("TRUESOFT_IAP_AVAILABLE", "TrueBase.Unity.SupabaseIAP, TrueBase.Unity.IAP"),

            // StoreKitSelector는 5.1에서 들어왔다. iOS SK1 강제(PlayNANOO 영수증 검증)에 필요하다.
            ("UNITY_IAP_V5_1", "Purchasing.Utilities.StoreKitSelector, Unity.Purchasing.Utilities"),
        };

        static IAPDefineSync()
        {
            // 도메인 리로드 도중 PlayerSettings를 건드리지 않도록 다음 에디터 틱에 실행.
            EditorApplication.delayCall += Sync;
        }

        private static void Sync()
        {
            foreach (BuildTargetGroup group in Enum.GetValues(typeof(BuildTargetGroup)))
            {
                if (group == BuildTargetGroup.Unknown)
                    continue;

                // Obsolete 빌드 타겟 그룹은 건너뜀.
                var field = typeof(BuildTargetGroup).GetField(group.ToString());
                if (field != null && Attribute.IsDefined(field, typeof(ObsoleteAttribute)))
                    continue;

                NamedBuildTarget target;
                try { target = NamedBuildTarget.FromBuildTargetGroup(group); }
                catch { continue; }

                string current;
                try { current = PlayerSettings.GetScriptingDefineSymbols(target); }
                catch { continue; }

                var defines = new List<string>(
                    current.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries));

                var changed = false;
                foreach (var (symbol, probeType) in Mirrored)
                {
                    var shouldDefine = Type.GetType(probeType) != null;
                    var has          = defines.Contains(symbol);

                    if (shouldDefine == has) continue;

                    if (shouldDefine) defines.Add(symbol);
                    else              defines.RemoveAll(s => s == symbol);
                    changed = true;
                }

                if (!changed)
                    continue; // 불필요한 재컴파일/루프 방지

                try { PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", defines)); }
                catch { /* 일부 그룹은 미설치 모듈로 실패할 수 있음 — 무시 */ }
            }
        }
    }
}
