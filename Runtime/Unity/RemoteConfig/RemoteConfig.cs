using System;
using System.Threading.Tasks;

namespace TrueBase.Unity
{
    /// <summary>
    /// <typeparamref name="T"/>의 <see cref="RemoteConfigKeyAttribute"/>에서 키를 자동으로 읽어
    /// Remote Config 접근자를 생성하는 제네릭 정적 클래스입니다.
    /// </summary>
    /// <remarks>
    /// <typeparamref name="T"/>에 <c>[RemoteConfigKey("key")]</c> 어트리뷰트가 없으면
    /// 첫 호출 시 <see cref="InvalidOperationException"/>이 발생합니다.
    /// </remarks>
    /// <example><code>
    /// [RemoteConfigKey("gameplay_v1")]
    /// public sealed class GameplayV1Config
    /// {
    ///     [JsonProperty("enabled")] public bool enabled;
    /// }
    ///
    /// // 리스너 등록
    /// _listener = RemoteConfig&lt;GameplayV1Config&gt;.CreateListener(OnConfigChanged);
    ///
    /// // 바인딩 (Value 프로퍼티로 동기 접근)
    /// _binding = RemoteConfig&lt;GameplayV1Config&gt;.CreateBinding();
    /// var cfg = _binding.Value;
    ///
    /// // 비동기 단발 읽기
    /// var reader = RemoteConfig&lt;GameplayV1Config&gt;.CreateReader();
    /// var cfg = await reader();
    /// </code></example>
    public static class RemoteConfig<T> where T : class, new()
    {
        private static string _cachedKey;

        private static string GetKey()
        {
            if (_cachedKey != null) return _cachedKey;

            var attr = (RemoteConfigKeyAttribute)Attribute.GetCustomAttribute(
                typeof(T), typeof(RemoteConfigKeyAttribute));

            if (attr == null)
                throw new InvalidOperationException(
                    $"[RemoteConfig<{typeof(T).Name}>] [RemoteConfigKey] 어트리뷰트가 없습니다.\n" +
                    $"예: [RemoteConfigKey(\"my_key\")] public sealed class {typeof(T).Name} {{ ... }}");

            _cachedKey = attr.Key;
            return _cachedKey;
        }

        /// <summary>
        /// 값을 비동기로 한 번 읽는 함수를 반환합니다.
        /// 캐시가 신선하면 즉시 반환하고, 만료됐으면 서버에서 갱신합니다.
        /// </summary>
        /// <param name="maxStale">유효로 간주할 최대 캐시 경과 시간(초). 0이면 DB 설정을 따릅니다.</param>
        /// <example><code>
        /// var cfg = await RemoteConfig&lt;GameplayV1Config&gt;.CreateReader()();
        /// </code></example>
        public static Func<Task<T>> CreateReader(int maxStale = 0) =>
            SupabaseSDK.CreateRemoteConfigReader<T>(GetKey(), maxStale);

        /// <summary>
        /// 폴링으로 값을 자동 갱신하는 바인딩 객체를 반환합니다.
        /// <c>binding.Value</c>로 최신 값을 동기적으로 읽을 수 있습니다.
        /// 객체 파괴 시 <see cref="IDisposable.Dispose"/>를 호출하세요.
        /// </summary>
        /// <param name="pollInterval">갱신 확인 주기(초). 0이면 자동 폴링 없음.</param>
        /// <example><code>
        /// _binding = RemoteConfig&lt;GameplayV1Config&gt;.CreateBinding();
        /// var cfg = _binding.Value;
        /// </code></example>
        public static RemoteConfigBinding<T> CreateBinding(float pollInterval = 60f) =>
            SupabaseSDK.CreateRemoteConfigBinding<T>(GetKey(), pollInterval);

        /// <summary>
        /// 값이 갱신될 때마다 콜백을 호출하는 리스너를 반환합니다.
        /// 객체 파괴 시 <see cref="IDisposable.Dispose"/>를 호출하세요.
        /// </summary>
        /// <param name="onChange">값이 갱신될 때 호출되는 콜백.</param>
        /// <param name="pollInterval">갱신 확인 주기(초). 0이면 자동 폴링 없음.</param>
        /// <example><code>
        /// _listener = RemoteConfig&lt;GameplayV1Config&gt;.CreateListener(cfg =&gt; ApplyConfig(cfg));
        /// </code></example>
        public static RemoteConfigListener<T> CreateListener(
            Action<T> onChange, float pollInterval = 60f) =>
            SupabaseSDK.CreateRemoteConfigListener<T>(GetKey(), pollInterval, onChange);
    }
}
