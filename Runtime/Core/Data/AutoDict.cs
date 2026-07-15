using System.Collections.Generic;
using Newtonsoft.Json;

namespace TrueBase.Core.Data
{
    /// <summary>
    /// 없는 키를 읽으면 예외 대신 <see cref="DefaultValue"/>를 반환하는 딕셔너리.
    /// 저장은 일반 딕셔너리처럼 동작합니다(없으면 추가, 있으면 갱신).
    /// 기본값은 <c>default(TValue)</c>이며, 필드에 <see cref="AutoDefaultAttribute"/>를 붙여 바꿀 수 있습니다.
    /// JSON에는 <see cref="Dictionary{TKey,TValue}"/>와 동일하게 일반 객체로 직렬화됩니다.
    /// <para>주의: 기본값 인덱서는 정적 타입이 <see cref="AutoDict{TKey,TValue}"/>일 때만 적용됩니다.
    /// 세이브 필드·프로퍼티를 모두 <c>AutoDict&lt;TKey,TValue&gt;</c>로 선언하세요.</para>
    /// </summary>
    public class AutoDict<TKey, TValue> : Dictionary<TKey, TValue>, IAutoDefaultable
    {
        [JsonIgnore] private TValue _default;
        // null이 아니면 TValue가 참조 타입([AutoDefault]로 주입됨) → 읽을 때마다 이 인자로 새 인스턴스를 생성(aliasing 방지).
        [JsonIgnore] private object[] _refCtorArgs;

        public AutoDict() { }

        /// <summary>없는 키를 읽을 때 반환할 기본값. 참조 타입을 [AutoDefault]로 주입한 경우 읽을 때마다 새 인스턴스를 반환합니다.</summary>
        [JsonIgnore]
        public TValue DefaultValue
        {
            get => FreshDefault();
            set { _default = value; _refCtorArgs = null; }
        }

        private TValue FreshDefault() => _refCtorArgs != null ? AutoDefaultConvert.To<TValue>(_refCtorArgs) : _default;

        public new TValue this[TKey key]
        {
            get
            {
                if (TryGetValue(key, out var v)) return v;
                var fresh = FreshDefault();
                // 참조 타입을 [AutoDefault]로 주입한 경우에만 조회 시점에 실제로 저장합니다.
                // 슬롯마다 독립 인스턴스라 aliasing 걱정 없이 "이미 있는 것처럼" 즉시 대입·수정할 수 있고,
                // DataSchema.EqualsValues가 기본값과 같은 항목은 변경 없음으로 취급하므로 조회만으로는 저장을 유발하지 않습니다.
                // 값 타입/수동 DefaultValue 대입은 지금처럼 비파괴로 남겨 둡니다.
                if (_refCtorArgs != null) base[key] = fresh;
                return fresh;
            }
            set => base[key] = value;
        }

        void IAutoDefaultable.SetDefaultValue(object[] values)
        {
            if (AutoDefaultConvert.IsReferenceKind(typeof(TValue))) { _refCtorArgs = values; _default = default; }
            else { _default = AutoDefaultConvert.To<TValue>(values); _refCtorArgs = null; }
        }

        object IAutoDefaultable.GetDefaultValueBoxed() => FreshDefault();

        // 서버에 없거나 값이 기본값인 키를 fallback에서 채운다. 서버의 "비기본값"은 유지한다.
        void IAutoDefaultable.FillMissingFrom(object other)
        {
            if (!(other is AutoDict<TKey, TValue> o)) return;
            var cmp = EqualityComparer<TValue>.Default;
            foreach (var kv in o)
            {
                if (TryGetValue(kv.Key, out var sv) && !cmp.Equals(sv, FreshDefault())) continue;
                base[kv.Key] = kv.Value;
            }
        }
    }
}
