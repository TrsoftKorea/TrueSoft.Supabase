using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine.Networking;

namespace TrueBase.Core.Http
{
    /// <summary>
    /// <see cref="ISupabaseHttpClient"/>의 <c>UnityWebRequest</c> 구현입니다.
    /// 네트워크 오류와 일시적 서버 오류(429·5xx)에 대해 지수 백오프로 최대 2회 재시도합니다.
    /// </summary>
    public sealed class UnitySupabaseHttpClient : ISupabaseHttpClient
    {
        private const int MaxRetries = 2;
        private readonly int _timeoutSeconds;

        /// <param name="timeoutSeconds">요청 1회당 타임아웃(초). (기본값: 30)</param>
        public UnitySupabaseHttpClient(int timeoutSeconds = 30)
        {
            _timeoutSeconds = timeoutSeconds;
        }

        /// <summary>HTTP 요청을 보내고, 재시도까지 모두 실패하면 <c>max_retries_exceeded</c> 실패를 반환합니다.</summary>
        /// <param name="method">HTTP 메서드(GET·POST·PATCH 등).</param>
        /// <param name="url">요청 전체 URL.</param>
        /// <param name="jsonBody">UTF-8 JSON 본문. null/빈 문자열이면 본문 없이 전송.</param>
        /// <param name="headers">요청 헤더. null 허용.</param>
        public async Task<SupabaseHttpResponse> SendAsync(
            string method,
            string url,
            string jsonBody,
            Dictionary<string, string> headers)
        {
            for (int attempt = 0; attempt <= MaxRetries; attempt++)
            {
                var response = await SendOnceAsync(method, url, jsonBody, headers);
                if (response.IsSuccess || !ShouldRetry(response, attempt))
                    return response;

                // 지수 백오프: 1초, 2초
                var delaySeconds = Math.Pow(2, attempt);
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
            }

            return SupabaseHttpResponse.Fail(0, "", "max_retries_exceeded");
        }

        private async Task<SupabaseHttpResponse> SendOnceAsync(
            string method,
            string url,
            string jsonBody,
            Dictionary<string, string> headers)
        {
            using var request = new UnityWebRequest(url, method);

            request.timeout = _timeoutSeconds;

            if (string.IsNullOrEmpty(jsonBody) == false)
            {
                var bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            }

            request.downloadHandler = new DownloadHandlerBuffer();

            if (headers != null)
            {
                foreach (var pair in headers)
                {
                    request.SetRequestHeader(pair.Key, pair.Value);
                }
            }

            var operation = request.SendWebRequest();

            while (!operation.isDone)
            {
                await Task.Yield();
            }

#if UNITY_2020_2_OR_NEWER
            var success = request.result == UnityWebRequest.Result.Success;
            var isConnectionError = request.result == UnityWebRequest.Result.ConnectionError;
#else
            var success = !request.isNetworkError && !request.isHttpError;
            var isConnectionError = request.isNetworkError;
#endif

            var body = request.downloadHandler?.text ?? "";
            var status = request.responseCode;

            if (success)
            {
                return SupabaseHttpResponse.Success(status, body);
            }

            return SupabaseHttpResponse.Fail(status, body, request.error);
        }

        /// <summary>
        /// 재시도 대상 판별. StatusCode 0(응답 자체를 못 받은 네트워크 오류)·429·500·502·503·504만 재시도합니다.
        /// </summary>
        /// <param name="attempt">0부터 시작하는 현재 시도 횟수.</param>
        private static bool ShouldRetry(SupabaseHttpResponse response, int attempt) =>
            attempt < MaxRetries &&
            (response.StatusCode == 0   ||
             response.StatusCode == 429 ||
             response.StatusCode == 500 ||
             response.StatusCode == 502 ||
             response.StatusCode == 503 ||
             response.StatusCode == 504);
    }
}
