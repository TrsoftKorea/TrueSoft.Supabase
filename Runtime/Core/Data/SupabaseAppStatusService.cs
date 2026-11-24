using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TrueBase.Core.Common;
using TrueBase.Core.Http;

namespace TrueBase.Core.Data
{
    /// <summary>
    /// 최소 판·점검 상태 조회(RPC <c>ts_app_status</c>). 로그인 전에도 부른다 — 로그인 중이면 그 계정이 점검 중 통과 계정인지도 받는다.
    /// </summary>
    public sealed class SupabaseAppStatusService
    {
        private readonly string _supabaseUrl;
        private readonly string _publishableKey;
        private readonly ISupabaseHttpClient _httpClient;

        public SupabaseAppStatusService(string supabaseUrl, string publishableKey, ISupabaseHttpClient httpClient)
        {
            _supabaseUrl = supabaseUrl?.TrimEnd('/') ?? throw new ArgumentNullException(nameof(supabaseUrl));
            _publishableKey = publishableKey?.Trim() ?? throw new ArgumentNullException(nameof(publishableKey));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        /// <param name="accessToken">로그인 중이면 그 토큰, 아니면 null.</param>
        /// <param name="platform"><c>android</c>·<c>ios</c>. 그 밖이면 null — 최소 판을 보지 않는다.</param>
        /// <param name="currentVersion">이 앱의 판(Application.version).</param>
        /// <param name="language">점검 문구 언어. null 이면 기기 언어.</param>
        /// <param name="defaultStoreUrl">운영 콘솔에 스토어 주소가 없을 때 쓸 주소.</param>
        public async Task<SupabaseResult<SupabaseAppStatus>> GetAsync(
            string accessToken, string platform, string currentVersion, string language, string defaultStoreUrl)
        {
            var headers = string.IsNullOrWhiteSpace(accessToken)
                ? new Dictionary<string, string> { { "apikey", _publishableKey }, { "Content-Type", "application/json" } }
                : SupabaseRestHelpers.AuthHeaders(_publishableKey, accessToken);
            var body = new JObject { ["p_platform"] = platform }.ToString(Formatting.None);

            var response = await _httpClient.SendAsync("POST", $"{_supabaseUrl}/rest/v1/rpc/ts_app_status", body, headers);
            if (response == null)
                return SupabaseResult<SupabaseAppStatus>.Fail(SupabaseErrorCode.NetworkError);
            if (!response.IsSuccess)
                return SupabaseResult<SupabaseAppStatus>.Fail(
                    SupabaseRestHelpers.ExtractRpcErrorCode(response.Body, response.ErrorMessage, "app_status_fetch"));

            JObject o;
            try
            {
                // 날짜는 글자로 받아 직접 읽는다 — 자동 변환은 DateTime 으로 바뀌어 DateTimeOffset 로 꺼낼 때 실패한다.
                using (var reader = new JsonTextReader(new StringReader(response.Body ?? "")) { DateParseHandling = DateParseHandling.None })
                    o = JObject.Load(reader);
            }
            catch (Exception)
            {
                return SupabaseResult<SupabaseAppStatus>.Fail(InvalidResponse);
            }

            var status = new SupabaseAppStatus
            {
                MinVersion = NullIfBlank(o.Value<string>("min_version")),
                StoreUrl = NullIfBlank(o.Value<string>("store_url")) ?? NullIfBlank(defaultStoreUrl),
                MaintenanceAllowed = o.Value<bool?>("allowed") ?? false,
            };
            status.UpdateRequired = AppVersion.IsLower(currentVersion, status.MinVersion);

            if (o["maintenance"] is JObject m)
            {
                // 글자가 아닌 값이 섞여 있으면(DB 를 직접 고친 경우) 언어별 글은 버리고 기본 문구를 쓴다 — 예외가 게임의 await 로 새지 않게.
                Dictionary<string, string> localized = null;
                if (m["localized"] is JObject l)
                {
                    localized = new Dictionary<string, string>();
                    foreach (var p in l.Properties())
                        if (p.Value.Type == JTokenType.String) localized[p.Name] = (string)p.Value;
                }
                var picked = localized == null ? null : MailLanguage.Pick(localized, language);
                status.MaintenanceMessage = string.IsNullOrEmpty(picked) ? m.Value<string>("message") ?? "" : picked;
                status.MaintenanceStartsAt = ParseTime(m.Value<string>("starts_at"));
                status.MaintenanceEndsAt = ParseTime(m.Value<string>("ends_at"));
                status.InMaintenance = (m.Value<bool?>("active") ?? false) && !status.MaintenanceAllowed;
            }
            return SupabaseResult<SupabaseAppStatus>.Success(status);
        }

        private const string InvalidResponse = "app_status_invalid_response";

        private static string NullIfBlank(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        private static DateTimeOffset? ParseTime(string s) =>
            DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t) ? t : (DateTimeOffset?)null;
    }

    /// <summary>앱 판 비교. 점으로 나눈 마디를 숫자로 비교하고, 마디마다 앞쪽 숫자만 읽는다("1.2.0b" → 1.2.0).</summary>
    internal static class AppVersion
    {
        /// <summary>current 가 min 보다 낮으면 true. 어느 쪽이든 읽지 못하면 false(막지 않는다).</summary>
        internal static bool IsLower(string current, string min)
        {
            if (!TryParse(current, out var a) || !TryParse(min, out var b)) return false;
            var n = Math.Max(a.Length, b.Length);
            for (var i = 0; i < n; i++)
            {
                var x = i < a.Length ? a[i] : 0;
                var y = i < b.Length ? b[i] : 0;
                if (x != y) return x < y;
            }
            return false;
        }

        private static bool TryParse(string s, out long[] parts)
        {
            parts = null;
            if (string.IsNullOrWhiteSpace(s)) return false;
            var raw = s.Trim().Split('.');
            var result = new long[raw.Length];
            for (var i = 0; i < raw.Length; i++)
            {
                var digits = 0;
                while (digits < raw[i].Length && digits < 9 && char.IsDigit(raw[i][digits])) digits++;
                if (digits == 0) return false;
                result[i] = long.Parse(raw[i].Substring(0, digits));
            }
            parts = result;
            return true;
        }
    }
}
