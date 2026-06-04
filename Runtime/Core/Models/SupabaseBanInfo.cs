using System;

namespace TrueBase.Core.Models
{
    /// <summary>
    /// 로그인 차단된 계정의 차단 정보. 차단 시 <see cref="Common.SupabaseResult{T}.BanInfo"/>로 반환됩니다.
    /// </summary>
    public sealed class SupabaseBanInfo
    {
        /// <summary>차단된 계정 ID (<c>auth.users.id</c>).</summary>
        public string AccountId { get; }

        /// <summary>
        /// 차단 해제 일시. <see langword="null"/>이면 영구 차단(<see cref="IsPermanentBan"/> 참고).
        /// </summary>
        public DateTimeOffset? BannedUntil { get; }

        /// <summary>어드민이 작성한 차단 사유 메시지. 설정되지 않은 경우 <see langword="null"/>.</summary>
        public string BanMessage { get; }

        /// <summary><see cref="BannedUntil"/>이 <see langword="null"/>인 영구 차단이면 <see langword="true"/>.</summary>
        public bool IsPermanentBan => !BannedUntil.HasValue;

        public SupabaseBanInfo(string accountId, DateTimeOffset? bannedUntil, string banMessage)
        {
            AccountId  = accountId;
            BannedUntil = bannedUntil;
            BanMessage  = banMessage;
        }
    }
}
