using System.Collections.Generic;

namespace Truesoft.Supabase.Core.Common
{
    /// <summary>
    /// Core 레이어 내부에서 공통으로 사용하는 유틸리티입니다.
    /// </summary>
    internal static class SupabaseCoreHelper
    {
        /// <summary>
        /// apikey·Content-Type·Authorization(선택)·Prefer(선택) 헤더를 생성합니다.
        /// </summary>
        internal static Dictionary<string, string> BuildApiHeaders(
            string publishableKey,
            string accessToken = null,
            string prefer = null)
        {
            var headers = new Dictionary<string, string>
            {
                { "apikey", publishableKey },
                { "Content-Type", "application/json" }
            };

            if (string.IsNullOrWhiteSpace(accessToken) == false)
                headers["Authorization"] = "Bearer " + accessToken;

            if (string.IsNullOrWhiteSpace(prefer) == false)
                headers["Prefer"] = prefer;

            return headers;
        }

        /// <summary>
        /// JSON 문자열 값 내 특수문자를 이스케이프합니다.
        /// </summary>
        internal static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value.Replace("\\", "\\\\").Replace("\"", "\\\"")
                        .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }
    }
}
