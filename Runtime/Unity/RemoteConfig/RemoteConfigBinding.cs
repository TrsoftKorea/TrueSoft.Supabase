using System;
using Newtonsoft.Json;

namespace Truesoft.Supabase.Unity.RemoteConfig
{
    /// <summary>
    /// RemoteConfig 키에 대한 폴링 기반 값 바인딩.
    /// 폴링 주기마다 서버에서 값을 갱신하며, Value로 현재 값을 읽습니다.
    /// 시스템 오브젝트에서 사용하는 경우 Dispose 불필요.
    /// </summary>
    public sealed class RemoteConfigBinding<T> : IDisposable where T : class, new()
    {
        private readonly string _key;
        private readonly Action<string> _onRawChanged;
        private T _value;
        private bool _disposed;

        /// <summary>현재 캐시된 값. 첫 fetch 완료 전에는 null.</summary>
        public T Value => _value;

        /// <param name="key">remote_config 테이블의 key 값.</param>
        /// <param name="pollIntervalSeconds">폴링 주기(초).</param>
        public RemoteConfigBinding(string key, float pollIntervalSeconds)
        {
            _key = key;
            _onRawChanged = OnRawValueChanged;
            if (pollIntervalSeconds > 0f)
                SupabaseSDK.SetRemoteConfigKeyPolling(_key, pollIntervalSeconds);
            SupabaseSDK.SubscribeRemoteConfig(_key, _onRawChanged, invokeIfCached: true);
            _ = SupabaseSDK.GetRemoteConfigAsync<T>(_key, maxStaleSeconds: 0);
        }

        private void OnRawValueChanged(string json)
        {
            if (_disposed) return;
            try { _value = JsonConvert.DeserializeObject<T>(json); }
            catch { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            SupabaseSDK.UnsubscribeRemoteConfig(_key, _onRawChanged);
        }
    }
}
