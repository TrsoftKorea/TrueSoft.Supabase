using System;
using System.Collections.Generic;
using System.Globalization;
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

        /// <summary>TypeOptions에서 타입명의 인덱스를 찾습니다. 없으면 "string"(7)로 폴백.</summary>
        internal static int IndexOf(string type)
        {
            for (var i = 0; i < TypeOptions.Length; i++)
                if (TypeOptions[i] == type) return i;
            // "string"은 인덱스 7로 고정 — TypeOptions 변경 시 이 상수도 함께 수정할 것
            return 7;
        }

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

        // 커스텀 타입 해석 (CSV 검증 · 클래스 값 Auto 승격 공용)

        /// <summary>
        /// CLR 타입 문자열이 컴파일 시 해석 가능한지 검사합니다.
        /// 카탈로그 타입은 통과, 컬렉션(List/배열/Dictionary, 이중 포함)은 요소 타입으로 재귀,
        /// 그 외 식별자(enum·커스텀 클래스)는 로드된 어셈블리에서 이름으로 찾습니다.
        /// <c>object</c>는 "미지정" 센티널로 별도 경고가 처리하므로 여기서는 통과시킵니다.
        /// </summary>
        internal static bool IsResolvableClrType(string clrType, Dictionary<string, bool> cache)
        {
            if (string.IsNullOrWhiteSpace(clrType)) return false;
            var t = clrType.Trim();
            if (cache.TryGetValue(t, out var cached)) return cached;

            bool ok;
            if (t.IndexOf("/*", StringComparison.Ordinal) >= 0)
                ok = false; // 미해결 플레이스홀더
            else if (t == "object" || Array.IndexOf(TypeOptions, t) >= 0)
                ok = true;
            else if (t.EndsWith("[]", StringComparison.Ordinal))
                ok = IsResolvableClrType(t.Substring(0, t.Length - 2), cache);
            else if (TryUnwrapList(t, out var elem))
                ok = IsResolvableClrType(elem, cache);
            else if (TryUnwrapDict(t, out var k, out var v))
                ok = IsResolvableClrType(k, cache) && IsResolvableClrType(v, cache);
            else
                ok = FindTypeByName(t) != null;

            cache[t] = ok;
            return ok;
        }

        /// <summary>
        /// 로드된 어셈블리에서 타입 이름으로 타입을 찾습니다. 정규화 이름, 중첩 타입, 단순 이름을 모두 시도합니다.
        /// C# 소스는 중첩 타입을 <c>Outer.Inner</c>로 쓰지만 리플렉션은 <c>Outer+Inner</c>를 요구하므로,
        /// 뒤쪽 <c>.</c>부터 차례로 <c>+</c>로 바꿔가며 재시도합니다.
        /// 정규화 이름 없는 단순 이름은 전체 어셈블리에서 이름만으로 찾되, 후보가 둘 이상(서로 다른 타입)이면
        /// 모호하므로 null을 반환합니다 — 이 결과가 CSV 검증뿐 아니라 클래스 값 Auto 승격의 실제 생성 코드에도
        /// 쓰이므로, 잘못된 타입을 조용히 골라 엉뚱한 코드를 내보내는 것보다 미해석 처리해 안전한 폴백으로
        /// 넘기는 편이 낫습니다. 호출 빈도가 낮은 검증·생성 경로라 전체 스캔 비용은 허용됩니다.
        /// </summary>
        internal static Type FindTypeByName(string name)
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();

            // 정규화 이름 + 중첩 타입(Outer.Inner → Outer+Inner) 후보들을 각 어셈블리에서 시도 — 유일 매칭이라 모호성 없음
            foreach (var candidate in NestedNameCandidates(name))
                foreach (var asm in assemblies)
                {
                    var found = asm.GetType(candidate, false);
                    if (found != null) return found;
                }

            // 네임스페이스 없는 단순 이름: 마지막 세그먼트로 전체 탐색. 서로 다른 타입이 둘 이상 매칭되면 모호 → null.
            var simple = name;
            var lastDot = simple.LastIndexOf('.');
            if (lastDot >= 0) simple = simple.Substring(lastDot + 1);

            Type match = null;
            foreach (var asm in assemblies)
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch { continue; }
                foreach (var t in types)
                {
                    if (t.Name != simple) continue;
                    if (match != null && match != t) return null;
                    match = t;
                }
            }
            return match;
        }

        /// <summary>
        /// <c>A.B.C</c>에 대해 뒤쪽 <c>.</c>부터 <c>+</c>로 바꾼 후보들을 순서대로 반환합니다:
        /// <c>A.B.C</c> → <c>A.B+C</c> → <c>A+B+C</c>. 어느 세그먼트 경계가 네임스페이스/중첩인지 모르므로 전부 시도합니다.
        /// </summary>
        internal static IEnumerable<string> NestedNameCandidates(string name)
        {
            yield return name;
            var chars = name.ToCharArray();
            for (var i = chars.Length - 1; i >= 0; i--)
            {
                if (chars[i] != '.') continue;
                chars[i] = '+';
                yield return new string(chars);
            }
        }

        /// <summary>
        /// CSV 기본값이 지정된 컬럼에서, 요소·값이 단순 값은 아니지만(클래스 등) 실제로 해석 가능한
        /// List/Dictionary(이중 포함)를 Auto 컬렉션 타입으로 치환합니다. 해석된 조각은 완전히 정규화된
        /// 이름으로 바꿔 써서, CSV에 네임스페이스를 안 적어도 생성 코드가 컴파일되게 합니다.
        /// 이미 스칼라 요소(MapToAutoType가 처리)이거나, 해석 불가·모호하면 false — 호출부는 SeedDefault_* 폴백으로 넘어갑니다.
        /// </summary>
        internal static bool TryUpgradeClassElementCollection(string clrType, out string upgradedType)
        {
            upgradedType = null;
            if (string.IsNullOrWhiteSpace(clrType)) return false;
            var t = clrType.Trim();
            if (t.IndexOf("/*", StringComparison.Ordinal) >= 0) return false;

            if (TryUnwrapList(t, out var li) && TryUnwrapList(li, out var l2) && !IsAutoValueLeaf(l2) && TryQualify(l2, out var ql2))
            { upgradedType = "AutoList2D<" + ql2 + ">"; return true; }
            if (TryUnwrapArray(t, out var ai) && TryUnwrapArray(ai, out var a2) && !IsAutoValueLeaf(a2) && TryQualify(a2, out var qa2))
            { upgradedType = "AutoList2D<" + qa2 + ">"; return true; }

            if (TryUnwrapList(t, out var l1) && !IsAutoValueLeaf(l1) && TryQualify(l1, out var ql1))
            { upgradedType = "AutoList<" + ql1 + ">"; return true; }
            if (TryUnwrapArray(t, out var a1) && !IsAutoValueLeaf(a1) && TryQualify(a1, out var qa1))
            { upgradedType = "AutoList<" + qa1 + ">"; return true; }

            if (TryUnwrapDict(t, out var k1, out var v1) && TryUnwrapDict(v1, out var k2, out var dv) && !IsAutoValueLeaf(dv)
                && TryQualify(k1, out var qk1) && TryQualify(k2, out var qk2) && TryQualify(dv, out var qdv))
            { upgradedType = "AutoDict2D<" + qk1 + ", " + qk2 + ", " + qdv + ">"; return true; }

            if (TryUnwrapDict(t, out var k, out var v) && !IsAutoValueLeaf(v)
                && TryQualify(k, out var qk) && TryQualify(v, out var qv))
            { upgradedType = "AutoDict<" + qk + ", " + qv + ">"; return true; }

            return false;
        }

        /// <summary>
        /// 스칼라 리프면 그대로 통과, 아니면 리플렉션으로 찾아 완전히 정규화된 이름(중첩 타입의 <c>+</c>는 <c>.</c>로)으로
        /// 치환합니다. 못 찾거나 모호하면 실패.
        /// </summary>
        private static bool TryQualify(string typeName, out string qualified)
        {
            var name = typeName.Trim();
            if (IsAutoValueLeaf(name) || Array.IndexOf(TypeOptions, name) >= 0) { qualified = name; return true; }

            var found = FindTypeByName(name);
            if (found == null) { qualified = null; return false; }
            qualified = found.FullName.Replace('+', '.');
            return true;
        }

        // 기존 생성 파일에서 어트리뷰트별 (키 → 타입) 추출 (재생성 시 타입 보존용)
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

        // 공용 문자열 유틸 (두 생성기 단일 소스)

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

        // 기본값(스칼라 초기화 / AutoDefault) 지원

        /// <summary>TypeOptions(스칼라 값 타입·string·날짜)인지 여부.</summary>
        internal static bool IsScalarTypeName(string clrType)
            => !string.IsNullOrWhiteSpace(clrType) && Array.IndexOf(TypeOptions, clrType.Trim()) >= 0;

        /// <summary>
        /// AutoList/AutoList2D/AutoDict/AutoDict2D 여부와 요소(값) 타입을 반환합니다.
        /// AutoDict 계열은 마지막 제네릭 인자(값 타입)를 요소 타입으로 봅니다. (이들만 <c>[AutoDefault]</c> 대상)
        /// </summary>
        internal static bool IsAutoDefaultableType(string clrType, out string elementType)
        {
            elementType = null;
            if (string.IsNullOrWhiteSpace(clrType)) return false;
            var t = clrType.Trim();
            if (!t.EndsWith(">", StringComparison.Ordinal)) return false;

            foreach (var prefix in new[] { "AutoList2D<", "AutoList<" })
                if (t.StartsWith(prefix, StringComparison.Ordinal))
                {
                    elementType = t.Substring(prefix.Length, t.Length - prefix.Length - 1).Trim();
                    return elementType.Length > 0;
                }

            foreach (var prefix in new[] { "AutoDict2D<", "AutoDict<" })
                if (t.StartsWith(prefix, StringComparison.Ordinal))
                {
                    var inner = t.Substring(prefix.Length, t.Length - prefix.Length - 1);
                    var parts = SplitTopLevelCommas(inner);
                    if (parts.Count == 0) return false;
                    elementType = parts[parts.Count - 1].Trim();
                    return elementType.Length > 0;
                }

            return false;
        }

        /// <summary>
        /// 스칼라 기본값(<paramref name="raw"/>)을 <paramref name="clrType"/>에 맞는 C# 리터럴로 변환합니다.
        /// 멱등 — 이미 리터럴 형태(따옴표·접미사 포함)면 그대로 둡니다. raw가 비었거나 타입과 안 맞으면 false.
        /// </summary>
        internal static bool TryFormatScalarLiteral(string clrType, string raw, out string literal)
        {
            literal = null;
            if (string.IsNullOrWhiteSpace(raw) || string.IsNullOrWhiteSpace(clrType)) return false;
            var s = raw.Trim();
            switch (clrType.Trim())
            {
                case "bool":
                    if (string.Equals(s, "true",  StringComparison.OrdinalIgnoreCase)) { literal = "true";  return true; }
                    if (string.Equals(s, "false", StringComparison.OrdinalIgnoreCase)) { literal = "false"; return true; }
                    return false;
                case "string":
                    literal = IsQuoted(s) ? s : "\"" + EscapeCSharpString(s) + "\"";
                    return true;
                case "int":
                    if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)) { literal = s; return true; }
                    return false;
                case "short":
                    if (short.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)) { literal = s; return true; }
                    return false;
                case "long":
                {
                    var v = TrimNumericSuffix(s, "lL");
                    if (long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)) { literal = v + "L"; return true; }
                    return false;
                }
                case "ulong":
                {
                    var v = TrimNumericSuffix(TrimNumericSuffix(s, "lL"), "uU");
                    if (ulong.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)) { literal = v + "UL"; return true; }
                    return false;
                }
                case "float":
                {
                    var v = TrimNumericSuffix(s, "fF");
                    if (float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) { literal = v + "f"; return true; }
                    return false;
                }
                case "double":
                {
                    var v = TrimNumericSuffix(s, "dD");
                    if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) { literal = v; return true; }
                    return false;
                }
                case "DateTimeOffset":
                case "DateTime":
                case "DateOnly":
                case "TimeOnly":
                {
                    var inner = IsQuoted(s) ? s.Substring(1, s.Length - 2) : s;
                    literal = clrType.Trim() + ".Parse(\"" + EscapeCSharpString(inner) + "\")";
                    return true;
                }
                default:
                    return false;
            }
        }

        /// <summary>
        /// <c>[AutoDefault(...)]</c> 인자 목록을 만듭니다. 단일 값은 요소 타입 리터럴로 변환(예: AutoList&lt;string&gt;→<c>"none"</c>),
        /// 쉼표로 나뉜 여러 값(struct 생성자 인자)은 그대로 전달합니다.
        /// </summary>
        internal static string FormatAutoDefaultArgs(string elementType, string raw)
        {
            var s = (raw ?? string.Empty).Trim();
            var parts = SplitTopLevelCommas(s);
            if (parts.Count <= 1)
            {
                var p = (parts.Count == 1 ? parts[0] : s).Trim();
                return TryFormatScalarLiteral(elementType, p, out var lit) ? lit : p;
            }

            for (var i = 0; i < parts.Count; i++) parts[i] = parts[i].Trim();
            return string.Join(", ", parts);
        }

        private static bool IsQuoted(string s)
            => s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"';

        private static string TrimNumericSuffix(string s, string suffixChars)
            => s.Length > 1 && suffixChars.IndexOf(s[s.Length - 1]) >= 0 ? s.Substring(0, s.Length - 1) : s;

        /// <summary>최상위(따옴표·꺾쇠·괄호 밖) 쉼표로 문자열을 분리합니다. 빈 입력은 빈 목록.</summary>
        internal static List<string> SplitTopLevelCommas(string s)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(s)) return result;

            var depth = 0;
            var inStr = false;
            var start = 0;
            for (var i = 0; i < s.Length; i++)
            {
                var ch = s[i];
                if (inStr)
                {
                    if (ch == '"' && (i == 0 || s[i - 1] != '\\')) inStr = false;
                    continue;
                }
                switch (ch)
                {
                    case '"': inStr = true; break;
                    case '<': case '(': case '[': depth++; break;
                    case '>': case ')': case ']': depth--; break;
                    case ',':
                        if (depth == 0) { result.Add(s.Substring(start, i - start)); start = i + 1; }
                        break;
                }
            }
            result.Add(s.Substring(start));
            return result;
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
