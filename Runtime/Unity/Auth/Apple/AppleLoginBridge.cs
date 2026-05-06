#if TRUESOFT_APPLE_AUTH_AVAILABLE
using System;
using UnityEngine;
using Truesoft.Supabase.Unity;

#if UNITY_IOS && !UNITY_EDITOR
using Apple.AuthenticationServices;
#endif

namespace Truesoft.Supabase.Unity.Auth.Apple
{
    public sealed class AppleLoginBridge : MonoBehaviour
    {
        private Action<AppleLoginResult> _onSuccess;
        private Action<string>           _onError;
        private string                   _pendingRawNonce;

        public void SignIn(Action<AppleLoginResult> onSuccess, Action<string> onError)
        {
            _onSuccess = onSuccess;
            _onError   = onError;

#if UNITY_IOS && !UNITY_EDITOR
            _pendingRawNonce = GenerateNonce();
            var hashedNonce  = HashNonce(_pendingRawNonce);

            var provider = new AuthorizationCodeProvider();
            var request  = provider.CreateRequest();
            request.RequestedScopes = new[] { AuthorizationScope.Email, AuthorizationScope.FullName };
            request.Nonce = hashedNonce;

            var controller = new AuthorizationController(new[] { request });
            controller.PerformRequests(OnAuthCompleted, OnAuthFailed);
#else
            onError?.Invoke("apple_signin_ios_only");
#endif
        }

#if UNITY_IOS && !UNITY_EDITOR
        private void OnAuthCompleted(IAuthorization[] authorizations)
        {
            foreach (var auth in authorizations)
            {
                if (auth.Credential is AppleIDCredential cred)
                {
                    var result = new AppleLoginResult
                    {
                        IdToken     = System.Text.Encoding.UTF8.GetString(cred.IdentityToken),
                        RawNonce    = _pendingRawNonce,
                        UserId      = cred.User,
                        Email       = cred.Email,
                        DisplayName = cred.FullName?.ToString(),
                        GivenName   = cred.FullName?.GivenName,
                        FamilyName  = cred.FullName?.FamilyName,
                    };
                    _onSuccess?.Invoke(result);
                    return;
                }
            }
            _onError?.Invoke("apple_credential_not_found");
        }

        private void OnAuthFailed(AuthorizationError error)
            => _onError?.Invoke("apple_auth_failed:" + error.LocalizedDescription);

        private static string GenerateNonce()
        {
            var bytes = new byte[32];
            using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
            rng.GetBytes(bytes);
            return System.Convert.ToBase64String(bytes)
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static string HashNonce(string rawNonce)
        {
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var bytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(rawNonce));
            return System.BitConverter.ToString(bytes).Replace("-", "").ToLower();
        }
#endif
    }
}
#endif // TRUESOFT_APPLE_AUTH_AVAILABLE
