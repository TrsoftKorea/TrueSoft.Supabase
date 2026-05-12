using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Truesoft.Supabase.Core.Common;
using Truesoft.Supabase.Core.Http;

namespace Truesoft.Supabase.Core.Auth
{
    /// <summary>
    /// 디바이스 지문 해시 기준 익명 복구 토큰을 조회/저장합니다.
    /// </summary>
    public sealed class SupabaseAnonymousRecoveryService
    {
        private readonly string _supabaseUrl;
        private readonly string _publishableKey;
        private readonly ISupabaseHttpClient _httpClient;
        private readonly ISupabaseJsonSerializer _jsonSerializer;

        public SupabaseAnonymousRecoveryService(
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

        /// <summary>
        /// RPC <c>ts_anon_recovery_get_refresh_token</c>을 호출해 복구용 refresh_token을 조회합니다.
        /// </summary>
        public async Task<SupabaseResult<string>> TryGetRefreshTokenByFingerprintAsync(string fingerprintHash, string serverCode = null)
        {
            if (string.IsNullOrWhiteSpace(fingerprintHash))
                return SupabaseResult<string>.Fail("fingerprint_hash_empty");

            var url = $"{_supabaseUrl}/rest/v1/rpc/ts_anon_recovery_get_refresh_token";
            var body = _jsonSerializer.ToJson(new GetRequest
            {
                p_fingerprint_hash = fingerprintHash.Trim(),
                p_server_code = string.IsNullOrWhiteSpace(serverCode) ? null : serverCode.Trim()
            });

            var response = await _httpClient.SendAsync(
                method: "POST",
                url: url,
                jsonBody: body,
                headers: CreateHeaders(prefer: null));

            if (response == null)
                return SupabaseResult<string>.Fail("http_response_null");

            if (response.IsSuccess == false)
                return SupabaseResult<string>.Fail(response.ErrorMessage ?? response.Body ?? "anonymous_recovery_get_failed");

            try
            {
                var rows = _jsonSerializer.FromJsonArray<GetResponseRow>(response.Body);
                var token = rows != null && rows.Length > 0 ? rows[0]?.refresh_token : null;
                return SupabaseResult<string>.Success(string.IsNullOrWhiteSpace(token) ? null : token.Trim());
            }
            catch (Exception e)
            {
                return SupabaseResult<string>.Fail("anonymous_recovery_parse_exception:" + e.Message);
            }
        }

        /// <summary>
        /// RPC <c>ts_anon_recovery_upsert_refresh_token</c>을 호출해 복구용 refresh_token을 저장합니다.
        /// </summary>
        public async Task<SupabaseResult<bool>> UpsertRefreshTokenByFingerprintAsync(
            string fingerprintHash,
            string refreshToken,
            string accountId,
            string serverCode = null)
        {
            if (string.IsNullOrWhiteSpace(fingerprintHash))
                return SupabaseResult<bool>.Fail("fingerprint_hash_empty");

            if (string.IsNullOrWhiteSpace(refreshToken))
                return SupabaseResult<bool>.Fail("refresh_token_empty");

            var url = $"{_supabaseUrl}/rest/v1/rpc/ts_anon_recovery_upsert_refresh_token";
            var body = _jsonSerializer.ToJson(new UpsertRequest
            {
                p_fingerprint_hash = fingerprintHash.Trim(),
                p_refresh_token = refreshToken.Trim(),
                p_account_id = string.IsNullOrWhiteSpace(accountId) ? null : accountId.Trim(),
                p_server_code = string.IsNullOrWhiteSpace(serverCode) ? null : serverCode.Trim()
            });

            var response = await _httpClient.SendAsync(
                method: "POST",
                url: url,
                jsonBody: body,
                headers: CreateHeaders(prefer: "return=minimal"));

            if (response == null)
                return SupabaseResult<bool>.Fail("http_response_null");

            if (response.IsSuccess == false)
                return SupabaseResult<bool>.Fail(response.ErrorMessage ?? response.Body ?? "anonymous_recovery_upsert_failed");

            return SupabaseResult<bool>.Success(true);
        }

        /// <summary>
        /// RPC <c>ts_anon_recovery_delete_by_fingerprint</c>으로 해당 지문 행을 삭제합니다(익명→OAuth 연동 후 정리용).
        /// </summary>
        public async Task<SupabaseResult<bool>> DeleteByFingerprintAsync(string fingerprintHash, string serverCode = null)
        {
            if (string.IsNullOrWhiteSpace(fingerprintHash))
                return SupabaseResult<bool>.Fail("fingerprint_hash_empty");

            var url = $"{_supabaseUrl}/rest/v1/rpc/ts_anon_recovery_delete_by_fingerprint";
            var body = _jsonSerializer.ToJson(new DeleteRequest
            {
                p_fingerprint_hash = fingerprintHash.Trim(),
                p_server_code = string.IsNullOrWhiteSpace(serverCode) ? null : serverCode.Trim()
            });

            var response = await _httpClient.SendAsync(
                method: "POST",
                url: url,
                jsonBody: body,
                headers: CreateHeaders(prefer: "return=minimal"));

            if (response == null)
                return SupabaseResult<bool>.Fail("http_response_null");

            if (response.IsSuccess == false)
                return SupabaseResult<bool>.Fail(response.ErrorMessage ?? response.Body ?? "anonymous_recovery_delete_failed");

            return SupabaseResult<bool>.Success(true);
        }

        private Dictionary<string, string> CreateHeaders(string prefer) =>
            SupabaseCoreHelper.BuildApiHeaders(_publishableKey, prefer: prefer);

        [Serializable]
        private sealed class GetRequest
        {
            public string p_fingerprint_hash;
            public string p_server_code;
        }

        [Serializable]
        private sealed class DeleteRequest
        {
            public string p_fingerprint_hash;
            public string p_server_code;
        }

        [Serializable]
        private sealed class UpsertRequest
        {
            public string p_fingerprint_hash;
            public string p_refresh_token;
            public string p_account_id;
            public string p_server_code;
        }

        [Serializable]
        private sealed class GetResponseRow
        {
            public string refresh_token;
        }
    }
}
