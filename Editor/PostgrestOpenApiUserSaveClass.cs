using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEngine.Networking;
using static TrueBase.Editor.GeneratorTypeCatalog;

namespace TrueBase.Editor
{
    /// <summary>PostgREST OpenAPI(JSON)에서 세이브 테이블 컬럼을 읽고 DataColumn이 붙은 C# 클래스 소스를 만듭니다.</summary>
    internal static class PostgrestOpenApiUserSaveClass
    {
        /// <summary>붙여넣기 오류로 섞인 공백·줄바꿈을 정리합니다.</summary>
        public static string NormalizeApiKey(string apiKey)
        {
            if (string.IsNullOrEmpty(apiKey))
                return apiKey;

            var s = apiKey.Trim().TrimStart('\uFEFF');
            s = s.Replace("\r", string.Empty).Replace("\n", string.Empty).Replace("\t", string.Empty);
            if (s.Length > 0 && s.StartsWith("eyJ", StringComparison.Ordinal))
            {
                var sb = new StringBuilder(s.Length);
                foreach (var ch in s)
                {
                    if (!char.IsWhiteSpace(ch))
                        sb.Append(ch);
                }

                s = sb.ToString();
            }

            return s.Trim();
        }

        /// <summary>프로젝트 루트만 남김. <c>…/rest/v1</c> 까지 붙여 넣은 경우 제거.</summary>
        public static string NormalizeProjectUrl(string projectUrl)
        {
            if (string.IsNullOrWhiteSpace(projectUrl))
                return projectUrl?.Trim() ?? string.Empty;

            var u = projectUrl.Trim().TrimStart('\uFEFF');
            const string marker = "/rest/v1";
            var idx = u.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
                u = u.Substring(0, idx);

            return u.TrimEnd('/');
        }

        public static string BuildRestRootUrl(string projectUrl)
        {
            var baseUrl = NormalizeProjectUrl(projectUrl);
            if (string.IsNullOrWhiteSpace(baseUrl))
                throw new ArgumentException("프로젝트 URL이 비어 있습니다.", nameof(projectUrl));
            return baseUrl.TrimEnd('/') + "/rest/v1/";
        }

        private static void SetOpenApiFetchHeaders(UnityWebRequest req, string apiKey)
        {
            req.SetRequestHeader("apikey", apiKey);
            if (IsLegacyJwtStyleApiKey(apiKey))
                req.SetRequestHeader("Authorization", "Bearer " + apiKey);
            req.SetRequestHeader("Accept", "application/openapi+json");
        }

        public static string FetchOpenApiJson(string restRootUrl, string apiKey, int timeoutSeconds)
        {
            var key = NormalizeApiKey(apiKey);
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("API 키가 비어 있습니다.", nameof(apiKey));

            using var req = UnityWebRequest.Get(restRootUrl);
            req.timeout = Math.Max(5, timeoutSeconds);
            SetOpenApiFetchHeaders(req, key);

            var op = req.SendWebRequest();
            while (op.isDone == false)
                System.Threading.Thread.Sleep(16);

            var body = req.downloadHandler?.text ?? string.Empty;

#if UNITY_2020_2_OR_NEWER
            var ok = req.result == UnityWebRequest.Result.Success;
#else
            var ok = req.isNetworkError == false && req.isHttpError == false;
#endif
            if (!ok)
            {
                var snippet = body.Length > 800 ? body.Substring(0, 800) + "…" : body;
                if (string.IsNullOrWhiteSpace(snippet))
                    snippet = "(no response body)";
                throw new IOException($"{req.error} (HTTP {req.responseCode})\n{snippet}");
            }

            return body;
        }

