using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TrueBase.Core.Common;
using TrueBase.Core.Http;

namespace TrueBase.Core.Data
{
    /// <summary>고객센터 창의 접속 표. 창(웹 페이지)이 이 표로 문의를 읽고 보낸다.</summary>
    public sealed class SupabaseCustomerCenterSession
    {
        public string Token { get; }
        public bool Banned { get; }
        public string Language { get; }

        public SupabaseCustomerCenterSession(string token, bool banned, string language)
        {
            Token = token;
            Banned = banned;
            Language = language;
        }
    }

    /// <summary>
    /// 고객센터. 창을 열기 전 본인 확인(Edge Function <c>support-api</c> 의 <c>session.start</c>)과
    /// 새 답변 수(RPC <c>ts_support_unread_count</c>)만 SDK 가 부르고, 문의 읽기·쓰기는 창이 직접 한다.
    /// </summary>
    public sealed class SupabaseCustomerCenterService
    {
        private const string FunctionName = "support-api";
        // 서버 응답 꼴이 틀린 경우 — 카탈로그 밖 저수준 사유.
        private const string InvalidResponse = "customer_center_invalid_response";

        private readonly string _supabaseUrl;
        private readonly string _publishableKey;
        private readonly ISupabaseHttpClient _httpClient;

        public SupabaseCustomerCenterService(string supabaseUrl, string publishableKey, ISupabaseHttpClient httpClient)
        {
            _supabaseUrl = (supabaseUrl ?? string.Empty).TrimEnd('/');
            _publishableKey = publishableKey ?? string.Empty;
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        /// <summary>
        /// 본인을 확인받고 창 접속 표를 받습니다. <paramref name="proofs"/> 는 서버가 받는 이름 그대로
        /// (<c>access_token</c>·<c>saved_access_token</c>·<c>refresh_token</c>·<c>id_token</c>·<c>provider</c>·<c>nonce</c>),
        /// 비어 있는 값은 빼고 보냅니다. 확인할 수 없으면 <see cref="SupabaseErrorCode.CustomerCenterLoginRequired"/>.
        /// </summary>
        public async Task<SupabaseResult<SupabaseCustomerCenterSession>> StartSessionAsync(
            IDictionary<string, string> proofs, IDictionary<string, string> meta)
        {
            var body = new Dictionary<string, object> { ["action"] = "session.start" };
            if (proofs != null)
            {
                foreach (var pair in proofs)
                    if (!string.IsNullOrWhiteSpace(pair.Value))
                        body[pair.Key] = pair.Value;
            }
            if (meta != null)
                body["meta"] = meta;

            var response = await _httpClient.SendAsync(
                method: "POST",
                url: $"{_supabaseUrl}/functions/v1/{FunctionName}",
                jsonBody: JsonConvert.SerializeObject(body),
                headers: new Dictionary<string, string> { ["apikey"] = _publishableKey });

            if (response == null || (response.StatusCode == 0 && !response.IsSuccess))
                return SupabaseResult<SupabaseCustomerCenterSession>.Fail(SupabaseErrorCode.NetworkError);

            JObject json;
            try
            {
                json = JObject.Parse(response.Body ?? string.Empty);
            }
            catch
            {
                return SupabaseResult<SupabaseCustomerCenterSession>.Fail(
                    response.IsSuccess ? InvalidResponse : response.ErrorMessage ?? "customer_center_failed");
            }

            if (json["ok"]?.Value<bool>() != true)
                return SupabaseResult<SupabaseCustomerCenterSession>.Fail(json["reason"]?.Value<string>() ?? "customer_center_failed");

            var data = json["data"] as JObject;
            var token = data?["token"]?.Value<string>();
            if (string.IsNullOrEmpty(token))
                return SupabaseResult<SupabaseCustomerCenterSession>.Fail(InvalidResponse);

            return SupabaseResult<SupabaseCustomerCenterSession>.Success(new SupabaseCustomerCenterSession(
                token, data["banned"]?.Value<bool>() == true, data["lang"]?.Value<string>()));
        }

        /// <summary>운영자 답을 아직 안 읽은 내 문의 수. 로그인 상태에서만.</summary>
        public async Task<SupabaseResult<int>> GetUnreadCountAsync(string accessToken)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
                return SupabaseResult<int>.Fail(SupabaseErrorCode.NotSignedIn);

            var r = await SupabaseRestHelpers.CallRpcAsync(
                _httpClient, _supabaseUrl, _publishableKey, accessToken, "ts_support_unread_count", "{}");
            if (!r.IsSuccess)
                return SupabaseResult<int>.Fail(r.ErrorCode);
            return int.TryParse(r.Data?.Trim(), out var count)
                ? SupabaseResult<int>.Success(count)
                : SupabaseResult<int>.Fail(InvalidResponse);
        }
    }
}
