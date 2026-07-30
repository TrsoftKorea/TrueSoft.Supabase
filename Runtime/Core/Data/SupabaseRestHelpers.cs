using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace TrueBase.Core.Data
{
    /// <summary>
    /// REST·RPC 서비스들이 공통으로 쓰는 헬퍼입니다.
    /// 서비스마다 같은 코드를 두면 한쪽만 고치는 사고가 나므로 여기 한 벌만 둡니다.
    /// </summary>
    internal static class SupabaseRestHelpers
    {
        /// <summary>PostgREST 호출용 인증 헤더를 만듭니다.</summary>
        /// <param name="prefer">비어 있지 않으면 <c>Prefer</c> 헤더를 추가합니다.</param>
        internal static Dictionary<string, string> AuthHeaders(string publishableKey, string accessToken, string prefer = null)
        {
            var headers = new Dictionary<string, string>
            {
                { "apikey", publishableKey },
                { "Authorization", "Bearer " + accessToken },
                { "Content-Type", "application/json" }
            };

            if (string.IsNullOrEmpty(prefer) == false)
                headers["Prefer"] = prefer;

            return headers;
        }

        /// <summary>
        /// RPC 실패 응답에서 사유 코드를 뽑습니다. 서버는 <c>raise exception '코드: 상세'</c> 형태로 던지므로
        /// 첫 <c>:</c> 앞부분만 취합니다. JSON이 아니거나 message가 없으면 폴백 메시지를, 그것도 없으면
        /// <c>&lt;rpc이름&gt;_failed</c>를 돌려줍니다.
        /// </summary>
        internal static string ExtractRpcErrorCode(string body, string fallbackMessage, string rpcName)
        {
            if (!string.IsNullOrWhiteSpace(body))
            {
                try
                {
                    var message = JObject.Parse(body)["message"]?.Value<string>();
                    if (!string.IsNullOrWhiteSpace(message))
                    {
                        var colon = message.IndexOf(':');
                        return colon > 0 ? message.Substring(0, colon).Trim() : message.Trim();
                    }
                }
                catch
                {
                    // JSON이 아니면 아래 폴백 사용
                }
            }

            if (!string.IsNullOrWhiteSpace(fallbackMessage))
                return fallbackMessage;

            return string.IsNullOrWhiteSpace(body) ? rpcName + "_failed" : body;
        }
    }
}
