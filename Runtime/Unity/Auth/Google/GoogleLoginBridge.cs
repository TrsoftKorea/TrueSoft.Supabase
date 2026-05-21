using System;
using UnityEngine;

namespace TrueBase.Unity.Auth.Google
{
    public sealed class GoogleLoginBridge : MonoBehaviour
    {
        private const string PluginClass = "com.truesoft.googleloginplugin.GoogleLoginPlugin";

        private Action<GoogleLoginResult> _onSuccess;
        private Action<string> _onError;
        private Action _onLogout;

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

        // Android AAR -> UnityPlayer.UnitySendMessage(...)
        public void OnGoogleLoginSuccess(string payload)
        {
            try
            {
                var parts = payload.Split(new[] { "|||" }, StringSplitOptions.None);

                var result = new GoogleLoginResult
                {
                    IdToken = Unescape(parts, 0),
                    GoogleUserId = Unescape(parts, 1),
                    DisplayName = Unescape(parts, 2),
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

        public void OnGoogleLoginError(string error)
        {
            _onError?.Invoke(error);
        }

        public void OnGoogleLogout(string _)
        {
            _onLogout?.Invoke();
        }

        private static string Unescape(string[] parts, int index)
        {
            if (parts == null || index < 0 || index >= parts.Length)
                return string.Empty;

            return parts[index].Replace("%7C%7C%7C", "|||");
        }
    }
}