using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace TrueBase.Core.Data
{
    /// <summary>
    /// 이중(2차원) 자동 확장 리스트. <c>grid[i]</c>는 <b>지연 프록시</b>(<see cref="AutoRow{T}"/>)를 돌려줍니다.
    /// <list type="bullet">
    /// <item><c>grid[i][j]</c> — <b>읽기는 비파괴</b>(없는 행/열이면 기본값, 아무것도 만들지 않음). <b>쓰기는 그 시점에 행·열을 생성·저장</b>.</item>
    /// <item><c>grid[i, j]</c> — 한 번의 호출로 동일하게 동작.</item>
    /// <item><c>grid[i]</c>(=<see cref="AutoRow{T}"/>)는 <see cref="IList{T}"/>를 구현해 List처럼 씁니다.
    ///   실제 <see cref="AutoList{T}"/> 행이 필요하면 <see cref="Row"/>.</item>
    /// </list>
    /// JSON에는 <c>[[...],[...]]</c> 중첩 배열로 직렬화됩니다(기존 컬럼과 호환).
    /// 기본값은 <c>default(T)</c>이며, 필드에 <see cref="AutoDefaultAttribute"/>로 바꿀 수 있습니다.
    /// </summary>
    public class AutoList2D<T> : List<AutoList<T>>, IAutoDefaultable
    {
        [JsonIgnore] private T _default;
        // null이 아니면 T가 참조 타입([AutoDefault]로 주입됨) → 각 행이 슬롯마다 독립적으로 재구성(aliasing 방지, 그리드 전체 공유 방지).
        [JsonIgnore] private object[] _refCtorArgs;

        public AutoList2D() { }

        /// <summary>범위 밖 읽기 및 확장 시 채울 기본값. 설정하면 기존 행들에도 전파됩니다.</summary>
        [JsonIgnore]
        public T DefaultValue
        {
            get => FreshDefault();
            set { _default = value; _refCtorArgs = null; PropagateDefault(); }
        }

        private T FreshDefault() => _refCtorArgs != null ? AutoDefaultConvert.To<T>(_refCtorArgs) : _default;

        private void PropagateDefault()
        {
            for (int i = 0; i < Count; i++)
                if (base[i] != null) ApplyDefaultTo(base[i]);
        }

        // 행 하나에 현재 기본값 레시피를 적용합니다. 참조 타입이면 원본 생성자 인자를 그대로 넘겨
        // 그 행이 슬롯마다 독립적으로 재구성하게 하고, 값 타입/수동 대입이면 인스턴스를 그대로 공유합니다.
        private void ApplyDefaultTo(AutoList<T> row)
        {
            if (_refCtorArgs != null) ((IAutoDefaultable)row).SetDefaultValue(_refCtorArgs);
            else row.DefaultValue = _default;
        }

        private AutoList<T> CreateDefaultRow()
        {
            var row = new AutoList<T>();
            ApplyDefaultTo(row);
            return row;
        }

        // AutoRow가 쓰는 내부 헬퍼 (같은 어셈블리 전용)
        internal AutoList<T> RawRowOrNull(int i) => (i >= 0 && i < Count) ? base[i] : null;

        internal AutoList<T> EnsureRow(int i)
        {
            while (Count <= i) Add(CreateDefaultRow());
            if (base[i] == null) base[i] = CreateDefaultRow();
            return base[i];
        }

        /// <summary>행 <paramref name="i"/>에 대한 안전 접근자(지연 프록시). 읽기는 비파괴, 쓰기 시 그 행을 생성·저장합니다.</summary>
        public new AutoRow<T> this[int i] => new AutoRow<T>(this, i);

        /// <summary>행·열을 모두 자동 확장하는 인덱서(한 번의 호출). 읽기는 비파괴(없으면 기본값).</summary>
        public T this[int i, int j]
        {
            get { var r = RawRowOrNull(i); return r != null ? r[j] : FreshDefault(); }
            set => EnsureRow(i)[j] = value;
        }


        /// <summary>행 <paramref name="i"/>의 실제 저장된 <see cref="AutoList{T}"/>를 반환합니다. 없으면 빈 리스트(저장 안 함·비파괴).</summary>
        public AutoList<T> Row(int i)
            => RawRowOrNull(i) ?? CreateDefaultRow();

        /// <summary>행 <paramref name="i"/>의 값을 통째로 교체합니다(없으면 그 행까지 확장).</summary>
        public void SetRow(int i, AutoList<T> row)
        {
            while (Count <= i) Add(CreateDefaultRow());
            if (row != null) ApplyDefaultTo(row);
            base[i] = row ?? CreateDefaultRow();
        }

        /// <summary>모든 셀을 행 순서로 평탄화해 열거합니다(실제 저장된 값만).</summary>
        public IEnumerable<T> Cells()
        {
            for (int i = 0; i < Count; i++)
            {
                var row = base[i];
                if (row == null) continue;
                for (int j = 0; j < row.Count; j++) yield return row[j];
            }
        }

        // 행 시프트 경고(그리드의 행 조작)
        private const string ShiftWarning =
            "AutoList2D의 행 인덱스는 슬롯 의미가 있어 이 연산은 이후 행을 밀어 매핑을 깨뜨립니다. 값은 [i, j] 또는 [i][j]로 접근하세요.";

        [Obsolete(ShiftWarning)] public new void Insert(int index, AutoList<T> item) => base.Insert(index, item);
        [Obsolete(ShiftWarning)] public new void RemoveAt(int index) => base.RemoveAt(index);
        [Obsolete(ShiftWarning)] public new void Reverse() => base.Reverse();

        void IAutoDefaultable.SetDefaultValue(object[] values)
        {
            if (AutoDefaultConvert.IsReferenceKind(typeof(T))) { _refCtorArgs = values; _default = default; }
            else { _default = AutoDefaultConvert.To<T>(values); _refCtorArgs = null; }
            PropagateDefault();
        }

        object IAutoDefaultable.GetDefaultValueBoxed() => FreshDefault();
    }

    /// <summary>
    /// <see cref="AutoList2D{T}"/>의 행 접근 프록시. <c>grid[i]</c>가 반환합니다. <see cref="IList{T}"/>를 구현해 List처럼 씁니다.
    /// <b>읽기는 비파괴</b>(없는 행/열이면 기본값, 열거·검색은 저장분만), <b>추가/삽입/인덱서 쓰기는 그 시점에 행을 생성·저장</b>합니다.
    /// 진짜 <see cref="AutoList{T}"/>가 필요하면 <see cref="AutoList2D{T}.Row"/>.
    /// </summary>
    public sealed class AutoRow<T> : IList<T>, IReadOnlyList<T>
    {
        private readonly AutoList2D<T> _owner;
        private readonly int _i;

        internal AutoRow(AutoList2D<T> owner, int i) { _owner = owner; _i = i; }

        // 저장된 행(List<T>로 업캐스트, 없으면 null) — List 기본 메서드 위임용
        private List<T> L => _owner.RawRowOrNull(_i);

        public int Count => L?.Count ?? 0;
        public bool IsReadOnly => false;

        /// <summary>열 <paramref name="j"/> 접근. 읽기는 비파괴(없으면 기본값), 쓰기는 이 행을 생성·저장 후 열 확장.</summary>
        public T this[int j]
        {
            get { var r = _owner.RawRowOrNull(_i); return r != null ? r[j] : _owner.DefaultValue; }
            set => _owner.EnsureRow(_i)[j] = value;
        }

        // 추가/삽입 (쓰기 → 그 시점에 행 생성·저장)
        public void Add(T item) => _owner.EnsureRow(_i).Add(item);
        public void AddRange(IEnumerable<T> collection) => ((List<T>)_owner.EnsureRow(_i)).AddRange(collection);
        public void Insert(int index, T item) => ((List<T>)_owner.EnsureRow(_i)).Insert(index, item);
        public void InsertRange(int index, IEnumerable<T> collection) => ((List<T>)_owner.EnsureRow(_i)).InsertRange(index, collection);

        // 삭제/정렬 (없는 행이면 no-op)
        public bool Remove(T item) => L?.Remove(item) ?? false;
        public void RemoveAt(int index) => L?.RemoveAt(index);
        public int RemoveAll(Predicate<T> match) => L?.RemoveAll(match) ?? 0;
        public void RemoveRange(int index, int count) => L?.RemoveRange(index, count);
        public void Clear() => L?.Clear();
        public void Sort() => L?.Sort();
        public void Sort(Comparison<T> comparison) => L?.Sort(comparison);
        public void Reverse() => L?.Reverse();

        // 조회 (저장분 대상, List<T>와 동일 시맨틱)
        public bool Contains(T item) => L?.Contains(item) ?? false;
        public int IndexOf(T item) => L?.IndexOf(item) ?? -1;
        public int LastIndexOf(T item) => L?.LastIndexOf(item) ?? -1;
        public bool Exists(Predicate<T> match) => L?.Exists(match) ?? false;
        public bool TrueForAll(Predicate<T> match) => L?.TrueForAll(match) ?? true;

        /// <summary>조건에 맞는 첫 원소. 없거나 없는 행이면 <see cref="AutoList2D{T}.DefaultValue"/>(인덱서와 동일).</summary>
        public T Find(Predicate<T> match)
        {
            var l = L;
            if (l == null) return _owner.DefaultValue;
            int idx = l.FindIndex(match);
            return idx >= 0 ? l[idx] : _owner.DefaultValue;
        }

        /// <summary>조건에 맞는 마지막 원소. 없거나 없는 행이면 <see cref="AutoList2D{T}.DefaultValue"/>(인덱서와 동일).</summary>
        public T FindLast(Predicate<T> match)
        {
            var l = L;
            if (l == null) return _owner.DefaultValue;
            int idx = l.FindLastIndex(match);
            return idx >= 0 ? l[idx] : _owner.DefaultValue;
        }
        public List<T> FindAll(Predicate<T> match) => L?.FindAll(match) ?? new List<T>();
        public int FindIndex(Predicate<T> match) => L?.FindIndex(match) ?? -1;
        public int FindLastIndex(Predicate<T> match) => L?.FindLastIndex(match) ?? -1;
        public List<T> GetRange(int index, int count) => L?.GetRange(index, count) ?? new List<T>();
        public void ForEach(Action<T> action) => L?.ForEach(action);
        public T[] ToArray() => L?.ToArray() ?? Array.Empty<T>();
        public void CopyTo(T[] array, int arrayIndex) => L?.CopyTo(array, arrayIndex);

        public IEnumerator<T> GetEnumerator()
        {
            var r = L;
            if (r == null) yield break;
            for (int j = 0; j < r.Count; j++) yield return r[j];
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
