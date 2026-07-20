using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using TrueBase.Core.Common;
using TrueBase.Core.Http;

namespace TrueBase.Core.Data
{
    /// <summary>
    /// 유저 세이브 테이블 REST 서비스. <see cref="DataColumnAttribute"/> 기반 select 로드와 diff PATCH 저장을 담당합니다.
    /// </summary>
    public sealed class SupabaseUserDataService
    {
        private readonly string _supabaseUrl;
        private readonly string _publishableKey;
        private readonly ISupabaseHttpClient _httpClient;
        private readonly ISupabaseJsonSerializer _jsonSerializer;

        public SupabaseUserDataService(
            string supabaseUrl,
            string publishableKey,
            ISupabaseHttpClient httpClient,
            ISupabaseJsonSerializer jsonSerializer)
        {
            _supabaseUrl = supabaseUrl?.TrimEnd('/') ?? throw new ArgumentNullException(nameof(supabaseUrl));
            _publishableKey = publishableKey ?? throw new ArgumentNullException(nameof(publishableKey));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _jsonSerializer = jsonSerializer ?? throw new ArgumentNullException(nameof(jsonSerializer));
        }

        /// <summary>
        /// 로그인 직후 지정 테이블에 본인 행이 존재하도록 보장합니다.
        /// DB RPC: <c>ts_ensure_my_row(p_table, p_user_id)</c> (SECURITY DEFINER).
        /// </summary>
        /// <param name="accessToken">현재 로그인 세션의 액세스 토큰. 필수.</param>
        /// <param name="tableName">대상 테이블명(<c>schema.table</c> 허용). 필수.</param>
        /// <param name="playerUserId">안정 플레이어 id(<c>user_id</c> 컬럼용). null·공백이면 RPC에 null로 전달되어 DB가 기존 값을 유지합니다.</param>
        public async Task<SupabaseResult<bool>> EnsureMyRowAsync(
            string accessToken,
            string tableName,
            string playerUserId)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
                return SupabaseResult<bool>.Fail(SupabaseErrorCode.AccessTokenEmpty);

            if (string.IsNullOrWhiteSpace(tableName))
                return SupabaseResult<bool>.Fail("table_name_empty");

            var stable = string.IsNullOrWhiteSpace(playerUserId) ? null : playerUserId.Trim();

            var url = $"{_supabaseUrl}/rest/v1/rpc/ts_ensure_my_row";
            var bodyJson = _jsonSerializer.ToJson(new EnsureMyRowBody
            {
                p_table = tableName.Trim(),
                p_user_id = stable
            });

            var response = await _httpClient.SendAsync(
                method: "POST",
                url: url,
                jsonBody: bodyJson,
                headers: CreateAuthHeaders(accessToken, prefer: null));

            if (response == null)
                return SupabaseResult<bool>.Fail(SupabaseErrorCode.NetworkError);

            if (response.IsSuccess == false)
                return SupabaseResult<bool>.Fail(response.Body ?? response.ErrorMessage ?? "ensure_row_failed");

            return SupabaseResult<bool>.Success(true);
        }

        /// <summary>
        /// 명시 컬럼 기반으로 부분 저장(PATCH)합니다. <paramref name="patch"/>에는 변경된 필드만 넣는 것을 전제로 합니다.
        /// </summary>
        /// <param name="accessToken">현재 로그인 세션의 액세스 토큰. 필수.</param>
        /// <param name="accountId">현재 계정 ID(<c>auth.users.id</c>). PATCH 필터(<c>account_id=eq.</c>)에 사용. 필수.</param>
        /// <param name="playerUserId">안정 플레이어 id. <paramref name="ensureRowFirst"/>가 true일 때 행 생성에만 사용됩니다.</param>
        /// <param name="tableName">대상 테이블명(<c>schema.table</c> 허용). 필수.</param>
        /// <param name="patch">변경할 컬럼과 값의 딕셔너리. 변경된 필드만 포함. 비어 있으면 실패.</param>
        /// <param name="ensureRowFirst">true면 PATCH 전에 <see cref="EnsureMyRowAsync"/>로 본인 행 존재를 보장합니다(기본값: true).</param>
        /// <param name="setUpdatedAtIsoUtc">true면 patch에 <c>updated_at</c>을 현재 UTC 시각으로 자동 추가합니다(기본값: true).</param>
        public async Task<SupabaseResult<bool>> PatchAsync(
            string accessToken,
            string accountId,
            string playerUserId,
            string tableName,
            Dictionary<string, object> patch,
            bool ensureRowFirst = true,
            bool setUpdatedAtIsoUtc = true)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
                return SupabaseResult<bool>.Fail(SupabaseErrorCode.AccessTokenEmpty);

            if (string.IsNullOrWhiteSpace(accountId))
                return SupabaseResult<bool>.Fail("account_id_empty");

            if (string.IsNullOrWhiteSpace(tableName))
                return SupabaseResult<bool>.Fail("table_name_empty");

