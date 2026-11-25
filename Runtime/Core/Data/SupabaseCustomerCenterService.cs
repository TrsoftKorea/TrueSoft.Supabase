using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TrueBase.Core.Common;
using TrueBase.Core.Http;
using TrueBase.Core.Models;

namespace TrueBase.Core.Data
{
    /// <summary>고객센터 창의 접속 표. 창(웹 페이지)이 이 표로 문의를 읽고 보낸다. SDK 안에서만 쓴다.</summary>
    internal sealed class SupabaseCustomerCenterSession
    {
        public string Token { get; }
        public bool Banned { get; }
        public string Language { get; }
        /// <summary>차단된 계정이면 정지 기한·사유. 구글·애플 로그인이 차단으로 거절됐을 때 SDK 가 이걸로 차단 정보를 채운다.</summary>
        public SupabaseBanInfo BanInfo { get; }

        public SupabaseCustomerCenterSession(string token, bool banned, string language, SupabaseBanInfo banInfo)
        {
            Token = token;
            Banned = banned;
            Language = language;
            BanInfo = banInfo;
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
        internal async Task<SupabaseResult<SupabaseCustomerCenterSession>> StartSessionAsync(
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
                // 날짜를 글자 그대로 받는다 — 기본 해석은 시각을 기기 시간대로 바꿔 놓아 정지 기한이 어긋날 수 있다.
                using var reader = new JsonTextReader(new System.IO.StringReader(response.Body ?? string.Empty)) { DateParseHandling = DateParseHandling.None };
                json = JObject.Load(reader);
            }
            catch
            {
                return SupabaseResult<SupabaseCustomerCenterSession>.Fail(
                    response.IsSuccess ? InvalidResponse : $"customer_center_http_{response.StatusCode}");
            }

            if (json["ok"]?.Value<bool>() != true)
                return SupabaseResult<SupabaseCustomerCenterSession>.Fail(json["reason"]?.Value<string>() ?? "customer_center_failed");

            var data = json["data"] as JObject;
            var token = data?["token"]?.Value<string>();
            if (string.IsNullOrEmpty(token))
                return SupabaseResult<SupabaseCustomerCenterSession>.Fail(InvalidResponse);

            SupabaseBanInfo banInfo = null;
            if (data["ban"] is JObject ban && !string.IsNullOrEmpty(ban["account_id"]?.Value<string>()))
            {
                DateTimeOffset? until = null;
                var rawUntil = ban["banned_until"]?.Type == JTokenType.String ? ban["banned_until"].Value<string>() : null;
                if (!string.IsNullOrWhiteSpace(rawUntil)
                    && DateTimeOffset.TryParse(rawUntil, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
                    until = parsed;
                banInfo = new SupabaseBanInfo(ban["account_id"].Value<string>(), until,
                    ban["message"]?.Type == JTokenType.Null ? null : ban["message"]?.Value<string>());
            }

            return SupabaseResult<SupabaseCustomerCenterSession>.Success(new SupabaseCustomerCenterSession(
                token, data["banned"]?.Value<bool>() == true, data["lang"]?.Value<string>(), banInfo));
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

        /// <summary>지금 시행 중인 이용약관·개인정보처리방침 판(RPC <c>ts_legal_versions</c>). 로그인 전에도 부른다.</summary>
        /// <param name="accessToken">로그인 중이면 그 토큰, 아니면 null.</param>
        public async Task<SupabaseResult<SupabaseLegalVersions>> GetLegalVersionsAsync(string accessToken)
        {
            var headers = string.IsNullOrWhiteSpace(accessToken)
                ? SupabaseCoreHelper.BuildApiHeaders(_publishableKey)
                : SupabaseRestHelpers.AuthHeaders(_publishableKey, accessToken);
            var response = await _httpClient.SendAsync("POST", $"{_supabaseUrl}/rest/v1/rpc/ts_legal_versions", "{}", headers);
            if (response == null)
                return SupabaseResult<SupabaseLegalVersions>.Fail(SupabaseErrorCode.NetworkError);
            if (!response.IsSuccess)
                return SupabaseResult<SupabaseLegalVersions>.Fail(
                    SupabaseRestHelpers.ExtractRpcErrorCode(response.Body, response.ErrorMessage, "ts_legal_versions"));
            var o = ParseObject(response.Body);
            return o == null
                ? SupabaseResult<SupabaseLegalVersions>.Fail(InvalidResponse)
                : SupabaseResult<SupabaseLegalVersions>.Success(ReadVersions(o));
        }

        /// <summary>내 마지막 약관 동의와 다시 물어야 하는지(RPC <c>ts_legal_consent_get</c>). 로그인 상태에서만.</summary>
        public async Task<SupabaseResult<SupabaseLegalConsent>> GetLegalConsentAsync(string accessToken)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
                return SupabaseResult<SupabaseLegalConsent>.Fail(SupabaseErrorCode.NotSignedIn);
            var r = await SupabaseRestHelpers.CallRpcAsync(
                _httpClient, _supabaseUrl, _publishableKey, accessToken, "ts_legal_consent_get", "{}");
            return ToConsent(r);
        }

        /// <summary>
        /// 약관 동의를 남깁니다(RPC <c>ts_legal_consent_save</c>). 판은 게시한 판이어야 하고, 같은 동의를 다시 보내면 쌓지 않습니다.
        /// 결과는 남긴 뒤의 <see cref="GetLegalConsentAsync"/> 와 같습니다.
        /// </summary>
        public async Task<SupabaseResult<SupabaseLegalConsent>> SaveLegalConsentAsync(
            string accessToken, int termsVersion, int privacyVersion, bool ageConfirmed)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
                return SupabaseResult<SupabaseLegalConsent>.Fail(SupabaseErrorCode.NotSignedIn);
            var body = new JObject
            {
                ["p_terms_version"] = termsVersion,
                ["p_privacy_version"] = privacyVersion,
                ["p_age_confirmed"] = ageConfirmed,
            }.ToString(Formatting.None);
            var r = await SupabaseRestHelpers.CallRpcAsync(
                _httpClient, _supabaseUrl, _publishableKey, accessToken, "ts_legal_consent_save", body);
            return ToConsent(r);
        }

        private static SupabaseResult<SupabaseLegalConsent> ToConsent(SupabaseResult<string> r)
        {
            if (!r.IsSuccess)
                return SupabaseResult<SupabaseLegalConsent>.Fail(r.ErrorCode);
            var o = ParseObject(r.Data);
            if (o == null)
                return SupabaseResult<SupabaseLegalConsent>.Fail(InvalidResponse);

            var consent = new SupabaseLegalConsent
            {
                NeedsConsent = o.Value<bool?>("needs_consent") ?? true,
                Current = ReadVersions(o),
            };
            if (o["consent"] is JObject c)
            {
                consent.HasConsent = true;
                consent.TermsVersion = c.Value<int?>("terms_version") ?? 0;
                consent.PrivacyVersion = c.Value<int?>("privacy_version") ?? 0;
                consent.AgeConfirmed = c.Value<bool?>("age_confirmed") ?? false;
                consent.AgreedAt = DateTimeOffset.TryParse(c.Value<string>("agreed_at"), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal, out var at) ? at : (DateTimeOffset?)null;
            }
            return SupabaseResult<SupabaseLegalConsent>.Success(consent);
        }

        private static SupabaseLegalVersions ReadVersions(JObject o)
        {
            var v = new SupabaseLegalVersions();
            if (o["terms"] is JObject t)
            {
                v.TermsVersion = t.Value<int?>("version") ?? 0;
                v.TermsEffectiveDate = ParseDate(t.Value<string>("effective_date"));
            }
            if (o["privacy"] is JObject p)
            {
                v.PrivacyVersion = p.Value<int?>("version") ?? 0;
                v.PrivacyEffectiveDate = ParseDate(p.Value<string>("effective_date"));
            }
            return v;
        }

        private static DateTime? ParseDate(string s) =>
            DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : (DateTime?)null;

        // 날짜는 글자로 받아 직접 읽는다 — 자동 변환은 시간대를 붙여 바꿔 버린다.
        private static JObject ParseObject(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return null;
            try
            {
                using (var reader = new JsonTextReader(new StringReader(body)) { DateParseHandling = DateParseHandling.None })
                    return JToken.ReadFrom(reader) as JObject;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
