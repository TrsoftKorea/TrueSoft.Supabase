using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Truesoft.Supabase.Core.Data
{
    /// <summary>
    /// <see cref="DataColumnAttribute"/>가 붙은 멤버로부터 <c>select</c> 목록·PATCH 딕셔너리를 만듭니다.
    /// </summary>
    public static class DataSchema
    {
        private const string UpdatedAtColumn = "updated_at";

        /// <summary>유저 세이브 테이블명. 고정값 — 변경 불가.</summary>
        public const string UserDataTableName = "user_data";

        /// <summary>
        /// <typeparamref name="T"/>에 붙은 컬럼들로 PostgREST <c>select</c>용 CSV를 만듭니다(정렬 안정).
        /// </summary>
        /// <param name="includeUpdatedAt"><c>updated_at</c>를 목록에 포함할지(로드 시 타임스탬프가 필요하면 true).</param>
        public static string GetSelectColumnsCsv<T>(bool includeUpdatedAt = true)
        {
            var names = GetColumnNames(typeof(T), includeUpdatedAt);
            if (names.Count == 0)
                throw new InvalidOperationException($"No {nameof(DataColumnAttribute)} on public fields/properties of {typeof(T).Name}.");

            return string.Join(",", names);
        }

        /// <summary>컬럼명 목록(중복 제거, 정렬).</summary>
        public static IReadOnlyList<string> GetColumnNames<T>(bool includeUpdatedAt = true) =>
            GetColumnNames(typeof(T), includeUpdatedAt);

        private static List<string> GetColumnNames(Type t, bool includeUpdatedAt)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var m in GetMappedMembers(t))
            {
                var col = ResolveColumnName(m);
                if (string.IsNullOrEmpty(col) == false)
                    set.Add(col);
            }

            if (includeUpdatedAt)
                set.Add(UpdatedAtColumn);

            var list = set.ToList();
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        /// <summary>
        /// 두 스냅샷을 비교해 변경된 컬럼만 PATCH용 딕셔너리로 만듭니다.
        /// </summary>
        public static Dictionary<string, object> BuildPatch<T>(T previous, T current)
        {
            if (current == null)
                throw new ArgumentNullException(nameof(current));

            var patch = new Dictionary<string, object>(StringComparer.Ordinal);
            var prev = previous; // may be null

            foreach (var m in GetMappedMembers(typeof(T)))
            {
                var col = ResolveColumnName(m);
                if (string.IsNullOrEmpty(col) || string.Equals(col, UpdatedAtColumn, StringComparison.Ordinal))
                    continue;

                var oldVal = GetValue(m, prev);
                var newVal = GetValue(m, current);

                if (EqualsValues(oldVal, newVal))
                    continue;

                patch[col] = newVal;
            }

            return patch;
        }

        /// <summary>
        /// <see cref="DataColumnAttribute"/> 멤버를 reflection으로 복사한 새 인스턴스를 반환합니다.
        /// <paramref name="src"/>가 null이면 <c>new T()</c>를 반환합니다.
        /// </summary>
        public static T CloneRow<T>(T src) where T : class, new()
        {
            var dst = new T();
            if (src != null)
                CopyColumnsInto(dst, src);
            return dst;
        }

        /// <summary>
        /// <see cref="DataColumnAttribute"/> 멤버를 <paramref name="src"/>에서 <paramref name="dst"/>로 복사합니다.
        /// <paramref name="src"/>가 null이면 <c>new T()</c>의 기본값으로 채웁니다.
        /// </summary>
        public static void CopyInto<T>(T dst, T src) where T : class, new()
        {
            if (dst == null) return;
            CopyColumnsInto(dst, src ?? new T());
        }

        private static void CopyColumnsInto<T>(T dst, T src)
        {
            foreach (var m in GetMappedMembers(typeof(T)))
            {
                var value = GetValue(m, src);
                if (m is FieldInfo f)
                    f.SetValue(dst, value);
                else if (m is PropertyInfo p && p.CanWrite)
                    p.SetValue(dst, value);
            }
        }

        private static bool EqualsValues(object a, object b)
        {
            if (a == null && b == null)
                return true;
            if (a == null || b == null)
                return false;

            // 클래스 타입(string 제외)이 같은 참조인 경우:
            // CloneRow가 참조를 그대로 복사한 것이므로 내부 변경을 감지할 수 없습니다.
            // 항상 변경된 것으로 처리해 패치에 포함합니다.
            var t = a.GetType();
            if (!t.IsValueType && t != typeof(string) && ReferenceEquals(a, b))
                return false;

            return a.Equals(b);
        }

        private static IEnumerable<MemberInfo> GetMappedMembers(Type t)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
            foreach (var p in t.GetProperties(flags))
            {
                if (p.GetIndexParameters().Length > 0)
                    continue;
                if (p.GetCustomAttribute<DataColumnAttribute>() == null)
                    continue;
                if (p.CanRead == false)
                    continue;
                yield return p;
            }

            foreach (var f in t.GetFields(flags))
            {
                if (f.GetCustomAttribute<DataColumnAttribute>() == null)
                    continue;
                yield return f;
            }
        }

        private static string ResolveColumnName(MemberInfo m)
        {
            var attr = m.GetCustomAttribute<DataColumnAttribute>();
            if (attr == null)
                return null;
            if (string.IsNullOrWhiteSpace(attr.ColumnName) == false)
                return attr.ColumnName.Trim();
            return m.Name;
        }

        /// <summary>유저 세이브 테이블명 <c>"user_data"</c>를 반환합니다.</summary>
        public static string ResolveTableName<T>() => UserDataTableName;

        /// <summary>유저 세이브 테이블명 <c>"user_data"</c>를 반환합니다.</summary>
        public static string ResolveTableName(Type t) => UserDataTableName;

        private static object GetValue(MemberInfo m, object instance)
        {
            if (instance == null)
                return null;
            return m switch
            {
                FieldInfo f => f.GetValue(instance),
                PropertyInfo p => p.GetValue(instance),
                _ => null
            };
        }
    }
}
