using System;
using System.Threading.Tasks;

namespace TrueBase.Unity
{
    /// <summary>
    /// PlayNanooRuntime이 StaticUserSave&lt;TRow&gt;와 상호작용하기 위한 비제네릭 인터페이스.
    /// StaticUserSave&lt;TRow&gt;가 구현하며, PlayNanooRuntime은 TRow를 알 필요 없이 세이브 데이터를 동기화합니다.
    /// </summary>
    public interface INanooSaveSyncable
    {
        /// <summary>
        /// DB에서 Row를 로드하고 (success, hasRow, updatedAt)을 반환합니다.
        /// 로드된 Row는 내부에 캐시되어 이후 <see cref="NanooPatchFromLastLoadedAsync"/>,
        /// <see cref="NanooApplyLastLoaded"/>, <see cref="NanooGetLastLoadedJson"/>에서 사용됩니다.
        /// </summary>
        Task<(bool success, bool hasRow, DateTime updatedAt)> NanooLoadWithStateAsync();

        /// <summary>새 유저 — 빈 Row에서 nanooJson으로 패치 후 Apply합니다.</summary>
        Task<bool> NanooPatchFromEmptyAsync(string nanooJson);

        /// <summary>PlayNanoo 데이터가 최신 — 마지막으로 로드한 DB Row에서 nanooJson으로 패치 후 Apply합니다.</summary>
        Task<bool> NanooPatchFromLastLoadedAsync(string nanooJson);

        /// <summary>SDK 데이터가 최신 — 마지막으로 로드한 DB Row를 Current에 Apply합니다.</summary>
        void NanooApplyLastLoaded();

        /// <summary>마지막으로 로드한 DB Row를 JSON으로 반환합니다. PlayNanoo에 저장할 때 사용합니다.</summary>
        string NanooGetLastLoadedJson();

        /// <summary>DB에서 로드하여 Current에 적용합니다 (신규 유저, PlayNanoo 데이터 없음).</summary>
        Task<bool> TryLoadAsync();

        /// <summary>현재 로컬 세이브 데이터를 JSON으로 반환합니다. PlayNanoo에 저장할 때 사용합니다.</summary>
        string NanooCurrentJson { get; }
    }
}
