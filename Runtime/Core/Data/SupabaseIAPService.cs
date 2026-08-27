using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using TrueBase.Core.Common;
using TrueBase.Core.Http;

namespace TrueBase.Core.Data
{
    /// <summary>
    /// IAP 지급 완료 기록 RPC. 사용 <c>ts_mark_purchase_granted</c> 하나뿐입니다.
    /// 영수증 검증은 Edge Function(purchase-verify-*)이 담당하므로 여기 없습니다.
    /// 게임이 직접 호출하는 API가 아니라 <see cref="TrueBase.Unity.BaseIAPFacade"/> 내부 전용입니다.
    /// </summary>
    public sealed class SupabaseIAPService
    {
        private readonly string _supabaseUrl;
        private readonly string _publishableKey;
        private readonly ISupabaseHttpClient _httpClient;

        public SupabaseIAPService(
            string supabaseUrl,
            string publishableKey,
            ISupabaseHttpClient httpClient)
        {
            _supabaseUrl = (supabaseUrl ?? string.Empty).TrimEnd('/');
            _publishableKey = publishableKey ?? string.Empty;
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        /// <summary>
        /// 주문을 지급 완료로 표시합니다. <c>onGrant</c>가 true를 반환한 직후 호출하세요.
        /// 본인 소유 주문만 갱신되며, 이미 표시돼 있으면 아무 일도 하지 않습니다(멱등).
        /// </summary>
        /// <param name="accessToken">현재 로그인 세션의 액세스 토큰. 필수.</param>
        /// <param name="orderId">주문 고유 ID(Google은 orderId, Apple은 transaction_id).</param>
        public async Task<SupabaseResult> MarkGrantedAsync(string accessToken, string orderId)
        {
            if (string.IsNullOrWhiteSpace(orderId))
                return SupabaseResult.Fail(SupabaseErrorCode.IapOrderIdEmpty);

            var body = JsonConvert.SerializeObject(new { p_order_id = orderId });
            var r = await CallRpcAsync(accessToken, "ts_mark_purchase_granted", body);
            return r.IsSuccess ? SupabaseResult.Ok : SupabaseResult.Fail(r.ErrorCode);
        }

        // -------------------------------------------------------------------
        // 공통 RPC 호출
        // -------------------------------------------------------------------

        // 값을 돌려주지 않는 RPC이므로 빈 본문을 정상으로 본다.
        private Task<SupabaseResult<string>> CallRpcAsync(string accessToken, string rpcName, string bodyJson) =>
            SupabaseRestHelpers.CallRpcAsync(
                _httpClient, _supabaseUrl, _publishableKey, accessToken, rpcName, bodyJson, allowEmptyBody: true);

    }
}
