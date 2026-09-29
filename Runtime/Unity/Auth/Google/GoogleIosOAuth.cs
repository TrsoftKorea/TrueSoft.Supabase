using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using TrueBase.Core.Common;
using TrueBase.Core.Http;
using UnityEngine;

namespace TrueBase.Unity.Auth.Google
{
    /// <summary>
    /// iOS 구글 로그인 한 번 분량의 OAuth 상태(PKCE·state)와 토큰 교환입니다.
    /// 로그인 창은 네이티브(<c>TrueSoftGoogleLogin.mm</c>)가 열고, 여기서는 URL을 만들고 돌아온 코드를 ID 토큰으로 바꿉니다.
    /// </summary>
    /// <remarks>
    /// 구글은 iOS 유형 OAuth 클라이언트에 client_secret 을 요구하지 않는다. 대신 PKCE 로 코드 가로채기를 막는다.
    /// nonce 는 넣지 않는다 — 안드로이드 경로와 같게, nonce 없는 토큰을 nonce 없이 Supabase 에 넘긴다.
    /// </remarks>
    internal sealed class GoogleIosOAuth
    {
        private const string AuthorizeEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
        private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
        private const string RevokeEndpoint = "https://oauth2.googleapis.com/revoke";
        private const string ClientIdSuffix = ".apps.googleusercontent.com";
        private const string SchemePrefix = "com.googleusercontent.apps.";
        private const int HttpTimeoutSeconds = 15;

        private readonly string _clientId;
        private readonly string _verifier;
        private readonly string _state;
        private readonly string _redirectUri;

        /// <summary>네이티브에 넘길 구글 인증 URL.</summary>
        public string AuthorizeUrl { get; }

        /// <summary>로그인 창이 돌아올 URL 스킴. 클라이언트 ID를 뒤집은 값입니다.</summary>
        public string CallbackScheme { get; }

        /// <summary>네이티브 메시지로 이 요청을 가려내는 번호. state와 같은 값이라 <c>|</c>가 들어가지 않습니다.</summary>
        public string RequestId => _state;

        /// <param name="clientId"><see cref="IsValidClientId"/>를 통과한 iOS OAuth 클라이언트 ID.</param>
        public GoogleIosOAuth(string clientId)
        {
            _clientId = clientId.Trim();
            _verifier = RandomBase64Url(32);
            _state = RandomBase64Url(16);
            CallbackScheme = SchemePrefix + _clientId.Substring(0, _clientId.Length - ClientIdSuffix.Length);
            _redirectUri = CallbackScheme + ":/oauth2redirect";

            string challenge;
            using (var sha = SHA256.Create())
                challenge = Base64Url(sha.ComputeHash(Encoding.ASCII.GetBytes(_verifier)));

            AuthorizeUrl = AuthorizeEndpoint + "?" + FormEncode(new Dictionary<string, string>
            {
                ["client_id"] = _clientId,
                ["redirect_uri"] = _redirectUri,
                ["response_type"] = "code",
                ["scope"] = "openid email profile",
                ["code_challenge"] = challenge,
                ["code_challenge_method"] = "S256",
                ["state"] = _state,
                // 안드로이드처럼 매번 계정을 고르게 한다. 없으면 사파리에 남은 계정으로 바로 넘어가 계정을 바꿀 수 없다.
                ["prompt"] = "select_account",
            });
        }

        /// <summary>구글 콘솔이 발급한 iOS 클라이언트 ID 형식(<c>….apps.googleusercontent.com</c>)인지 확인합니다.</summary>
        public static bool IsValidClientId(string clientId)
        {
            if (string.IsNullOrWhiteSpace(clientId))
                return false;

            var trimmed = clientId.Trim();
            return trimmed.Length > ClientIdSuffix.Length &&
                   trimmed.EndsWith(ClientIdSuffix, StringComparison.Ordinal);
        }

        /// <summary>
        /// 로그인 창이 돌려준 콜백 URL에서 인가 코드를 꺼냅니다. 실패하면 <paramref name="error"/>에 사유가 담깁니다.
        /// </summary>
        public bool TryReadCode(string callbackUrl, out string code, out string error)
        {
            code = null;
            var query = ParseQuery(callbackUrl);

            query.TryGetValue("state", out var state);
            if (!string.Equals(state, _state, StringComparison.Ordinal))
            {
                Debug.LogWarning("[Supabase.Auth.Google] iOS 로그인 응답의 state가 요청과 다릅니다. 응답을 버립니다.");
                error = SupabaseErrorCode.GoogleSignInFailed;
                return false;
            }

            if (query.TryGetValue("error", out var oauthError))
            {
                // 동의 화면에서 "취소"를 누르면 access_denied 로 돌아온다.
                if (oauthError == "access_denied")
                {
                    error = SupabaseErrorCode.GoogleSignInCancelled;
                    return false;
                }

                Debug.LogWarning($"[Supabase.Auth.Google] iOS 로그인 거절: {oauthError}");
                error = SupabaseErrorCode.GoogleSignInFailed;
                return false;
            }

            if (!query.TryGetValue("code", out code) || string.IsNullOrEmpty(code))
            {
                Debug.LogWarning("[Supabase.Auth.Google] iOS 로그인 응답에 인가 코드가 없습니다.");
                error = SupabaseErrorCode.GoogleSignInFailed;
                return false;
            }

            error = null;
            return true;
        }

