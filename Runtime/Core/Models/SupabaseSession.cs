using System;

namespace TrueBase.Core.Auth
{
    /// <summary>
    /// 로그인 세션. Auth 응답의 토큰과 사용자 정보를 담습니다.
    /// <see cref="AccessToken"/>은 API 인증에, <see cref="RefreshToken"/>은 세션 복원에 사용합니다.
    /// </summary>
    [Serializable]
    public sealed class SupabaseSession
    {
        public string access_token;
        public string refresh_token;
        public string token_type;
        public int expires_in;
        public long expires_at;
        public SupabaseUser user;

        /// <summary>
        /// Auth 응답 파싱 직후만 사용. Google OAuth로 <b>방금 생성된</b> 계정으로 추정되면 true(서버 JSON에는 없음).
        /// </summary>
        public bool likely_brand_new_google_signup;

        public string AccessToken => access_token;
        public string RefreshToken => refresh_token;
        public string TokenType => token_type;
        public int ExpiresIn => expires_in;
        public long ExpiresAt => expires_at;
        public SupabaseUser User => user;
    }

    /// <summary>
    /// Supabase Auth 응답의 <c>user_metadata</c> 필드.
    /// 닉네임 설정 후 <c>displayName / full_name / name</c>이 동기화됩니다.
    /// </summary>
    [Serializable]
    public sealed class SupabaseUserMetadata
    {
        public string displayName;
        public string full_name;
        public string name;
    }

    /// <summary>
    /// 로그인한 사용자 정보. Auth 응답의 <c>user</c> 필드에서 채워집니다.
    /// </summary>
    [Serializable]
    public sealed class SupabaseUser
    {
        /// <summary>Supabase Auth <c>auth.users.id</c> (JWT <c>sub</c>).</summary>
        public string id;

        public string email;
        public bool is_anonymous;

        /// <summary>
        /// DB <c>profiles.user_id</c> / <c>user_saves.user_id</c>에 넣을 안정 플레이어 id.
        /// OAuth면 응답의 <c>identities[0].identity_data.sub</c>로 채우고, 없으면 <see cref="id"/>와 동일하게 둡니다.
        /// </summary>
        public string player_user_id;

        /// <summary><c>auth.user_metadata</c>. 닉네임 설정 시 <c>displayName</c>이 동기화됩니다.</summary>
        public SupabaseUserMetadata user_metadata;

        /// <summary>
        /// 현재 계정에 연동된 인증 프로바이더 목록 (<c>"google"</c>, <c>"apple"</c>, <c>"email"</c> 등).
        /// 익명 계정은 빈 배열. 로그인 응답의 <c>identities[].provider</c>에서 자동으로 채워집니다.
        /// </summary>
        public string[] linked_providers;

        public string Id => id;
        public string Email => email;
        public bool IsAnonymous => is_anonymous;

        /// <summary><see cref="player_user_id"/>가 비어 있으면 <see cref="id"/>를 반환합니다.</summary>
        public string PlayerUserId =>
            string.IsNullOrWhiteSpace(player_user_id) ? id : player_user_id.Trim();

        /// <summary>현재 세션에 캐시된 표시 이름. 닉네임 설정 성공 후 SDK가 자동 갱신합니다.</summary>
        public string Name =>
            string.IsNullOrWhiteSpace(user_metadata?.displayName) ? string.Empty : user_metadata.displayName.Trim();

        /// <summary>Google 계정이 연동되어 있으면 true.</summary>
        public bool IsLinkedWithGoogle => HasLinkedProvider("google");

        /// <summary>Apple 계정이 연동되어 있으면 true.</summary>
        public bool IsLinkedWithApple => HasLinkedProvider("apple");

        /// <summary>지정한 프로바이더가 연동되어 있으면 true. (<c>"google"</c>, <c>"apple"</c>, <c>"email"</c> 등)</summary>
        private bool HasLinkedProvider(string provider)
        {
            if (linked_providers == null || string.IsNullOrWhiteSpace(provider))
                return false;
            foreach (var p in linked_providers)
                if (string.Equals(p, provider, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }
    }
}