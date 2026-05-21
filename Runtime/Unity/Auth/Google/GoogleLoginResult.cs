using System;

namespace TrueBase.Unity.Auth.Google
{
    [Serializable]
    public sealed class GoogleLoginResult
    {
        public string IdToken;
        public string AccessToken;
        public string GoogleUserId;
        public string DisplayName;
        public string GivenName;
        public string FamilyName;
        public string ProfileImageUrl;
    }
}