using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;

namespace TrueBase.Editor
{
    /// <summary>
    /// IAP 어셈블리(<c>TrueBase.Unity.IAP</c>) 컴파일 여부를 전역 Scripting Define
    /// <c>TRUESOFT_IAP_AVAILABLE</c>로 자동 미러링합니다.
    /// <para>게임 코드의 <c>#if TRUESOFT_IAP_AVAILABLE</c> 가드가 프로젝트별 추가 설정 없이 동작하도록 합니다.
    /// 버전 게이트의 단일 소스는 IAP asmdef의 versionDefine(v4 4.x / v5 5.2.1+)이며,
    /// 이 스크립트는 "IAP 어셈블리가 존재하는가"라는 결과만 전역 define으로 전파합니다.</para>
    /// </summary>
    [InitializeOnLoad]
    internal static class IAPDefineSync
    {
        private const string Symbol = "TRUESOFT_IAP_AVAILABLE";

        // IAP 어셈블리가 컴파일됐으면 이 타입이 존재합니다(없으면 null).
        private const string ProbeType = "TrueBase.Unity.SupabaseIAP, TrueBase.Unity.IAP";

        static IAPDefineSync()
        {
            // 도메인 리로드 도중 PlayerSettings를 건드리지 않도록 다음 에디터 틱에 실행.
            EditorApplication.delayCall += Sync;
        }

        private static void Sync()
        {
            var shouldDefine = Type.GetType(ProbeType) != null;

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
                var has = defines.Contains(Symbol);

                if (shouldDefine && !has)
                    defines.Add(Symbol);
                else if (!shouldDefine && has)
                    defines.RemoveAll(s => s == Symbol);
                else
                    continue; // 변경 없음 → 불필요한 재컴파일/루프 방지

                try { PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", defines)); }
                catch { /* 일부 그룹은 미설치 모듈로 실패할 수 있음 — 무시 */ }
            }
        }
    }
}
