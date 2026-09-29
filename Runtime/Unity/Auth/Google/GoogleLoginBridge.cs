using System;
using TrueBase.Core.Common;
using UnityEngine;
#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace TrueBase.Unity.Auth.Google
{
    /// <summary>
    /// 네이티브 Google 로그인 브릿지입니다. Android는 Play Services 플러그인(AAR), iOS는 기본 로그인 창
    /// (<c>TrueSoftGoogleLogin.mm</c>)을 씁니다. 네이티브 호출 결과는 <c>UnitySendMessage</c>로 이 컴포넌트의
    /// <c>OnGoogle*</c> 콜백에 전달되므로 씬에 배치된 GameObject에 붙어 있어야 합니다. 그 밖의 플랫폼에서는 즉시 onError를 호출합니다.
    /// </summary>
    internal sealed class GoogleLoginBridge : MonoBehaviour
    {
        private const string PluginClass = "com.truesoft.googleloginplugin.GoogleLoginPlugin";
        private const string LogTag = "[Supabase.Auth.Google]";

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void TrueSoftGoogleLogin_Start(string gameObjectName, string authUrl, string callbackScheme, string requestId);
#endif

        // Android 전용. 플러그인 콜백이 요청을 구분하지 않아 마지막 호출의 콜백만 들고 있다.
        private Action<GoogleLoginResult> _onSuccess;
        private Action<string> _onError;
        private Action _onLogout;
        private Action _onRevoke;

        // iOS: 진행 중인 로그인 한 건. 네이티브 메시지에 요청 번호가 붙어 와서, 번호가 다르면 지난 요청의 늦은 결과로 보고 버린다.
        private IosRequest _iosRequest;
        // iOS: 접근 권한 회수에 쓸 마지막 액세스 토큰. iOS에는 이것을 대신 들고 있는 네이티브 계정 상태가 없다.
        private string _iosAccessToken;

        private sealed class IosRequest
        {
            public readonly GoogleIosOAuth Flow;
            public readonly Action<GoogleLoginResult> OnSuccess;
            public readonly Action<string> OnError;
            // 창이 결과를 돌려준 뒤(토큰 교환 중)에도 진행 중으로 남는다. 같은 결과가 두 번 처리되지 않게 표시만 한다.
            public bool Answered;

            public IosRequest(GoogleIosOAuth flow, Action<GoogleLoginResult> onSuccess, Action<string> onError)
            {
                Flow = flow;
                OnSuccess = onSuccess;
                OnError = onError;
            }
        }

        /// <summary>계정 선택 UI를 띄워 Google 로그인을 수행합니다.</summary>
        /// <param name="clientId">
        /// Android는 Google Cloud Console의 웹 애플리케이션 OAuth 클라이언트 ID, iOS는 iOS OAuth 클라이언트 ID.
        /// 비어 있으면 즉시 onError.
        /// </param>
        public void SignIn(string clientId, Action<GoogleLoginResult> onSuccess, Action<string> onError)
        {
            if (string.IsNullOrWhiteSpace(clientId))
            {
                onError?.Invoke("web_client_id_empty");
                return;
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            _onSuccess = onSuccess;
            _onError = onError;
            try
            {
                using var plugin = new AndroidJavaClass(PluginClass);
                plugin.CallStatic("signIn", gameObject.name, clientId, true);
            }
            catch (Exception e)
            {
                _onError?.Invoke("google_login_bridge_exception:" + e.Message);
            }
#elif UNITY_IOS && !UNITY_EDITOR
            StartIos(clientId, onSuccess, onError);
#else
            onError?.Invoke("google_login_mobile_only");
#endif
        }

#if UNITY_IOS && !UNITY_EDITOR
        private void StartIos(string clientId, Action<GoogleLoginResult> onSuccess, Action<string> onError)
        {
            // 창이 떠 있거나 토큰 교환 중이면 새 창을 열지 않고 이 호출만 돌려보낸다.
            // 앞 창을 닫고 새로 열면 닫히는 중인 창 때문에 새 창이 못 뜨거나 iOS 확인 창이 두 번 뜬다.
            // 창은 언제나 완료 핸들러로 끝나므로(취소 포함) 진행 중 표시가 남아 잠기지 않는다.
            if (_iosRequest != null)
            {
                onError?.Invoke(SupabaseErrorCode.OAuthLoginInProgress);
                return;
            }

            try
            {
                var flow = new GoogleIosOAuth(clientId);
                _iosRequest = new IosRequest(flow, onSuccess, onError);
                TrueSoftGoogleLogin_Start(gameObject.name, flow.AuthorizeUrl, flow.CallbackScheme, flow.RequestId);
            }
            catch (Exception e)
            {
                _iosRequest = null;
                Debug.LogWarning($"{LogTag} iOS 로그인 창을 열지 못했습니다: {e.Message}");
                onError?.Invoke(SupabaseErrorCode.GoogleSignInFailed);
            }
        }
#endif

        /// <summary>UI 없이 마지막으로 로그인한 Google 계정의 ID 토큰을 반환합니다. 저장된 계정이 없으면 onError를 호출합니다. Android 전용.</summary>
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
#if UNITY_ANDROID && !UNITY_EDITOR
            _onError = onError;
            try
            {
                using var plugin = new AndroidJavaClass(PluginClass);
                plugin.CallStatic("signOut", gameObject.name);
            }
            catch (Exception e)
            {
                _onError?.Invoke("google_logout_bridge_exception:" + e.Message);
            }
#elif UNITY_IOS && !UNITY_EDITOR
            // iOS 로그인 창은 앱에 계정 상태를 남기지 않는다. 다음 로그인도 prompt=select_account 로 계정을 다시 고른다.
            _iosAccessToken = null;
            onComplete?.Invoke();
#else
            onError?.Invoke("google_logout_android_only");
#endif
        }

        /// <summary>앱에 부여된 Google 계정 접근 권한을 회수합니다. 다음 로그인 시 동의 화면이 다시 표시됩니다.</summary>
        public void RevokeAccess(Action onComplete, Action<string> onError)
        {
            _onRevoke = onComplete;
#if UNITY_ANDROID && !UNITY_EDITOR
            _onError = onError;
            try
            {
                using var plugin = new AndroidJavaClass(PluginClass);
                plugin.CallStatic("revokeAccess", gameObject.name);
            }
            catch (Exception e)
            {
                _onError?.Invoke("google_revoke_bridge_exception:" + e.Message);
            }
#elif UNITY_IOS && !UNITY_EDITOR
            RevokeIosAsync(onComplete, onError);
#else
            onError?.Invoke("google_revoke_android_only");
#endif
        }

        /// <summary>
        /// iOS: 이번 실행에서 받은 액세스 토큰으로 구글 쪽 권한을 회수합니다.
        /// 토큰이 없으면(앱을 다시 켠 뒤 저장된 세션으로 들어온 경우 등) 회수할 수단이 없어 <c>GoogleRevokeNoToken</c>으로 실패합니다.
        /// </summary>
        private async void RevokeIosAsync(Action onComplete, Action<string> onError)
        {
            var token = _iosAccessToken;
            if (string.IsNullOrEmpty(token))
            {
                onError?.Invoke(SupabaseErrorCode.GoogleRevokeNoToken);
                return;
            }

            try
            {
                var error = await GoogleIosOAuth.RevokeAsync(token);
                if (error != null)
                {
                    onError?.Invoke(error);
                    return;
                }

                _iosAccessToken = null;
                onComplete?.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{LogTag} iOS 접근 권한 회수 중 예외: {e.Message}");
                onError?.Invoke("google_revoke_failed");
            }
        }

        // 아래 OnGoogle* 메서드는 네이티브(Android AAR·iOS .mm)가 UnitySendMessage로 호출하는 콜백입니다. 직접 호출하지 마세요.

        /// <summary>iOS 로그인 창이 돌아온 콜백. message는 <c>요청번호|||콜백 URL</c>. 인가 코드를 ID 토큰으로 바꿔 성공 콜백으로 넘깁니다.</summary>
        public async void OnGoogleIosRedirect(string message)
        {
            var request = TakeIosRequest(message, out var callbackUrl);
            if (request == null)
                return;

            try
            {
                if (!request.Flow.TryReadCode(callbackUrl, out var code, out var readError))
                {
                    FailIos(request, readError);
                    return;
                }

                // 교환이 끝날 때까지 요청을 진행 중으로 둔다 — 그사이 들어온 로그인 호출이 새 창을 열지 않게.
                var (result, exchangeError) = await request.Flow.ExchangeAsync(code);
                if (result == null)
                {
                    FailIos(request, exchangeError);
                    return;
                }

                _iosAccessToken = result.AccessToken;
                FinishIos(request);
                request.OnSuccess?.Invoke(result);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{LogTag} iOS 로그인 응답 처리 중 예외: {e.Message}");
                FailIos(request, SupabaseErrorCode.GoogleSignInFailed);
            }
        }

        /// <summary>iOS 로그인 창 실패 콜백. message는 <c>요청번호|||오류</c>.</summary>
        public void OnGoogleIosError(string message)
        {
            var request = TakeIosRequest(message, out var error);
            if (request == null)
                return;

            if (error != SupabaseErrorCode.GoogleSignInCancelled)
            {
                Debug.LogWarning($"{LogTag} iOS 로그인 창 오류: {error}");
                error = SupabaseErrorCode.GoogleSignInFailed;
            }

            FailIos(request, error);
        }

        /// <summary>
        /// 메시지의 요청 번호가 진행 중인 iOS 요청과 같고 아직 답을 받지 않았으면 그 요청을 돌려주고, 아니면 null.
        /// 요청은 <see cref="FinishIos"/>가 불릴 때까지 진행 중으로 남습니다.
        /// </summary>
        private IosRequest TakeIosRequest(string message, out string value)
        {
            value = null;
            var request = _iosRequest;
            if (request == null || request.Answered || string.IsNullOrEmpty(message))
                return null;

            var sep = message.IndexOf("|||", StringComparison.Ordinal);
            if (sep < 0 || !string.Equals(message.Substring(0, sep), request.Flow.RequestId, StringComparison.Ordinal))
                return null;

            request.Answered = true;
            value = message.Substring(sep + 3);
            return request;
        }

        // 콜백보다 먼저 비운다 — 콜백 안에서 곧바로 다시 로그인을 불러도 "진행 중"으로 막히지 않게.
        private void FinishIos(IosRequest request)
        {
            if (ReferenceEquals(_iosRequest, request))
                _iosRequest = null;
        }

        private void FailIos(IosRequest request, string error)
        {
            FinishIos(request);
            request.OnError?.Invoke(error);
        }

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
