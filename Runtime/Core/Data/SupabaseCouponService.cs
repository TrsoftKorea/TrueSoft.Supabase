using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TrueBase.Core.Common;
using TrueBase.Core.Http;

namespace TrueBase.Core.Data
{
    /// <summary>
    /// 쿠폰 RPC. 사용 <c>ts_coupon_redeem</c> 하나뿐입니다.
    /// 쿠폰 정의·발급은 운영(service_role) 전용이라 여기에 없습니다.
    /// 보상은 응답으로 오지 않고 서버가 우편으로 넣습니다.
    /// </summary>
    public sealed class SupabaseCouponService
    {
        private readonly string _supabaseUrl;
        private readonly string _publishableKey;
        private readonly ISupabaseHttpClient _httpClient;

        public SupabaseCouponService(
            string supabaseUrl,
            string publishableKey,
            ISupabaseHttpClient httpClient)
        {
            _supabaseUrl = (supabaseUrl ?? string.Empty).TrimEnd('/');
            _publishableKey = publishableKey ?? string.Empty;
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        /// <summary>
        /// 쿠폰을 사용합니다. 성공하면 서버가 보상 우편을 만듭니다.
        /// 실패 사유: coupon_not_found · coupon_inactive · coupon_expired · coupon_already_used · coupon_exhausted.
        /// </summary>
        /// <param name="accessToken">현재 로그인 세션의 액세스 토큰. 필수.</param>
        /// <param name="code">유저가 입력한 코드. 대소문자·앞뒤 공백은 서버가 정규화합니다.</param>
        public async Task<SupabaseResult> RedeemAsync(string accessToken, string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return SupabaseResult.Fail(SupabaseErrorCode.CouponNotFound);

            var body = JsonConvert.SerializeObject(new { p_code = code.Trim() });
            var r = await CallRpcAsync(accessToken, "ts_coupon_redeem", body);
            return r.IsSuccess ? SupabaseResult.Ok : SupabaseResult.Fail(r.ErrorCode);
        }

        // -------------------------------------------------------------------
        // 공통 RPC 호출
        // -------------------------------------------------------------------

        // 쿠폰 RPC 는 값을 돌려주지 않는 것도 있어 빈 본문을 정상으로 본다.
        private Task<SupabaseResult<string>> CallRpcAsync(string accessToken, string rpcName, string bodyJson) =>
            SupabaseRestHelpers.CallRpcAsync(
                _httpClient, _supabaseUrl, _publishableKey, accessToken, rpcName, bodyJson, allowEmptyBody: true);

    }
}
