using System.Collections.Generic;
using Newtonsoft.Json;

namespace TrueBase.Core.Data
{
    /// <summary>
    /// 자동 확장 리스트. 게임 버전업으로 크기가 늘어나는 필드(스테이지 클리어 등)에서
    /// 매 접속 선확장 없이 안전하게 인덱싱하기 위한 타입입니다.
    /// <list type="bullet">
    /// <item>범위 밖 인덱스를 <b>읽으면</b> <see cref="DefaultValue"/>를 반환합니다(확장하지 않음 → 단순 조회는 변경으로 취급되지 않음).</item>
    /// <item>범위 밖 인덱스에 <b>저장하면</b> 그 위치까지 <see cref="DefaultValue"/>로 채운 뒤 저장합니다.</item>
    /// </list>
    /// 기본값은 <c>default(T)</c>이며, 필드에 <see cref="AutoDefaultAttribute"/>를 붙여 바꿀 수 있습니다.
    /// JSON에는 <see cref="List{T}"/>와 동일하게 일반 배열로 직렬화됩니다.
    /// <para>주의: 자동 확장 인덱서는 정적 타입이 <see cref="AutoList{T}"/>일 때만 적용됩니다.
    /// 세이브 필드·프로퍼티를 모두 <c>AutoList&lt;T&gt;</c>로 선언하세요(<c>List&lt;T&gt;</c>로 캐스팅하면 기본 인덱서가 쓰입니다).</para>
    /// </summary>
    public class AutoList<T> : List<T>, IAutoDefaultable
    {
        [JsonIgnore] private T _default;

        public AutoList() { }

        /// <summary>범위 밖 읽기 및 확장 시 채울 기본값.</summary>
        [JsonIgnore]
        public T DefaultValue
        {
            get => _default;
            set => _default = value;
        }

        public new T this[int index]
        {
            get => (index >= 0 && index < Count) ? base[index] : _default;
            set
            {
                while (Count <= index) Add(_default); // 해당 위치까지 기본값으로 확장 (음수면 루프 미실행 → base가 예외)
                base[index] = value;
            }
        }

        void IAutoDefaultable.SetDefaultValue(object[] values) => _default = AutoDefaultConvert.To<T>(values);
    }
}
