#if TRUESOFT_APPLE_AUTH_AVAILABLE
using System;
using Truesoft.Supabase.Unity;

namespace Truesoft.Supabase.Unity.Auth.Apple
{
    public sealed class iOSAppleLoginProvider
    {
        private readonly AppleLoginBridge _bridge;

        public iOSAppleLoginProvider(AppleLoginBridge bridge)
        {
            _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        }

        public void SignIn(Action<AppleLoginResult> onSuccess, Action<string> onError)
            => _bridge.SignIn(onSuccess, onError);
    }
}
#endif // TRUESOFT_APPLE_AUTH_AVAILABLE
