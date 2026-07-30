namespace TrueBase.Unity
{
    /// <summary>
    /// SDK가 초기화되지 않았을 때 어디서든 같은 점검 안내를 출력하기 위한 문구입니다.
    /// </summary>
    internal static class SupabaseUnitySetupHelp
    {
        /// <summary>콘솔에 그대로 붙여 넣을 수 있는 최소 체크리스트(한국어).</summary>
        public const string InitializationChecklistKo =
            "[Supabase 초기화]\n" +
            "1) TrueSoft > Supabase > 설정 에셋 만들기\n" +
            "2) URL·Publishable 키 입력 후 Assets/Resources/SupabaseSettings.asset 저장\n" +
            "3) 씬에 SupabaseRuntime (메뉴로 추가 가능)";
    }
}
