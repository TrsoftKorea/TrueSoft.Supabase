namespace TrueBase.Core.Auth
{
    /// <summary>
    /// 세션을 설정할 때의 출처(새 로그인 vs 복원·갱신). 중복 로그인 토큰 정책에 사용합니다.
    /// </summary>
    public enum SupabaseSessionChangeKind
    {
        /// <summary>새 로그인(익명·구글 등). 서버에 새 <c>session_token</c>을 등록합니다.</summary>
        NewSignIn,

        /// <summary>저장된 refresh로 복원하거나 토큰 갱신. 로컬·서버 토큰을 맞추기만 하고 불필요하게 매번 새 토큰을 쓰지 않습니다.</summary>
        RestoredOrRefreshed
    }
}
