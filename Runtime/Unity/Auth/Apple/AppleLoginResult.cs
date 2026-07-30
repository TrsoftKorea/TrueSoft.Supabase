using System;

namespace TrueBase.Unity.Auth.Apple
{
    /// <summary>iOS 네이티브 Sign in with Apple 성공 시 반환되는 계정 정보.</summary>
    [Serializable]
    internal sealed class AppleLoginResult
    {
        /// <summary>Supabase 로그인에 사용하는 Apple identityToken(JWT).</summary>
        public string IdToken;
        /// <summary>Apple 계정 고유 ID.</summary>
        public string AppleUserId;
        /// <summary>이메일. 최초 로그인 시에만 제공될 수 있으며 이후에는 비어 있을 수 있습니다.</summary>
        public string Email;
        /// <summary>이름. 최초 로그인 시에만 제공될 수 있습니다.</summary>
        public string GivenName;
        /// <summary>성. 최초 로그인 시에만 제공될 수 있습니다.</summary>
        public string FamilyName;
    }
}