        /// <summary>인가 코드를 구글 토큰 엔드포인트에서 ID 토큰으로 바꿉니다. 실패하면 <c>Result</c>가 null입니다.</summary>
        public async Task<(GoogleLoginResult Result, string Error)> ExchangeAsync(string code)
        {
            var body = FormEncode(new Dictionary<string, string>
            {
                ["code"] = code,
                ["client_id"] = _clientId,
                ["code_verifier"] = _verifier,
                ["grant_type"] = "authorization_code",
                ["redirect_uri"] = _redirectUri,
            });

            // 인가 코드는 한 번만 쓸 수 있다. 응답만 끊긴 뒤 같은 코드로 다시 보내면 invalid_grant 로 거절되니 재시도하지 않는다.
            var response = await PostFormAsync(TokenEndpoint, body, maxRetries: 0);
            if (!response.IsSuccess)
            {
                Debug.LogWarning($"[Supabase.Auth.Google] iOS 토큰 교환 실패: HTTP {response.StatusCode} {response.Body} {response.ErrorMessage}");
                return (null, response.StatusCode == 0 ? SupabaseErrorCode.NetworkError : SupabaseErrorCode.GoogleSignInFailed);
            }

            string idToken;
            string accessToken;
            try
            {
                var json = JObject.Parse(response.Body ?? "");
                idToken = (string)json["id_token"];
                accessToken = (string)json["access_token"];
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Supabase.Auth.Google] iOS 토큰 응답을 읽지 못했습니다: {e.Message}");
                return (null, SupabaseErrorCode.GoogleSignInFailed);
            }

            if (string.IsNullOrWhiteSpace(idToken))
                return (null, SupabaseErrorCode.GoogleIdTokenEmpty);

            var result = new GoogleLoginResult
            {
                IdToken = idToken,
                AccessToken = accessToken ?? string.Empty,
            };
            FillProfile(result, idToken);
            return (result, null);
        }

        /// <summary>액세스 토큰을 구글에서 회수합니다. 성공하면 null, 실패하면 사유를 돌려줍니다.</summary>
        public static async Task<string> RevokeAsync(string accessToken)
        {
            var response = await PostFormAsync(RevokeEndpoint, FormEncode(new Dictionary<string, string>
            {
                ["token"] = accessToken,
            }));
            if (response.IsSuccess)
                return null;

            Debug.LogWarning($"[Supabase.Auth.Google] iOS 접근 권한 회수 실패: HTTP {response.StatusCode} {response.Body}");
            return "google_revoke_failed";
        }

        private static Task<SupabaseHttpResponse> PostFormAsync(string url, string body, int maxRetries = 2) =>
            new UnitySupabaseHttpClient(HttpTimeoutSeconds, maxRetries).SendAsync(
                "POST",
                url,
                body,
                new Dictionary<string, string> { ["Content-Type"] = "application/x-www-form-urlencoded" });

        /// <summary>ID 토큰 본문에서 계정 정보를 채웁니다. 서명 검증은 Supabase가 하므로 여기서는 읽기만 합니다.</summary>
        private static void FillProfile(GoogleLoginResult result, string idToken)
        {
            try
            {
                var parts = idToken.Split('.');
                if (parts.Length < 2)
                    return;

                var claims = JObject.Parse(Encoding.UTF8.GetString(FromBase64Url(parts[1])));
                result.GoogleUserId = (string)claims["sub"] ?? string.Empty;
                result.Name = (string)claims["name"] ?? string.Empty;
                result.GivenName = (string)claims["given_name"] ?? string.Empty;
                result.FamilyName = (string)claims["family_name"] ?? string.Empty;
                result.ProfileImageUrl = (string)claims["picture"] ?? string.Empty;
            }
            catch (Exception e)
            {
                // 프로필은 부가 정보다. 못 읽어도 로그인은 ID 토큰만으로 진행된다.
                Debug.LogWarning($"[Supabase.Auth.Google] iOS ID 토큰 본문을 읽지 못했습니다: {e.Message}");
            }
        }

        private static Dictionary<string, string> ParseQuery(string url)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(url))
                return result;

            var start = url.IndexOf('?');
            if (start < 0)
                return result;

            var end = url.IndexOf('#', start);
            var query = end < 0 ? url.Substring(start + 1) : url.Substring(start + 1, end - start - 1);
            foreach (var pair in query.Split('&'))
            {
                if (pair.Length == 0)
                    continue;

                var eq = pair.IndexOf('=');
                var key = Unescape(eq < 0 ? pair : pair.Substring(0, eq));
                var value = eq < 0 ? string.Empty : Unescape(pair.Substring(eq + 1));
                result[key] = value;
            }

            return result;
        }

        private static string Unescape(string s) => Uri.UnescapeDataString(s.Replace('+', ' '));

        private static string FormEncode(Dictionary<string, string> values)
        {
            var sb = new StringBuilder();
            foreach (var pair in values)
            {
                if (sb.Length > 0)
                    sb.Append('&');
                sb.Append(Uri.EscapeDataString(pair.Key)).Append('=').Append(Uri.EscapeDataString(pair.Value ?? ""));
            }

            return sb.ToString();
        }

        private static string RandomBase64Url(int byteCount)
        {
            var bytes = new byte[byteCount];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(bytes);
            return Base64Url(bytes);
        }

        private static string Base64Url(byte[] bytes) =>
            Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private static byte[] FromBase64Url(string s)
        {
            var b64 = s.Replace('-', '+').Replace('_', '/');
            switch (b64.Length % 4)
            {
                case 2: b64 += "=="; break;
                case 3: b64 += "="; break;
            }

            return Convert.FromBase64String(b64);
        }
    }
}