        public static ParseTableResult ParseTableColumns(string openApiJson, string tableName, HashSet<string> skipColumns)
        {
            var warnings = new List<string>();
            var root = JObject.Parse(openApiJson);
            var schemaToken = FindTableSchemaToken(root, tableName);
            if (schemaToken == null)
            {
                return ParseTableResult.Fail($"테이블 '{tableName}' 스키마를 OpenAPI에서 찾지 못했습니다.");
            }

            var schemaObj = ResolveSchema(root, schemaToken as JObject);
            if (schemaObj == null)
            {
                return ParseTableResult.Fail("스키마를 해석할 수 없습니다.");
            }

            var props = schemaObj["properties"] as JObject;
            if (props == null)
                return ParseTableResult.Fail("스키마에 properties가 없습니다.");

            var list = new List<OpenApiColumn>();
            foreach (var p in props.Properties())
            {
                var colName = p.Name;
                if (skipColumns != null && skipColumns.Contains(colName))
                    continue;

                if (IsValidCSharpIdentifierChars(colName) == false)
                {
                    warnings.Add(
                        $"컬럼 '{colName}' 건너뜀: C# 식별자가 아닙니다.");
                    continue;
                }

                var propObj = p.Value as JObject ?? new JObject();
                propObj = ResolveSchema(root, propObj);
                if (propObj == null)
                {
                    warnings.Add($"컬럼 '{colName}' 건너뜀: 속성 스키마를 해석하지 못했습니다.");
                    continue;
                }

                var desc = propObj["description"]?.Value<string>();
                if (string.IsNullOrWhiteSpace(desc))
                    desc = propObj["title"]?.Value<string>();

                list.Add(new OpenApiColumn(colName, MapToClr(propObj), desc));
            }

            return ParseTableResult.Ok(list, warnings);
        }

        public static string GenerateSource(
            IReadOnlyList<OpenApiColumn> columns,
            string className,
            string namespaceName,
            string tableLabel,
            IReadOnlyList<string> extraUsings = null)
        {
            if (columns == null || columns.Count == 0)
                throw new InvalidOperationException("생성할 컬럼이 없습니다.");

            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated>");
            sb.AppendLine("// PostgREST OpenAPI → C# 클래스");
            sb.AppendLine("// Generated (UTC): " + DateTime.UtcNow.ToString("O"));
            sb.AppendLine("// Menu: TrueSoft/Supabase/유저 데이터 클래스 생성");
            sb.AppendLine("// </auto-generated>");
            sb.AppendLine();
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using UnityEngine;");
            sb.AppendLine("using Newtonsoft.Json;");
            sb.AppendLine("using TrueBase.Core.Data;");
            sb.AppendLine("using TrueBase.Unity;");
            if (extraUsings != null)
                foreach (var ns in extraUsings)
                    if (!string.IsNullOrWhiteSpace(ns))
                        sb.AppendLine("using " + ns.Trim() + ";");
            sb.AppendLine();

            var useNs = string.IsNullOrWhiteSpace(namespaceName) == false;
            var indent = useNs ? "    " : "";

            if (useNs)
            {
                sb.AppendLine("namespace " + namespaceName.Trim());
                sb.AppendLine("{");
            }

            AppendStaticUserSaveClass(sb, indent, className.Trim(), tableLabel, columns);

            if (useNs)
                sb.AppendLine("}");

            return sb.ToString();
        }

