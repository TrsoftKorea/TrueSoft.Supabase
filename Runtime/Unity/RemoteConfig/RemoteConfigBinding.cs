using System;
using System.Threading;
using UnityEngine;

namespace Truesoft.Supabase.Unity.RemoteConfig
{
    /// <summary>
    /// RemoteConfig 키에 대한 폴링 기반 값 바인딩.
    /// 폴링 주기마다 서버에서 값을 갱신하며, Value로 현재 값을 읽습니다.
    /// <para>
    /// <c>destroyCancellationToken</c>을 전달하면 MonoBehaviour 파괴 시 자동으로 구독이 해제됩니다.
    /// 전달하지 않으면 사용 후 <see cref="Dispose"/>를 직접 호출하세요.
    /// </para>
    /// </summary>
    public sealed class RemoteConfigBinding<T> : IDisposable where T : class, new()
    {
        private readonly string _key;
        private readonly Action<string> _onRawChanged;
        private CancellationTokenRegistration _tokenRegistration;
        private T _value;
        private bool _disposed;

        /// <summary>현재 캐시된 값. 첫 fetch 완료 전에는 null.</summary>
        public T Value => _value;

        /// <param name="key">remote_config 테이블의 key 값.</param>
        /// <param name="pollIntervalSeconds">폴링 주기(초).</param>
        /// <param name="cancellationToken">취소 시 자동 Dispose. <c>destroyCancellationToken</c> 전달 권장.</param>
        public RemoteConfigBinding(string key, float pollIntervalSeconds,
            CancellationToken cancellationToken = default)
        {
            _key = key;
            _onRawChanged = OnRawValueChanged;
            if (pollIntervalSeconds > 0f)
                SupabaseSDK.SetRemoteConfigKeyPolling(_key, pollIntervalSeconds);
            SupabaseSDK.SubscribeRemoteConfig(_key, _onRawChanged, invokeIfCached: true);
            _ = SupabaseSDK.GetRemoteConfigAsync<T>(_key, maxStaleSeconds: 0);

            if (cancellationToken != default)
                _tokenRegistration = cancellationToken.Register(Dispose);
        }

        private void OnRawValueChanged(string json)
        {
            if (_disposed) return;
            try { _value = JsonUtility.FromJson<T>(json); }
            catch { }
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
