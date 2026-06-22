using System;

namespace TrueBase.Core.Data
{
    /// <summary>
    /// <see cref="AutoList{T}"/> / <see cref="AutoDict{TKey,TValue}"/> 등의 "범위 밖 기본값"을 지정합니다.
    /// <para>인자는 어트리뷰트 상수(int·long·float·double·bool·string·enum 등)입니다.
    /// SDK가 로드 직후 인스턴스에 주입하며, JSON 데이터에는 저장되지 않습니다(타입=코드에 귀속).</para>
    /// <list type="bullet">
    /// <item>단순 값 1개 → 해당 값으로 변환: <c>[AutoDefault(-1)]</c> (AutoList&lt;int&gt;).</item>
    /// <item>상수 여러 개 → 요소 타입의 <b>생성자로 조립</b>: <c>[AutoDefault(-1, 0)]</c> → <c>new Pos(-1, 0)</c>
    /// (struct·튜플 등 복합 값 타입의 기본값. 인자는 생성자 매개변수와 맞아야 함).</item>
    /// </list>
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false)]
    public sealed class AutoDefaultAttribute : Attribute
    {
        /// <summary>주입할 기본값 상수들(1개=직접 변환, 여러 개=생성자 인자).</summary>
        public object[] Values { get; }

        public AutoDefaultAttribute(params object[] values) => Values = values;
    }

    /// <summary>
    /// 범위 밖 기본값을 상수 배열로 주입받는 타입. <see cref="AutoList{T}"/>·<see cref="AutoDict{TKey,TValue}"/> 등이 구현합니다.
    /// </summary>
    public interface IAutoDefaultable
    {
        /// <summary>상수 배열을 대상 타입으로 변환/조립해 기본값으로 설정합니다.</summary>
        void SetDefaultValue(object[] values);
    }

    internal static class AutoDefaultConvert
    {
        /// <summary>
        /// 상수 배열을 <typeparamref name="T"/>로 변환합니다.
        /// 단순 값 1개는 직접 변환(enum·숫자 형변환 포함), 그 외(복합 값 타입)는 생성자로 조립합니다.
        /// </summary>
        public static T To<T>(object[] values)
        {
            if (values == null || values.Length == 0)
                return default;

            var target = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);

            if (values.Length == 1)
            {
                var v = values[0];
                if (v == null) return default;
                if (v is T direct) return direct;
                if (target.IsEnum) return (T)Enum.ToObject(target, v);
                if (target.IsPrimitive || target == typeof(string) || target == typeof(decimal))
                    return (T)Convert.ChangeType(v, target);
            }

            // 복합 값 타입(struct·튜플 등): 인자로 생성자 호출
            return (T)Activator.CreateInstance(target, values);
        }
    }
}