        private static void AppendStaticUserSaveClass(
            StringBuilder sb,
            string indent,
            string className,
            string tableLabel,
            IReadOnlyList<OpenApiColumn> columns)
        {
            // 단순 값 요소 컬렉션을 자동 확장 타입(AutoList/AutoDict/2D)으로 치환. 튜플·struct·클래스 요소는 그대로.
            columns = MapColumnsToAutoTypes(columns);

            sb.AppendLine(indent + "/// <summary>유저 세이브 클래스.</summary>");
            sb.AppendLine(indent + "public sealed partial class " + className + " : StaticUserSave<" + className + ".Row>");
            sb.AppendLine(indent + "{");
            sb.AppendLine(indent + "    public static readonly " + className + " Instance = new();");
            sb.AppendLine(indent + "    private " + className + "() : base() { }");
            sb.AppendLine();
            sb.AppendLine(indent + "    public static System.Threading.Tasks.Task<bool> TryLoadAsync() => ((StaticUserSave<Row>)Instance).TryLoadAsync();");
            sb.AppendLine();
            sb.AppendLine(indent + "    // 필드는 internal — 반드시 아래 정적 프로퍼티(MarkDirty 포함)로만 접근하세요.");
            sb.AppendLine(indent + "    // [JsonObject(Fields)]로 Newtonsoft가 internal 필드를 직렬화/역직렬화합니다.");
            sb.AppendLine(indent + "    [Serializable]");
            sb.AppendLine(indent + "    [JsonObject(MemberSerialization.Fields)]");
            sb.AppendLine(indent + "    public sealed class Row");
            sb.AppendLine(indent + "    {");
            // 필드는 리플렉션(Newtonsoft 역직렬화) / 정적 프로퍼티로만 접근 → '미사용/미할당' 경고 억제
            sb.AppendLine(indent + "#pragma warning disable CS0169, CS0649");
            foreach (var c in columns)
            {
                var fieldName     = LegalFieldName(MemberSourceOf(c));
                var priorityParam = c.Priority == 1 /* Normal */ ? string.Empty : ", DataSavePriority." + PriorityName(c.Priority);
                if (string.IsNullOrWhiteSpace(c.Comment) == false)
                    sb.AppendLine(indent + "        /// <summary>" + EscapeXml(c.Comment.Trim()) + "</summary>");

                // 기본값: 스칼라면 필드 초기화식(= 리터럴), Auto 컬렉션이면 [AutoDefault(...)]로 분기.
                var autoDefaultAttr = string.Empty;
                string scalarInit = null;
                if (!string.IsNullOrWhiteSpace(c.DefaultValue))
                {
                    if (GeneratorTypeCatalog.IsAutoDefaultableType(c.ClrType, out var elemType))
                        autoDefaultAttr = "[AutoDefault(" + GeneratorTypeCatalog.FormatAutoDefaultArgs(elemType, c.DefaultValue) + ")] ";
                    else if (GeneratorTypeCatalog.IsScalarTypeName(c.ClrType)
                             && GeneratorTypeCatalog.TryFormatScalarLiteral(c.ClrType, c.DefaultValue, out var lit))
                        scalarInit = " = " + lit;
                }

                // 컬렉션(List·배열·Dictionary 등)은 null 방지를 위해 빈 인스턴스로 초기화(읽기만 해도 변경으로 오인되지 않게).
                // 스칼라 기본값이 있으면 그 초기화식을 우선(스칼라는 컬렉션 init 대상이 아님).
                // 필드는 internal — 정적 프로퍼티로 접근. (private는 중첩 Row라 바깥 클래스에서 접근 불가)
                var fieldInit = scalarInit ?? (TryGetCollectionInit(c.ClrType, out var fieldInitExpr) ? " = " + fieldInitExpr : string.Empty);
                // 정리된 필드명이 컬럼명과 다르면 JSON 키를 컬럼명으로 보존(직렬화 자동 보장).
                var jsonProp = MemberNameOf(fieldName) != c.Name
                    ? "[JsonProperty(\"" + EscapeCSharpString(c.Name) + "\")] "
                    : string.Empty;
                sb.AppendLine(indent + "        [DataColumn(\"" + EscapeCSharpString(c.Name) + "\"" + priorityParam + ")] " + autoDefaultAttr + jsonProp + "internal " + c.ClrType + " " + fieldName + fieldInit + ";");
            }

            // updated_at: 타임스탬프 비교(이관 등)에 사용. DB에 없는 테이블을 위해 항상 포함.
            if (!columns.Any(c => c.Name == "updated_at"))
                sb.AppendLine(indent + "        [DataColumn(\"updated_at\")] internal string updated_at;");

            sb.AppendLine(indent + "#pragma warning restore CS0169, CS0649");
            sb.AppendLine(indent + "    }");

            // updated_at은 DB 트리거가 자동 설정 — 개발자가 실수로 set하지 않도록 정적 프로퍼티 제외
            foreach (var c in columns.Where(c => c.Name != "updated_at"))
            {
                var memberSource = MemberSourceOf(c);
                var fieldName = LegalFieldName(memberSource);
                var propName = ToPascalCase(memberSource);
                sb.AppendLine();

                if (TryGetCollectionInit(c.ClrType, out var propInitExpr))
                {
                    // 컬렉션(List·배열·Dictionary 등): 일반 컬렉션처럼 그대로 사용하세요.
                    // Add/[key]=/[i]= 같은 제자리 수정도 자동 동기화가 값 비교로 감지해 저장합니다(MarkDirty 수동 불필요).
                    // 필드가 빈 인스턴스로 초기화돼 있어 null 걱정 없이 바로 쓸 수 있습니다.
                    sb.AppendLine(indent + "    /// <summary>일반 컬렉션처럼 사용하세요. 직접 수정(Add/[key]=/[i]=)해도 자동 저장에 반영됩니다.</summary>");
                    sb.AppendLine(indent + "    public static " + c.ClrType + " " + propName);
                    sb.AppendLine(indent + "    {");
                    sb.AppendLine(indent + "        get => Instance.Current." + fieldName + ";");
                    sb.AppendLine(indent + "        set { Instance.Current." + fieldName + " = value ?? " + propInitExpr + "; Instance.MarkDirty(); }");
                    sb.AppendLine(indent + "    }");
                    continue;
                }

                sb.AppendLine(indent + "    public static " + c.ClrType + " " + propName);
                sb.AppendLine(indent + "    {");
                sb.AppendLine(indent + "        get => Instance.Current." + fieldName + ";");
                sb.AppendLine(indent + "        set { Instance.Current." + fieldName + " = value; Instance.MarkDirty(); }");
                sb.AppendLine(indent + "    }");
            }

            sb.AppendLine(indent + "}");
        }

