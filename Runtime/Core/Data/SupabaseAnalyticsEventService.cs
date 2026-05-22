using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TrueBase.Core.Common;
using TrueBase.Core.Http;
using TrueBase.Core.Models;

namespace TrueBase.Core.Data
{
    /// <summary>
    /// 애널리틱스 이벤트(<c>analytics_events</c>) REST 서비스.
    /// 광고 재생 등 이벤트를 이름 단위로 기록합니다.
    /// </summary>
    public sealed class SupabaseAnalyticsEventService
    {
        private readonly string _supabaseUrl;
        private readonly string _publishableKey;
        private readonly string _table;
        private readonly ISupabaseHttpClient _httpClient;
        private readonly ISupabaseJsonSerializer _jsonSerializer;

        public SupabaseAnalyticsEventService(
            string supabaseUrl,
            string publishableKey,
            ISupabaseHttpClient httpClient,
            ISupabaseJsonSerializer jsonSerializer,
            string analyticsEventsTable = "analytics_events")
        {
            _supabaseUrl    = supabaseUrl?.TrimEnd('/') ?? throw new ArgumentNullException(nameof(supabaseUrl));
            _publishableKey = publishableKey ?? throw new ArgumentNullException(nameof(publishableKey));
            _httpClient     = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _jsonSerializer = jsonSerializer ?? throw new ArgumentNullException(nameof(jsonSerializer));
            _table = SupabaseRestTableRef.Normalize(analyticsEventsTable, nameof(analyticsEventsTable));
        }

        /// <summary>이벤트를 기록합니다 (INSERT).</summary>
        public async Task<SupabaseResult<bool>> RecordEventAsync(
            string accessToken,
            string accountId,
            string userId,
            string sessionId,
            string eventName)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
                return SupabaseResult<bool>.Fail("access_token_empty");

            if (string.IsNullOrWhiteSpace(eventName))
                return SupabaseResult<bool>.Fail("event_name_empty");

            var row = new AnalyticsEventInsert
            {
                account_id = accountId,
                user_id    = string.IsNullOrWhiteSpace(userId)    ? null : userId,
                session_id = string.IsNullOrWhiteSpace(sessionId) ? null : sessionId,
                event_name = eventName,
            };

            var url      = SupabaseRestTableRef.BuildTableUrl(_supabaseUrl, _table);
            var bodyJson = "[" + _jsonSerializer.ToJson(row) + "]";

            var response = await _httpClient.SendAsync(
                method: "POST",
                url: url,
                jsonBody: bodyJson,
                headers: CreateAuthHeaders(accessToken));

            if (response == null)
                return SupabaseResult<bool>.Fail("http_response_null");

            if (!response.IsSuccess)
                return SupabaseResult<bool>.Fail(response.ErrorMessage ?? response.Body ?? "event_record_failed");

            return SupabaseResult<bool>.Success(true);
        }

        private Dictionary<string, string> CreateAuthHeaders(string accessToken)
        {
            return new Dictionary<string, string>
            {
                { "apikey",        _publishableKey },
                { "Authorization", "Bearer " + accessToken },
                { "Content-Type",  "application/json" },
                { "Prefer",        "return=minimal" },
            };
        }
    }
}
