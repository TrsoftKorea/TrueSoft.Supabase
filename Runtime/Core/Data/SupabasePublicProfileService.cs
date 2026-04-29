using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Truesoft.Supabase.Core.Common;
using Truesoft.Supabase.Core.Http;

namespace Truesoft.Supabase.Core.Data
{
    /// <summary>
    /// 공개 프로필(soft 탈퇴 시각, 활동 시각) 및 표시 이름(displayName) 관련 API.
    /// - DB: <c>profiles</c> (user_id / account_id / withdrawn_at / last_activity_at)
    /// - 표시 이름: Edge Function <c>displayname-get</c> (내부적으로 <c>display_names</c> 테이블 사용)
    /// - 유니크 강제: <c>display_names</c>의 unique index (<c>server_id</c>, lower(trim(display_name)))
    /// </summary>
    public sealed class SupabasePublicProfileService
    {
        private const int DisplayNameMaxLength = 64;

        /// <summary>PostgREST <c>IS NOT NULL</c> — 활성 행만 (<c>account_id</c>가 있는 프로필).</summary>
        private const string ActiveProfileFilter = "account_id=is.not_null";

        private readonly string _supabaseUrl;
        private readonly string _publishableKey;
        private readonly string _profilesTable;
        private readonly string _displayNamesTable;
        private readonly string _defaultServerCode;
        private readonly ISupabaseHttpClient _httpClient;
        private readonly ISupabaseJsonSerializer _jsonSerializer;

        /// <summary>EnsureMyProfileRowAsync 가 <c>game_servers</c>에서 한 번 조회한 기본 월드 id (캐시).</summary>
        private string _cachedDefaultGameServerId;

        public SupabasePublicProfileService(
            string supabaseUrl,
            string publishableKey,
            ISupabaseHttpClient httpClient,
            ISupabaseJsonSerializer jsonSerializer,
            string profilesTable = "user_profiles",
            string displayNamesTable = "display_names",
            string defaultServerCode = "GLOBAL")
        {
            _supabaseUrl = supabaseUrl?.TrimEnd('/') ?? throw new ArgumentNullException(nameof(supabaseUrl));
            _publishableKey = publishableKey ?? throw new ArgumentNullException(nameof(publishableKey));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _jsonSerializer = jsonSerializer ?? throw new ArgumentNullException(nameof(jsonSerializer));
            _profilesTable = SupabaseRestTableRef.Normalize(profilesTable, nameof(profilesTable));
            _displayNamesTable = SupabaseRestTableRef.Normalize(displayNamesTable, nameof(displayNamesTable));
            _defaultServerCode = string.IsNullOrWhiteSpace(defaultServerCode) ? "GLOBAL" : defaultServerCode.Trim();
        }

        /// <summary>
        /// 로그인 세션 기준(동일 서버)으로 표시 이름을 조회합니다.
        /// Edge Function <c>displayname-get</c>를 호출하며, <paramref name="playerUserId"/>는 <c>profiles.user_id</c>(안정 플레이어 id)입니다.
        /// </summary>
        public async Task<SupabaseResult<string>> GetDisplayNameAsync(string accessToken, string playerUserId)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
                return SupabaseResult<string>.Fail("access_token_empty");

            if (string.IsNullOrWhiteSpace(playerUserId))
                return SupabaseResult<string>.Fail("player_user_id_empty");

            var id = playerUserId.Trim();
            var url = $"{_supabaseUrl}/functions/v1/displayname-get";
            var bodyJson = _jsonSerializer.ToJson(new DisplayNameGetRequest { user_id = id });

            var response = await _httpClient.SendAsync(
                method: "POST",
                url: url,
                jsonBody: bodyJson,
                headers: CreateUserHeaders(accessToken, prefer: null));

            if (response == null)
                return SupabaseResult<string>.Fail("http_response_null");

            if (response.IsSuccess == false)
                return SupabaseResult<string>.Fail(response.ErrorMessage ?? response.Body ?? "public_profile_fetch_failed");

            try
            {
                var parsed = _jsonSerializer.FromJson<DisplayNameGetResponse>(response.Body);
                if (parsed == null || !parsed.ok)
                    return SupabaseResult<string>.Fail(string.IsNullOrWhiteSpace(parsed?.reason) ? "display_name_get_failed" : parsed.reason);

                return SupabaseResult<string>.Success(parsed.display_name ?? string.Empty);
            }
            catch (Exception e)
            {
                return SupabaseResult<string>.Fail("display_name_get_parse_exception:" + e.Message);
            }
        }

