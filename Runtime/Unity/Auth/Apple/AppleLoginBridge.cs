using System;
using UnityEngine;
#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace TrueBase.Unity.Auth.Apple
{
    /// <summary>
    /// iOS 네이티브 Sign in with Apple(<c>ASAuthorizationController</c>) 브릿지입니다.
    /// SHA256 해시된 nonce를 네이티브에 전달하고, 결과(identityToken 등)를 콜백으로 받습니다.
    /// raw nonce 생성·해시는 <see cref="SupabaseSDK"/>에서 처리합니다.
    /// </summary>
    public sealed class AppleLoginBridge : MonoBehaviour
    {
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void TrueSoftAppleLogin_SignIn(string gameObjectName, string hashedNonce);
#endif

        private Action<AppleLoginResult> _onSuccess;
        private Action<string> _onError;

        public void SignIn(string hashedNonce, Action<AppleLoginResult> onSuccess, Action<string> onError)
        {
            _onSuccess = onSuccess;
            _onError = onError;

#if UNITY_IOS && !UNITY_EDITOR
            try
            {
                TrueSoftAppleLogin_SignIn(gameObject.name, hashedNonce ?? string.Empty);
            }
            catch (Exception e)
            {
                _onError?.Invoke("apple_login_bridge_exception:" + e.Message);
            }
#else
            onError?.Invoke("apple_login_ios_only");
#endif
        }

        // 네이티브 -> UnityPlayer.UnitySendMessage(...)
        public void OnAppleLoginSuccess(string payload)
        {
            try
            {
                var parts = payload.Split(new[] { "|||" }, StringSplitOptions.None);
                var result = new AppleLoginResult
                {
                    IdToken = Unescape(parts, 0),
                    AppleUserId = Unescape(parts, 1),
                    Email = Unescape(parts, 2),
                    GivenName = Unescape(parts, 3),
                    FamilyName = Unescape(parts, 4),
                };
                _onSuccess?.Invoke(result);
            }
            catch (Exception e)
            {
                _onError?.Invoke("apple_login_parse_exception:" + e.Message);
            }
        }

        public void OnAppleLoginError(string error)
        {
            _onError?.Invoke(error);
        }

        private static string Unescape(string[] parts, int index)
        {
            if (parts == null || index < 0 || index >= parts.Length)
                return string.Empty;

            return parts[index].Replace("%7C%7C%7C", "|||");
        }
    }
}
