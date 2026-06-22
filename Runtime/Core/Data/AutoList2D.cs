using System.Collections.Generic;
using Newtonsoft.Json;

namespace TrueBase.Core.Data
{
    /// <summary>
    /// 이중(2차원) 자동 확장 리스트. <c>[i, j]</c> 인덱서로 접근합니다.
    /// <list type="bullet">
    /// <item>범위 밖 <b>읽기</b> → <see cref="DefaultValue"/> 반환(확장하지 않음, 비파괴 → 조회가 저장을 유발하지 않음).</item>
    /// <item>범위 밖 <b>쓰기</b> → 행·열 양쪽을 그 위치까지 확장한 뒤 저장.</item>
    /// </list>
    /// JSON에는 <c>[[...],[...]]</c> 중첩 배열로 직렬화됩니다(<see cref="List{T}"/>의 <see cref="List{T}"/>와 동일 형태).
    /// 기본값은 <c>default(T)</c>이며, 필드에 <see cref="AutoDefaultAttribute"/>로 바꿀 수 있습니다. 값 타입 <typeparamref name="T"/>에 적합합니다.
    /// </summary>
    public class AutoList2D<T> : List<List<T>>, IAutoDefaultable
    {
        [JsonIgnore] private T _default;

        public AutoList2D() { }

        /// <summary>범위 밖 읽기 및 확장 시 채울 기본값.</summary>
        [JsonIgnore]
        public T DefaultValue
        {
            get => _default;
            set => _default = value;
        }

        public T this[int i, int j]
        {
            get => (i >= 0 && i < Count && base[i] != null && j >= 0 && j < base[i].Count)
                ? base[i][j]
                : _default;
            set
            {
                while (Count <= i) Add(new List<T>());     // 행 확장(빈 행)
                if (base[i] == null) base[i] = new List<T>();
                var row = base[i];
                while (row.Count <= j) row.Add(_default);   // 열 확장(기본값)
                row[j] = value;
            }
        }

        void IAutoDefaultable.SetDefaultValue(object[] values) => _default = AutoDefaultConvert.To<T>(values);
    }
}
