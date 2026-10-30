using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using TrueBase.Core.Common;
using UnityEngine.Networking;

namespace TrueBase.Core.Http
{
    /// <summary>
    /// <see cref="ISupabaseHttpClient"/>의 <c>UnityWebRequest</c> 구현입니다.
    /// 네트워크 오류와 일시적 서버 오류(429·5xx)에 대해 지수 백오프로 최대 2회 재시도합니다.
    /// </summary>
    internal sealed class UnitySupabaseHttpClient : ISupabaseHttpClient
    {
        private readonly int _timeoutSeconds;
        private readonly int _maxRetries;

        /// <param name="timeoutSeconds">요청 1회당 타임아웃(초). (기본값: 30)</param>
        /// <param name="maxRetries">첫 시도 뒤 재시도 횟수. 두 번 보내면 안 되는 요청(일회용 코드 교환 등)은 0. (기본값: 2)</param>
        public UnitySupabaseHttpClient(int timeoutSeconds = 30, int maxRetries = 2)
        {
            _timeoutSeconds = timeoutSeconds;
            _maxRetries = maxRetries;
        }

        /// <summary>HTTP 요청을 보내고, 재시도까지 모두 실패하면 마지막 시도의 실패를 반환합니다.</summary>
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
            for (int attempt = 0; attempt <= _maxRetries; attempt++)
            {
                var response = await SendOnceAsync(method, url, jsonBody, headers, attempt == _maxRetries);
                if (response.IsSuccess || !ShouldRetry(response, attempt))
                    return response;

                // 지수 백오프: 1초, 2초
                var delaySeconds = Math.Pow(2, attempt);
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
            }

            // 위 반복은 마지막 시도에서 반드시 돌아가므로 여기 오지 않는다. 왔다면 응답을 못 받은 것으로 본다.
            return SupabaseHttpResponse.Fail(0, "", SupabaseErrorCode.NetworkError);
        }

        private async Task<SupabaseHttpResponse> SendOnceAsync(
            string method,
            string url,
            string jsonBody,
            Dictionary<string, string> headers,
            bool isLastAttempt)
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

            // 상태 0·401 은 응답 객체가, 429·5xx 는 로그인 쪽이 사유 코드로 바꾸면서 원래 글이 사라진다 — 진단용으로 여기 남긴다.
            // 재시도하는 상태는 마지막 시도에서만 남긴다. 주소의 쿼리는 빼고 적는다.
            var retriable = status == 0 || status == 429 || status >= 500;
            if ((retriable && isLastAttempt) || status == 401)
                UnityEngine.Debug.LogWarning(
                    $"[Supabase.Http] {method} {StripQuery(url)} → {status}: {request.error} {Truncate(body, 300)}");

            return SupabaseHttpResponse.Fail(status, body, request.error);
        }

        private static string StripQuery(string url)
        {
            var q = url?.IndexOf('?') ?? -1;
            return q < 0 ? url : url.Substring(0, q);
        }

        private static string Truncate(string s, int max) =>
            string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max) + "…";

        /// <summary>
        /// 재시도 대상 판별. StatusCode 0(응답 자체를 못 받은 네트워크 오류)·429·500·502·503·504만 재시도합니다.
        /// 서버 요청 제한(429 + request_limited)은 분 단위로 풀리므로 재시도하지 않습니다.
        /// </summary>
        /// <param name="attempt">0부터 시작하는 현재 시도 횟수.</param>
        private bool ShouldRetry(SupabaseHttpResponse response, int attempt) =>
            attempt < _maxRetries &&
            !response.IsRequestLimited &&
            (response.StatusCode == 0   ||
             response.StatusCode == 429 ||
             response.StatusCode == 500 ||
             response.StatusCode == 502 ||
             response.StatusCode == 503 ||
             response.StatusCode == 504);
    }
}
