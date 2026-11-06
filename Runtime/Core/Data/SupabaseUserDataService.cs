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
        private const string PatchNoRowsAffected = "patch_no_rows_affected";

        private readonly string _supabaseUrl;
        private readonly string _publishableKey;
        private readonly ISupabaseHttpClient _httpClient;
        private readonly ISupabaseJsonSerializer _jsonSerializer;

        // 이번 실행에서 저장 경로가 행을 보장해 둔 "테이블|계정" → 그때의 플레이어 id. 저장마다 보장 요청을 보내지 않으려는 기억이다.
        // 플레이어 id 까지 보는 이유: 게스트가 구글·애플을 연동하면 id 가 바뀌는데 user_data.user_id 를 고치는 곳은 ts_ensure_my_row 뿐이다.
        private readonly Dictionary<string, string> _ensuredRows = new Dictionary<string, string>(StringComparer.Ordinal);

        // 이 기기에서 세이브를 지운 횟수. 저장을 보내는 사이 삭제가 있었으면 행을 다시 만들지 않으려고 본다.
        private int _deleteCount;

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
                return SupabaseResult<bool>.Fail(SupabaseRestHelpers.ExtractRpcErrorCode(response.Body, response.ErrorMessage, "ensure_row"));

            return SupabaseResult<bool>.Success(true);
        }

        /// <summary>
        /// 명시 컬럼 기반으로 부분 저장(PATCH)합니다. <paramref name="patch"/>에는 변경된 필드만 넣는 것을 전제로 합니다.
        /// </summary>
        /// <param name="accessToken">현재 로그인 세션의 액세스 토큰. 필수.</param>
        /// <param name="accountId">현재 계정 ID(<c>auth.users.id</c>). PATCH 필터(<c>account_id=eq.</c>)에 사용. 필수.</param>
        /// <param name="playerUserId">안정 플레이어 id. <paramref name="ensureRow"/>가 true일 때 행 보장에만 사용됩니다.</param>
        /// <param name="tableName">대상 테이블명(<c>schema.table</c> 허용). 필수.</param>
        /// <param name="patch">변경할 컬럼과 값의 딕셔너리. 변경된 필드만 포함. 비어 있으면 실패.</param>
        /// <param name="ensureRow">true면 본인 행이 있도록 보장합니다(기본값: true). 이 계정·플레이어 id로 처음 저장할 때만 PATCH 전에 <see cref="EnsureMyRowAsync"/>를 부르고, 그 뒤로는 PATCH가 행을 못 찾았을 때만 부른 뒤 다시 보냅니다.</param>
        /// <param name="setUpdatedAtIsoUtc">true면 patch에 <c>updated_at</c>을 현재 UTC 시각으로 자동 추가합니다(기본값: true).</param>
        public async Task<SupabaseResult<bool>> PatchAsync(
            string accessToken,
            string accountId,
            string playerUserId,
            string tableName,
            Dictionary<string, object> patch,
            bool ensureRow = true,
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

            var deleteCount = _deleteCount;
            var rowKey = BuildRowKey(tableName, accountId);
            var stable = string.IsNullOrWhiteSpace(playerUserId) ? null : playerUserId.Trim();
            var ensuredNow = false;
            if (ensureRow && (_ensuredRows.TryGetValue(rowKey, out var ensuredFor) == false || ensuredFor != stable))
            {
                var ensured = await EnsureMyRowAsync(accessToken, tableName, stable);
                if (ensured == null || !ensured.IsSuccess)
                    return SupabaseResult<bool>.Fail(ensured?.ErrorCode ?? "ensure_row_failed");

                if (deleteCount != _deleteCount)
                    return SupabaseResult<bool>.Fail(PatchNoRowsAffected);

                _ensuredRows[rowKey] = stable;
                ensuredNow = true;
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

            string bodyJson;
            try
            {
                bodyJson = JsonConvert.SerializeObject(payload);
            }
            catch (Exception e)
            {
                return SupabaseResult<bool>.Fail("data_patch_build_failed:" + e.Message);
            }

            var sent = await SendPatchAsync(accessToken, url, bodyJson);

            // 기억해 둔 행이 사라졌을 수 있다 — 다른 기기에서 세이브를 지운 경우 등.
            // 이 기기에서 방금 지운 것이면 되살리지 않는다.
            if (ensureRow && !ensuredNow && sent.ErrorCode == PatchNoRowsAffected && deleteCount == _deleteCount)
            {
                var ensured = await EnsureMyRowAsync(accessToken, tableName, stable);
                if (ensured == null || !ensured.IsSuccess)
                    return SupabaseResult<bool>.Fail(ensured?.ErrorCode ?? "ensure_row_failed");

                if (deleteCount != _deleteCount)
                    return sent;

                sent = await SendPatchAsync(accessToken, url, bodyJson);
            }

            return sent;
        }

        private async Task<SupabaseResult<bool>> SendPatchAsync(string accessToken, string url, string bodyJson)
        {
            var response = await _httpClient.SendAsync(
                method: "PATCH",
                url: url,
                jsonBody: bodyJson,
                headers: CreateAuthHeaders(accessToken, prefer: "return=representation"));

            if (response == null)
                return SupabaseResult<bool>.Fail(SupabaseErrorCode.NetworkError);

            if (response.IsSuccess == false)
                return SupabaseResult<bool>.Fail(SupabaseRestHelpers.ExtractRpcErrorCode(response.Body, response.ErrorMessage, "patch"));

            // return=representation: 빈 배열([])이면 필터에 매칭된 행이 없었음
            var trimmedBody = response.Body?.Trim();
            if (string.IsNullOrEmpty(trimmedBody) || trimmedBody == "[]")
                return SupabaseResult<bool>.Fail(PatchNoRowsAffected);

            return SupabaseResult<bool>.Success(true);
        }

        private static string BuildRowKey(string tableName, string accountId) =>
            tableName.Trim() + "|" + accountId.Trim();

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

            // 보내기 전에 표시한다. 응답을 기다리는 사이 도착한 저장이 행을 다시 만들면 지운 세이브가 되살아난다.
            _deleteCount++;
            _ensuredRows.Remove(BuildRowKey(tableName, accountId));

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
                return SupabaseResult<bool>.Fail(SupabaseRestHelpers.ExtractRpcErrorCode(response.Body, response.ErrorMessage, "delete"));

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
                return SupabaseResult<DataColumnsLoadResult<T>>.Fail(SupabaseRestHelpers.ExtractRpcErrorCode(response.Body, response.ErrorMessage, "load"));

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
        /// <param name="playerUserId">안정 플레이어 id. <paramref name="ensureRow"/>가 true일 때 행 보장에만 사용됩니다.</param>
        /// <param name="previous">직전 스냅샷. null이면 <paramref name="current"/>의 모든 매핑 컬럼이 전송됩니다.</param>
        /// <param name="current">현재 상태. 필수.</param>
        /// <param name="ensureRow">true면 본인 행이 있도록 보장합니다(기본값: true). <see cref="PatchAsync"/>를 참고하세요.</param>
        /// <param name="setUpdatedAtIsoUtc">true면 <c>updated_at</c>을 현재 UTC 시각으로 자동 추가합니다(기본값: true).</param>
        /// <param name="sentColumns">null이 아니면 이번 요청에 담는 칸 이름을 채웁니다. 저장 요약 로그가 비교를 다시 하지 않게 하려는 것입니다.</param>
        public async Task<SupabaseResult<bool>> PatchDiffAsync<T>(
            string accessToken,
            string accountId,
            string playerUserId,
            T previous,
            T current,
            bool ensureRow = true,
            bool setUpdatedAtIsoUtc = true,
            ICollection<string> sentColumns = null)
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

            if (sentColumns != null)
            {
                foreach (var column in patch.Keys)
                    sentColumns.Add(column);
            }

            return await PatchAsync(
                accessToken,
                accountId,
                playerUserId,
                tableName,
                patch,
                ensureRow,
                setUpdatedAtIsoUtc);
        }

        private Dictionary<string, string> CreateAuthHeaders(string accessToken, string prefer = null) =>
            SupabaseRestHelpers.AuthHeaders(_publishableKey, accessToken, prefer);

        [Serializable]
        private sealed class EnsureMyRowBody
        {
            public string p_table;
            public string p_user_id;
        }
    }
}
