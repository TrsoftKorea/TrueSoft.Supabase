using System.Collections.Generic;
using Newtonsoft.Json;

namespace TrueBase.Core.Data
{
    /// <summary>
    /// 이중(2단계) 딕셔너리. <c>[k1, k2]</c> 인덱서로 접근합니다.
    /// <list type="bullet">
    /// <item>없는 키 조합을 <b>읽으면</b> <see cref="DefaultValue"/>를 반환합니다(예외 없음, 비파괴).</item>
    /// <item><b>쓰면</b> 안쪽 딕셔너리가 없을 때 자동 생성한 뒤 저장합니다.</item>
    /// </list>
    /// JSON에는 <c>{"k1":{"k2":v}}</c> 중첩 객체로 직렬화됩니다(<see cref="Dictionary{TKey,TValue}"/> 중첩과 동일 형태).
    /// 기본값은 <c>default(TValue)</c>이며, 필드에 <see cref="AutoDefaultAttribute"/>로 바꿀 수 있습니다. 값 타입 <typeparamref name="TValue"/>에 적합합니다.
    /// </summary>
    public class AutoDict2D<TKey1, TKey2, TValue> : Dictionary<TKey1, Dictionary<TKey2, TValue>>, IAutoDefaultable
    {
        [JsonIgnore] private TValue _default;

        public AutoDict2D() { }

        /// <summary>없는 키 조합을 읽을 때 반환할 기본값.</summary>
        [JsonIgnore]
        public TValue DefaultValue
        {
            get => _default;
            set => _default = value;
        }

        public TValue this[TKey1 key1, TKey2 key2]
        {
            get => (TryGetValue(key1, out var inner) && inner != null && inner.TryGetValue(key2, out var v))
                ? v
                : _default;
            set
            {
                if (!TryGetValue(key1, out var inner) || inner == null)
                {
                    inner = new Dictionary<TKey2, TValue>();
                    base[key1] = inner;
                }
                inner[key2] = value;
            }
        }

        void IAutoDefaultable.SetDefaultValue(object[] values) => _default = AutoDefaultConvert.To<TValue>(values);
    }
}