        /// <summary>
        /// displayName이 사용 가능한지 조회합니다(현재 로그인 서버 기준).
        /// 로그인 후 본인이 이미 같은 이름을 쓰는 경우(수정 화면 등)에는 <paramref name="ignoreAccountIdForSelf"/>에 현재 <c>auth.uid()</c>(세션의 사용자 id)를 넘기면 사용 가능으로 처리합니다.
        /// </summary>
        public async Task<SupabaseResult<bool>> IsDisplayNameAvailableAsync(
            string accessToken,
            string displayName,
            string ignoreAccountIdForSelf = null)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
                return SupabaseResult<bool>.Fail("access_token_empty");

            var norm = NormalizeDisplayName(displayName);
            if (norm.Length == 0)
                return SupabaseResult<bool>.Fail("display_name_empty");
            if (norm.Length > DisplayNameMaxLength)
                return SupabaseResult<bool>.Fail("display_name_too_long");

            var url =
                $"{SupabaseRestTableRef.BuildTableUrl(_supabaseUrl, _displayNamesTable)}" +
                $"?select=account_id" +
                $"&display_name=eq.{Uri.EscapeDataString(norm)}" +
                $"&limit=1";

            var response = await _httpClient.SendAsync(
                method: "GET",
                url: url,
                jsonBody: null,
                headers: CreateUserHeaders(accessToken, prefer: null));

            if (response == null)
                return SupabaseResult<bool>.Fail("http_response_null");

            if (response.IsSuccess == false)
                return SupabaseResult<bool>.Fail(response.ErrorMessage ?? response.Body ?? "display_name_check_failed");