            if (patch == null || patch.Count == 0)
                return SupabaseResult<bool>.Fail("patch_empty");

            if (ensureRowFirst)
            {
                var ensured = await EnsureMyRowAsync(accessToken, tableName, playerUserId);
                if (ensured == null || !ensured.IsSuccess)
                    return SupabaseResult<bool>.Fail(ensured?.ErrorCode ?? "ensure_row_failed");
            }

            var url =
                $"{SupabaseRestTableRef.BuildTableUrl(_supabaseUrl, tableName)}" +
                $"?account_id=eq.{Uri.EscapeDataString(accountId.Trim())}";

            var payload = patch;
            if (setUpdatedAtIsoUtc)
            {
                payload = new Dictionary<string, object>(patch);
                payload["updated_at"] = DateTime.UtcNow.ToString("o");
            }

            var bodyJson = JsonConvert.SerializeObject(payload);

            var response = await _httpClient.SendAsync(
                method: "PATCH",
                url: url,
                jsonBody: bodyJson,
                headers: CreateAuthHeaders(accessToken, prefer: "return=representation"));

            if (response == null)
                return SupabaseResult<bool>.Fail(SupabaseErrorCode.NetworkError);

            if (response.IsSuccess == false)
                return SupabaseResult<bool>.Fail(response.ErrorMessage ?? response.Body ?? "patch_failed");

            // return=representation: 빈 배열([])이면 필터에 매칭된 행이 없었음
            var trimmedBody = response.Body?.Trim();
            if (string.IsNullOrEmpty(trimmedBody) || trimmedBody == "[]")
                return SupabaseResult<bool>.Fail("patch_no_rows_affected");

