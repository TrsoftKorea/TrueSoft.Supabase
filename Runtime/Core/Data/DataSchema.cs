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
                if (!HasDataColumnAttribute(p))
                    continue;
                if (p.CanRead == false)
                    continue;
                yield return p;
            }

            foreach (var f in t.GetFields(flags))
            {
                if (!HasDataColumnAttribute(f))
                    continue;
                yield return f;
            }
        }

        /// <summary>
        /// Core 또는 Unity 어셈블리의 DataColumnAttribute 여부를 확인합니다.
        /// 타입 동등성 대신 이름 비교를 사용해 어셈블리와 무관하게 동작합니다.
        /// </summary>
        private static bool HasDataColumnAttribute(MemberInfo m)
        {
            // 빠른 경로: Core 자신의 어트리뷰트
            if (m.GetCustomAttribute<DataColumnAttribute>() != null)
                return true;
            // 폴백: Unity 어셈블리 등 다른 어셈블리에 정의된 동명 어트리뷰트
            foreach (var attr in m.GetCustomAttributes(false))
                if (attr.GetType().Name == nameof(DataColumnAttribute))
                    return true;
            return false;
        }

        private static string ResolveColumnName(MemberInfo m)
        {
            // Core 어트리뷰트 (타입 안전)
            var attr = m.GetCustomAttribute<DataColumnAttribute>();
            if (attr != null)
                return string.IsNullOrWhiteSpace(attr.ColumnName) ? m.Name : attr.ColumnName.Trim();

            // 폴백: 이름 기반 조회 (Unity 어셈블리 등)
            foreach (var a in m.GetCustomAttributes(false))
            {
                if (a.GetType().Name != nameof(DataColumnAttribute)) continue;
                var prop    = a.GetType().GetProperty("ColumnName");
                var colName = prop?.GetValue(a) as string;
                return string.IsNullOrWhiteSpace(colName) ? m.Name : colName.Trim();
            }

            return null;
        }

        /// <summary>멤버에 붙은 DataColumnAttribute의 <see cref="DataSavePriority"/>를 반환합니다. 어트리뷰트가 없거나 Priority를 읽을 수 없으면 <see cref="DataSavePriority.Normal"/>.</summary>
        private static DataSavePriority ResolveColumnPriority(MemberInfo m)
        {
            // Core 어트리뷰트 (타입 안전)
            var attr = m.GetCustomAttribute<DataColumnAttribute>();
            if (attr != null) return attr.Priority;

            // 폴백: 이름 기반 조회 (Unity 어셈블리 등)
            foreach (var a in m.GetCustomAttributes(false))
            {
                if (a.GetType().Name != nameof(DataColumnAttribute)) continue;
                var prop = a.GetType().GetProperty("Priority");
                if (prop?.GetValue(a) is int intVal)
                    return (DataSavePriority)intVal;
                // DataSavePriority 타입 자체로 읽히는 경우도 처리
                if (prop?.GetValue(a) is DataSavePriority p)
                    return p;
            }

            return DataSavePriority.Normal;
        }

        /// <summary>
        /// <paramref name="previous"/>와 <paramref name="current"/>를 비교해
        /// 변경된 필드 중 가장 높은(Urgent에 가까운) <see cref="DataSavePriority"/>를 반환합니다.
        /// 변경된 필드가 없으면 <c>null</c>을 반환합니다.
        /// </summary>
        public static DataSavePriority? GetHighestDirtyPriority<T>(T previous, T current)
        {
            if (current == null) return null;

            DataSavePriority? highest = null;

            foreach (var m in GetMappedMembers(typeof(T)))
            {
                var col = ResolveColumnName(m);
                if (string.IsNullOrEmpty(col) || string.Equals(col, UpdatedAtColumn, StringComparison.Ordinal))
                    continue;

                var oldVal = GetValue(m, previous);
                var newVal = GetValue(m, current);
                if (EqualsValues(oldVal, newVal)) continue;

                var priority = ResolveColumnPriority(m);
                if (highest == null || (int)priority < (int)highest.Value)
                    highest = priority;

                // Urgent(0) は最高値 — これ以上上がらない
                if (highest == DataSavePriority.Urgent) break;
            }

            return highest;
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
