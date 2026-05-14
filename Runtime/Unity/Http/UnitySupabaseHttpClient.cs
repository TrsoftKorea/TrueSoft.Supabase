using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Truesoft.Supabase.Core.Http
{
    public sealed class UnitySupabaseHttpClient : ISupabaseHttpClient
    {
        private const int MaxRetries = 2;
        private readonly int _timeoutSeconds;

        public UnitySupabaseHttpClient(int timeoutSeconds = 30)
        {
            _timeoutSeconds = timeoutSeconds;
        }

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

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var shortUrl = url.Contains("/rest/v1/") ? url[url.IndexOf("/rest/v1/")..] : url;
            Debug.Log(
                $"[SupabaseHTTP] {method} {shortUrl}\n" +
                $"Body: {(string.IsNullOrEmpty(jsonBody) ? "(none)" : jsonBody)}\n" +
                $"Status: {status} | Response: {(string.IsNullOrEmpty(body) ? "(empty)" : body)}");
#endif

            if (success)
            {
                return SupabaseHttpResponse.Success(status, body);
            }

            return SupabaseHttpResponse.Fail(status, body, request.error);
        }

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
