using System;

namespace TrueBase.Unity.Auth.Google
{
    /// <summary>
    /// <see cref="GoogleLoginBridge"/>에 웹 클라이언트 ID를 고정해 두고 호출을 위임하는 래퍼입니다.
    /// 호출부에서 매번 클라이언트 ID를 넘기지 않아도 됩니다.
    /// </summary>
    internal sealed class AndroidGoogleLoginProvider
    {
        private readonly GoogleLoginBridge _bridge;
        private readonly string _webClientId;

        /// <param name="bridge">씬에 존재하는 <see cref="GoogleLoginBridge"/>. null이면 예외.</param>
        /// <param name="webClientId">Google Cloud Console의 웹 애플리케이션 OAuth 클라이언트 ID.</param>
        public AndroidGoogleLoginProvider(GoogleLoginBridge bridge, string webClientId)
        {
            _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
            _webClientId = webClientId;
        }

        /// <summary>계정 선택 UI를 띄워 Google 로그인을 수행합니다.</summary>
        public void SignIn(Action<GoogleLoginResult> onSuccess, Action<string> onError)
        {
            _bridge.SignIn(_webClientId, onSuccess, onError);
        }

        /// <inheritdoc cref="GoogleLoginBridge.SilentSignIn"/>
        public void SilentSignIn(Action<GoogleLoginResult> onSuccess, Action<string> onError)
        {
            _bridge.SilentSignIn(_webClientId, onSuccess, onError);
        }

        /// <summary>네이티브 Google 세션을 로그아웃합니다.</summary>
        public void SignOut(Action onComplete, Action<string> onError)
        {
            _bridge.SignOut(onComplete, onError);
        }
    }
}