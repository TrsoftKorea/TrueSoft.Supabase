#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TrueBase.Unity
{
    /// <summary>
    /// 에디터 디버그 전용 레지스트리. Play 모드에서 등록된 <see cref="StaticUserSave{TRow}"/> 인스턴스의
    /// 현재 데이터에 접근해 값을 보고·편집·저장·재로드할 수 있게 합니다.
    /// <c>#if UNITY_EDITOR</c>로 감싸 빌드에는 포함되지 않습니다.
    /// </summary>
    public static class StaticUserSaveDebug
    {
        public sealed class Entry
        {
            public string SyncKey;
            public Func<object> GetRow;            // 현재 Current 행을 반환
            public Action MarkDirty;
            public Func<Task<bool>> ReloadAsync;   // 서버에서 다시 로드
        }

        private static readonly List<Entry> _entries = new List<Entry>();

        public static IReadOnlyList<Entry> Entries => _entries;

        /// <summary>세이브 인스턴스를 등록합니다. 같은 SyncKey가 있으면 교체합니다.</summary>
        public static void Register(Entry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.SyncKey)) return;
            _entries.RemoveAll(e => e.SyncKey == entry.SyncKey);
            _entries.Add(entry);
        }
    }
}
#endif
