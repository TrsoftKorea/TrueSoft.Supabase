using System;
using System.Threading;
using Newtonsoft.Json;

namespace Truesoft.Supabase.Unity.RemoteConfig
{
    /// <summary>
    /// RemoteConfig 키에 대한 폴링 기반 반응형 구독.
    /// 값이 갱신될 때마다 등록한 콜백을 호출합니다.
    /// <para>
    /// <c>destroyCancellationToken</c>을 전달하면 MonoBehaviour 파괴 시 자동으로 구독이 해제됩니다.
    /// 전달하지 않으면 사용 후 <see cref="Dispose"/>를 직접 호출하세요.
    /// </para>
    /// </summary>
    public sealed class RemoteConfigListener<T> : IDisposable where T : class, new()
    {
        private readonly string _key;
        private readonly Action<string> _onRawChanged;
        private CancellationTokenRegistration _tokenRegistration;
        private bool _disposed;

        /// <param name="key">remote_config 테이블의 key 값.</param>
        /// <param name="pollIntervalSeconds">폴링 주기(초).</param>
        /// <param name="onChange">값이 갱신될 때 호출되는 콜백.</param>
        /// <param name="invokeIfCached">생성 시 캐시에 값이 있으면 즉시 콜백 호출 여부.</param>
        /// <param name="cancellationToken">취소 시 자동 Dispose. <c>destroyCancellationToken</c> 전달 권장.</param>
        public RemoteConfigListener(string key, float pollIntervalSeconds, Action<T> onChange,
            bool invokeIfCached = true, CancellationToken cancellationToken = default)
        {
            _key = key;
            _onRawChanged = json =>
            {
                if (_disposed) return;
                try
                {
                    var value = JsonConvert.DeserializeObject<T>(json);
                    onChange?.Invoke(value);
                }
                catch { }
            };
            if (pollIntervalSeconds > 0f)
                SupabaseSDK.SetRemoteConfigKeyPolling(_key, pollIntervalSeconds);
            SupabaseSDK.SubscribeRemoteConfig(_key, _onRawChanged, invokeIfCached);
            _ = SupabaseSDK.GetRemoteConfigAsync<T>(_key, maxStaleSeconds: 0);

            if (cancellationToken != default)
                _tokenRegistration = cancellationToken.Register(Dispose);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _tokenRegistration.Dispose();
            SupabaseSDK.UnsubscribeRemoteConfig(_key, _onRawChanged);
        }
    }
}
