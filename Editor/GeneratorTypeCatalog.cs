using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace TrueBase.Editor
{
    /// <summary>
    /// 유저 세이브·RemoteConfig 클래스 생성기가 공유하는 타입 카탈로그.
    /// (드롭다운 타입 목록·카테고리·인덱스 — 두 생성기의 단일 소스 오브 트루스)
    /// </summary>
    internal static class GeneratorTypeCatalog
    {
        internal static readonly string[] TypeOptions =
        {
            "bool",            // 0
            "int",             // 1
            "short",           // 2
            "long",            // 3
            "ulong",           // 4
            "float",           // 5
            "double",          // 6
            "string",          // 7
            "DateTimeOffset",  // 8
            "DateTime",        // 9
            "DateOnly",        // 10
            "TimeOnly",        // 11
        };

        /// <summary>Dictionary / List&lt;T&gt; / T[] 등 TypeOptions에 없는 타입을 내부적으로 표현하는 sentinel 인덱스.</summary>
        internal const int CustomTypeIndex = 12;

        /// <summary>카테고리에서 허용하는 TypeOptions 인덱스 배열을 반환합니다.</summary>
        public static int[] GetAllowedTypeIndices(FieldTypeCategory cat)
        {
            switch (cat)
            {
                case FieldTypeCategory.Boolean: return new[] { 0 };                       // bool
                case FieldTypeCategory.Integer: return new[] { 1, 2, 3, 4 };             // int/short/long/ulong
                case FieldTypeCategory.Float:   return new[] { 5, 6 };                    // float/double
                case FieldTypeCategory.String:  return new[] { 7, 8, 9, 10, 11 };        // string + 날짜 타입
                case FieldTypeCategory.Json:    return new int[0];                         // 전부 별도 팝업 처리 (Dictionary/List). string은 text 전용
                case FieldTypeCategory.Array:   return new int[0];                         // 별도 팝업 처리 (DrawTypePopup 참조)
                default:                        return new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 };
            }
        }

        /// <summary>TypeOptions에서 타입명의 인덱스를 찾습니다. 없으면 "string"(7)로 폴백.</summary>
        internal static int IndexOf(string type)
        {
            for (var i = 0; i < TypeOptions.Length; i++)
                if (TypeOptions[i] == type) return i;
            // "string"은 인덱스 7로 고정 — TypeOptions 변경 시 이 상수도 함께 수정할 것
            return 7;
        }

        // ── 자동 확장 컬렉션 타입 치환 ────────────────────────────────────────────────
        // 정책: 유저 세이브 생성기에서만 적용(게임이 수정·저장). RemoteConfig(읽기 전용)는 호출하지 않음
        // — 안전 읽기가 잘못된 설정 구조를 가릴 수 있어 일반 List/Dictionary를 유지합니다.

        // 자동 확장 타입으로 치환 가능한 "단순 값" 요소 타입(튜플·struct·클래스는 제외).
        private static readonly HashSet<string> s_autoValueLeafTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "int", "long", "short", "ulong", "uint", "byte", "sbyte", "float", "double", "decimal", "bool", "string"
        };

        private static bool IsAutoValueLeaf(string t) => t != null && s_autoValueLeafTypes.Contains(t.Trim());

        /// <summary>
        /// 요소가 단순 값 타입인 컬렉션을 자동 확장 타입으로 치환합니다.
        /// List/배열→AutoList, 이중 리스트→AutoList2D, Dictionary→AutoDict, 이중 딕셔너리→AutoDict2D.
        /// 튜플·struct·클래스 요소, 미인식 타입은 그대로 둡니다.
        /// </summary>
        internal static string MapToAutoType(string clrType)
        {
            if (string.IsNullOrWhiteSpace(clrType)) return clrType;
            var t = clrType.Trim();
            if (t.IndexOf("/*", StringComparison.Ordinal) >= 0) return clrType; // 미해결 스칼라(주석) → 그대로

            // 이중 리스트: List<List<X>> / X[][] → AutoList2D<X>
            if (TryUnwrapList(t, out var li) && TryUnwrapList(li, out var l2) && IsAutoValueLeaf(l2))
                return "AutoList2D<" + l2 + ">";
            if (TryUnwrapArray(t, out var ai) && TryUnwrapArray(ai, out var a2) && IsAutoValueLeaf(a2))
                return "AutoList2D<" + a2 + ">";

            // 1차원 리스트/배열: List<X> / X[] → AutoList<X>
            if (TryUnwrapList(t, out var l1) && IsAutoValueLeaf(l1)) return "AutoList<" + l1 + ">";
            if (TryUnwrapArray(t, out var a1) && IsAutoValueLeaf(a1)) return "AutoList<" + a1 + ">";

            // 이중 딕셔너리: Dictionary<K1, Dictionary<K2, X>> → AutoDict2D<K1, K2, X>
            if (TryUnwrapDict(t, out var k1, out var v1) && TryUnwrapDict(v1, out var k2, out var dv) && IsAutoValueLeaf(dv))
                return "AutoDict2D<" + k1 + ", " + k2 + ", " + dv + ">";

            // 1차원 딕셔너리: Dictionary<K, X> → AutoDict<K, X>
            if (TryUnwrapDict(t, out var k, out var v) && IsAutoValueLeaf(v)) return "AutoDict<" + k + ", " + v + ">";

            return clrType;
        }

        private static bool TryUnwrapList(string t, out string inner)
        {
            inner = null;
            t = t.Trim();
            if (t.StartsWith("List<", StringComparison.Ordinal) && t.EndsWith(">", StringComparison.Ordinal))
            {
                inner = t.Substring(5, t.Length - 6).Trim();
                return inner.Length > 0;
            }
            return false;
        }

        private static bool TryUnwrapArray(string t, out string elem)
        {
            elem = null;
            t = t.Trim();
            if (t.EndsWith("[]", StringComparison.Ordinal))
            {
                elem = t.Substring(0, t.Length - 2).Trim();
                return elem.Length > 0;
            }
            return false;
        }

        private static bool TryUnwrapDict(string t, out string key, out string val)
        {
            key = val = null;
            t = t.Trim();
            if (!t.StartsWith("Dictionary<", StringComparison.Ordinal) || !t.EndsWith(">", StringComparison.Ordinal))
                return false;

            var inner = t.Substring("Dictionary<".Length, t.Length - "Dictionary<".Length - 1);
            var depth = 0;
            var comma = -1;
            for (var i = 0; i < inner.Length; i++)
            {
                var ch = inner[i];
                if (ch == '<') depth++;
                else if (ch == '>') depth--;
                else if (ch == ',' && depth == 0) { comma = i; break; }
            }
            if (comma < 0) return false;

            key = inner.Substring(0, comma).Trim();
            val = inner.Substring(comma + 1).Trim();
            return key.Length > 0 && val.Length > 0;
        }

        // ── 기존 생성 파일에서 어트리뷰트별 (키 → 타입) 추출 (재생성 시 타입 보존용) ──────
        /// <summary>
        /// 생성된 .cs 소스에서 <c>[attributeName("key")] public/internal TYPE field;</c> 패턴을 읽어
        /// (키 → 타입명) 매핑을 반환합니다. 키가 생략된 경우 필드명을 키로 사용합니다.
        /// 유저 세이브(<c>DataColumn</c>)·RemoteConfig(<c>JsonProperty</c>) 양쪽이 공유합니다.
        /// </summary>
        internal static Dictionary<string, string> ExtractAttributedFieldTypes(string source, string attributeName)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(source)) return result;

            var pattern = new Regex(
                @"\[" + Regex.Escape(attributeName) + @"(?:\(""([^""]*)""\))?\]\s+(?:public|internal)\s+(.+?)\s+@?(\w+)\s*;",
                RegexOptions.Multiline);

            foreach (Match m in pattern.Matches(source))
            {
                var key      = m.Groups[1].Success && m.Groups[1].Value.Length > 0 ? m.Groups[1].Value : m.Groups[3].Value;
                var typeName = m.Groups[2].Value.Trim();
                if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(typeName))
                    result[key] = typeName;
            }

            return result;
        }

        /// <summary>
        /// 생성된 유저 세이브 .cs 에서 <c>[DataColumn("col")] [JsonProperty?] public/internal TYPE field …;</c> 를 읽어
        /// (컬럼명 → C# 필드 식별자) 매핑을 반환합니다. 사이에 <c>[JsonProperty(...)]</c> 등 다른 어트리뷰트가 끼어 있어도 매칭합니다.
        /// 재생성 시 커스텀 필드명 복원에 사용합니다. ('@' 키워드 접두는 제거)
        /// </summary>
        internal static Dictionary<string, string> ExtractDataColumnFieldNames(string source)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(source)) return result;

            var pattern = new Regex(
                @"\[DataColumn\(""([^""]*)""[^)]*\)\]\s*(?:\[[^\]]*\]\s*)*(?:public|internal)\s+.+?\s+(@?\w+)\s*(?:=[^;]*)?;",
                RegexOptions.Multiline);

            foreach (Match m in pattern.Matches(source))
            {
                var col   = m.Groups[1].Value;
                var field = m.Groups[2].Value;
                if (field.StartsWith("@", StringComparison.Ordinal)) field = field.Substring(1);
                if (!string.IsNullOrEmpty(col) && !string.IsNullOrEmpty(field))
                    result[col] = field;
            }

            return result;
        }

        // ── 공용 문자열 유틸 (두 생성기 단일 소스) ────────────────────────────────────

        /// <summary>
        /// 이름을 PascalCase 식별자로 변환합니다. 비식별 문자(_, -, ., 공백 등)는 단어 구분자로 보고
        /// 각 단어 첫 글자를 대문자로, 구분자는 제거합니다. 숫자로 시작하면 '_' 접두.
        /// </summary>
        internal static string ToPascalCase(string name)
        {
            if (string.IsNullOrEmpty(name)) return "_";

            var sb = new StringBuilder(name.Length);
            var startOfWord = true;
            foreach (var ch in name)
            {
                if (!char.IsLetterOrDigit(ch)) { startOfWord = true; continue; }
                sb.Append(startOfWord ? char.ToUpperInvariant(ch) : ch);
                startOfWord = false;
            }

            if (sb.Length == 0) return "_";
            if (char.IsDigit(sb[0])) sb.Insert(0, '_'); // 숫자로 시작하면 식별자 무효 → '_' 접두
            return sb.ToString();
        }

        /// <summary>
        /// 컬럼/키 이름을 유효한 C# 필드 식별자로 변환합니다. 유효하지 않은 문자는 '_'로,
        /// 숫자로 시작하면 '_' 접두, C# 키워드는 '@' 접두. (JSON 키는 호출부에서 <c>[JsonProperty]</c>로 보존)
        /// </summary>
        internal static string LegalFieldName(string rawName)
        {
            if (string.IsNullOrEmpty(rawName)) return "_";

            var sb = new StringBuilder(rawName.Length);
            foreach (var ch in rawName)
                sb.Append(char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_');

            var name = sb.ToString();
            if (name.Length == 0 || char.IsDigit(name[0]))
                name = "_" + name;

            return IsCSharpKeyword(name) ? "@" + name : name;
        }

        internal static bool IsCSharpKeyword(string s)
        {
            switch (s)
            {
                case "abstract": case "as": case "base": case "bool": case "break":
                case "byte": case "case": case "catch": case "char": case "checked":
                case "class": case "const": case "continue": case "decimal": case "default":
                case "delegate": case "do": case "double": case "else": case "enum":
                case "event": case "explicit": case "extern": case "false": case "finally":
                case "fixed": case "float": case "for": case "foreach": case "goto":
                case "if": case "implicit": case "in": case "int": case "interface":
                case "internal": case "is": case "lock": case "long": case "namespace":
                case "new": case "null": case "object": case "operator": case "out":
                case "override": case "params": case "private": case "protected": case "public":
                case "readonly": case "ref": case "return": case "sbyte": case "sealed":
                case "short": case "sizeof": case "stackalloc": case "static": case "string":
                case "struct": case "switch": case "this": case "throw": case "true":
                case "try": case "typeof": case "uint": case "ulong": case "unchecked":
                case "unsafe": case "ushort": case "using": case "virtual": case "void":
                case "volatile": case "while":
                    return true;
                default:
                    return false;
            }
        }

        internal static string EscapeXml(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Replace("&", "&amp;", StringComparison.Ordinal)
                    .Replace("<", "&lt;", StringComparison.Ordinal)
                    .Replace(">", "&gt;", StringComparison.Ordinal);
        }

        internal static string EscapeCSharpString(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Replace("\\", "\\\\", StringComparison.Ordinal)
                    .Replace("\"", "\\\"", StringComparison.Ordinal);
        }

        /// <summary>
        /// 레거시 대시보드 JWT 키(<c>eyJ…</c>) 여부. PostgREST에서 <c>Authorization: Bearer</c>에 동일 값을 두는 패턴 판별용.
        /// 새 Publishable/Secret 키(<c>sb_publishable_</c>·<c>sb_secret_</c>)는 JWT가 아니므로 false.
        /// </summary>
        internal static bool IsLegacyJwtStyleApiKey(string key)
        {
            return !string.IsNullOrEmpty(key) && key.Length >= 20
                && key.StartsWith("eyJ", StringComparison.Ordinal)
                && key.IndexOf('.', StringComparison.Ordinal) > 0;
        }
    }

    /// <summary>필드의 JSON 타입 카테고리 — Inspector 드롭다운 필터링에 사용합니다.</summary>
    internal enum FieldTypeCategory
    {
        Boolean,  // bool, 커스텀
        Integer,  // int, short, long, ulong, 커스텀
        Float,    // float, double, 커스텀
        String,   // string, DateTimeOffset, DateTime, DateOnly, TimeOnly, 커스텀
        Json,     // Dictionary<string,object>(기본) / List<T> — 별도 팝업. string은 text 전용  ← jsonb/$ref/allOf 등 복잡한 DB 타입
        Array,    // 커스텀 전용
        Unknown,  // 전체 표시
    }
}
