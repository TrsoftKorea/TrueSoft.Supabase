using System.Threading.Tasks;
using Truesoft.Supabase.Core.Common;

namespace Truesoft.Supabase.Unity.RemoteConfig
{
    /// <summary>
    /// 단일 RemoteConfig 키에 대한 타입 안전 fetch.
    /// </summary>
    public sealed class RemoteConfigEntry<T> where T : class, new()
    {
        private readonly string _key;
        private readonly int _maxStaleSeconds;

        /// <param name="key">remote_config 테이블의 key 값.</param>
        /// <param name="maxStaleSeconds">0 초과이면 캐시 유효 시간(초). 0 이하면 기본값(300초) 사용.</param>
        public RemoteConfigEntry(string key, int maxStaleSeconds = 0)
        {
            _key = key;
            _maxStaleSeconds = maxStaleSeconds;
        }

        public string Key => _key;

        public Task<SupabaseResult<T>> FetchAsync() => SupabaseSDK.GetRemoteConfigAsync<T>(_key, _maxStaleSeconds);

        public async Task<(bool success, T value)> TryFetchAsync()
        {
            var r = await FetchAsync().ConfigureAwait(true);
            return (r.IsSuccess, r.IsSuccess ? r.Data : null);
        }
    }
}
