using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using TrueBase.Core.Common;
using TrueBase.Core.Http;

namespace TrueBase.Core.Data
{
    /// <summary>
    /// Postgres RPC <c>ts_server_now</c>로 서버 기준 시각을 조회합니다. 로그인 없이 Publishable 키로 호출 가능합니다.
    /// </summary>
    public sealed class SupabaseServerTimeService
    {
        private readonly string _supabaseUrl;
        private readonly string _publishableKey;
        private readonly ISupabaseHttpClient _httpClient;
        private readonly ISupabaseJsonSerializer _jsonSerializer;

        public SupabaseServerTimeService(
            string supabaseUrl,
            string publishableKey,
            ISupabaseHttpClient httpClient,
            ISupabaseJsonSerializer jsonSerializer)
        {
            _supabaseUrl = supabaseUrl?.TrimEnd('/') ?? throw new ArgumentNullException(nameof(supabaseUrl));
            _publishableKey = publishableKey?.Trim() ?? throw new ArgumentNullException(nameof(publishableKey));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _jsonSerializer = jsonSerializer ?? throw new ArgumentNullException(nameof(jsonSerializer));
        }

        /// <summary>서버 시각을 UTC <see cref="DateTime"/>으로 반환합니다.</summary>
        public async Task<SupabaseResult<DateTime>> GetServerUtcNowAsync()
        {
            var url = $"{_supabaseUrl}/rest/v1/rpc/ts_server_now";
            const string bodyJson = "{}";

            var response = await _httpClient.SendAsync(
                method: "POST",
                url: url,
                jsonBody: bodyJson,
                headers: CreatePublishableKeyHeaders());

            if (response == null)
                return SupabaseResult<DateTime>.Fail("http_response_null");

            if (response.IsSuccess == false)
                return SupabaseResult<DateTime>.Fail(response.ErrorMessage ?? response.Body ?? "server_time_fetch_failed");

            try
            {
                var rows = _jsonSerializer.FromJsonArray<ServerTimeRow>(response.Body);
                if (rows == null || rows.Length == 0 || rows[0] == null)
                    return SupabaseResult<DateTime>.Fail("server_time_empty");

                var raw = rows[0].server_time;
                if (string.IsNullOrWhiteSpace(raw))
                    return SupabaseResult<DateTime>.Fail("server_time_parse_empty");

                if (DateTimeOffset.TryParse(
                        raw.Trim(),
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                        out var dto))
                    return SupabaseResult<DateTime>.Success(dto.UtcDateTime);

                if (DateTime.TryParse(raw.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
                    return SupabaseResult<DateTime>.Success(DateTime.SpecifyKind(dt.ToUniversalTime(), DateTimeKind.Utc));

                return SupabaseResult<DateTime>.Fail("server_time_parse_failed");
            }
            catch (Exception e)
            {
                return SupabaseResult<DateTime>.Fail("server_time_parse_failed:" + e.Message);
            }
        }

        private Dictionary<string, string> CreatePublishableKeyHeaders()
        {
            return new Dictionary<string, string>
            {
                { "apikey", _publishableKey },
                { "Content-Type", "application/json" }
            };
        }

        [Serializable]
        private sealed class ServerTimeRow
        {
            public string server_time;
        }
    }
}
