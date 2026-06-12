using System;

namespace TrueBase.Unity.Auth.Google
{
    public sealed class AndroidGoogleLoginProvider
    {
        private readonly GoogleLoginBridge _bridge;
        private readonly string _webClientId;

        public AndroidGoogleLoginProvider(GoogleLoginBridge bridge, string webClientId)
        {
            _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
            _webClientId = webClientId;
        }

        public void SignIn(Action<GoogleLoginResult> onSuccess, Action<string> onError)
        {
            _bridge.SignIn(_webClientId, onSuccess, onError);
        }

        public void SilentSignIn(Action<GoogleLoginResult> onSuccess, Action<string> onError)
        {
            _bridge.SilentSignIn(_webClientId, onSuccess, onError);
        }

        public void SignOut(Action onComplete, Action<string> onError)
        {
            _bridge.SignOut(onComplete, onError);
        }
    }
}