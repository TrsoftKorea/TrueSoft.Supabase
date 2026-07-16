using System;
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
        // null이 아니면 T가 참조 타입([AutoDefault]로 주입됨) → 슬롯마다 이 인자로 새 인스턴스를 생성(aliasing 방지).
        // 값 타입/수동 DefaultValue 대입은 _default를 그대로 공유해도 안전하므로 기존 방식 유지.
        [JsonIgnore] private object[] _refCtorArgs;

        public AutoList() { }

        /// <summary>범위 밖 읽기 및 확장 시 채울 기본값. 참조 타입을 [AutoDefault]로 주입한 경우 읽을 때마다 새 인스턴스를 반환합니다.</summary>
        [JsonIgnore]
        public T DefaultValue
        {
            get => FreshDefault();
            set { _default = value; _refCtorArgs = null; }
        }

        private T FreshDefault() => _refCtorArgs != null ? AutoDefaultConvert.To<T>(_refCtorArgs) : _default;

        public new T this[int index]
        {
            get => (index >= 0 && index < Count) ? base[index] : FreshDefault();
            set
            {
                while (Count <= index) Add(FreshDefault()); // 해당 위치까지 기본값으로 확장 (음수면 루프 미실행 → base가 예외)
                base[index] = value;
            }
        }


        /// <summary>범위 밖이면 확장 없이 <see cref="DefaultValue"/>를 반환합니다(<c>this[i]</c> 읽기와 동일, 의도 명시용).</summary>
        public T GetOrDefault(int index) => this[index];

        /// <summary><paramref name="count"/>개가 되도록 <see cref="DefaultValue"/>로 채웁니다(이미 크거나 같으면 그대로). <c>Count</c>를 논리 크기에 맞출 때 사용.</summary>
        public void EnsureCount(int count)
        {
            while (Count < count) Add(FreshDefault());
        }

        // AutoList의 인덱스는 슬롯 의미(예: stageId)라, 원소를 밀거나 재정렬하면 이후 인덱스↔의미 매핑이 깨집니다.
        // 슬롯을 비우려면 제거가 아니라 this[i] = DefaultValue 로 덮어쓰세요.
        private const string ShiftWarning =
            "AutoList의 인덱스는 슬롯 의미가 있어 이 연산은 이후 인덱스를 밀어 매핑을 깨뜨립니다. 슬롯을 비우려면 this[i] = 기본값 을 쓰세요.";

        [Obsolete(ShiftWarning)] public new void Insert(int index, T item) => base.Insert(index, item);
        [Obsolete(ShiftWarning)] public new void InsertRange(int index, IEnumerable<T> collection) => base.InsertRange(index, collection);
        [Obsolete(ShiftWarning)] public new void RemoveAt(int index) => base.RemoveAt(index);
        [Obsolete(ShiftWarning)] public new void RemoveRange(int index, int count) => base.RemoveRange(index, count);
        [Obsolete(ShiftWarning)] public new void Reverse() => base.Reverse();
        [Obsolete(ShiftWarning)] public new void Reverse(int index, int count) => base.Reverse(index, count);
        [Obsolete(ShiftWarning)] public new void Sort() => base.Sort();
        [Obsolete(ShiftWarning)] public new void Sort(Comparison<T> comparison) => base.Sort(comparison);
        [Obsolete(ShiftWarning)] public new void Sort(IComparer<T> comparer) => base.Sort(comparer);

        void IAutoDefaultable.SetDefaultValue(object[] values)
        {
            if (AutoDefaultConvert.IsReferenceKind(typeof(T))) { _refCtorArgs = values; _default = default; }
            else { _default = AutoDefaultConvert.To<T>(values); _refCtorArgs = null; }
        }

        object IAutoDefaultable.GetDefaultValueBoxed() => FreshDefault();

        // 서버에 없거나 값이 기본값인 슬롯을 fallback에서 채운다. 서버의 "비기본값"은 유지, 서버가 더 길면 안 자른다.
        void IAutoDefaultable.FillMissingFrom(object other)
        {
            if (!(other is AutoList<T> o)) return;
            var cmp = EqualityComparer<T>.Default;
            for (int i = 0; i < o.Count; i++)
            {
                // 서버가 이 인덱스를 갖고, 값이 null이 아니며 기본값도 아니면 그대로 둔다.
                // null은 "데이터 없음"의 확실한 신호라, [AutoDefault]로 기본값이 non-null인 참조 타입이어도 빈 슬롯으로 본다.
                if (i < Count && base[i] != null && !cmp.Equals(base[i], FreshDefault())) continue;
                // 없거나 null·기본값이면 fallback 값으로 채운다(인덱서가 필요한 만큼 확장).
                this[i] = o[i];
            }
        }
    }
}
