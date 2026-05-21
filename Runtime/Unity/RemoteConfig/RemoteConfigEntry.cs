using System.Threading.Tasks;
using TrueBase.Core.Common;

namespace TrueBase.Unity
{
    /// <summary>
    /// 단일 RemoteConfig 키에 대한 타입 안전 fetch.
    /// </summary>
    public sealed class RemoteConfigEntry<T> where T : class, new()
    {
        private readonly string _key;
        private readonly int _maxStale;

        /// <param name="key">remote_config 테이블의 key 값.</param>
        /// <param name="maxStale">0 초과이면 캐시 유효 시간(초). 0 이하면 기본값(300초) 사용.</param>
        public RemoteConfigEntry(string key, int maxStale = 0)
        {
            _key = key;
            _maxStale = maxStale;
        }

        public string Key => _key;

        public Task<SupabaseResult<T>> FetchAsync() => SupabaseSDK.GetRemoteConfigAsync<T>(_key, _maxStale);

        public async Task<(bool success, T value)> TryFetchAsync()
        {
            var r = await FetchAsync().ConfigureAwait(true);
            return (r.IsSuccess, r.IsSuccess ? r.Data : null);
        }
    }
}
