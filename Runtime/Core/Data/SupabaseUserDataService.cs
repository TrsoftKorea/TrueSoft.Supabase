using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Truesoft.Supabase.Core.Common;
using Truesoft.Supabase.Core.Http;

namespace Truesoft.Supabase.Core.Data
{
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
        public async Task<SupabaseResult<bool>> EnsureMyRowAsync(
            string accessToken,
            string tableName,
            string playerUserId)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
                return SupabaseResult<bool>.Fail("access_token_empty");

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
                return SupabaseResult<bool>.Fail("http_response_null");

            if (response.IsSuccess == false)
                return SupabaseResult<bool>.Fail(response.Body ?? response.ErrorMessage ?? "ensure_row_failed");

            return SupabaseResult<bool>.Success(true);
        }

        /// <summary>
        /// 명시 컬럼 기반으로 부분 저장(PATCH)합니다. <paramref name="patch"/>에는 변경된 필드만 넣는 것을 전제로 합니다.
        /// </summary>
        /// <param name="accessToken">현재 로그인 세션의 액세스 토큰.</param>
        /// <param name="accountId">현재 계정 ID(auth.users.id).</param>
        /// <param name="playerUserId">플레이어 사용자 ID. RLS 필터링에 사용.</param>
        /// <param name="tableName">대상 테이블명.</param>
        /// <param name="patch">변경할 컬럼과 값의 딕셔너리. 변경된 필드만 포함.</param>
        /// <param name="ensureRowFirst">true일 때, PATCH 전에 해당 행이 존재하는지 확인하고 없으면 생성(기본값: true).</param>
        /// <param name="setUpdatedAtIsoUtc">true일 때, patch에 updated_at을 현재 UTC 시각으로 자동 추가(기본값: true).</param>
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
                return SupabaseResult<bool>.Fail("access_token_empty");

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
                    return SupabaseResult<bool>.Fail(ensured?.ErrorMessage ?? "ensure_row_failed");
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
                headers: CreateAuthHeaders(accessToken, prefer: "return=minimal"));

            if (response == null)
                return SupabaseResult<bool>.Fail("http_response_null");

            if (response.IsSuccess == false)
                return SupabaseResult<bool>.Fail(response.ErrorMessage ?? response.Body ?? "patch_failed");

            return SupabaseResult<bool>.Success(true);
        }

        /// <summary>
        /// 프로젝트별 명시 컬럼을 select로 지정해 로드합니다.
        /// </summary>
        public async Task<SupabaseResult<T>> LoadColumnsAsync<T>(
            string accessToken,
            string accountId,
            string tableName,
            string selectColumnsCsv) where T : class, new()
        {
            var r = await LoadColumnsWithRowStateAsync<T>(accessToken, accountId, tableName, selectColumnsCsv);
            if (!r.IsSuccess)
                return SupabaseResult<T>.Fail(r.ErrorMessage ?? "load_failed");
            return SupabaseResult<T>.Success(r.Data.Row);
        }

        /// <summary>
        /// 본인 행 존재 여부(<see cref="DataColumnsLoadResult{T}.HasRow"/>)와 함께 로드합니다.
        /// </summary>
        public async Task<SupabaseResult<DataColumnsLoadResult<T>>> LoadColumnsWithRowStateAsync<T>(
            string accessToken,
            string accountId,
            string tableName,
            string selectColumnsCsv) where T : class, new()
        {
            if (string.IsNullOrWhiteSpace(accessToken))
                return SupabaseResult<DataColumnsLoadResult<T>>.Fail("access_token_empty");

            if (string.IsNullOrWhiteSpace(accountId))
                return SupabaseResult<DataColumnsLoadResult<T>>.Fail("account_id_empty");

            if (string.IsNullOrWhiteSpace(tableName))
                return SupabaseResult<DataColumnsLoadResult<T>>.Fail("table_name_empty");

            if (string.IsNullOrWhiteSpace(selectColumnsCsv))
                return SupabaseResult<DataColumnsLoadResult<T>>.Fail("select_columns_empty");

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
                return SupabaseResult<DataColumnsLoadResult<T>>.Fail("http_response_null");

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

        /// <summary>
        /// <see cref="DataColumnAttribute"/>로 표시한 컬럼만 모아 <c>select</c> 후 로드합니다.
        /// 대상 타입에 <see cref="DataTableAttribute"/>가 필요합니다.
        /// </summary>
        public async Task<SupabaseResult<T>> LoadAttributedAsync<T>(
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
                return SupabaseResult<T>.Fail("data_schema_invalid:" + e.Message);
            }

            return await LoadColumnsAsync<T>(accessToken, accountId, tableName, csv);
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
        /// <see cref="DataSchema.BuildPatch{T}(T, T)"/>로 변경분만 PATCH합니다.
        /// 대상 타입에 <see cref="DataTableAttribute"/>가 필요합니다.
        /// </summary>
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
