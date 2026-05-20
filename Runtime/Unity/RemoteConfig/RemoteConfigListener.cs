using System;
using Newtonsoft.Json;

namespace Truesoft.Supabase.Unity
{
    /// <summary>
    /// RemoteConfig 키에 대한 폴링 기반 반응형 구독.
    /// 값이 갱신될 때마다 등록한 콜백을 호출합니다.
    /// 시스템 오브젝트에서 사용하는 경우 Dispose 불필요.
    /// </summary>
    public sealed class RemoteConfigListener<T> : IDisposable where T : class, new()
    {
        private readonly string _key;
        private readonly Action<string> _onRawChanged;
        private bool _disposed;

        /// <param name="key">remote_config 테이블의 key 값.</param>
        /// <param name="pollIntervalSeconds">폴링 주기(초).</param>
        /// <param name="onChange">값이 갱신될 때 호출되는 콜백.</param>
        /// <param name="invokeIfCached">생성 시 캐시에 값이 있으면 즉시 콜백 호출 여부.</param>
        public RemoteConfigListener(string key, float pollIntervalSeconds, Action<T> onChange,
            bool invokeIfCached = true)
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
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            SupabaseSDK.UnsubscribeRemoteConfig(_key, _onRawChanged);
        }
    }
}
