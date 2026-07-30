using System;
using UnityEngine;

namespace TrueBase.Unity.Auth.Google
{
    /// <summary>
    /// Android 네이티브 Google 로그인 플러그인(AAR) 브릿지입니다.
    /// 네이티브 호출 결과는 <c>UnitySendMessage</c>로 이 컴포넌트의 <c>OnGoogle*</c> 콜백에 전달되므로
    /// 씬에 배치된 GameObject에 붙어 있어야 합니다. Android 외 플랫폼에서는 즉시 onError를 호출합니다.
    /// </summary>
    internal sealed class GoogleLoginBridge : MonoBehaviour
    {
        private const string PluginClass = "com.truesoft.googleloginplugin.GoogleLoginPlugin";

        private Action<GoogleLoginResult> _onSuccess;
        private Action<string> _onError;
        private Action _onLogout;
        private Action _onRevoke;

        /// <summary>계정 선택 UI를 띄워 Google 로그인을 수행합니다.</summary>
        /// <param name="webClientId">Google Cloud Console의 웹 애플리케이션 OAuth 클라이언트 ID. 비어 있으면 <c>web_client_id_empty</c>로 실패.</param>
        public void SignIn(string webClientId, Action<GoogleLoginResult> onSuccess, Action<string> onError)
        {
            if (string.IsNullOrWhiteSpace(webClientId))
            {
                onError?.Invoke("web_client_id_empty");
                return;
            }

            _onSuccess = onSuccess;
            _onError = onError;

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var plugin = new AndroidJavaClass(PluginClass);
                plugin.CallStatic("signIn", gameObject.name, webClientId, true);
            }
            catch (Exception e)
            {
                _onError?.Invoke("google_login_bridge_exception:" + e.Message);
            }
#else
            onError?.Invoke("google_login_android_only");
#endif
        }

        /// <summary>UI 없이 마지막으로 로그인한 Google 계정의 ID 토큰을 반환합니다. 저장된 계정이 없으면 onError를 호출합니다.</summary>
        /// <param name="webClientId">Google Cloud Console의 웹 애플리케이션 OAuth 클라이언트 ID. 비어 있으면 <c>web_client_id_empty</c>로 실패.</param>
        public void SilentSignIn(string webClientId, Action<GoogleLoginResult> onSuccess, Action<string> onError)
        {
            if (string.IsNullOrWhiteSpace(webClientId))
            {
                onError?.Invoke("web_client_id_empty");
                return;
            }

            _onSuccess = onSuccess;
            _onError = onError;

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var plugin = new AndroidJavaClass(PluginClass);
                plugin.CallStatic("silentSignIn", gameObject.name, webClientId);
            }
            catch (Exception e)
            {
                _onError?.Invoke("google_silent_signin_exception:" + e.Message);
            }
#else
            onError?.Invoke("google_login_android_only");
#endif
        }

        /// <summary>네이티브 Google 세션을 로그아웃합니다.</summary>
        public void SignOut(Action onComplete, Action<string> onError)
        {
            _onLogout = onComplete;
            _onError = onError;

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var plugin = new AndroidJavaClass(PluginClass);
                plugin.CallStatic("signOut", gameObject.name);
            }
            catch (Exception e)
            {
                _onError?.Invoke("google_logout_bridge_exception:" + e.Message);
            }
#else
            onError?.Invoke("google_logout_android_only");
#endif
        }

        /// <summary>앱에 부여된 Google 계정 접근 권한을 회수합니다. 다음 로그인 시 동의 화면이 다시 표시됩니다.</summary>
        public void RevokeAccess(Action onComplete, Action<string> onError)
        {
            _onRevoke = onComplete;
            _onError = onError;

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var plugin = new AndroidJavaClass(PluginClass);
                plugin.CallStatic("revokeAccess", gameObject.name);
            }
            catch (Exception e)
            {
                _onError?.Invoke("google_revoke_bridge_exception:" + e.Message);
            }
#else
            onError?.Invoke("google_revoke_android_only");
#endif
        }

        // 아래 OnGoogle* 메서드는 Android AAR이 UnityPlayer.UnitySendMessage(...)로 호출하는 콜백입니다. 직접 호출하지 마세요.

        /// <summary>네이티브 로그인 성공 콜백. payload는 <c>|||</c> 구분 필드(IdToken|GoogleUserId|Name|GivenName|FamilyName|ProfileImageUrl|AccessToken).</summary>
        public void OnGoogleLoginSuccess(string payload)
        {
            try
            {
                var parts = payload.Split(new[] { "|||" }, StringSplitOptions.None);

                var result = new GoogleLoginResult
                {
                    IdToken = Unescape(parts, 0),
                    GoogleUserId = Unescape(parts, 1),
                    Name = Unescape(parts, 2),
                    GivenName = Unescape(parts, 3),
                    FamilyName = Unescape(parts, 4),
                    ProfileImageUrl = Unescape(parts, 5),
                    AccessToken = Unescape(parts, 6),
                };

                _onSuccess?.Invoke(result);
            }
            catch (Exception e)
            {
                _onError?.Invoke("google_login_parse_exception:" + e.Message);
            }
        }

        /// <summary>네이티브 로그인 실패 콜백.</summary>
        public void OnGoogleLoginError(string error)
        {
            _onError?.Invoke(error);
        }

        /// <summary>네이티브 로그아웃 완료 콜백.</summary>
        public void OnGoogleLogout(string _)
        {
            _onLogout?.Invoke();
        }

        /// <summary>네이티브 접근 권한 회수 완료 콜백.</summary>
        public void OnGoogleRevokeAccess(string _)
        {
            _onRevoke?.Invoke();
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