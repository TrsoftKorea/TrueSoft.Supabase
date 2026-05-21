namespace TrueBase.Core.Data
{
    /// <summary>
    /// 공개 프로필 조회 결과. <c>profiles</c>의 <c>id</c>(행 PK), <c>user_id</c>(안정 플레이어 id), <c>withdrawn_at</c>를 반영합니다.
    /// 표시 이름은 Auth user metadata(<c>displayName</c>)를 사용합니다.
    /// </summary>
    public sealed class PublicProfileSnapshot
    {
        public PublicProfileSnapshot(string profileRowId, string playerUserId, string displayName, string withdrawnAtIso)
        {
            ProfileRowId = profileRowId ?? string.Empty;
            UserId = playerUserId ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            WithdrawnAtIso = withdrawnAtIso;
        }

        /// <summary>테이블 PK (<c>profiles.id</c>).</summary>
        public string ProfileRowId { get; }

        /// <summary>플레이어 안정 id (<c>profiles.user_id</c>, OAuth <c>sub</c> 등).</summary>
        public string UserId { get; }

        public string DisplayName { get; }

        /// <summary>ISO 8601 문자열. null이거나 빈 문자열이면 탈퇴(비활성) 처리 전제가 아님.</summary>
        public string WithdrawnAtIso { get; }

        public bool IsWithdrawn => string.IsNullOrWhiteSpace(WithdrawnAtIso) == false;
    }
}
