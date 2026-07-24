using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;

namespace TrueBase.Core.Data
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
        /// <paramref name="previous"/>와 <paramref name="current"/> 사이에 값이 바뀐 매핑 컬럼이
        /// 하나라도 있으면 true. 컬렉션 등 참조 타입의 제자리 변경도 값 비교로 감지합니다.
        /// (MarkDirty 플래그 없이 변경을 잡아내는 동기화 경로에 사용)
        /// </summary>
        public static bool HasChanges<T>(T previous, T current)
        {
            if (current == null)
                return false;

            foreach (var m in GetMappedMembers(typeof(T)))
            {
                var col = ResolveColumnName(m);
                if (string.IsNullOrEmpty(col) || string.Equals(col, UpdatedAtColumn, StringComparison.Ordinal))
                    continue;

                if (!EqualsValues(GetValue(m, previous), GetValue(m, current)))
                    return true;
            }

            return false;
        }

        private static readonly Dictionary<Type, bool> _hasRefColumnsCache = new();

        /// <summary>
        /// T에 값 타입·string이 아닌(컬렉션·클래스 등) 매핑 컬럼이 하나라도 있으면 true. 결과는 캐시됩니다.
        /// 참조 컬럼이 없는 스칼라 전용 Row는 값 비교 비용을 들일 필요가 없으므로 이 판별로 건너뜁니다.
        /// </summary>
        public static bool HasReferenceColumns<T>()
        {
            var t = typeof(T);
            if (_hasRefColumnsCache.TryGetValue(t, out var cached))
                return cached;

            var has = false;
            foreach (var m in GetMappedMembers(t))
            {
                var mt = (m as FieldInfo)?.FieldType ?? (m as PropertyInfo)?.PropertyType;
                if (mt != null && !mt.IsValueType && mt != typeof(string))
                {
                    has = true;
                    break;
                }
            }

            _hasRefColumnsCache[t] = has;
            return has;
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

        /// <summary>
        /// 서버 Row를 <paramref name="dst"/>에 적용하되, <b>참조 타입 컬럼</b>이 서버에서 null(SQL NULL)이면
        /// <paramref name="fallback"/>의 값을 유지합니다. 값 타입 컬럼은 항상 <paramref name="serverRow"/>가 우선합니다
        /// (서버의 "없음"과 "기본값"을 구분할 수 없으므로).
        /// <para>
        /// <paramref name="fallback"/>이 null이거나 해당 멤버가 null이면 결과는 <see cref="CopyInto{T}"/>와 동일합니다
        /// (참조 null→null, 값 타입 그대로). 즉 fallback이 없으면 기존 복사 동작의 엄격한 상위집합입니다.
        /// </para>
        /// 로드 전에 개발자가 지정한 컬렉션 초기값을, 서버에 아직 값이 없을 때만 살리는 데 사용합니다.
        /// </summary>
        public static void MergeServerOverFallback<T>(T dst, T serverRow, T fallback) where T : class, new()
        {
            if (dst == null) return;
            var server = serverRow ?? new T();
            foreach (var m in GetMappedMembers(typeof(T)))
            {
                var mt = MemberValueType(m);
                if (mt != null && mt.IsValueType)
                {
                    // 값 타입: 항상 서버 우선.
                    TrySetValue(m, dst, GetValue(m, server));
                    continue;
                }

                var serverVal   = GetValue(m, server);
                var fallbackVal = GetValue(m, fallback);

                // Auto* 컬렉션(AutoList·AutoDict·2D): 서버 데이터는 유지하고 fallback으로 "빈 슬롯·키"만 채운다.
                // 빈 = 서버에 슬롯/키가 없거나 값이 기본값. 게임 업데이트로 커지거나 키가 추가돼도 기존(비기본) 값 보존.
                if (serverVal is IAutoDefaultable && fallbackVal is IAutoDefaultable)
                {
                    // dst가 서버 row·fallback 스냅샷과 참조를 공유하지 않도록 둘 다 복제.
                    var mergedClone = (IAutoDefaultable)DeepCloneIfReference(serverVal);
                    var fbClone     = (IAutoDefaultable)DeepCloneIfReference(fallbackVal);
                    // JSON 복제로 소실된 기본값 레시피를 [AutoDefault]에서 복원 — FillMissingFrom이 "서버값=기본값"을 판정하는 데 필요.
                    var autoDef = m.GetCustomAttribute<AutoDefaultAttribute>();
                    if (autoDef != null)
                    {
                        mergedClone.SetDefaultValue(autoDef.Values);
                        fbClone.SetDefaultValue(autoDef.Values);
                    }
                    mergedClone.FillMissingFrom(fbClone);
                    TrySetValue(m, dst, mergedClone);
                    continue;
                }

                // 그 외 참조 타입: 서버에 값이 있으면 서버, 없으면(null) fallback을 유지(컬럼 단위).
                TrySetValue(m, dst, DeepCloneIfReference(serverVal ?? fallbackVal));
            }
        }

        private static void CopyColumnsInto<T>(T dst, T src)
        {
            foreach (var m in GetMappedMembers(typeof(T)))
            {
                // 참조 타입은 깊은 복사 — 스냅샷이 현재 인스턴스와 참조를 공유하지 않게 해
                // EqualsValues가 내부 값 변경을 감지하고, 불변 시 PATCH에서 제외할 수 있게 합니다.
                var value = DeepCloneIfReference(GetValue(m, src));
                if (m is FieldInfo f)
                    f.SetValue(dst, value);
                else if (m is PropertyInfo p && p.CanWrite)
                    p.SetValue(dst, value);
            }
        }

        /// <summary>
        /// 참조 타입(컬렉션·클래스)을 JSON 왕복으로 독립 복제합니다. 값 타입·string은 그대로 반환.
        /// 복제 불가(순환참조 등) 시 원본 참조를 반환 → EqualsValues의 ReferenceEquals 경로가
        /// 안전하게 '변경됨'으로 처리(데이터 누락 없음).
        /// </summary>
        private static object DeepCloneIfReference(object value)
        {
            if (value == null) return null;
            var t = value.GetType();
            if (t.IsValueType || t == typeof(string)) return value;
            try { return JsonConvert.DeserializeObject(JsonConvert.SerializeObject(value), t); }
            catch { return value; }
        }

        private static bool EqualsValues(object a, object b)
        {
            if (a == null && b == null)
                return true;
            if (a == null || b == null)
                return false;

            var t = a.GetType();
            if (t.IsValueType || t == typeof(string))
                return a.Equals(b);

            // 참조 타입이 같은 인스턴스 = 스냅샷 깊은복제 실패로 참조를 공유 중.
            // 내부 변경을 값으로 판별할 수 없으므로 안전하게 '변경됨'으로 처리합니다.
            if (ReferenceEquals(a, b))
                return false;

            // AutoDict/AutoDict2D(참조 타입 값)는 "기본값과 같은 항목=없는 것"으로 정규화한 뒤 비교합니다.
            // 조회만으로 자동 생성된, 아직 손대지 않은 항목이 변경으로 잡혀 불필요한 저장을 유발하지 않게 하기 위함입니다.
            // AutoList 계열·값 타입 등 대상이 아니면 false를 반환해 아래 전체 JSON 비교로 폴백합니다.
            if (a is IAutoDefaultable && TryNormalizeAutoDictForCompare(a, out var normA) && TryNormalizeAutoDictForCompare(b, out var normB))
            {
                try { return JsonValueEquals(normA, normB); }
                catch { return false; }
            }

            // 컬렉션은 개수가 다르면 내용 비교 없이 즉시 변경으로 판정(직렬화 생략).
            if (a is ICollection ca && b is ICollection cb && ca.Count != cb.Count)
                return false;

            // 독립 인스턴스 → 전송될 JSON 기준으로 구조적 값 비교.
            // 같으면 실제 변경 없음 → PATCH에서 제외(불필요한 전송 방지).
            try { return JsonValueEquals(a, b); }
            catch { return false; }
        }

        [ThreadStatic] private static StringBuilder _cmpSbA;
        [ThreadStatic] private static StringBuilder _cmpSbB;

        // JsonConvert.SerializeObject(a) == SerializeObject(b) 와 동일한 판정이지만,
        // 결과 문자열 2개를 새로 할당하지 않고 재사용 StringBuilder에 직렬화해 비교합니다.
        // HasChanges가 빈번히 호출될 때 큰 JSON 문자열 할당으로 인한 GC 스파이크를 없앱니다.
        private static bool JsonValueEquals(object a, object b)
        {
            var sbA = _cmpSbA ??= new StringBuilder(128);
            var sbB = _cmpSbB ??= new StringBuilder(128);
            sbA.Length = 0;
            sbB.Length = 0;

            var serializer = JsonSerializer.CreateDefault();
            using (var w = new StringWriter(sbA)) serializer.Serialize(w, a);
            using (var w = new StringWriter(sbB)) serializer.Serialize(w, b);

            if (sbA.Length != sbB.Length) return false;
            for (int i = 0; i < sbA.Length; i++)
                if (sbA[i] != sbB[i]) return false;
            return true;
        }

        /// <summary>
        /// <paramref name="obj"/>가 <see cref="AutoDict{TKey,TValue}"/>/<see cref="AutoDict2D{TKey1,TKey2,TValue}"/>이고
        /// 값이 참조 타입이면, 값이 현재 기본값과 구조적으로 같은 항목을 제외한 정규화된 표현(키→JSON 맵)을 만듭니다.
        /// AutoDict2D는 안쪽 딕셔너리를 재귀적으로 정규화하고, 정규화 후 빈 안쪽 딕셔너리는 바깥 키째로 제외합니다
        /// (딕셔너리는 항목 순서에 의미가 없어 제외해도 안전 — AutoList 계열은 슬롯 순서가 의미 있어 대상에서 뺍니다).
        /// 대상이 아니면(AutoList 계열, 값 타입 값 등) false — 호출부는 전체 JSON 비교로 폴백합니다.
        /// </summary>
        private static bool TryNormalizeAutoDictForCompare(object obj, out object normalized)
        {
            normalized = null;
            if (obj is not IAutoDefaultable autoDefaultable || obj is not IDictionary dict)
                return false;

            var type = obj.GetType();
            if (!type.IsGenericType) return false;
            var genericDef = type.GetGenericTypeDefinition();

            if (genericDef == typeof(AutoDict<,>))
            {
                var valueType = type.GetGenericArguments()[1];
                if (!AutoDefaultConvert.IsReferenceKind(valueType)) return false;

                var freshDefault = autoDefaultable.GetDefaultValueBoxed();
                var defaultJson  = freshDefault != null ? JsonConvert.SerializeObject(freshDefault) : "null";

                var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
                foreach (DictionaryEntry entry in dict)
                {
                    var entryJson = JsonConvert.SerializeObject(entry.Value);
                    if (entryJson == defaultJson) continue; // 기본값과 동일 → 없는 것과 동일 취급
                    result[JsonConvert.SerializeObject(entry.Key)] = entryJson;
                }
                normalized = result;
                return true;
            }

            if (genericDef == typeof(AutoDict2D<,,>))
            {
                var valueType = type.GetGenericArguments()[2];
                if (!AutoDefaultConvert.IsReferenceKind(valueType)) return false;

                var result = new SortedDictionary<string, object>(StringComparer.Ordinal);
                foreach (DictionaryEntry entry in dict)
                {
                    if (entry.Value == null) continue;
                    if (!TryNormalizeAutoDictForCompare(entry.Value, out var innerNorm)) continue;
                    if (((IDictionary)innerNorm).Count == 0) continue; // 안쪽이 전부 기본값 → 바깥 키도 없는 것과 동일 취급
                    result[JsonConvert.SerializeObject(entry.Key)] = innerNorm;
                }
                normalized = result;
                return true;
            }

            return false;
        }

        private static readonly Dictionary<Type, MemberInfo[]> _mappedMembersCache = new();

        // [DataColumn] 매핑 멤버(프로퍼티→필드 순)를 타입별로 한 번만 리플렉션해 캐시합니다.
        // HasChanges 등 매 프레임·저장 요청마다 도는 경로에서 리플렉션 재열거 할당을 없앱니다.
        private static MemberInfo[] GetMappedMembers(Type t)
        {
            if (_mappedMembersCache.TryGetValue(t, out var cached))
                return cached;

            // private 필드도 매핑 — 생성 클래스가 [DataColumn] 필드를 private으로 두고
            // 정적 프로퍼티(MarkDirty setter)로만 접근하도록 강제하기 위함.
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var list = new List<MemberInfo>();
            foreach (var p in t.GetProperties(flags))
            {
                if (p.GetIndexParameters().Length > 0) continue;
                if (!HasDataColumnAttribute(p)) continue;
                if (p.CanRead == false) continue;
                list.Add(p);
            }
            foreach (var f in t.GetFields(flags))
            {
                if (!HasDataColumnAttribute(f)) continue;
                list.Add(f);
            }

            var arr = list.ToArray();
            _mappedMembersCache[t] = arr;
            return arr;
        }

        private static readonly Dictionary<Type, MemberInfo[]> _referenceMembersCache = new();

        // 참조 타입(컬렉션·클래스) 매핑 멤버만 골라 캐시합니다. referenceOnly 우선순위 검사에서
        // 매 호출 값타입 여부를 재판별하지 않도록 미리 걸러 둡니다.
        private static MemberInfo[] GetReferenceMembers(Type t)
        {
            if (_referenceMembersCache.TryGetValue(t, out var cached))
                return cached;

            var list = new List<MemberInfo>();
            foreach (var m in GetMappedMembers(t))
            {
                var mt = MemberValueType(m);
                if (mt != null && !mt.IsValueType && mt != typeof(string))
                    list.Add(m);
            }

            var arr = list.ToArray();
            _referenceMembersCache[t] = arr;
            return arr;
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

        private static readonly Dictionary<MemberInfo, string> _columnNameCache = new();

        // 컬럼명 조회는 어트리뷰트 읽기라 매 호출 비용이 있으므로 멤버별로 캐시합니다.
        private static string ResolveColumnName(MemberInfo m)
        {
            if (_columnNameCache.TryGetValue(m, out var c))
                return c;
            c = ResolveColumnNameCore(m);
            _columnNameCache[m] = c;
            return c;
        }

        private static string ResolveColumnNameCore(MemberInfo m)
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
        private static readonly Dictionary<MemberInfo, DataSavePriority> _columnPriorityCache = new();

        // 우선순위 조회도 어트리뷰트 읽기라 멤버별로 캐시합니다(GetHighestDirtyPriority 경로에서 반복 호출).
        private static DataSavePriority ResolveColumnPriority(MemberInfo m)
        {
            if (_columnPriorityCache.TryGetValue(m, out var cached))
                return cached;
            var p = ResolveColumnPriorityCore(m);
            _columnPriorityCache[m] = p;
            return p;
        }

        private static DataSavePriority ResolveColumnPriorityCore(MemberInfo m)
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
        public static DataSavePriority? GetHighestDirtyPriority<T>(T previous, T current, bool referenceOnly = false)
        {
            if (current == null) return null;

            DataSavePriority? highest = null;

            // referenceOnly면 참조 컬럼만 담긴 캐시 배열을 순회(스칼라는 setter가 증분 추적하므로 제외).
            var members = referenceOnly ? GetReferenceMembers(typeof(T)) : GetMappedMembers(typeof(T));
            foreach (var m in members)
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

                // Urgent(0)가 최고 우선순위 — 더 올라갈 수 없음
                if (highest == DataSavePriority.Urgent) break;
            }

            return highest;
        }

        private static readonly Dictionary<Type, MemberInfo[]> _autoDefaultMembersCache = new();

        /// <summary>
        /// <paramref name="row"/>의 <see cref="AutoDefaultAttribute"/> 멤버(AutoList/AutoDict)에 지정 기본값을 주입합니다.
        /// 로드 직후(Current 구성 후) 호출하세요. 멤버 목록은 타입별 1회 캐시됩니다.
        /// </summary>
        public static void ApplyAutoDefaults<T>(T row) where T : class
        {
            if (row == null) return;

            foreach (var m in GetAutoDefaultMembers(typeof(T)))
            {
                var attr = m.GetCustomAttribute<AutoDefaultAttribute>();
                if (attr == null) continue;

                var value = GetValue(m, row);
                if (value == null)
                {
                    // DB 컬럼이 null이면 로드/역직렬화 시 이 멤버가 null이 된다 — 빈 인스턴스로 복구한다.
                    // (복구하지 않으면 이후 AutoList/AutoDict 접근이 NullReferenceException으로 터진다.)
                    var memberType = MemberValueType(m);
                    if (memberType != null && typeof(IAutoDefaultable).IsAssignableFrom(memberType))
                    {
                        value = Activator.CreateInstance(memberType);
                        TrySetValue(m, row, value);
                    }
                }

                if (value is IAutoDefaultable target)
                    target.SetDefaultValue(attr.Values);
            }
        }

        private static Type MemberValueType(MemberInfo m) => m switch
        {
            FieldInfo f => f.FieldType,
            PropertyInfo p => p.PropertyType,
            _ => null
        };

        private static void TrySetValue(MemberInfo m, object instance, object value)
        {
            switch (m)
            {
                case FieldInfo f: f.SetValue(instance, value); break;
                case PropertyInfo p when p.CanWrite: p.SetValue(instance, value); break;
            }
        }

        private static MemberInfo[] GetAutoDefaultMembers(Type t)
        {
            if (_autoDefaultMembersCache.TryGetValue(t, out var cached))
                return cached;

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var list = new List<MemberInfo>();
            foreach (var f in t.GetFields(flags))
                if (f.GetCustomAttribute<AutoDefaultAttribute>() != null)
                    list.Add(f);
            foreach (var p in t.GetProperties(flags))
                if (p.GetIndexParameters().Length == 0 && p.CanRead && p.GetCustomAttribute<AutoDefaultAttribute>() != null)
                    list.Add(p);

            var arr = list.ToArray();
            _autoDefaultMembersCache[t] = arr;
            return arr;
        }

        /// <summary>유저 세이브 테이블명 <c>"user_data"</c>를 반환합니다.</summary>
        public static string ResolveTableName<T>() => UserDataTableName;

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
