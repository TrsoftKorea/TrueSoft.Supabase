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

        public AutoDict() { }

        /// <summary>없는 키를 읽을 때 반환할 기본값.</summary>
        [JsonIgnore]
        public TValue DefaultValue
        {
            get => _default;
            set => _default = value;
        }

        public new TValue this[TKey key]
        {
            get => TryGetValue(key, out var v) ? v : _default;
            set => base[key] = value;
        }

        void IAutoDefaultable.SetDefaultValue(object[] values) => _default = AutoDefaultConvert.To<TValue>(values);
    }
}
