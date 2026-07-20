using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TrueBase.Core.Common;
using TrueBase.Core.Http;

namespace TrueBase.Core.Data
{
    /// <summary>
    /// 계정당 하나의 활성 세션 토큰(<c>user_sessions.session_token</c>)을 두어, 다른 기기에서 로그인하면 이전 기기에서 감지할 수 있게 합니다.
    /// </summary>
    public sealed class SupabaseUserSessionService
    {
        private readonly string _supabaseUrl;
        private readonly string _publishableKey;
        private readonly string _table;
        private readonly ISupabaseHttpClient _httpClient;
        private readonly ISupabaseJsonSerializer _jsonSerializer;

        public SupabaseUserSessionService(
            string supabaseUrl,
            string publishableKey,
            ISupabaseHttpClient httpClient,
            ISupabaseJsonSerializer jsonSerializer,
            string userSessionsTable = "user_sessions")
        {
            _supabaseUrl = supabaseUrl?.TrimEnd('/') ?? throw new ArgumentNullException(nameof(supabaseUrl));
            _publishableKey = publishableKey ?? throw new ArgumentNullException(nameof(publishableKey));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _jsonSerializer = jsonSerializer ?? throw new ArgumentNullException(nameof(jsonSerializer));
            _table = SupabaseRestTableRef.Normalize(userSessionsTable, nameof(userSessionsTable));
        }

        /// <summary>서버에 등록된 현재 계정의 세션 토큰을 조회합니다. 행이 없으면 성공 결과에 null을 담아 반환합니다.</summary>
        /// <param name="accessToken">현재 로그인 세션의 액세스 토큰. 필수.</param>
        /// <param name="accountId">현재 계정 ID(<c>auth.users.id</c>). 필수.</param>
        public async Task<SupabaseResult<string>> GetSessionTokenAsync(string accessToken, string accountId)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
                return SupabaseResult<string>.Fail(SupabaseErrorCode.AccessTokenEmpty);

            if (string.IsNullOrWhiteSpace(accountId))
                return SupabaseResult<string>.Fail("account_id_empty");

            var url =
                $"{SupabaseRestTableRef.BuildTableUrl(_supabaseUrl, _table)}" +
                $"?select=session_token" +
                $"&account_id=eq.{Uri.EscapeDataString(accountId.Trim())}" +
                $"&limit=1";

            var response = await _httpClient.SendAsync(
                method: "GET",
                url: url,
                jsonBody: null,
                headers: CreateAuthHeaders(accessToken, prefer: null));

            if (response == null)
                return SupabaseResult<string>.Fail(SupabaseErrorCode.NetworkError);

            if (response.IsSuccess == false)
                return SupabaseResult<string>.Fail(response.ErrorMessage ?? response.Body ?? "user_session_get_failed");

            try
            {
                var rows = _jsonSerializer.FromJsonArray<SessionTokenRow>(response.Body);
                if (rows == null || rows.Length == 0 || rows[0] == null || string.IsNullOrWhiteSpace(rows[0].session_token))
                    return SupabaseResult<string>.Success(null);

                return SupabaseResult<string>.Success(rows[0].session_token.Trim());
            }
            catch (Exception e)
            {
                return SupabaseResult<string>.Fail("user_session_parse_failed:" + e.Message);
            }
        }

        /// <summary>현재 계정의 세션 토큰을 upsert합니다(<c>on_conflict=account_id</c>). 이 기기의 토큰을 활성 세션으로 등록할 때 사용합니다.</summary>
        /// <param name="accessToken">현재 로그인 세션의 액세스 토큰. 필수.</param>
        /// <param name="accountId">현재 계정 ID(<c>auth.users.id</c>). 필수.</param>
        /// <param name="sessionToken">이 기기에서 발급한 세션 토큰. 필수.</param>
        public async Task<SupabaseResult<bool>> UpsertSessionTokenAsync(
            string accessToken,
            string accountId,
            string sessionToken)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
                return SupabaseResult<bool>.Fail(SupabaseErrorCode.AccessTokenEmpty);

            if (string.IsNullOrWhiteSpace(accountId))
                return SupabaseResult<bool>.Fail("account_id_empty");

            if (string.IsNullOrWhiteSpace(sessionToken))
                return SupabaseResult<bool>.Fail("session_token_empty");

            var url = $"{SupabaseRestTableRef.BuildTableUrl(_supabaseUrl, _table)}?on_conflict=account_id";

            var body = new UpsertSessionRow
            {
                account_id = accountId.Trim(),
                session_token = sessionToken.Trim(),
                updated_at = DateTime.UtcNow.ToString("o")
            };

            var singleJson = _jsonSerializer.ToJson(body);
            var bodyJson = "[" + singleJson + "]";

            var response = await _httpClient.SendAsync(
                method: "POST",
                url: url,
                jsonBody: bodyJson,
                headers: CreateAuthHeaders(accessToken, "resolution=merge-duplicates,return=minimal"));

            if (response == null)
                return SupabaseResult<bool>.Fail(SupabaseErrorCode.NetworkError);

            if (response.IsSuccess == false)
                return SupabaseResult<bool>.Fail(response.ErrorMessage ?? response.Body ?? "user_session_upsert_failed");

            return SupabaseResult<bool>.Success(true);
        }

        /// <summary>현재 계정의 세션 행을 삭제합니다(로그아웃 시 호출).</summary>
        /// <param name="accessToken">현재 로그인 세션의 액세스 토큰. 필수.</param>
        /// <param name="accountId">현재 계정 ID(<c>auth.users.id</c>). 필수.</param>
        public async Task<SupabaseResult<bool>> DeleteMySessionRowAsync(string accessToken, string accountId)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
                return SupabaseResult<bool>.Fail(SupabaseErrorCode.AccessTokenEmpty);

            if (string.IsNullOrWhiteSpace(accountId))
                return SupabaseResult<bool>.Fail("account_id_empty");

            var url =
                $"{SupabaseRestTableRef.BuildTableUrl(_supabaseUrl, _table)}" +
                $"?account_id=eq.{Uri.EscapeDataString(accountId.Trim())}";

            var response = await _httpClient.SendAsync(
                method: "DELETE",
                url: url,
                jsonBody: null,
                headers: CreateAuthHeaders(accessToken, "return=minimal"));

            if (response == null)
                return SupabaseResult<bool>.Fail(SupabaseErrorCode.NetworkError);

            if (response.IsSuccess == false)
                return SupabaseResult<bool>.Fail(response.ErrorMessage ?? response.Body ?? "user_session_delete_failed");

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

            if (string.IsNullOrEmpty(prefer) == false)
                headers["Prefer"] = prefer;

            return headers;
        }

        [Serializable]
        private sealed class SessionTokenRow
        {
            public string session_token;
        }

        [Serializable]
        private sealed class UpsertSessionRow
        {
            public string account_id;
            public string session_token;
            public string updated_at;
        }
    }
}
