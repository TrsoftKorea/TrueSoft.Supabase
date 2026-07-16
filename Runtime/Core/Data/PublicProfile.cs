namespace TrueBase.Core.Data
{
    /// <summary>
    /// 공개 프로필 조회 결과. <c>profiles</c>의 <c>id</c>(행 PK), <c>user_id</c>(안정 플레이어 id), <c>withdrawn_at</c>를 반영합니다.
    /// 표시 이름은 Auth user metadata(<c>displayName</c>)를 사용합니다.
    /// </summary>
    public sealed class PublicProfile
    {
        /// <summary>로그인 전 또는 프로필 조회 실패 시 반환되는 빈 인스턴스.</summary>
        public static readonly PublicProfile Empty = new PublicProfile(string.Empty, string.Empty, string.Empty, null, string.Empty);

        public PublicProfile(string profileRowId, string playerUserId, string displayName, string withdrawnAtIso, string serverCode = "")
        {
            ProfileRowId = profileRowId ?? string.Empty;
            PlayerUserId = playerUserId ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            WithdrawnAtIso = withdrawnAtIso;
            ServerCode = serverCode ?? string.Empty;
        }

        /// <summary>테이블 PK (<c>profiles.id</c>).</summary>
        public string ProfileRowId { get; }

        /// <summary>영속 플레이어 id (<c>profiles.user_id</c>, OAuth <c>sub</c> 등). 재인증에도 유지되며, 세션 신원인 <c>Supabase.UserId</c>(account_id)와는 다른 값입니다.</summary>
        public string PlayerUserId { get; }

        public string DisplayName { get; }

        /// <summary>ISO 8601 문자열. null이거나 빈 문자열이면 탈퇴(비활성) 처리 전제가 아님.</summary>
        public string WithdrawnAtIso { get; }

        /// <summary>플레이어가 속한 서버 코드 (예: "GLOBAL", "KR1"). 로그인 완료 전에는 빈 문자열.</summary>
        public string ServerCode { get; }

        public bool IsWithdrawn => string.IsNullOrWhiteSpace(WithdrawnAtIso) == false;
    }
}
