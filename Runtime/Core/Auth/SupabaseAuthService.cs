using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Truesoft.Supabase.Core.Common;
using Truesoft.Supabase.Core.Http;

namespace Truesoft.Supabase.Core.Auth
{
    public sealed class SupabaseAuthService
    {
        private readonly string _supabaseUrl;
        private readonly string _publishableKey;
        private readonly ISupabaseHttpClient _httpClient;
        private readonly ISupabaseJsonSerializer _jsonSerializer;
        private readonly Dictionary<string, string> _defaultHeaders;

        public SupabaseAuthService(
            string supabaseUrl,
            string publishableKey,
            ISupabaseHttpClient httpClient,
            ISupabaseJsonSerializer jsonSerializer)
        {
            if (string.IsNullOrWhiteSpace(supabaseUrl))
                throw new ArgumentException("supabaseUrl is null or empty", nameof(supabaseUrl));

            if (string.IsNullOrWhiteSpace(publishableKey))
                throw new ArgumentException("publishableKey is null or empty", nameof(publishableKey));

            _supabaseUrl = supabaseUrl.TrimEnd('/');
            _publishableKey = publishableKey.Trim();
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _jsonSerializer = jsonSerializer ?? throw new ArgumentNullException(nameof(jsonSerializer));
            _defaultHeaders = new Dictionary<string, string>
            {
                { "apikey", _publishableKey },
                { "Content-Type", "application/json" }
            };
        }

        /// <summary>Google OAuth ID Token을 사용하여 로그인합니다.</summary>
        /// <param name="idToken">Google에서 발급한 ID Token. iOS/웹 클라이언트에서 사용.</param>
        /// <remarks>
        /// Android에서는 GoogleLoginBridge의 TrySignInWithGoogleAsync()를 사용하세요.
        /// ID Token은 반드시 현재 애플리케이션의 OAuth 2.0 클라이언트 ID로 발급되어야 합니다.
        /// </remarks>
        public async Task<SupabaseResult<SupabaseSession>> SignInWithGoogleIdTokenAsync(string idToken)
        {
            return await SignInWithIdTokenAsync("google", idToken);
        }

        /// <summary>
        /// 게스트(익명) 로그인.
        /// Supabase Auth의 signInAnonymously()와 동일한 개념으로, 사용자가 입력 없이 가입만 수행합니다.
        /// </summary>
        public async Task<SupabaseResult<SupabaseSession>> SignInAnonymouslyAsync()
        {
            // Supabase Auth 익명 가입은 POST /auth/v1/signup 요청에 빈 JSON 바디({})를 보내는 방식으로 동작합니다.
            // 프로젝트 설정에서 "Anonymous sign-ins" 활성화가 필요합니다.
            var url = $"{_supabaseUrl}/auth/v1/signup";

            var bodyJson = _jsonSerializer.ToJson(new AnonymousSignupRequest());

            var response = await _httpClient.SendAsync(
                method: "POST",
                url: url,
                jsonBody: bodyJson,
                headers: _defaultHeaders);

            return HandleSessionResponse(response, "anonymous_signin_failed");
        }

        /// <summary>
        /// 현재 로그인된 사용자(게스트 포함)에 OIDC ID 토큰으로 identity를 링크합니다.
        /// 호스티드 GoTrue는 <c>POST /user/identities/link_token</c> 대신
        /// <c>POST /token?grant_type=id_token</c> 바디에 <c>link_identity: true</c>를 두고,
        /// <c>Authorization: Bearer</c>에 현재 세션 access token을 넣는 방식(supabase-js <c>linkIdentity</c>)을 사용합니다.
        /// </summary>
        public async Task<SupabaseResult<SupabaseSession>> LinkIdentityWithIdTokenAsync(
            string accessToken,
            string provider,
            string idToken,
            string nonce = null,
            string oauthAccessToken = null)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
                return SupabaseResult<SupabaseSession>.Fail("access_token_empty");

