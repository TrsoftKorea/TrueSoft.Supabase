using System.Collections.Generic;
using System.Threading.Tasks;

namespace TrueBase.Core.Http
{
    /// <summary>
    /// Core 서비스가 사용하는 HTTP 전송 추상화. Unity 레이어의 <c>UnitySupabaseHttpClient</c>가 구현합니다.
    /// </summary>
    public interface ISupabaseHttpClient
    {
        /// <summary>HTTP 요청을 보내고 응답을 반환합니다. 실패 시 예외 대신 실패 상태의 <see cref="SupabaseHttpResponse"/>를 반환해야 합니다.</summary>
        /// <param name="method">HTTP 메서드(<c>GET</c>·<c>POST</c>·<c>PATCH</c>·<c>PUT</c>·<c>DELETE</c>).</param>
        /// <param name="url">요청 전체 URL.</param>
        /// <param name="jsonBody">JSON 요청 본문. 본문 없는 요청은 null.</param>
        /// <param name="headers">요청 헤더(<c>apikey</c>·<c>Authorization</c>·<c>Prefer</c> 등).</param>
        Task<SupabaseHttpResponse> SendAsync(
            string method,
            string url,
            string jsonBody,
            Dictionary<string, string> headers);
    }
}