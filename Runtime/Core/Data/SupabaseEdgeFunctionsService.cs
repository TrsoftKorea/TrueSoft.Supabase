using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TrueBase.Core.Common;
using TrueBase.Core.Http;

namespace TrueBase.Core.Data
{
    /// <summary>
    /// Supabase Edge Functions 호출 서비스 (<c>POST /functions/v1/{functionName}</c>).
    /// </summary>
    public sealed class SupabaseEdgeFunctionsService
    {
        private readonly string _supabaseUrl;
        private readonly string _publishableKey;
        private readonly ISupabaseHttpClient _httpClient;
        private readonly ISupabaseJsonSerializer _jsonSerializer;

        public SupabaseEdgeFunctionsService(
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

        /// <summary>Edge Function을 호출해 상태 코드·본문을 그대로 반환합니다(파싱 없음).</summary>
        /// <param name="functionName">함수 이름(URL 경로 세그먼트). 필수 — 자동으로 URL 인코딩됩니다.</param>
        /// <param name="accessToken">로그인 세션의 access token. null이면 Publishable 키만으로 호출합니다(기본값: null).</param>
        /// <param name="requestBody">JSON으로 직렬화할 요청 본문 객체. null이면 본문 없이 전송(기본값: null).</param>
        public async Task<SupabaseResult<SupabaseFunctionResponse>> InvokeRawAsync(
            string functionName,
            string accessToken = null,
            object requestBody = null)
        {
            if (string.IsNullOrWhiteSpace(functionName))
                return SupabaseResult<SupabaseFunctionResponse>.Fail("function_name_empty");

            var url = $"{_supabaseUrl}/functions/v1/{Uri.EscapeDataString(functionName)}";
            var bodyJson = requestBody == null ? null : _jsonSerializer.ToJson(requestBody);

            var response = await _httpClient.SendAsync(
                method: "POST",
                url: url,
                jsonBody: bodyJson,
                headers: CreateHeaders(accessToken));

            if (response == null)
                return SupabaseResult<SupabaseFunctionResponse>.Fail(SupabaseErrorCode.NetworkError);

            if (response.IsSuccess == false)
                return SupabaseResult<SupabaseFunctionResponse>.Fail(FormatHttpError(response, "function_invoke_failed"));

            var data = new SupabaseFunctionResponse
            {
                StatusCode = response.StatusCode,
                Body = response.Body ?? string.Empty
            };

            return SupabaseResult<SupabaseFunctionResponse>.Success(data);
        }

        /// <summary>Edge Function을 호출하고 응답 JSON을 <typeparamref name="TResponse"/>로 파싱합니다. 배열 루트 응답은 첫 원소를 사용합니다.</summary>
        /// <param name="functionName">함수 이름(URL 경로 세그먼트). 필수.</param>
        /// <param name="accessToken">로그인 세션의 access token. null이면 Publishable 키만으로 호출합니다(기본값: null).</param>
        /// <param name="requestBody">JSON으로 직렬화할 요청 본문 객체. null이면 본문 없이 전송(기본값: null).</param>
        public async Task<SupabaseResult<TResponse>> InvokeAsync<TResponse>(
            string functionName,
            string accessToken = null,
            object requestBody = null)
        {
            var raw = await InvokeRawAsync(functionName, accessToken, requestBody);
            if (raw.IsSuccess == false)
                return SupabaseResult<TResponse>.Fail(raw.ErrorCode ?? "function_invoke_failed");

            if (raw.Data == null || string.IsNullOrWhiteSpace(raw.Data.Body))
                return SupabaseResult<TResponse>.Fail("function_response_empty");

            try
            {
                var body = raw.Data.Body;
                var trimmed = body?.TrimStart();
                if (string.IsNullOrWhiteSpace(trimmed))
                    return SupabaseResult<TResponse>.Fail("function_response_empty");

                // Unity JsonUtility는 루트 배열(JSON이 []로 시작) 파싱에 취약하므로,
                // 응답이 배열 루트인 경우 첫 번째 원소를 객체 루트처럼 취급합니다.
                if (trimmed.StartsWith("["))
                {
                    var arr = _jsonSerializer.FromJsonArray<TResponse>(trimmed);
                    if (arr == null || arr.Length == 0)
                        return SupabaseResult<TResponse>.Fail("function_response_empty");

                    return SupabaseResult<TResponse>.Success(arr[0]);
                }

                var parsed = _jsonSerializer.FromJson<TResponse>(body);
                return SupabaseResult<TResponse>.Success(parsed);
            }
            catch (Exception e)
            {
                return SupabaseResult<TResponse>.Fail("function_parse_failed:" + e.Message);
            }
        }

        private Dictionary<string, string> CreateHeaders(string accessToken) =>
            SupabaseCoreHelper.BuildApiHeaders(_publishableKey, accessToken);

        private static string FormatHttpError(SupabaseHttpResponse response, string fallback)
        {
            if (response == null)
                return "http_response_null";

            // Supabase Edge Function은 보통 JSON body에 더 자세한 오류를 담습니다.
            var body = response.Body;
            var err = response.ErrorMessage;

            if (string.IsNullOrWhiteSpace(body))
                body = err;

            if (string.IsNullOrWhiteSpace(body))
                body = fallback;

            const int maxLen = 2000;
            body = body.Trim();
            if (body.Length > maxLen)
                body = body.Substring(0, maxLen) + "...(truncated)";

            if (string.IsNullOrWhiteSpace(err) || string.Equals(err, body, StringComparison.Ordinal))
                return $"http_{response.StatusCode}:{body}";

            err = err.Trim();
            if (err.Length > 800)
                err = err.Substring(0, 800) + "...";

            return $"http_{response.StatusCode}:body={body}|error={err}";
        }
    }

    [Serializable]
    public sealed class SupabaseFunctionResponse
    {
        public long StatusCode;
        public string Body;
    }
}

