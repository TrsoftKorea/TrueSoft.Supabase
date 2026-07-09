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

        /// <summary>네이티브 Sign in with Apple UI를 띄웁니다. iOS 외 플랫폼에서는 즉시 onError를 호출합니다.</summary>
        /// <param name="hashedNonce">raw nonce의 SHA-256 해시(hex). 서버는 raw nonce로 identityToken의 nonce 클레임을 검증합니다. null이면 빈 문자열로 전달.</param>
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

        // 아래 OnApple* 메서드는 iOS 네이티브가 UnityPlayer.UnitySendMessage(...)로 호출하는 콜백입니다. 직접 호출하지 마세요.

        /// <summary>네이티브 로그인 성공 콜백. payload는 <c>|||</c> 구분 필드(IdToken|AppleUserId|Email|GivenName|FamilyName).</summary>
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

        /// <summary>네이티브 로그인 실패 콜백.</summary>
        public void OnAppleLoginError(string error)
        {
            _onError?.Invoke(error);
        }

        /// <summary>
        /// payload 필드를 꺼내며 네이티브에서 이스케이프된 구분자(<c>%7C%7C%7C</c>)를 <c>|||</c>로 복원합니다.
        /// </summary>
        /// <param name="parts">구분자로 분리된 필드 배열.</param>
        /// <param name="index">꺼낼 필드 인덱스. 범위를 벗어나면 빈 문자열 반환.</param>
        private static string Unescape(string[] parts, int index)
        {
            if (parts == null || index < 0 || index >= parts.Length)
                return string.Empty;

            return parts[index].Replace("%7C%7C%7C", "|||");
        }
    }
}
