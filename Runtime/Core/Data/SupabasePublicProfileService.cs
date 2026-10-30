using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using TrueBase.Core.Common;
using TrueBase.Core.Http;

namespace TrueBase.Core.Data
{
    /// <summary>
    /// 공개 프로필(soft 탈퇴 시각, 활동 시각) 및 표시 이름(displayName) 관련 API.
    /// - DB: 프로필 테이블(기본 <c>user_profiles</c>) — user_id / account_id / withdrawn_at / last_activity_at
    /// - 표시 이름: Edge Function <c>displayname-get</c> (내부적으로 <c>display_names</c> 테이블 사용)
    /// - 유니크 강제: <c>display_names</c>의 unique index (<c>server_id</c>, lower(trim(display_name)))
    /// </summary>
    public sealed class SupabasePublicProfileService
    {
        private const int NameMaxLength = 64;

        /// <summary><see cref="AfterSignInAsync"/> 가 DB 에 <c>ts_after_sign_in</c> 이 없을 때 돌려주는 사유. SDK 내부에서만 쓴다.</summary>
        internal const string AfterSignInRpcMissing = "after_sign_in_rpc_missing";

        /// <summary>PostgREST <c>IS NOT NULL</c> — 활성 행만 (<c>account_id</c>가 있는 프로필).</summary>
        private const string ActiveProfileFilter = "account_id=is.not_null";

        private readonly string _supabaseUrl;
        private readonly string _publishableKey;
        private readonly string _profilesTable;
        private readonly string _defaultServerCode;
        private readonly ISupabaseHttpClient _httpClient;
        private readonly ISupabaseJsonSerializer _jsonSerializer;

        public SupabasePublicProfileService(
            string supabaseUrl,
            string publishableKey,
            ISupabaseHttpClient httpClient,
            ISupabaseJsonSerializer jsonSerializer,
            string profilesTable = "user_profiles",
            string defaultServerCode = "GLOBAL")
        {
            _supabaseUrl = supabaseUrl?.TrimEnd('/') ?? throw new ArgumentNullException(nameof(supabaseUrl));
            _publishableKey = publishableKey ?? throw new ArgumentNullException(nameof(publishableKey));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _jsonSerializer = jsonSerializer ?? throw new ArgumentNullException(nameof(jsonSerializer));
            _profilesTable = SupabaseRestTableRef.Normalize(profilesTable, nameof(profilesTable));
            _defaultServerCode = string.IsNullOrWhiteSpace(defaultServerCode) ? "GLOBAL" : defaultServerCode.Trim();
        }

        /// <summary>
        /// 로그인 세션 기준(동일 서버)으로 표시 이름을 조회합니다. Edge Function <c>displayname-get</c>.
        /// </summary>
        /// <param name="accessToken">현재 로그인 세션의 액세스 토큰. 필수.</param>
        /// <param name="playerUserId">조회 대상의 안정 플레이어 id(<c>profiles.user_id</c>). 필수 — 본인 외 다른 플레이어도 조회 가능합니다.</param>
        public async Task<SupabaseResult<string>> GetNameAsync(string accessToken, string playerUserId)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
                return SupabaseResult<string>.Fail(SupabaseErrorCode.AccessTokenEmpty);

            if (string.IsNullOrWhiteSpace(playerUserId))
                return SupabaseResult<string>.Fail("player_user_id_empty");

            var id = playerUserId.Trim();
            var url = $"{_supabaseUrl}/functions/v1/displayname-get";
            var bodyJson = _jsonSerializer.ToJson(new NameGetRequest { user_id = id });

            var response = await _httpClient.SendAsync(
                method: "POST",
                url: url,
                jsonBody: bodyJson,
                headers: CreateUserHeaders(accessToken, prefer: null));

            if (response == null)
                return SupabaseResult<string>.Fail(SupabaseErrorCode.NetworkError);

            if (response.IsSuccess == false)
                return SupabaseResult<string>.Fail(SupabaseRestHelpers.ExtractRpcErrorCode(response.Body, response.ErrorMessage, "public_profile_fetch"));