        private static JToken FindTableSchemaToken(JObject root, string tableName)
        {
            if (string.IsNullOrWhiteSpace(tableName))
                throw new ArgumentException("테이블 이름이 비어 있습니다.", nameof(tableName));

            var shortName = tableName.Contains(".", StringComparison.Ordinal)
                ? tableName.Substring(tableName.LastIndexOf('.') + 1)
                : tableName;

            var variants = new HashSet<string>(StringComparer.Ordinal)
            {
                shortName,
                tableName.Replace(".", "_")
            };

            if (root["definitions"] is JObject defs)
            {
                foreach (var k in variants)
                {
                    if (defs[k] != null)
                        return defs[k];
                }
            }

            if (root["components"]?["schemas"] is JObject schemas)
            {
                foreach (var k in variants)
                {
                    if (schemas[k] != null)
                        return schemas[k];
                }
            }

            return null;
        }

        private static JObject ResolveSchema(JObject root, JObject node)
        {
            if (node == null)
                return null;
            if (node["$ref"] is JValue refVal)
            {
                var resolved = ResolveJsonPointer(root, refVal.Value<string>());
                return resolved ?? node;
            }

            return node;
        }

        private static JObject ResolveJsonPointer(JObject root, string pointer)
        {
            if (string.IsNullOrEmpty(pointer) || pointer[0] != '#')
                return null;

            var parts = pointer.TrimStart('#').TrimStart('/').Split('/');
            JToken cur = root;
            foreach (var part in parts)
            {
                var unescaped = part.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
                cur = cur?[unescaped];
            }

            return cur as JObject;
        }

