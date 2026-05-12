using System;
using Truesoft.Supabase.Unity.RemoteConfig;
using UnityEngine;

namespace Truesoft.Supabase.Unity
{
    /// <summary>
    /// <see cref="MonoBehaviour"/>에서 Supabase API를 간결하게 호출하기 위한 확장 메서드입니다.
    /// <c>destroyCancellationToken</c>을 자동으로 전달하므로, MonoBehaviour가 파괴될 때 구독이 자동으로 해제됩니다.
    /// </summary>
    public static class SupabaseMonoBehaviourExtensions
    {
        /// <summary>
        /// 폴링 기반 RemoteConfigBinding을 생성합니다.
        /// <c>owner</c>가 파괴될 때 자동으로 구독이 해제됩니다.
        /// </summary>
        /// <param name="owner">소유 MonoBehaviour. 파괴 시 자동 Dispose.</param>
        /// <param name="key">remote_config 테이블의 key 값.</param>
        /// <param name="pollIntervalSeconds">폴링 주기(초).</param>
        public static RemoteConfigBinding<T> CreateRemoteConfigBinding<T>(
            this MonoBehaviour owner, string key, float pollIntervalSeconds)
            where T : class, new() =>
            Supabase.CreateRemoteConfigBinding<T>(key, pollIntervalSeconds,
                owner.destroyCancellationToken);

        /// <summary>
        /// 폴링 기반 반응형 RemoteConfig 구독을 생성합니다.
        /// <c>owner</c>가 파괴될 때 자동으로 구독이 해제됩니다.
        /// </summary>
        /// <param name="owner">소유 MonoBehaviour. 파괴 시 자동 Dispose.</param>
        /// <param name="key">remote_config 테이블의 key 값.</param>
        /// <param name="pollIntervalSeconds">폴링 주기(초).</param>
        /// <param name="onChange">값이 갱신될 때 호출되는 콜백.</param>
        /// <param name="invokeIfCached">생성 시 캐시에 값이 있으면 즉시 콜백 호출 여부.</param>
        public static RemoteConfigListener<T> CreateRemoteConfigListener<T>(
            this MonoBehaviour owner, string key, float pollIntervalSeconds,
            Action<T> onChange, bool invokeIfCached = true)
            where T : class, new() =>
            Supabase.CreateRemoteConfigListener<T>(key, pollIntervalSeconds, onChange,
                invokeIfCached, owner.destroyCancellationToken);
    }
}
