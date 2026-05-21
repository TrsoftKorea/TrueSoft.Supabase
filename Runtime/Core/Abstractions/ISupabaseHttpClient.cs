using System.Collections.Generic;
using System.Threading.Tasks;

namespace TrueBase.Core.Http
{
    public interface ISupabaseHttpClient
    {
        Task<SupabaseHttpResponse> SendAsync(
            string method,
            string url,
            string jsonBody,
            Dictionary<string, string> headers);
    }
}