            try
            {
                var rows = _jsonSerializer.FromJsonArray<AccountIdRow>(response.Body);
                if (rows == null || rows.Length == 0 || string.IsNullOrWhiteSpace(rows[0]?.account_id))
                    return SupabaseResult<bool>.Success(true);

                var holder = rows[0].account_id?.Trim() ?? string.Empty;
                var ignore = ignoreAccountIdForSelf?.Trim();
                if (string.IsNullOrWhiteSpace(ignore) == false
                    && string.Equals(holder, ignore, StringComparison.OrdinalIgnoreCase))
                    return SupabaseResult<bool>.Success(true);
                return SupabaseResult<bool>.Success(false);
            }
            catch (Exception e)
            {
                return SupabaseResult<bool>.Fail("display_name_check_parse_exception:" + e.Message);
            }
        }

        /// <summary>
        /// 본인 displayName 유니크 claim을 upsert 합니다(<c>display_names</c>).
        /// auth.user_metadata(displayName) 업데이트는 별도(<c>/auth/v1/user</c>).
        /// </summary>
        public async Task<SupabaseResult<bool>> UpsertMyDisplayNameClaimAsync(
            string accessToken,
            string accountId,
            string playerUserId,
            string displayName)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
                return SupabaseResult<bool>.Fail("access_token_empty");

            if (string.IsNullOrWhiteSpace(accountId))
                return SupabaseResult<bool>.Fail("account_id_empty");

            var stable = string.IsNullOrWhiteSpace(playerUserId) ? accountId.Trim() : playerUserId.Trim();
            var norm = NormalizeDisplayName(displayName);
            if (norm.Length == 0)
                return SupabaseResult<bool>.Fail("display_name_empty");

            if (norm.Length > DisplayNameMaxLength)
                return SupabaseResult<bool>.Fail("display_name_too_long");

            var url = $"{SupabaseRestTableRef.BuildTableUrl(_supabaseUrl, _displayNamesTable)}?on_conflict=account_id";
            var body = new UpsertDisplayNameRow
            {
                account_id = accountId.Trim(),
                user_id = stable,
                display_name = norm,
                updated_at = DateTime.UtcNow.ToString("o")
            };

            var singleJson = _jsonSerializer.ToJson(body);
            var bodyJson = "[" + singleJson + "]";

            var response = await _httpClient.SendAsync(
                method: "POST",
                url: url,
                jsonBody: bodyJson,
                headers: CreateUserHeaders(accessToken, "resolution=merge-duplicates,return=minimal"));

            if (response == null)
                return SupabaseResult<bool>.Fail("http_response_null");

            if (response.IsSuccess == false)
                return SupabaseResult<bool>.Fail(response.ErrorMessage ?? response.Body ?? "display_name_upsert_failed");

            return SupabaseResult<bool>.Success(true);
        }

        /// <summary>
        /// 공개 프로필 한 행을 조회합니다. <paramref name="playerUserId"/>는 <c>profiles.user_id</c>입니다.
        /// 행이 없으면 displayName·탈퇴 시각은 비어 있는 스냅샷을 반환합니다.
        /// </summary>
        public async Task<SupabaseResult<PublicProfileSnapshot>> GetProfileAsync(string accessToken, string playerUserId)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
                return SupabaseResult<PublicProfileSnapshot>.Fail("access_token_empty");

            if (string.IsNullOrWhiteSpace(playerUserId))
                return SupabaseResult<PublicProfileSnapshot>.Fail("player_user_id_empty");

            var id = playerUserId.Trim();
            var url =
                $"{SupabaseRestTableRef.BuildTableUrl(_supabaseUrl, _profilesTable)}" +
                $"?select=id,user_id,withdrawn_at" +
                $"&user_id=eq.{Uri.EscapeDataString(id)}" +
                $"&{ActiveProfileFilter}" +
                $"&limit=1";

            var response = await _httpClient.SendAsync(
                method: "GET",
                url: url,
                jsonBody: null,
                headers: CreateUserHeaders(accessToken, prefer: null));

            if (response == null)
                return SupabaseResult<PublicProfileSnapshot>.Fail("http_response_null");

            if (response.IsSuccess == false)
                return SupabaseResult<PublicProfileSnapshot>.Fail(response.ErrorMessage ?? response.Body ?? "public_profile_fetch_failed");

            try
            {
                var rows = _jsonSerializer.FromJsonArray<ProfileRowFull>(response.Body);
                if (rows == null || rows.Length == 0 || rows[0] == null)
                {
                    var displayNameWhenNoProfile = await GetDisplayNameAsync(accessToken, id);
                    var resolvedName = (displayNameWhenNoProfile != null && displayNameWhenNoProfile.IsSuccess)
                        ? (displayNameWhenNoProfile.Data ?? string.Empty)
                        : string.Empty;
                    return SupabaseResult<PublicProfileSnapshot>.Success(new PublicProfileSnapshot(string.Empty, id, resolvedName, null));
                }

                var row = rows[0];
                var w = row.withdrawn_at;
                if (string.IsNullOrWhiteSpace(w))
                    w = null;

                var rowId = string.IsNullOrWhiteSpace(row.id) ? string.Empty : row.id.Trim();
                var stable = string.IsNullOrWhiteSpace(row.user_id) ? id : row.user_id.Trim();
                var displayNameWhenProfile = await GetDisplayNameAsync(accessToken, stable);
                var displayName = (displayNameWhenProfile != null && displayNameWhenProfile.IsSuccess)
                    ? (displayNameWhenProfile.Data ?? string.Empty)
                    : string.Empty;
                return SupabaseResult<PublicProfileSnapshot>.Success(new PublicProfileSnapshot(rowId, stable, displayName, w));
            }
            catch (Exception e)
            {
                return SupabaseResult<PublicProfileSnapshot>.Fail("public_profile_parse_exception:" + e.Message);
            }
        }

        /// <summary>
        /// 로그인 직후, 현재 계정(account_id)에 대응하는 profiles 행이 항상 존재하도록 합니다.
        /// RPC <c>ts_ensure_my_profile</c>(SECURITY DEFINER)로 upsert하며, <c>withdrawn_at</c>은 null로 정리합니다.
        /// </summary>
        public async Task<SupabaseResult<bool>> EnsureMyProfileRowAsync(
            string accessToken,
            string accountId,
            string playerUserId)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
                return SupabaseResult<bool>.Fail("access_token_empty");

            if (string.IsNullOrWhiteSpace(accountId))
                return SupabaseResult<bool>.Fail("account_id_empty");

            var stable = string.IsNullOrWhiteSpace(playerUserId) ? accountId.Trim() : playerUserId.Trim();

            var url = $"{_supabaseUrl.TrimEnd('/')}/rest/v1/rpc/ts_ensure_my_profile";
            var bodyJson = _jsonSerializer.ToJson(new EnsureMyProfileRpcBody { p_user_id = stable });

            var response = await _httpClient.SendAsync(
                method: "POST",
                url: url,
                jsonBody: bodyJson,
                headers: CreateUserHeaders(accessToken, prefer: null));

            if (response == null)
                return SupabaseResult<bool>.Fail("http_response_null");

            if (response.IsSuccess == false)
            {
                var detail = string.IsNullOrWhiteSpace(response.Body) == false
                    ? response.Body.Trim()
                    : (response.ErrorMessage ?? "profile_ensure_rpc_failed");
                return SupabaseResult<bool>.Fail(detail);
            }

            return SupabaseResult<bool>.Success(true);
        }

        /// <summary>
        /// <c>game_servers</c> 공개 SELECT(RLS)로 <see cref="_defaultServerCode"/>에 해당하는 <c>id</c>를 한 번 조회해 캐시합니다.
        /// (레거시/기타 REST 경로용. <see cref="EnsureMyProfileRowAsync"/> 는 <c>ts_ensure_my_profile</c> RPC를 사용합니다.)
        /// </summary>

        private static string ExtractFirstUuidFromJson(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return null;

            var m = Regex.Match(
                body.Trim(),
                @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
            return m.Success ? m.Value : null;
        }

        /// <summary>현재 로그인 계정의 서버 식별자를 조회합니다.</summary>
        public async Task<SupabaseResult<MyServerInfo>> GetMyServerIdAsync(string accessToken)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
                return SupabaseResult<MyServerInfo>.Fail("access_token_empty");

            var url = $"{_supabaseUrl}/rest/v1/rpc/ts_my_server_id";
            var response = await _httpClient.SendAsync(
                method: "POST",
                url: url,
                jsonBody: "{}",
                headers: CreateUserHeaders(accessToken, prefer: null));

            if (response == null)
                return SupabaseResult<MyServerInfo>.Fail("http_response_null");

            if (response.IsSuccess == false)
                return SupabaseResult<MyServerInfo>.Fail(response.ErrorMessage ?? response.Body ?? "my_server_fetch_failed");

            try
            {
                var rows = _jsonSerializer.FromJsonArray<MyServerInfoRow>(response.Body);
                if (rows == null || rows.Length == 0 || rows[0] == null || string.IsNullOrWhiteSpace(rows[0].server_id))
                    return SupabaseResult<MyServerInfo>.Fail("my_server_not_found");

                return SupabaseResult<MyServerInfo>.Success(new MyServerInfo(
                    rows[0].server_id.Trim(),
                    string.IsNullOrWhiteSpace(rows[0].server_code) ? _defaultServerCode : rows[0].server_code.Trim()));
            }
            catch (Exception e)
            {
                return SupabaseResult<MyServerInfo>.Fail("my_server_parse_exception:" + e.Message);
            }
        }

        /// <summary>현재 로그인 계정을 지정 서버 코드로 이주시킵니다.</summary>
        public async Task<SupabaseResult<bool>> TransferMyServerAsync(string accessToken, string targetServerCode, string reason = null)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
                return SupabaseResult<bool>.Fail("access_token_empty");
            if (string.IsNullOrWhiteSpace(targetServerCode))
                return SupabaseResult<bool>.Fail("target_server_code_empty");

            var url = $"{_supabaseUrl}/rest/v1/rpc/ts_transfer_my_server";
            var body = _jsonSerializer.ToJson(new TransferServerRequest
            {
                p_target_server_code = targetServerCode.Trim(),
                p_reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim()
            });

            var response = await _httpClient.SendAsync(
                method: "POST",
                url: url,
                jsonBody: body,
                headers: CreateUserHeaders(accessToken, prefer: null));

            if (response == null)
                return SupabaseResult<bool>.Fail("http_response_null");
            if (response.IsSuccess == false)
                return SupabaseResult<bool>.Fail(response.ErrorMessage ?? response.Body ?? "server_transfer_failed");

            try
            {
                var rows = _jsonSerializer.FromJsonArray<TransferServerRow>(response.Body);
                if (rows == null || rows.Length == 0 || rows[0] == null)
                    return SupabaseResult<bool>.Fail("server_transfer_result_empty");
                if (!rows[0].ok)
                    return SupabaseResult<bool>.Fail(string.IsNullOrWhiteSpace(rows[0].reason) ? "server_transfer_failed" : rows[0].reason.Trim());
                return SupabaseResult<bool>.Success(true);
            }
            catch (Exception e)
            {
                return SupabaseResult<bool>.Fail("server_transfer_parse_exception:" + e.Message);
            }
        }

        /// <summary>
        /// 본인 행의 <c>withdrawn_at</c>만 갱신합니다. <paramref name="withdrawnAtIso"/>가 null/빈 문자열이면 SQL NULL로 지웁니다(탈퇴 표시 해제).
        /// </summary>
        public async Task<SupabaseResult<bool>> PatchMyWithdrawnAtAsync(
            string accessToken,
            string accountId,
            string withdrawnAtIso)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
                return SupabaseResult<bool>.Fail("access_token_empty");

            if (string.IsNullOrWhiteSpace(accountId))
                return SupabaseResult<bool>.Fail("account_id_empty");

            var url =
                $"{SupabaseRestTableRef.BuildTableUrl(_supabaseUrl, _profilesTable)}" +
                $"?account_id=eq.{Uri.EscapeDataString(accountId.Trim())}";

            string jsonBody;
            if (string.IsNullOrWhiteSpace(withdrawnAtIso))
                jsonBody = "{\"withdrawn_at\":null}";
            else
                jsonBody = _jsonSerializer.ToJson(new WithdrawnPatchBody { withdrawn_at = withdrawnAtIso.Trim() });

            var response = await _httpClient.SendAsync(
                method: "PATCH",
                url: url,
                jsonBody: jsonBody,
                headers: CreateUserHeaders(accessToken, "return=minimal"));

            if (response == null)
                return SupabaseResult<bool>.Fail("http_response_null");

            if (response.IsSuccess == false)
                return SupabaseResult<bool>.Fail(response.ErrorMessage ?? response.Body ?? "withdrawn_at_patch_failed");

            return SupabaseResult<bool>.Success(true);
        }

        /// <summary>
        /// 본인 행의 <c>last_activity_at</c>을 현재 시각으로 갱신합니다. Retool 운영 대시보드 모니터링용.
        /// </summary>
        public async Task<SupabaseResult<bool>> PatchMyLastActivityAtAsync(
            string accessToken,
            string accountId)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
                return SupabaseResult<bool>.Fail("access_token_empty");

            if (string.IsNullOrWhiteSpace(accountId))
                return SupabaseResult<bool>.Fail("account_id_empty");

            var url =
                $"{SupabaseRestTableRef.BuildTableUrl(_supabaseUrl, _profilesTable)}" +
                $"?account_id=eq.{Uri.EscapeDataString(accountId.Trim())}";

            var jsonBody = _jsonSerializer.ToJson(new LastActivityAtPatchBody { last_activity_at = DateTime.UtcNow.ToString("o") });

            var response = await _httpClient.SendAsync(
                method: "PATCH",
                url: url,
                jsonBody: jsonBody,
                headers: CreateUserHeaders(accessToken, "return=minimal"));

            if (response == null)
                return SupabaseResult<bool>.Fail("http_response_null");

            if (response.IsSuccess == false)
                return SupabaseResult<bool>.Fail(response.ErrorMessage ?? response.Body ?? "last_activity_at_patch_failed");

            return SupabaseResult<bool>.Success(true);
        }

        /// <summary>
        /// 서버에서 현재 시각 기준으로 유예 기간(일) 뒤의 <c>withdrawn_at</c>을 계산해 예약합니다.
        /// RPC: <c>ts_request_withdrawal</c>.
        /// </summary>
        public async Task<SupabaseResult<string>> RequestMyWithdrawalByDelayDaysAsync(
            string accessToken,
            int delayDays)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
                return SupabaseResult<string>.Fail("access_token_empty");

            if (delayDays < 0)
                delayDays = 0;

            var url = $"{_supabaseUrl}/rest/v1/rpc/ts_request_withdrawal";
            var bodyJson = _jsonSerializer.ToJson(new WithdrawalRequestBody
            {
                p_delay_days = delayDays
            });

            var response = await _httpClient.SendAsync(
                method: "POST",
                url: url,
                jsonBody: bodyJson,
                headers: CreateUserHeaders(accessToken, prefer: null));

            if (response == null)
                return SupabaseResult<string>.Fail("http_response_null");

            if (response.IsSuccess == false)
                return SupabaseResult<string>.Fail(response.ErrorMessage ?? response.Body ?? "withdrawal_request_failed");

            try
            {
                var rows = _jsonSerializer.FromJsonArray<WithdrawalRequestRow>(response.Body);
                if (rows == null || rows.Length == 0 || rows[0] == null || string.IsNullOrWhiteSpace(rows[0].scheduled_at))
                    return SupabaseResult<string>.Fail("withdrawal_request_scheduled_at_empty");

                return SupabaseResult<string>.Success(rows[0].scheduled_at.Trim());
            }
            catch (Exception e)
            {
                return SupabaseResult<string>.Fail("withdrawal_request_parse_exception:" + e.Message);
            }
        }

        /// <summary>
        /// 로그인한 본인의 탈퇴 예약 게이트 상태를 조회합니다.
        /// RPC: <c>ts_my_withdrawal_status</c>.
        /// </summary>
        public async Task<SupabaseResult<MyWithdrawalStatus>> GetMyWithdrawalStatusAsync(string accessToken)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
                return SupabaseResult<MyWithdrawalStatus>.Fail("access_token_empty");

            var url = $"{_supabaseUrl}/rest/v1/rpc/ts_my_withdrawal_status";

            var response = await _httpClient.SendAsync(
                method: "POST",
                url: url,
                jsonBody: "{}",
                headers: CreateUserHeaders(accessToken, prefer: null));

            if (response == null)
                return SupabaseResult<MyWithdrawalStatus>.Fail("http_response_null");

            if (response.IsSuccess == false)
                return SupabaseResult<MyWithdrawalStatus>.Fail(response.ErrorMessage ?? response.Body ?? "withdrawal_status_failed");

            try
            {
                var rows = _jsonSerializer.FromJsonArray<WithdrawalStatusRow>(response.Body);
                if (rows == null || rows.Length == 0 || rows[0] == null)
                    return SupabaseResult<MyWithdrawalStatus>.Success(new MyWithdrawalStatus(string.Empty, null, null, false, 0));

                var row = rows[0];
                var status = new MyWithdrawalStatus(
                    row.display_name ?? string.Empty,
                    string.IsNullOrWhiteSpace(row.withdrawn_at) ? null : row.withdrawn_at.Trim(),
                    string.IsNullOrWhiteSpace(row.server_now) ? null : row.server_now.Trim(),
                    row.is_scheduled,
                    row.seconds_remaining);

                return SupabaseResult<MyWithdrawalStatus>.Success(status);
            }
            catch (Exception e)
            {
                return SupabaseResult<MyWithdrawalStatus>.Fail("withdrawal_status_parse_exception:" + e.Message);
            }
        }

        private static string NormalizeDisplayName(string displayName)
        {
            return displayName == null ? string.Empty : displayName.Trim();
        }

        private Dictionary<string, string> CreateUserHeaders(string accessToken, string prefer)
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
        private sealed class AccountIdRow
        {
            public string account_id;
        }

        [Serializable]
        private sealed class UpsertDisplayNameRow
        {
            public string account_id;
            public string user_id;
            public string display_name;
            public string updated_at;
        }

        [Serializable]
        private sealed class ProfileRowFull
        {
            public string id;
            public string user_id;
            public string withdrawn_at;
        }

        [Serializable]
        private sealed class DisplayNameGetRequest
        {
            public string user_id;
        }

        [Serializable]
        private sealed class MyServerInfoRow
        {
            public string server_id;
            public string server_code;
        }

        [Serializable]
        private sealed class TransferServerRequest
        {
            public string p_target_server_code;
            public string p_reason;
        }

        [Serializable]
        private sealed class TransferServerRow
        {
            public bool ok;
            public string reason;
            public string target_server_id;
        }

        [Serializable]
        private sealed class DisplayNameGetResponse
        {
            public bool ok;
            public string display_name;
            public string reason;
        }

        [Serializable]
        private sealed class EnsureMyProfileRpcBody
        {
            public string p_user_id;
        }

        private sealed class GameServerIdRow
        {
            public string id;
        }

        [Serializable]
        private sealed class WithdrawnPatchBody
        {
            public string withdrawn_at;
        }

        [Serializable]
        private sealed class LastActivityAtPatchBody
        {
            public string last_activity_at;
        }

        [Serializable]
        private sealed class WithdrawalRequestBody
        {
            public int p_delay_days;
        }

        [Serializable]
        private sealed class WithdrawalRequestRow
        {
            public string scheduled_at;
        }

        [Serializable]
        private sealed class WithdrawalStatusRow
        {
            public string display_name;
            public string withdrawn_at;
            public string server_now;
            public bool is_scheduled;
            public long seconds_remaining;
        }
    }
}
