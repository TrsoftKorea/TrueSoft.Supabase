using System;

namespace TrueBase.Core.Models
{
    /// <summary><c>apple-token</c> Edge Function 요청. 애플 로그인 코드 보관(<c>store</c>)·철회(<c>revoke</c>).</summary>
    [Serializable]
    internal sealed class AppleAuthTokenRequest
    {
        public string action;
        public string authorization_code;
        public string client_id;
    }

    /// <summary><c>apple-token</c> Edge Function 응답.</summary>
    [Serializable]
    internal sealed class AppleAuthTokenResponse
    {
        public bool ok;
        public bool stored;
        public bool revoked;
        public string reason;
    }
}