            try
            {
                var parsed = _jsonSerializer.FromJson<NameGetResponse>(response.Body);
                if (parsed == null || !parsed.ok)
                    return SupabaseResult<string>.Fail(string.IsNullOrWhiteSpace(parsed?.reason) ? "display_name_get_failed" : parsed.reason);

                return SupabaseResult<string>.Success(parsed.display_name ?? string.Empty);
            }
            catch (Exception e)
            {
                return SupabaseResult<string>.Fail("display_name_get_parse_failed:" + e.Message);
            }
        }

        /// <summary>
        /// displayName이 사용 가능한지 조회합니다(전역 기준). RPC <c>ts_display_name_check</c>.
        /// 중복이면 <c>false</c>, 금칙어·금지 닉네임이면 <see cref="SupabaseErrorCode.NameForbidden"/> 실패입니다.
        /// </summary>
        /// <param name="accessToken">현재 로그인 세션의 액세스 토큰. 필수.</param>
        /// <param name="displayName">검사할 표시 이름. 앞뒤 공백 제거 후 최대 64자.</param>
        /// <param name="ignoreAccountIdForSelf">본인이 이미 같은 이름을 쓰는 경우(수정 화면 등) 현재 <c>auth.uid()</c>를 넘기면 그 계정의 기존 이름은 사용 가능으로 처리됩니다(기본값: null).</param>
        public async Task<SupabaseResult<bool>> IsNameAvailableAsync(
            string accessToken,
            string displayName,
            string ignoreAccountIdForSelf = null)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
                return SupabaseResult<bool>.Fail(SupabaseErrorCode.AccessTokenEmpty);

            var norm = NormalizeName(displayName);
            if (norm.Length == 0)
                return SupabaseResult<bool>.Fail("display_name_empty");
            if (norm.Length > NameMaxLength)
                return SupabaseResult<bool>.Fail(SupabaseErrorCode.NameTooLong);

            // SECURITY DEFINER RPC 사용: RLS 우회, DB 내부에서 server_id 직접 조회
            // → REST + RLS 방식의 server_id 불일치 문제 원천 차단
            // JsonUtility 직렬화(IL2CPP private 중첩 클래스 → {} 반환 가능)에 의존하지 않고 직접 JSON 구성
            var ignoreUuid = string.IsNullOrWhiteSpace(ignoreAccountIdForSelf) ? null : ignoreAccountIdForSelf.Trim();
            var bodyJson = BuildIsNameAvailableBody(norm, ignoreUuid);

            var url = $"{_supabaseUrl.TrimEnd('/')}/rest/v1/rpc/ts_display_name_check";
            var response = await _httpClient.SendAsync(
                method: "POST",
                url: url,
                jsonBody: bodyJson,
                headers: CreateUserHeaders(accessToken, prefer: null));

            if (response == null)
                return SupabaseResult<bool>.Fail(SupabaseErrorCode.NetworkError);

            if (response.IsSuccess == false)
                return SupabaseResult<bool>.Fail(SupabaseRestHelpers.ExtractRpcErrorCode(response.Body, response.ErrorMessage, "display_name_check"));

