using System;

namespace TrueBase.Unity.Auth.Apple
{
    [Serializable]
    public sealed class AppleLoginResult
    {
        public string IdToken;
        public string AppleUserId;
        public string Email;
        public string GivenName;
        public string FamilyName;
    }
}
