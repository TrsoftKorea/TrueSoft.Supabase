using UnityEngine;

namespace TrueBase.Unity
{
    /// <summary>
    /// 씬/샘플에서 SDK가 초기화되지 않을 때 동일한 점검 안내를 출력하기 위한 헬퍼입니다.
    /// </summary>
    internal static class SupabaseUnitySetupHelp
    {
        /// <summary>콘솔에 그대로 붙여 넣을 수 있는 최소 체크리스트(한국어).</summary>
        public const string InitializationChecklistKo =
            "[Supabase 초기화]\n" +
            "1) TrueSoft > Supabase > 설정 에셋 만들기\n" +
            "2) URL·Publishable 키 입력 후 Assets/Resources/SupabaseSettings.asset 저장\n" +
            "3) 씬에 SupabaseRuntime (메뉴로 추가 가능)";

        /// <summary>
        /// <paramref name="context"/>는 로그 태그로 사용됩니다 (예: BasicSetup, FullSDKUsage).
        /// </summary>
        public static void LogInitializationTimeout(string context)
        {
            Debug.LogError(
                $"[{context}] {timeoutMessageKo}\n{InitializationChecklistKo}");
        }

        private const string timeoutMessageKo =
            "SDK 초기화 시간 초과. 아래를 확인하세요.";
    }
}
