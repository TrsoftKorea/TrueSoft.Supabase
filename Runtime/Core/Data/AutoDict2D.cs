using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace TrueBase.Core.Data
{
    /// <summary>
    /// 이중(2단계) 딕셔너리. <c>dict[k1]</c>은 <b>지연 프록시</b>(<see cref="AutoDictRow{TKey1,TKey2,TValue}"/>)를 돌려줍니다.
    /// <list type="bullet">
    /// <item><c>dict[k1][k2]</c> — <b>읽기는 비파괴</b>(없는 키면 기본값, 아무것도 만들지 않음). <b>쓰기는 그 시점에 안쪽 딕셔너리를 생성·저장</b>.</item>
    /// <item><c>dict[k1, k2]</c> — 한 번의 호출로 동일하게 동작.</item>
    /// <item><c>dict[k1]</c>(=<see cref="AutoDictRow{TKey1,TKey2,TValue}"/>)은 <see cref="IReadOnlyDictionary{TKey,TValue}"/>를 구현하고 <c>Add</c>·<c>Remove</c>·<c>Clear</c>를 지원합니다.
    ///   실제 <see cref="AutoDict{TKey,TValue}"/>가 필요하면 <see cref="Inner"/>.</item>
    /// </list>
    /// JSON에는 <c>{"k1":{"k2":v}}</c> 중첩 객체로 직렬화됩니다(기존 컬럼과 호환).
    /// 기본값은 <c>default(TValue)</c>이며, 필드에 <see cref="AutoDefaultAttribute"/>로 바꿀 수 있습니다.
    /// </summary>
    public class AutoDict2D<TKey1, TKey2, TValue> : Dictionary<TKey1, AutoDict<TKey2, TValue>>, IAutoDefaultable
    {
        [JsonIgnore] private TValue _default;

        public AutoDict2D() { }

        /// <summary>없는 키 조합을 읽을 때 반환할 기본값. 설정하면 기존 안쪽 딕셔너리에도 전파됩니다.</summary>
        [JsonIgnore]
        public TValue DefaultValue
        {
            get => _default;
            set { _default = value; PropagateDefault(); }
        }

        private void PropagateDefault()
        {
            foreach (var inner in Values)
                if (inner != null) inner.DefaultValue = _default;
        }

        // ── AutoDictRow가 쓰는 내부 헬퍼 (같은 어셈블리 전용) ──
        internal AutoDict<TKey2, TValue> InnerOrNull(TKey1 key1)
            => TryGetValue(key1, out var inner) ? inner : null;

        internal AutoDict<TKey2, TValue> EnsureInner(TKey1 key1)
        {
            if (!TryGetValue(key1, out var inner) || inner == null)
            {
                inner = new AutoDict<TKey2, TValue> { DefaultValue = _default };
                base[key1] = inner;
            }
            return inner;
        }

        /// <summary>바깥 키 <paramref name="key1"/>에 대한 안전 접근자(지연 프록시). 읽기는 비파괴, 쓰기 시 안쪽 딕셔너리를 생성·저장합니다.</summary>
        public new AutoDictRow<TKey1, TKey2, TValue> this[TKey1 key1] => new AutoDictRow<TKey1, TKey2, TValue>(this, key1);

        /// <summary>바깥·안쪽 키를 모두 자동 생성하는 인덱서(한 번의 호출). 읽기는 비파괴(없으면 기본값).</summary>
        public TValue this[TKey1 key1, TKey2 key2]
        {
            get { var inner = InnerOrNull(key1); return inner != null ? inner[key2] : _default; }
            set => EnsureInner(key1)[key2] = value;
        }

        // ── 실제 안쪽 딕셔너리 접근 ────────────────────────────────────────────

        /// <summary>키 <paramref name="key1"/>의 실제 안쪽 <see cref="AutoDict{TKey,TValue}"/>를 반환합니다. 없으면 빈 딕셔너리(저장 안 함·비파괴).</summary>
        public AutoDict<TKey2, TValue> Inner(TKey1 key1)
            => InnerOrNull(key1) ?? new AutoDict<TKey2, TValue> { DefaultValue = _default };

        /// <summary>키 <paramref name="key1"/>의 안쪽 딕셔너리를 통째로 교체/설정합니다.</summary>
        public void SetInner(TKey1 key1, AutoDict<TKey2, TValue> inner)
        {
            if (inner != null) inner.DefaultValue = _default;
            base[key1] = inner ?? new AutoDict<TKey2, TValue> { DefaultValue = _default };
        }

        void IAutoDefaultable.SetDefaultValue(object[] values)
        {
            _default = AutoDefaultConvert.To<TValue>(values);
            PropagateDefault();
        }
    }

    /// <summary>
    /// <see cref="AutoDict2D{TKey1,TKey2,TValue}"/>의 바깥 키 접근 프록시. <c>dict[k1]</c>이 반환합니다.
    /// <see cref="IReadOnlyDictionary{TKey,TValue}"/>를 구현하고 <c>Add</c>·<c>Remove</c>·<c>Clear</c>를 지원합니다.
    /// <b>읽기는 비파괴</b>(없으면 기본값), <b>쓰기는 그 시점에 안쪽 딕셔너리를 생성·저장</b>합니다. 진짜 딕셔너리가 필요하면 <see cref="AutoDict2D{TKey1,TKey2,TValue}.Inner"/>.
    /// </summary>
    public sealed class AutoDictRow<TKey1, TKey2, TValue> : IReadOnlyDictionary<TKey2, TValue>
    {
        private readonly AutoDict2D<TKey1, TKey2, TValue> _owner;
        private readonly TKey1 _k1;

        internal AutoDictRow(AutoDict2D<TKey1, TKey2, TValue> owner, TKey1 k1) { _owner = owner; _k1 = k1; }

        // 저장된 안쪽 딕셔너리(없으면 null)
        private AutoDict<TKey2, TValue> D => _owner.InnerOrNull(_k1);

        public int Count => D?.Count ?? 0;

        /// <summary>안쪽 키 <paramref name="key2"/> 접근. 읽기는 비파괴(없으면 기본값), 쓰기는 안쪽 딕셔너리를 생성·저장.</summary>
        public TValue this[TKey2 key2]
        {
            get { var d = D; return d != null ? d[key2] : _owner.DefaultValue; }
            set => _owner.EnsureInner(_k1)[key2] = value;
        }

        public IEnumerable<TKey2> Keys { get { var d = D; return d != null ? d.Keys : Enumerable.Empty<TKey2>(); } }
        public IEnumerable<TValue> Values { get { var d = D; return d != null ? d.Values : Enumerable.Empty<TValue>(); } }

        public bool ContainsKey(TKey2 key2) => D?.ContainsKey(key2) ?? false;
        public bool ContainsValue(TValue value) => D?.ContainsValue(value) ?? false;

        public bool TryGetValue(TKey2 key2, out TValue value)
        {
            var d = D;
            if (d != null && d.TryGetValue(key2, out value)) return true;
            value = _owner.DefaultValue;
            return false;
        }

        // ── 쓰기 (그 시점에 안쪽 딕셔너리 생성·저장) ──
        public void Add(TKey2 key2, TValue value) => _owner.EnsureInner(_k1).Add(key2, value);

        // ── 삭제 (없으면 no-op) ──
        public bool Remove(TKey2 key2) => D?.Remove(key2) ?? false;
        public void Clear() => D?.Clear();

        public IEnumerator<KeyValuePair<TKey2, TValue>> GetEnumerator()
        {
            var d = D;
            if (d == null) yield break;
            foreach (var kv in d) yield return kv;
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
