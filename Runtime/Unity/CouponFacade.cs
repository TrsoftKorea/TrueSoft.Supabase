using System;
using System.Threading.Tasks;
using TrueBase.Core.Auth;
using TrueBase.Core.Common;
using TrueBase.Core.Data;

namespace TrueBase.Unity
{
    /// <summary>
    /// 로그인 세션을 사용하는 쿠폰 API.
    /// 쿠폰 정의·발급은 운영(Retool) 전용이라 여기에 없습니다.
    /// </summary>
    internal sealed class CouponFacade
    {
        private readonly SupabaseCouponService _coupon;
        private readonly Func<SupabaseSession> _sessionGetter;

        /// <param name="coupon">REST 호출을 수행할 서비스. null이면 예외.</param>
        /// <param name="sessionGetter">현재 세션 제공자. null이면 세션 없는 오버로드는 <c>auth_not_signed_in</c>으로 실패합니다.</param>
        public CouponFacade(SupabaseCouponService coupon, Func<SupabaseSession> sessionGetter = null)
        {
            _coupon = coupon ?? throw new ArgumentNullException(nameof(coupon));
            _sessionGetter = sessionGetter;
        }

        /// <summary>쿠폰을 사용합니다. 보상은 우편으로 지급됩니다.</summary>
        /// <param name="code">유저가 입력한 코드.</param>
        public Task<SupabaseResult> RedeemAsync(string code) =>
            RedeemAsync(_sessionGetter?.Invoke(), code);

        public async Task<SupabaseResult> RedeemAsync(SupabaseSession session, string code)
        {
            var token = RequireToken(session);
            if (token == null)
                return SupabaseResult.Fail(SupabaseErrorCode.NotSignedIn);

            return await _coupon.RedeemAsync(token, code);
        }

        /// <summary>세션에서 액세스 토큰을 추출합니다. 세션이 null이거나 토큰이 비어 있으면 null.</summary>
        private static string RequireToken(SupabaseSession session)
        {
            if (session == null || string.IsNullOrWhiteSpace(session.AccessToken))
                return null;

            return session.AccessToken;
        }
    }
}