            return SupabaseResult<bool>.Success(true);
        }

        /// <summary>
        /// 본인 세이브 행을 삭제합니다(<c>DELETE ... account_id=eq.</c>). RLS <c>delete_own_authenticated</c> 정책으로 허용됩니다.
        /// 매칭 행이 없어도(이미 없음) 성공으로 간주합니다(멱등). 다음 로드 시 <c>ts_ensure_my_row</c>가 기본 행을 재생성합니다.
        /// </summary>
        /// <param name="accessToken">현재 로그인 세션의 액세스 토큰. 필수.</param>
        /// <param name="accountId">현재 계정 ID(<c>auth.users.id</c>). DELETE 필터(<c>account_id=eq.</c>)에 사용. 필수.</param>
        /// <param name="tableName">대상 테이블명(<c>schema.table</c> 허용). 필수.</param>
        public async Task<SupabaseResult<bool>> DeleteMyRowAsync(
            string accessToken,
            string accountId,
            string tableName)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
                return SupabaseResult<bool>.Fail(SupabaseErrorCode.AccessTokenEmpty);

            if (string.IsNullOrWhiteSpace(accountId))
                return SupabaseResult<bool>.Fail("account_id_empty");

            if (string.IsNullOrWhiteSpace(tableName))
                return SupabaseResult<bool>.Fail("table_name_empty");

            var url =
                $"{SupabaseRestTableRef.BuildTableUrl(_supabaseUrl, tableName)}" +
                $"?account_id=eq.{Uri.EscapeDataString(accountId.Trim())}";

            var response = await _httpClient.SendAsync(
                method: "DELETE",
                url: url,
                jsonBody: null,
                headers: CreateAuthHeaders(accessToken, prefer: "return=representation"));

            if (response == null)
                return SupabaseResult<bool>.Fail(SupabaseErrorCode.NetworkError);

            if (response.IsSuccess == false)
                return SupabaseResult<bool>.Fail(response.ErrorMessage ?? response.Body ?? "delete_failed");

            // 매칭 행이 없어도(빈 배열) 삭제 목적은 달성 — 멱등 성공.
            return SupabaseResult<bool>.Success(true);
        }

        /// <summary>
        /// 본인 행 존재 여부(<see cref="DataColumnsLoadResult{T}.HasRow"/>)와 함께 로드합니다. 신규 유저(행 없음)와 인증 실패를 구분할 수 있습니다.
        /// </summary>
        /// <param name="accessToken">현재 로그인 세션의 액세스 토큰. 필수.</param>
        /// <param name="accountId">현재 계정 ID(<c>auth.users.id</c>). 행 필터에 사용. 필수.</param>
        /// <param name="tableName">대상 테이블명(<c>schema.table</c> 허용). 필수.</param>
        /// <param name="selectColumnsCsv">조회할 컬럼 CSV. 필수.</param>
        public async Task<SupabaseResult<DataColumnsLoadResult<T>>> LoadColumnsWithRowStateAsync<T>(
            string accessToken,
            string accountId,
            string tableName,
            string selectColumnsCsv) where T : class, new()
        {
            if (string.IsNullOrWhiteSpace(accessToken))
                return SupabaseResult<DataColumnsLoadResult<T>>.Fail(SupabaseErrorCode.AccessTokenEmpty);

            if (string.IsNullOrWhiteSpace(accountId))
                return SupabaseResult<DataColumnsLoadResult<T>>.Fail("account_id_empty");

            if (string.IsNullOrWhiteSpace(tableName))
                return SupabaseResult<DataColumnsLoadResult<T>>.Fail("table_name_empty");

            if (string.IsNullOrWhiteSpace(selectColumnsCsv))
                return SupabaseResult<DataColumnsLoadResult<T>>.Fail(SupabaseErrorCode.SelectColumnsEmpty);

            var url =
                $"{SupabaseRestTableRef.BuildTableUrl(_supabaseUrl, tableName)}" +
                $"?select={Uri.EscapeDataString(selectColumnsCsv.Trim())}" +
                $"&account_id=eq.{Uri.EscapeDataString(accountId.Trim())}" +
                $"&limit=1";

            var response = await _httpClient.SendAsync(
                method: "GET",
                url: url,
                jsonBody: null,
                headers: CreateAuthHeaders(accessToken, prefer: null));

            if (response == null)
                return SupabaseResult<DataColumnsLoadResult<T>>.Fail(SupabaseErrorCode.NetworkError);

            if (response.IsSuccess == false)
                return SupabaseResult<DataColumnsLoadResult<T>>.Fail(response.ErrorMessage ?? response.Body ?? "load_failed");

            try
            {
                var rows = _jsonSerializer.FromJsonArray<T>(response.Body);
                if (rows == null || rows.Length == 0 || rows[0] == null)
                {
                    return SupabaseResult<DataColumnsLoadResult<T>>.Success(
                        new DataColumnsLoadResult<T>(hasRow: false, row: new T()));
                }

                return SupabaseResult<DataColumnsLoadResult<T>>.Success(
                    new DataColumnsLoadResult<T>(hasRow: true, row: rows[0]));
            }
            catch (Exception e)
            {
                return SupabaseResult<DataColumnsLoadResult<T>>.Fail("load_parse_failed:" + e.Message);
            }
        }

        /// <inheritdoc cref="LoadColumnsWithRowStateAsync{T}(string, string, string, string)"/>
        public async Task<SupabaseResult<DataColumnsLoadResult<T>>> LoadAttributedWithRowStateAsync<T>(
            string accessToken,
            string accountId,
            bool includeUpdatedAt = true) where T : class, new()
        {
            string tableName;
            string csv;
            try
            {
                tableName = DataSchema.ResolveTableName<T>();
                csv = DataSchema.GetSelectColumnsCsv<T>(includeUpdatedAt);
            }
            catch (Exception e)
            {
                return SupabaseResult<DataColumnsLoadResult<T>>.Fail("data_schema_invalid:" + e.Message);
            }

            return await LoadColumnsWithRowStateAsync<T>(accessToken, accountId, tableName, csv);
        }

        /// <summary>
        /// <see cref="DataSchema.BuildPatch{T}(T, T)"/>로 변경분만 PATCH합니다. 변경이 없으면 HTTP 요청 없이 <c>Success(false)</c>를 반환합니다.
        /// </summary>
        /// <param name="accessToken">현재 로그인 세션의 액세스 토큰. 필수.</param>
        /// <param name="accountId">현재 계정 ID(<c>auth.users.id</c>). PATCH 필터에 사용. 필수.</param>
        /// <param name="playerUserId">안정 플레이어 id. <paramref name="ensureRowFirst"/>가 true일 때 행 생성에만 사용됩니다.</param>
        /// <param name="previous">직전 스냅샷. null이면 <paramref name="current"/>의 모든 매핑 컬럼이 전송됩니다.</param>
        /// <param name="current">현재 상태. 필수.</param>
        /// <param name="ensureRowFirst">true면 PATCH 전에 본인 행 존재를 보장합니다(기본값: true).</param>
        /// <param name="setUpdatedAtIsoUtc">true면 <c>updated_at</c>을 현재 UTC 시각으로 자동 추가합니다(기본값: true).</param>
        public async Task<SupabaseResult<bool>> PatchDiffAsync<T>(
            string accessToken,
            string accountId,
            string playerUserId,
            T previous,
            T current,
            bool ensureRowFirst = true,
            bool setUpdatedAtIsoUtc = true)
        {
            string tableName;
            Dictionary<string, object> patch;
            try
            {
                tableName = DataSchema.ResolveTableName<T>();
                patch = DataSchema.BuildPatch(previous, current);
            }
            catch (Exception e)
            {
                return SupabaseResult<bool>.Fail("data_patch_build_failed:" + e.Message);
            }

            if (patch == null || patch.Count == 0)
                return SupabaseResult<bool>.Success(false); // false = 변경 없음, HTTP 요청 미발송

            return await PatchAsync(
                accessToken,
                accountId,
                playerUserId,
                tableName,
                patch,
                ensureRowFirst,
                setUpdatedAtIsoUtc);
        }

        private Dictionary<string, string> CreateAuthHeaders(string accessToken, string prefer = null)
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
        private sealed class EnsureMyRowBody
        {
            public string p_table;
            public string p_user_id;
        }
    }
}
