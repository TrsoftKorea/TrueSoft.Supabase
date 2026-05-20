using System;

namespace Truesoft.Supabase.Unity
{
    /// <summary>
    /// Remote Config 키를 DTO 클래스에 직접 선언합니다.
    /// <see cref="RemoteConfig{T}"/>가 이 어트리뷰트에서 키를 읽습니다.
    /// </summary>
    /// <example><code>
    /// [RemoteConfigKey("gameplay_v1")]
    /// public sealed class GameplayV1Config
    /// {
    ///     [JsonProperty("enabled")] public bool enabled;
    /// }
    ///
    /// // 사용:
    /// var listener = RemoteConfig&lt;GameplayV1Config&gt;.CreateListener(OnConfigChanged);
    /// </code></example>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct,
        Inherited = false, AllowMultiple = false)]
    public sealed class RemoteConfigKeyAttribute : Attribute
    {
        /// <summary>DB <c>remote_config</c> 테이블의 키 이름.</summary>
        public string Key { get; }

        /// <param name="key">DB <c>remote_config</c> 테이블의 키 이름.</param>
        public RemoteConfigKeyAttribute(string key) => Key = key;
    }
}