        private static string MapToClr(JObject prop)
        {
            var typeStr = PrimaryType(prop["type"]);

            var format = prop["format"]?.Value<string>();

            if (typeStr == null && prop["allOf"] != null)
                return "string /* allOf: refine manually */";

            if (prop["$ref"] != null && typeStr == null)
                return "string /* $ref: refine manually */";

            if (typeStr == "array")
            {
                var elem = MapArrayElementClr(prop);
                return elem != null
                    ? "List<" + elem + ">"
                    : "string /* array of objects/composite — refine manually */";
            }

            if (typeStr == "object")
                return "string /* json/jsonb — refine manually */";

            if (typeStr == "boolean")
                return "bool";

            if (typeStr == "integer")
            {
                if (string.Equals(format, "int8", StringComparison.OrdinalIgnoreCase)) return "long";
                if (string.Equals(format, "int64", StringComparison.OrdinalIgnoreCase)) return "long";
                if (string.Equals(format, "uint64", StringComparison.OrdinalIgnoreCase)) return "ulong";
                if (string.Equals(format, "int16", StringComparison.OrdinalIgnoreCase)) return "short";
                return "int";
            }

            if (typeStr == "number")
            {
                if (string.Equals(format, "float", StringComparison.OrdinalIgnoreCase)) return "float";
                return "double";
            }

            if (typeStr == "string")
                return "string";

            return "string /* unknown type — refine manually */";
        }

        /// <summary>
        /// 배열 컬럼(<c>int[]</c>·<c>text[]</c> 등)의 요소 CLR 타입을 구합니다. <c>items</c> 스키마를
        /// <see cref="MapToClr"/>로 재매핑하며, 요소가 미해결(주석 포함: object/$ref 등)이면 null을 반환해
        /// 호출부가 수동 정제 플레이스홀더로 떨어지게 합니다. (스칼라 요소만 자동 <c>List&lt;T&gt;</c>로 매핑)
        /// </summary>
        private static string MapArrayElementClr(JObject prop)
        {
            if (!(prop["items"] is JObject items))
                return null;
            var itemClr = MapToClr(items);
            return itemClr.IndexOf("/*", StringComparison.Ordinal) >= 0 ? null : itemClr;
        }

        private static string PrimaryType(JToken typeToken)
        {
            if (typeToken == null)
                return null;
            if (typeToken.Type == JTokenType.String)
                return typeToken.Value<string>();
            if (typeToken is JArray arr)
            {
                foreach (var x in arr)
                {
                    if (x.Type == JTokenType.String && string.Equals(x.Value<string>(), "null", StringComparison.Ordinal) == false)
                        return x.Value<string>();
                }
            }

            return null;
        }

        private static bool IsValidCSharpIdentifierChars(string s)
        {
            if (string.IsNullOrEmpty(s))
                return false;
            var first = s[0];
            if (char.IsLetter(first) == false && first != '_')
                return false;
            for (var i = 1; i < s.Length; i++)
            {
                var c = s[i];
                if (char.IsLetterOrDigit(c) == false && c != '_')
                    return false;
            }

            return true;
        }

        /// <summary>필드 토큰에서 실제 멤버 이름(JSON 키로 쓰임)을 구합니다 — '@' 접두만 제거.</summary>
        private static string MemberNameOf(string fieldToken) =>
            fieldToken.StartsWith("@", StringComparison.Ordinal) ? fieldToken.Substring(1) : fieldToken;

        /// <summary>필드·프로퍼티 이름의 원천 — 커스텀 FieldName이 있으면 그것을, 없으면 컬럼명을 사용합니다.</summary>
        private static string MemberSourceOf(OpenApiColumn c) =>
            string.IsNullOrWhiteSpace(c.FieldName) ? c.Name : c.FieldName.Trim();

        private static string PriorityName(int priority) => priority switch
        {
            0 => "Urgent",
            2 => "Lazy",
            _ => "Normal"
        };

        // new 가능한(파라미터 없는 생성자) 컬렉션 타입 prefix 목록.
        private static readonly string[] s_newableCollectionPrefixes =
        {
            "List<", "Dictionary<", "HashSet<", "SortedList<", "SortedDictionary<",
            "SortedSet<", "Queue<", "Stack<", "LinkedList<", "ObservableCollection<",
            "AutoList<", "AutoList2D<", "AutoDict<", "AutoDict2D<"
        };

