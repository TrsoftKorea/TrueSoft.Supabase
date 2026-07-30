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

        private async Task<SupabaseResult<string>> CallRpcAsync(string accessToken, string rpcName, string bodyJson)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
                return SupabaseResult<string>.Fail(SupabaseErrorCode.AccessTokenEmpty);

            var response = await _httpClient.SendAsync(
                method: "POST",
                url: $"{_supabaseUrl}/rest/v1/rpc/{rpcName}",
                jsonBody: bodyJson,
                headers: CreateAuthHeaders(accessToken));

            if (response == null)
                return SupabaseResult<string>.Fail(SupabaseErrorCode.NetworkError);

            if (response.IsSuccess == false)
                return SupabaseResult<string>.Fail(ExtractErrorCode(response.Body, response.ErrorMessage, rpcName));

            return SupabaseResult<string>.Success(response.Body?.Trim() ?? string.Empty);
        }

        /// <summary>
        /// PostgREST 오류 본문에서 서버가 <c>raise exception</c>으로 던진 사유를 뽑아냅니다.
        /// 상세가 붙은 경우 앞의 코드만 사용해 <c>SupabaseReason</c> 매핑이 동작하게 합니다.
        /// </summary>
        private static string ExtractErrorCode(string body, string fallbackMessage, string rpcName) =>
            SupabaseRestHelpers.ExtractRpcErrorCode(body, fallbackMessage, rpcName);

        private Dictionary<string, string> CreateAuthHeaders(string accessToken) =>
            SupabaseRestHelpers.AuthHeaders(_publishableKey, accessToken);
    }
}