            if (string.IsNullOrWhiteSpace(provider))
                return SupabaseResult<SupabaseSession>.Fail("provider_empty");

            if (string.IsNullOrWhiteSpace(idToken))
                return SupabaseResult<SupabaseSession>.Fail("id_token_empty");

            var url = $"{_supabaseUrl}/auth/v1/token?grant_type=id_token";
            var bodyJson = BuildIdTokenLinkIdentityJson(
                provider.Trim(),
                idToken.Trim(),
                nonce,
                string.IsNullOrWhiteSpace(oauthAccessToken) ? null : oauthAccessToken.Trim());

            var headers = new Dictionary<string, string>
            {
                { "apikey", _publishableKey },
                { "Authorization", "Bearer " + accessToken.Trim() },
                { "Content-Type", "application/json" }
            };

            var response = await _httpClient.SendAsync(
                method: "POST",
                url: url,
                jsonBody: bodyJson,
                headers: headers);

            return HandleSessionResponse(response, "link_identity_id_token_failed");
        }

        private static string BuildIdTokenLinkIdentityJson(
            string provider,
            string idToken,
            string nonce,
            string oauthAccessToken)
        {
            var sb = new StringBuilder(256);
            sb.Append("{\"provider\":\"").Append(JsonEscapeForBody(provider))
                .Append("\",\"id_token\":\"").Append(JsonEscapeForBody(idToken))
                .Append("\",\"link_identity\":true");
            if (string.IsNullOrWhiteSpace(nonce) == false)
                sb.Append(",\"nonce\":\"").Append(JsonEscapeForBody(nonce.Trim())).Append('"');
            if (string.IsNullOrWhiteSpace(oauthAccessToken) == false)
                sb.Append(",\"access_token\":\"").Append(JsonEscapeForBody(oauthAccessToken)).Append('"');
            sb.Append('}');
            return sb.ToString();
        }

