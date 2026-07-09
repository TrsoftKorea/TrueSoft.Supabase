using TrueBase.Core.Models;

namespace TrueBase.Unity
{
    /// <summary>
    /// SDK Try* 메서드의 통합 반환 타입. 성공 여부와 실패 이유를 함께 제공합니다.
    /// </summary>
    /// <remarks>
    /// <c>implicit operator bool</c>을 제공하므로 <c>if (await Try*())</c> 형태로 바로 분기할 수 있습니다.
    /// <example>
    /// <code>
    /// // 성공 여부만 필요할 때
    /// if (await Supabase.TrySignInAnonymouslyAsync()) { }
    ///
    /// // 실패 이유 확인
    /// var result = await Supabase.TrySignInAnonymouslyAsync();
    /// if (!result.Success)
    /// {
    ///     if (result.Reason == SupabaseFailReason.UserBanned) ShowBanScreen(result.BanInfo);
    ///     else if (result.Reason == SupabaseFailReason.NetworkError) ShowRetry();
    ///     else Debug.Log($"[Login] 실패: {result.Reason}");
    /// }
    /// </code>
    /// </example>
    /// </remarks>
    public readonly struct SupabaseCallResult
    {
        /// <summary>성공 결과 싱글턴.</summary>
        public static readonly SupabaseCallResult Ok = new(true, null, null);

        /// <summary>성공 여부.</summary>
        public bool Success { get; }

        /// <summary>
        /// 실패 원인 식별자(snake_case). 성공 시 <see langword="null"/>.
        /// 주요 값은 <see cref="SupabaseFailReason"/>에 상수로 정의되어 있습니다.
        /// </summary>
        public string Reason { get; }

        /// <summary>
        /// 계정 차단 정보. <see cref="Reason"/> == <c>"user_banned"</c>일 때만 유효하며, 그 외에는 <see langword="null"/>입니다.
        /// </summary>
        public SupabaseBanInfo BanInfo { get; }

        private SupabaseCallResult(bool success, string reason, SupabaseBanInfo banInfo)
        {
            Success = success;
            Reason  = reason;
            BanInfo = banInfo;
        }

        /// <summary>실패 결과를 생성합니다.</summary>
        public static SupabaseCallResult Fail(string reason, SupabaseBanInfo banInfo = null)
            => new(false, reason, banInfo);

        /// <summary><see cref="Success"/>로의 암묵적 변환. <c>if (await Try*())</c> 형태로 바로 분기할 수 있습니다.</summary>
        public static implicit operator bool(SupabaseCallResult r) => r.Success;
    }
}
