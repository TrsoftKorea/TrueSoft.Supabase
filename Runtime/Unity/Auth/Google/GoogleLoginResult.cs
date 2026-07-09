using System;

namespace TrueBase.Unity.Auth.Google
{
    /// <summary>Android 네이티브 Google 로그인 성공 시 반환되는 계정 정보.</summary>
    [Serializable]
    public sealed class GoogleLoginResult
    {
        /// <summary>Supabase 로그인에 사용하는 Google ID 토큰.</summary>
        public string IdToken;
        /// <summary>Google OAuth 액세스 토큰.</summary>
        public string AccessToken;
        /// <summary>Google 계정 고유 ID.</summary>
        public string GoogleUserId;
        /// <summary>표시 이름.</summary>
        public string DisplayName;
        /// <summary>이름.</summary>
        public string GivenName;
        /// <summary>성.</summary>
        public string FamilyName;
        /// <summary>프로필 이미지 URL.</summary>
        public string ProfileImageUrl;
    }
}