        /// <summary>
        /// ClrType이 컬렉션(List·Dictionary·HashSet·배열 등)이면 true와 null 방지용 초기화식을 반환합니다.
        /// 예: <c>List&lt;int&gt;</c>→<c>new List&lt;int&gt;()</c>, <c>int[]</c>→<c>System.Array.Empty&lt;int&gt;()</c>.
        /// 컬렉션 컬럼은 필드를 빈 인스턴스로 초기화하고 일반 mutable 프로퍼티로 노출하기 위해 분기합니다.
        /// </summary>
        private static bool TryGetCollectionInit(string clrType, out string initExpr)
        {
            initExpr = null;
            if (string.IsNullOrWhiteSpace(clrType))
                return false;

            var t = clrType.Trim();
            // 주석(예: "string /* json */")이 붙은 경우 타입 부분만 사용
            var commentIdx = t.IndexOf("/*", StringComparison.Ordinal);
            if (commentIdx >= 0)
                t = t.Substring(0, commentIdx).Trim();

            // 배열 T[] → new T[]() 는 불가하므로 System.Array.Empty<T>() 사용
            if (t.EndsWith("[]", StringComparison.Ordinal))
            {
                var elem = t.Substring(0, t.Length - 2).Trim();
                if (elem.Length == 0) return false;
                initExpr = "System.Array.Empty<" + elem + ">()";
                return true;
            }

            // new 가능한 제네릭 컬렉션 → new TYPE()
            foreach (var prefix in s_newableCollectionPrefixes)
            {
                if (t.StartsWith(prefix, StringComparison.Ordinal) && t.EndsWith(">", StringComparison.Ordinal))
                {
                    initExpr = "new " + t + "()";
                    return true;
                }
            }

            return false;
        }

        private static IReadOnlyList<OpenApiColumn> MapColumnsToAutoTypes(IReadOnlyList<OpenApiColumn> columns)
        {
            var list = new List<OpenApiColumn>(columns.Count);
            foreach (var c in columns)
            {
                var clr = GeneratorTypeCatalog.MapToAutoType(c.ClrType);
                clr = FallbackUnrefinedJsonType(clr);
                list.Add(new OpenApiColumn(c.Name, clr, c.Comment, c.Priority, c.FieldName, c.DefaultValue));
            }
            return list;
        }

        /// <summary>
        /// 사용자가 정제하지 않은 jsonb(객체) 플레이스홀더(<c>string /* json/jsonb … */</c>)를
        /// 안전한 <c>Dictionary&lt;string, object&gt;</c>로 떨굽니다. (string 필드로 굳으면 object→string 역직렬화가 깨짐)
        /// $ref·allOf·unknown 등 구조를 알 수 없는 플레이스홀더는 그대로 둬 수동 정제를 유도합니다.
        /// </summary>
        private static string FallbackUnrefinedJsonType(string clrType)
        {
            if (string.IsNullOrWhiteSpace(clrType)) return clrType;
            if (clrType.IndexOf("/*", StringComparison.Ordinal) >= 0 &&
                clrType.IndexOf("json", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Dictionary<string, object>";
            return clrType;
        }

        /// <summary>
        /// 재생성된 소스(<paramref name="newSource"/>)에 기존 파일(<paramref name="existingSource"/>)의
        /// 필드별 <c>[AutoDefault(...)]</c>를 컬럼명 기준으로 병합해 보존합니다.
        /// (생성기는 커스텀 기본값을 모르므로, 수동으로 붙인 기본값이 재생성에 사라지지 않게 합니다.)
        /// 같은 컬럼 필드에 이미 AutoDefault가 있으면 건너뜁니다.
        /// </summary>
        public static string MergePreservedAutoDefaults(string newSource, string existingSource)
        {
            if (string.IsNullOrEmpty(newSource) || string.IsNullOrEmpty(existingSource))
                return newSource;

            var preserved = ExtractAutoDefaultsByColumn(existingSource);
            if (preserved.Count == 0)
                return newSource;

            var lines = newSource.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.IndexOf("[DataColumn(", StringComparison.Ordinal) < 0)
                    continue;
                if (line.IndexOf("[AutoDefault(", StringComparison.Ordinal) >= 0)
                    continue;

                var col = ExtractDataColumnName(line);
                if (col == null || !preserved.TryGetValue(col, out var attr))
                    continue;

                var dcIdx = line.IndexOf("[DataColumn(", StringComparison.Ordinal);
                var closeIdx = line.IndexOf(']', dcIdx);
                if (closeIdx < 0)
                    continue;

                // [DataColumn(...)] 뒤에 보존 [AutoDefault(...)] 삽입 (줄 끝 '\r' 등은 그대로 유지)
                lines[i] = line.Substring(0, closeIdx + 1) + " " + attr + line.Substring(closeIdx + 1);
            }

            return string.Join("\n", lines);
        }