            // RPC가 text 를 JSON 문자열로 반환 → "\"available\"" / "\"taken\"" / "\"forbidden\""
            switch (response.Body?.Trim().Trim('"'))
            {
                case "available": return SupabaseResult<bool>.Success(true);
                case "taken": return SupabaseResult<bool>.Success(false);
                case "forbidden": return SupabaseResult<bool>.Fail(SupabaseErrorCode.NameForbidden);
                default: return SupabaseResult<bool>.Fail("display_name_check_parse_failed");
            }
        }

        /// <summary>
        /// 공개 프로필 한 행을 조회합니다. 행이 없으면 displayName·탈퇴 시각이 비어 있는 스냅샷을 반환합니다(실패 아님).
        /// </summary>
        /// <param name="accessToken">현재 로그인 세션의 액세스 토큰. 필수.</param>
        /// <param name="playerUserId">조회 대상의 안정 플레이어 id(<c>profiles.user_id</c>). 필수.</param>
        public async Task<SupabaseResult<PublicProfile>> GetProfileAsync(string accessToken, string playerUserId)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
                return SupabaseResult<PublicProfile>.Fail(SupabaseErrorCode.AccessTokenEmpty);

            if (string.IsNullOrWhiteSpace(playerUserId))
                return SupabaseResult<PublicProfile>.Fail("player_user_id_empty");

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
                return SupabaseResult<PublicProfile>.Fail(SupabaseErrorCode.NetworkError);

            if (response.IsSuccess == false)
                return SupabaseResult<PublicProfile>.Fail(SupabaseRestHelpers.ExtractRpcErrorCode(response.Body, response.ErrorMessage, "public_profile_fetch"));

            try
            {
                var rows = _jsonSerializer.FromJsonArray<ProfileRowFull>(response.Body);
                string rowId;
                string userIdForName;
                string withdrawnAt;

                if (rows == null || rows.Length == 0 || rows[0] == null)
                {
                    // 프로필 행 없음: id만 사용
                    rowId = string.Empty;
                    userIdForName = id;
                    withdrawnAt = null;
                }
                else
                {
                    // 프로필 행 존재
                    var row = rows[0];
                    rowId = string.IsNullOrWhiteSpace(row.id) ? string.Empty : row.id.Trim();
                    userIdForName = string.IsNullOrWhiteSpace(row.user_id) ? id : row.user_id.Trim();
                    withdrawnAt = string.IsNullOrWhiteSpace(row.withdrawn_at) ? null : row.withdrawn_at;
                }

                // displayName을 한 번만 조회
                var displayNameResult = await GetNameAsync(accessToken, userIdForName);
                var displayName = (displayNameResult != null && displayNameResult.IsSuccess)
                    ? (displayNameResult.Data ?? string.Empty)
                    : string.Empty;

                return SupabaseResult<PublicProfile>.Success(
                    new PublicProfile(rowId, userIdForName, displayName, withdrawnAt));
            }
            catch (Exception e)
            {
                return SupabaseResult<PublicProfile>.Fail("public_profile_parse_failed:" + e.Message);
            }
        }

        /// <summary>
        /// 로그인 직후, 현재 계정(account_id)에 대응하는 profiles 행이 항상 존재하도록 합니다.
        /// RPC <c>ts_ensure_my_profile</c>(SECURITY DEFINER)로 upsert합니다. 탈퇴 예약(<c>withdrawn_at</c>)은 건드리지 않습니다 — 취소는 취소 토큰으로만 합니다.
        /// </summary>
        /// <param name="accessToken">현재 로그인 세션의 액세스 토큰. 필수.</param>
        /// <param name="accountId">현재 계정 ID(<c>auth.users.id</c>). 필수.</param>
        /// <param name="playerUserId">안정 플레이어 id(<c>profiles.user_id</c>). null·공백이면 <paramref name="accountId"/>를 대신 사용합니다.</param>
        /// <param name="platform">접속 플랫폼 문자열(예: <c>"Android"</c>). null이면 RPC에 null로 전달됩니다(기본값: null).</param>
        public async Task<SupabaseResult<bool>> EnsureMyProfileRowAsync(
            string accessToken,
            string accountId,
            string playerUserId,
            string platform = null)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
                return SupabaseResult<bool>.Fail(SupabaseErrorCode.AccessTokenEmpty);

            if (string.IsNullOrWhiteSpace(accountId))
                return SupabaseResult<bool>.Fail("account_id_empty");

            var stable = string.IsNullOrWhiteSpace(playerUserId) ? accountId.Trim() : playerUserId.Trim();

            var url = $"{_supabaseUrl.TrimEnd('/')}/rest/v1/rpc/ts_ensure_my_profile";
            var bodyJson = _jsonSerializer.ToJson(new EnsureMyProfileRpcBody { p_user_id = stable, p_platform = platform });

            var response = await _httpClient.SendAsync(
                method: "POST",
                url: url,
                jsonBody: bodyJson,
                headers: CreateUserHeaders(accessToken, prefer: null));

            if (response == null)
                return SupabaseResult<bool>.Fail(SupabaseErrorCode.NetworkError);

            if (response.IsSuccess == false)
                return SupabaseResult<bool>.Fail(SupabaseRestHelpers.ExtractRpcErrorCode(response.Body, response.ErrorMessage, "profile_ensure_rpc"));

            return SupabaseResult<bool>.Success(true);
        }

        /// <summary>
        /// 로그인 직후 단계(탈퇴 예약 확인·프로필 보장·내 프로필·닉네임·서버 코드)를 한 번 왕복으로 처리합니다. RPC <c>ts_after_sign_in</c>.
        /// 탈퇴 예약이 있으면 프로필을 건드리지 않고 <see cref="AfterSignInSnapshot.WithdrawnAtIso"/>만 채워 돌려줍니다.
        /// DB 에 함수가 없으면 <see cref="AfterSignInRpcMissing"/> 으로 실패합니다.
        /// </summary>
        internal async Task<SupabaseResult<AfterSignInSnapshot>> AfterSignInAsync(
            string accessToken,
            string accountId,
            string playerUserId,
            string platform)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
                return SupabaseResult<AfterSignInSnapshot>.Fail(SupabaseErrorCode.AccessTokenEmpty);

            if (string.IsNullOrWhiteSpace(accountId))
                return SupabaseResult<AfterSignInSnapshot>.Fail("account_id_empty");

            var stable = string.IsNullOrWhiteSpace(playerUserId) ? accountId.Trim() : playerUserId.Trim();
            var url = $"{_supabaseUrl.TrimEnd('/')}/rest/v1/rpc/ts_after_sign_in";
            var bodyJson = _jsonSerializer.ToJson(new EnsureMyProfileRpcBody { p_user_id = stable, p_platform = platform });

            var response = await _httpClient.SendAsync(
                method: "POST",
                url: url,
                jsonBody: bodyJson,
                headers: CreateUserHeaders(accessToken, prefer: null));

            if (response == null)
                return SupabaseResult<AfterSignInSnapshot>.Fail(SupabaseErrorCode.NetworkError);

            // RPC 엔드포인트의 404 는 함수가 없다는 뜻이다 — SDK 만 먼저 올리고 install.sql 을 다시 돌리지 않은 프로젝트.
            if (response.StatusCode == 404)
                return SupabaseResult<AfterSignInSnapshot>.Fail(AfterSignInRpcMissing);

            if (response.IsSuccess == false)
                return SupabaseResult<AfterSignInSnapshot>.Fail(SupabaseRestHelpers.ExtractRpcErrorCode(response.Body, response.ErrorMessage, "after_sign_in"));

            try
            {
                var parsed = _jsonSerializer.FromJson<AfterSignInRow>(response.Body);
                if (parsed == null)
                    return SupabaseResult<AfterSignInSnapshot>.Fail("after_sign_in_empty");

                var withdrawnAt = string.IsNullOrWhiteSpace(parsed.withdrawn_at) ? null : parsed.withdrawn_at.Trim();
                PublicProfile profile = null;
                if (parsed.profile != null)
                {
                    profile = new PublicProfile(
                        parsed.profile.id?.Trim(),
                        string.IsNullOrWhiteSpace(parsed.profile.user_id) ? stable : parsed.profile.user_id.Trim(),
                        parsed.profile.display_name,
                        null,
                        parsed.profile.server_code?.Trim());
                }

                return SupabaseResult<AfterSignInSnapshot>.Success(new AfterSignInSnapshot(withdrawnAt, profile));
            }
            catch (Exception e)
            {
                return SupabaseResult<AfterSignInSnapshot>.Fail("after_sign_in_parse_failed:" + e.Message);
            }
        }

        /// <summary>현재 로그인 계정의 서버 식별자를 조회합니다. RPC <c>ts_my_server_id</c>.</summary>
        /// <param name="accessToken">현재 로그인 세션의 액세스 토큰. 필수.</param>
        public async Task<SupabaseResult<ServerInfo>> GetMyServerIdAsync(string accessToken)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
                return SupabaseResult<ServerInfo>.Fail(SupabaseErrorCode.AccessTokenEmpty);

            var url = $"{_supabaseUrl}/rest/v1/rpc/ts_my_server_id";
            var response = await _httpClient.SendAsync(
                method: "POST",
                url: url,
                jsonBody: "{}",
                headers: CreateUserHeaders(accessToken, prefer: null));

            if (response == null)
                return SupabaseResult<ServerInfo>.Fail(SupabaseErrorCode.NetworkError);

            if (response.IsSuccess == false)
                return SupabaseResult<ServerInfo>.Fail(SupabaseRestHelpers.ExtractRpcErrorCode(response.Body, response.ErrorMessage, "my_server_fetch"));

            try
            {
                var rows = _jsonSerializer.FromJsonArray<ServerInfoRow>(response.Body);
                if (rows == null || rows.Length == 0 || rows[0] == null || string.IsNullOrWhiteSpace(rows[0].server_id))
                    return SupabaseResult<ServerInfo>.Fail("my_server_not_found");

                return SupabaseResult<ServerInfo>.Success(new ServerInfo(
                    rows[0].server_id.Trim(),
                    string.IsNullOrWhiteSpace(rows[0].server_code) ? _defaultServerCode : rows[0].server_code.Trim()));
            }
            catch (Exception e)
            {
                return SupabaseResult<ServerInfo>.Fail("my_server_parse_failed:" + e.Message);
            }
        }

        /// <summary>현재 로그인 계정을 지정 서버 코드로 이주시킵니다. RPC <c>ts_transfer_my_server</c>.</summary>
        /// <param name="accessToken">현재 로그인 세션의 액세스 토큰. 필수.</param>
        /// <param name="targetServerCode">이동할 서버 코드(예: <c>KR1</c>). 필수.</param>
        /// <param name="reason">이주 사유(감사 로그용). null·공백이면 전달하지 않습니다(기본값: null).</param>
        public async Task<SupabaseResult<bool>> TransferServerAsync(string accessToken, string targetServerCode, string reason = null)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
                return SupabaseResult<bool>.Fail(SupabaseErrorCode.AccessTokenEmpty);
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
                return SupabaseResult<bool>.Fail(SupabaseErrorCode.NetworkError);
            if (response.IsSuccess == false)
                return SupabaseResult<bool>.Fail(SupabaseRestHelpers.ExtractRpcErrorCode(response.Body, response.ErrorMessage, "server_transfer"));

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
                return SupabaseResult<bool>.Fail("server_transfer_parse_failed:" + e.Message);
            }
        }

        /// <summary>
        /// 서버에서 현재 시각 기준으로 유예 기간(일) 뒤의 <c>withdrawn_at</c>을 계산해 예약합니다.
        /// RPC: <c>ts_request_withdrawal</c>. 성공 시 예약된 시각(ISO 8601)을 반환합니다.
        /// </summary>
        /// <param name="accessToken">현재 로그인 세션의 액세스 토큰. 필수.</param>
        /// <param name="delayDays">탈퇴까지 유예 일수. 서버가 최소 1일로 맞춥니다(바로 지우는 건 즉시 탈퇴).</param>
        public async Task<SupabaseResult<string>> RequestWithdrawalByDelayDaysAsync(
            string accessToken,
            int delayDays)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
                return SupabaseResult<string>.Fail(SupabaseErrorCode.AccessTokenEmpty);

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
                return SupabaseResult<string>.Fail(SupabaseErrorCode.NetworkError);

            if (response.IsSuccess == false)
                return SupabaseResult<string>.Fail(SupabaseRestHelpers.ExtractRpcErrorCode(response.Body, response.ErrorMessage, "withdrawal_request"));

            try
            {
                var rows = _jsonSerializer.FromJsonArray<WithdrawalRequestRow>(response.Body);
                if (rows == null || rows.Length == 0 || rows[0] == null || string.IsNullOrWhiteSpace(rows[0].scheduled_at))
                    return SupabaseResult<string>.Fail("withdrawal_request_scheduled_at_empty");

                return SupabaseResult<string>.Success(rows[0].scheduled_at.Trim());
            }
            catch (Exception e)
            {
                return SupabaseResult<string>.Fail("withdrawal_request_parse_failed:" + e.Message);
            }
        }

        /// <summary>
        /// 로그인한 본인의 탈퇴 예약 게이트 상태를 조회합니다.
        /// RPC: <c>ts_my_withdrawal_status</c>.
        /// </summary>
        /// <param name="accessToken">현재 로그인 세션의 액세스 토큰. 필수.</param>
        public async Task<SupabaseResult<WithdrawalStatus>> GetWithdrawalStatusAsync(string accessToken)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
                return SupabaseResult<WithdrawalStatus>.Fail(SupabaseErrorCode.AccessTokenEmpty);

            var url = $"{_supabaseUrl}/rest/v1/rpc/ts_my_withdrawal_status";

            var response = await _httpClient.SendAsync(
                method: "POST",
                url: url,
                jsonBody: "{}",
                headers: CreateUserHeaders(accessToken, prefer: null));

            if (response == null)
                return SupabaseResult<WithdrawalStatus>.Fail(SupabaseErrorCode.NetworkError);

            if (response.IsSuccess == false)
                return SupabaseResult<WithdrawalStatus>.Fail(SupabaseRestHelpers.ExtractRpcErrorCode(response.Body, response.ErrorMessage, "withdrawal_status"));

            try
            {
                var rows = _jsonSerializer.FromJsonArray<WithdrawalStatusRow>(response.Body);
                if (rows == null || rows.Length == 0 || rows[0] == null)
                    return SupabaseResult<WithdrawalStatus>.Success(new WithdrawalStatus(string.Empty, null, null, false, 0));

                var row = rows[0];
                var status = new WithdrawalStatus(
                    row.display_name ?? string.Empty,
                    string.IsNullOrWhiteSpace(row.withdrawn_at) ? null : row.withdrawn_at.Trim(),
                    string.IsNullOrWhiteSpace(row.server_now) ? null : row.server_now.Trim(),
                    row.is_scheduled,
                    row.seconds_remaining);

                return SupabaseResult<WithdrawalStatus>.Success(status);
            }
            catch (Exception e)
            {
                return SupabaseResult<WithdrawalStatus>.Fail("withdrawal_status_parse_failed:" + e.Message);
            }
        }

        /// <summary>
        /// <c>ts_display_name_check</c> RPC 호출용 JSON 바디를 직접 구성합니다.
        /// IL2CPP 빌드에서 <c>JsonUtility</c>가 private 중첩 클래스를 <c>{}</c>로 직렬화하는 문제를 우회합니다.
        /// </summary>
        private static string BuildIsNameAvailableBody(string displayName, string ignoreAccountId)
        {
            var escapedName = SupabaseCoreHelper.EscapeJson(displayName);
            if (ignoreAccountId == null)
                return "{\"p_display_name\":\"" + escapedName + "\"}";

            // UUID는 hex·하이픈만 포함하므로 이스케이프 불필요하지만 일관성 유지
            var escapedId = SupabaseCoreHelper.EscapeJson(ignoreAccountId);
            return "{\"p_display_name\":\"" + escapedName + "\",\"p_ignore_account_id\":\"" + escapedId + "\"}";
        }

        private static string NormalizeName(string displayName)
        {
            return displayName == null ? string.Empty : displayName.Trim();
        }

        private Dictionary<string, string> CreateUserHeaders(string accessToken, string prefer) =>
            SupabaseRestHelpers.AuthHeaders(_publishableKey, accessToken, prefer);

        [Serializable]
        private sealed class ProfileRowFull
        {
            public string id;
            public string user_id;
            public string withdrawn_at;
        }

        [Serializable]
        private sealed class NameGetRequest
        {
            public string user_id;
        }

        [Serializable]
        private sealed class ServerInfoRow
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
        private sealed class NameGetResponse
        {
            public bool ok;
            public string display_name;
            public string reason;
        }

        [Serializable]
        private sealed class EnsureMyProfileRpcBody
        {
            public string p_user_id;
            public string p_platform;
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

        [Serializable]
        private sealed class AfterSignInRow
        {
            public string withdrawn_at;
            public AfterSignInProfileRow profile;
        }

        [Serializable]
        private sealed class AfterSignInProfileRow
        {
            public string id;
            public string user_id;
            public string display_name;
            public string server_code;
        }
    }

    /// <summary><c>ts_after_sign_in</c> 결과. 탈퇴 예약이 있으면 <see cref="WithdrawnAtIso"/>가 차고 <see cref="Profile"/>은 null입니다.</summary>
    internal sealed class AfterSignInSnapshot
    {
        public AfterSignInSnapshot(string withdrawnAtIso, PublicProfile profile)
        {
            WithdrawnAtIso = withdrawnAtIso;
            Profile = profile;
        }

        public string WithdrawnAtIso { get; }

        public PublicProfile Profile { get; }
    }
}