        private static string JsonEscapeForBody(string s)
        {
            if (string.IsNullOrEmpty(s))
                return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        public async Task<SupabaseResult<SupabaseSession>> SignInWithIdTokenAsync(
            string provider,
            string idToken,
            string nonce = null)
        {
            if (string.IsNullOrWhiteSpace(provider))
                return SupabaseResult<SupabaseSession>.Fail("provider_empty");

            if (string.IsNullOrWhiteSpace(idToken))
                return SupabaseResult<SupabaseSession>.Fail("id_token_empty");

            var url = $"{_supabaseUrl}/auth/v1/token?grant_type=id_token";

            var body = new SignInWithIdTokenRequest
            {
                provider = provider,
                id_token = idToken,
                nonce = nonce
            };

            var bodyJson = _jsonSerializer.ToJson(body);

            var response = await _httpClient.SendAsync(
                method: "POST",
                url: url,
                jsonBody: bodyJson,
                headers: _defaultHeaders);

            return HandleSessionResponse(response, "supabase_auth_failed");
        }

        public async Task<SupabaseResult<SupabaseSession>> RefreshSessionAsync(string refreshToken)
        {
            if (string.IsNullOrWhiteSpace(refreshToken))
                return SupabaseResult<SupabaseSession>.Fail("refresh_token_empty");

            var url = $"{_supabaseUrl}/auth/v1/token?grant_type=refresh_token";

            var body = new RefreshTokenRequest
            {
                refresh_token = refreshToken
            };

            var bodyJson = _jsonSerializer.ToJson(body);

            var response = await _httpClient.SendAsync(
                method: "POST",
                url: url,
                jsonBody: bodyJson,
                headers: _defaultHeaders);

            return HandleSessionResponse(response, "refresh_failed");
        }

        private SupabaseResult<SupabaseSession> HandleSessionResponse(SupabaseHttpResponse response, string defaultError)
        {
            if (response == null)
                return SupabaseResult<SupabaseSession>.Fail("http_response_null");

            if (response.IsSuccess == false)
            {
                var errorMessage = ExtractErrorMessage(response.Body);
                if (string.IsNullOrWhiteSpace(errorMessage))
                    errorMessage = response.ErrorMessage ?? defaultError;

                return SupabaseResult<SupabaseSession>.Fail(errorMessage);
            }

            if (string.IsNullOrWhiteSpace(response.Body))
                return SupabaseResult<SupabaseSession>.Fail("response_body_empty");

            try
            {
                var session = _jsonSerializer.FromJson<SupabaseSession>(response.Body);

                if (session == null)
                    return SupabaseResult<SupabaseSession>.Fail("session_null");

                if (string.IsNullOrWhiteSpace(session.access_token))
                    return SupabaseResult<SupabaseSession>.Fail("access_token_empty");

                // profiles.user_id / user_saves.user_id에 넣을 안정 id를 session에 반영합니다.
                ApplyStablePlayerUserId(session, response.Body);
                session.likely_brand_new_google_signup = ComputeLikelyBrandNewGoogleSignUp(response.Body);

                return SupabaseResult<SupabaseSession>.Success(session);
            }
            catch (Exception e)
            {
                return SupabaseResult<SupabaseSession>.Fail("session_parse_exception:" + e.Message);
            }
        }

        /// <summary>
        /// <c>profiles</c>/<c>user_saves</c>의 <c>user_id</c> 컬럼용. OAuth <c>identity_data.sub</c>가 있으면 사용하고, 없으면 auth user id.
        /// </summary>
        private static void ApplyStablePlayerUserId(SupabaseSession session, string responseBody)
        {
            if (session?.user == null || string.IsNullOrWhiteSpace(responseBody))
                return;

            try
            {
                var jo = JObject.Parse(responseBody);
                var userToken = jo["user"];
                var stable = ExtractStablePlayerUserId(userToken);
                if (string.IsNullOrWhiteSpace(stable) == false)
                    session.user.player_user_id = stable.Trim();
                else if (string.IsNullOrWhiteSpace(session.user.id) == false)
                    session.user.player_user_id = session.user.id.Trim();
            }
            catch
            {
                if (string.IsNullOrWhiteSpace(session.user.id) == false)
                    session.user.player_user_id = session.user.id.Trim();
            }
        }

        /// <summary>
        /// Google OAuth로 <b>방금</b> 생성된 Auth 사용자로 보이면 true (<c>created_at</c> ≈ <c>last_sign_in_at</c>).
        /// </summary>
        private static bool ComputeLikelyBrandNewGoogleSignUp(string responseBody)
        {
            if (string.IsNullOrWhiteSpace(responseBody))
                return false;

            try
            {
                var jo = JObject.Parse(responseBody);
                var user = jo["user"];
                if (user == null || UserHasGoogleIdentity(user) == false)
                    return false;

                return CreatedWithinSecondsOfLastSignIn(user, maxSeconds: 12d);
            }
            catch
            {
                return false;
            }
        }

        private static bool UserHasGoogleIdentity(JToken user)
        {
            if (user["identities"] is not JArray identities || identities.Count == 0)
                return false;

            foreach (var id in identities)
            {
                var p = id?["provider"]?.Value<string>();
                if (string.Equals(p, "google", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static bool CreatedWithinSecondsOfLastSignIn(JToken user, double maxSeconds)
        {
            var cStr = user["created_at"]?.Value<string>();
            var lStr = user["last_sign_in_at"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(cStr) || string.IsNullOrWhiteSpace(lStr))
                return false;

            if (DateTime.TryParse(cStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var c) == false)
                return false;
            if (DateTime.TryParse(lStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var l) == false)
                return false;

            return Math.Abs((l - c).TotalSeconds) <= maxSeconds;
        }

        /// <summary>
        /// <c>auth.users.id</c>(UUID)로부터 규칙에 맞는 익명 기본 displayName을 만듭니다 (<c>Player_</c> + UUID 앞 8자, 소문자).
        /// </summary>
        public static string BuildAnonymousDefaultDisplayNameFromAuthUserId(string authUserId)
        {
            if (string.IsNullOrWhiteSpace(authUserId))
                return "Player_unknown";

            var compact = authUserId.Replace("-", "");
            if (compact.Length < 8)
                compact = compact.PadRight(8, '0');
            return "Player_" + compact.Substring(0, 8).ToLowerInvariant();
        }

        /// <summary>
        /// <c>PUT /auth/v1/user</c>로 <c>user_metadata</c>의 <c>displayName</c>, <c>full_name</c>, <c>name</c>을 같은 값으로 갱신합니다(Google OIDC·대시보드 표시와 맞춤).
        /// </summary>
        public async Task<SupabaseResult<bool>> UpdateUserMetadataDisplayNameAsync(string accessToken, string displayName)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
                return SupabaseResult<bool>.Fail("access_token_empty");

            if (string.IsNullOrWhiteSpace(displayName))
                return SupabaseResult<bool>.Fail("display_name_empty");

            var url = $"{_supabaseUrl}/auth/v1/user";
            var d = displayName.Trim();
            // Google 등은 full_name/name을 쓰고 Studio도 그걸 표시하는 경우가 있어 displayName과 같이 맞춥니다.
            var bodyJson = _jsonSerializer.ToJson(new UpdateUserMetadataBody
            {
                data = new UserMetadataDisplayNamePatch
                {
                    displayName = d,
                    full_name = d,
                    name = d
                }
            });

            var headers = new Dictionary<string, string>
            {
                { "apikey", _publishableKey },
                { "Authorization", "Bearer " + accessToken.Trim() },
                { "Content-Type", "application/json" }
            };

            var response = await _httpClient.SendAsync(
                method: "PUT",
                url: url,
                jsonBody: bodyJson,
                headers: headers);

            if (response == null)
                return SupabaseResult<bool>.Fail("http_response_null");

            if (response.IsSuccess == false)
            {
                var msg = ExtractErrorMessage(response.Body);
                if (string.IsNullOrWhiteSpace(msg))
                    msg = response.ErrorMessage ?? response.Body ?? "auth_update_user_failed";
                return SupabaseResult<bool>.Fail(msg);
            }

            return SupabaseResult<bool>.Success(true);
        }

        private static string ExtractStablePlayerUserId(JToken userToken)
        {
            if (userToken == null)
                return null;

            if (userToken["identities"] is JArray identities && identities.Count > 0)
            {
                var idData = identities[0]?["identity_data"];
                if (idData != null)
                {
                    var sub = idData["sub"]?.Value<string>();
                    if (string.IsNullOrWhiteSpace(sub) == false)
                        return sub;
                }
            }

            return userToken["id"]?.Value<string>();
        }

        private string ExtractErrorMessage(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return null;

            try
            {
                var error = _jsonSerializer.FromJson<SupabaseErrorResponse>(body);

                if (error == null)
                    return null;

                if (string.IsNullOrWhiteSpace(error.msg) == false)
                    return error.msg;

                if (string.IsNullOrWhiteSpace(error.error_description) == false)
                    return error.error_description;

                if (string.IsNullOrWhiteSpace(error.error) == false)
                    return error.error;

                return null;
            }
            catch
            {
                return null;
            }
        }

        [Serializable]
        private sealed class SignInWithIdTokenRequest
        {
            public string provider;
            public string id_token;
            public string nonce;
        }

        [Serializable]
        private sealed class AnonymousSignupRequest
        {
            // JsonUtility로 "{}"를 만들기 위한 용도입니다.
        }

        [Serializable]
        private sealed class RefreshTokenRequest
        {
            public string refresh_token;
        }

        [Serializable]
        private sealed class SupabaseErrorResponse
        {
            public string error;
            public string error_description;
            public string msg;
            public int code;
        }

        [Serializable]
        private sealed class UpdateUserMetadataBody
        {
            public UserMetadataDisplayNamePatch data;
        }

        [Serializable]
        private sealed class UserMetadataDisplayNamePatch
        {
            public string displayName;
            public string full_name;
            public string name;
        }

    }
}