        private static Dictionary<string, string> ExtractAutoDefaultsByColumn(string source)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var line in source.Split('\n'))
            {
                if (line.IndexOf("[AutoDefault(", StringComparison.Ordinal) < 0)
                    continue;
                if (line.IndexOf("[DataColumn(", StringComparison.Ordinal) < 0)
                    continue;

                var col = ExtractDataColumnName(line);
                if (col == null)
                    continue;
                var attr = ExtractBracketedAttribute(line, "[AutoDefault(");
                if (!string.IsNullOrEmpty(attr))
                    map[col] = attr;
            }

            return map;
        }

        /// <summary>
        /// 생성된 소스에서 컬럼별 "기본값 원본 입력"을 복원합니다(재생성 시 UI 복원용).
        /// <c>[AutoDefault(args)]</c>→ args 그대로. 스칼라 <c>= expr</c>→ 따옴표·Parse 해제값. 컬렉션 init(<c>new …</c>)은 무시.
        /// </summary>
        public static Dictionary<string, string> ExtractDefaultsByColumn(string source)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(source)) return map;

            foreach (var line in source.Split('\n'))
            {
                if (line.IndexOf("[DataColumn(", StringComparison.Ordinal) < 0) continue;
                var col = ExtractDataColumnName(line);
                if (col == null) continue;

                // 1) [AutoDefault(args)] → args 그대로
                var adIdx = line.IndexOf("[AutoDefault(", StringComparison.Ordinal);
                if (adIdx >= 0)
                {
                    var open  = adIdx + "[AutoDefault(".Length;
                    var close = line.IndexOf(")]", open, StringComparison.Ordinal);
                    if (close > open)
                    {
                        map[col] = line.Substring(open, close - open).Trim();
                        continue;
                    }
                }

                // 2) 스칼라 초기화식
                var raw = ExtractScalarInitRaw(line);
                if (raw != null) map[col] = raw;
            }

            return map;
        }

        /// <summary>필드 줄에서 스칼라 초기화식(<c>= expr;</c>)의 원본 입력을 복원합니다. 컬렉션 init(<c>new …</c>/Array.Empty)이면 null.</summary>
        private static string ExtractScalarInitRaw(string line)
        {
            var semi = line.LastIndexOf(';');
            if (semi < 0) return null;
            var internalIdx = line.IndexOf("internal ", StringComparison.Ordinal);
            if (internalIdx < 0) return null;
            var eq = line.IndexOf('=', internalIdx);
            if (eq < 0 || eq > semi) return null;

            var expr = line.Substring(eq + 1, semi - eq - 1).Trim();
            if (expr.Length == 0) return null;
            if (expr.StartsWith("new ", StringComparison.Ordinal) || expr.IndexOf("Array.Empty", StringComparison.Ordinal) >= 0)
                return null; // 컬렉션 인스턴스 init — 기본값 아님

            // TYPE.Parse("X") → X
            var parse = Regex.Match(expr, @"^\w[\w.]*\.Parse\(""(.*)""\)$");
            if (parse.Success) return parse.Groups[1].Value;

            // "문자열" → 따옴표 제거
            if (expr.Length >= 2 && expr[0] == '"' && expr[expr.Length - 1] == '"')
                return expr.Substring(1, expr.Length - 2);

            return expr; // 숫자/bool 등 — 멱등 포매터가 재변환
        }

        /// <summary>필드 줄에서 <c>[DataColumn("name"…)]</c>의 컬럼명(첫 문자열 인자)을 추출합니다.</summary>
        private static string ExtractDataColumnName(string line)
        {
            var i = line.IndexOf("[DataColumn(", StringComparison.Ordinal);
            if (i < 0) return null;
            var q1 = line.IndexOf('"', i);
            if (q1 < 0) return null;
            var q2 = line.IndexOf('"', q1 + 1);
            if (q2 < 0) return null;
            return line.Substring(q1 + 1, q2 - q1 - 1);
        }

        /// <summary><paramref name="startToken"/>(예: <c>[AutoDefault(</c>)부터 첫 <c>]</c>까지의 어트리뷰트 문자열을 반환합니다.</summary>
        private static string ExtractBracketedAttribute(string line, string startToken)
        {
            var i = line.IndexOf(startToken, StringComparison.Ordinal);
            if (i < 0) return null;
            var end = line.IndexOf(']', i);
            if (end < 0) return null;
            return line.Substring(i, end - i + 1);
        }

    }

    internal readonly struct OpenApiColumn
    {
        /// <param name="priority">저장 우선순위. 0=Urgent, 1=Normal(기본), 2=Lazy.</param>
        /// <param name="fieldName">C# 필드명 커스터마이즈. null/빈 값이면 컬럼명(<paramref name="name"/>)을 사용.</param>
        /// <param name="defaultValue">기본값(원본 입력). 스칼라면 필드 초기화식으로, Auto 컬렉션이면 <c>[AutoDefault]</c>로 변환. null/빈 값이면 미지정.</param>
        public OpenApiColumn(string name, string clrType, string comment, int priority = 1, string fieldName = null, string defaultValue = null)
        {
            Name         = name;
            ClrType      = clrType;
            Comment      = comment;
            Priority     = priority;
            FieldName    = fieldName;
            DefaultValue = defaultValue;
        }

        public string Name    { get; }
        public string ClrType { get; }
        public string Comment { get; }
        /// <summary>저장 우선순위. 0=Urgent, 1=Normal, 2=Lazy.</summary>
        public int    Priority { get; }
        /// <summary>C# 필드명(커스텀). null/빈 값이면 컬럼명을 사용. DataColumn 매핑은 항상 컬럼명을 씁니다.</summary>
        public string FieldName { get; }
        /// <summary>기본값(원본 입력). 스칼라→필드 초기화식, Auto 컬렉션→<c>[AutoDefault]</c>. null/빈 값이면 미지정.</summary>
        public string DefaultValue { get; }
    }

    internal sealed class ParseTableResult
    {
        private ParseTableResult(IReadOnlyList<OpenApiColumn> columns, IReadOnlyList<string> warnings, string errorMessage)
        {
            Columns = columns;
            Warnings = warnings;
            ErrorMessage = errorMessage;
        }

        public IReadOnlyList<OpenApiColumn> Columns { get; }
        public IReadOnlyList<string> Warnings { get; }
        public string ErrorMessage { get; }
        public bool IsSuccess => string.IsNullOrEmpty(ErrorMessage);

        public static ParseTableResult Fail(string message) =>
            new ParseTableResult(Array.Empty<OpenApiColumn>(), Array.Empty<string>(), message);

        public static ParseTableResult Ok(List<OpenApiColumn> columns, List<string> warnings) =>
            new ParseTableResult(columns, warnings, null);
    }
}
