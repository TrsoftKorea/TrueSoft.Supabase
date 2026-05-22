using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TrueBase.Core.Common;
using TrueBase.Core.Http;
using TrueBase.Core.Models;

namespace TrueBase.Core.Data
{
    /// <summary>
    /// 애널리틱스 세션(<c>analytics_sessions</c>) REST 서비스.
    /// 로그인 후 세션 시작·keep-alive·종료를 PostgREST로 직접 처리합니다.
    /// </summary>
    public sealed class SupabaseAnalyticsSessionService
    {
        private readonly string _supabaseUrl;
        private readonly string _publishableKey;
        private readonly string _table;
        private readonly ISupabaseHttpClient _httpClient;
        private readonly ISupabaseJsonSerializer _jsonSerializer;

        public SupabaseAnalyticsSessionService(
            string supabaseUrl,
            string publishableKey,
            ISupabaseHttpClient httpClient,
            ISupabaseJsonSerializer jsonSerializer,
            string analyticsSessionsTable = "analytics_sessions")
        {
            _supabaseUrl    = supabaseUrl?.TrimEnd('/') ?? throw new ArgumentNullException(nameof(supabaseUrl));
            _publishableKey = publishableKey ?? throw new ArgumentNullException(nameof(publishableKey));
            _httpClient     = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _jsonSerializer = jsonSerializer ?? throw new ArgumentNullException(nameof(jsonSerializer));
            _table = SupabaseRestTableRef.Normalize(analyticsSessionsTable, nameof(analyticsSessionsTable));
        }

        /// <summary>세션을 시작합니다 (INSERT).</summary>
        public async Task<SupabaseResult<bool>> StartSessionAsync(
            string accessToken,
            string sessionId,
            string accountId,
            string userId,
            string platform,
            string appVersion)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
                return SupabaseResult<bool>.Fail("access_token_empty");

            if (string.IsNullOrWhiteSpace(sessionId))
                return SupabaseResult<bool>.Fail("session_id_empty");

            var row = new AnalyticsSessionInsert
            {
                session_id  = sessionId,
                account_id  = accountId,
                user_id     = string.IsNullOrWhiteSpace(userId) ? null : userId,
                platform    = platform,
                app_version = appVersion,
            };

            var url      = SupabaseRestTableRef.BuildTableUrl(_supabaseUrl, _table);
            var bodyJson = "[" + _jsonSerializer.ToJson(row) + "]";

            var response = await _httpClient.SendAsync(
                method: "POST",
                url: url,
                jsonBody: bodyJson,
                headers: CreateAuthHeaders(accessToken, "return=minimal"));

            if (response == null)
                return SupabaseResult<bool>.Fail("http_response_null");

            if (!response.IsSuccess)
                return SupabaseResult<bool>.Fail(response.ErrorMessage ?? response.Body ?? "session_start_failed");

            return SupabaseResult<bool>.Success(true);
        }

        /// <summary>keep-alive — <c>last_active_at</c>을 갱신합니다 (PATCH).</summary>
        public async Task<SupabaseResult<bool>> UpdateLastActiveAsync(string accessToken, string sessionId)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
                return SupabaseResult<bool>.Fail("access_token_empty");

            if (string.IsNullOrWhiteSpace(sessionId))
                return SupabaseResult<bool>.Fail("session_id_empty");

            var url = $"{SupabaseRestTableRef.BuildTableUrl(_supabaseUrl, _table)}" +
                      $"?session_id=eq.{Uri.EscapeDataString(sessionId)}";

            var patch    = new AnalyticsSessionPatch { last_active_at = DateTime.UtcNow.ToString("o") };
            var bodyJson = _jsonSerializer.ToJson(patch);

            var response = await _httpClient.SendAsync(
                method: "PATCH",
                url: url,
                jsonBody: bodyJson,
                headers: CreateAuthHeaders(accessToken, "return=minimal"));

            if (response == null)
                return SupabaseResult<bool>.Fail("http_response_null");

            if (!response.IsSuccess)
                return SupabaseResult<bool>.Fail(response.ErrorMessage ?? response.Body ?? "session_update_failed");

            return SupabaseResult<bool>.Success(true);
        }

        /// <summary>세션을 종료합니다 — <c>is_closed=true</c>, <c>ended_at</c>을 설정합니다 (PATCH).</summary>
        public async Task<SupabaseResult<bool>> CloseSessionAsync(string accessToken, string sessionId)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
                return SupabaseResult<bool>.Fail("access_token_empty");

            if (string.IsNullOrWhiteSpace(sessionId))
                return SupabaseResult<bool>.Fail("session_id_empty");

            var url = $"{SupabaseRestTableRef.BuildTableUrl(_supabaseUrl, _table)}" +
                      $"?session_id=eq.{Uri.EscapeDataString(sessionId)}";

            var now   = DateTime.UtcNow.ToString("o");
            var patch = new AnalyticsSessionClosePatch { ended_at = now, is_closed = true };
            var bodyJson = _jsonSerializer.ToJson(patch);

            var response = await _httpClient.SendAsync(
                method: "PATCH",
                url: url,
                jsonBody: bodyJson,
                headers: CreateAuthHeaders(accessToken, "return=minimal"));

            if (response == null)
                return SupabaseResult<bool>.Fail("http_response_null");

            if (!response.IsSuccess)
                return SupabaseResult<bool>.Fail(response.ErrorMessage ?? response.Body ?? "session_close_failed");

            return SupabaseResult<bool>.Success(true);
        }

        private Dictionary<string, string> CreateAuthHeaders(string accessToken, string prefer)
        {
            var headers = new Dictionary<string, string>
            {
                { "apikey", _publishableKey },
                { "Authorization", "Bearer " + accessToken },
                { "Content-Type", "application/json" }
            };

            if (!string.IsNullOrEmpty(prefer))
                headers["Prefer"] = prefer;

            return headers;
        }
    }
}
