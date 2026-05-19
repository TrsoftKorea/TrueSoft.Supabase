using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine.Networking;

namespace Truesoft.Supabase.Editor
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

        /// <summary>
        /// 레거시 대시보드 JWT 키(예: <c>eyJ…</c>)는 PostgREST에서 <c>Authorization: Bearer</c>에 동일 값을 두는 패턴이 흔합니다.
        /// 새 Publishable/Secret 키(<c>sb_publishable_</c>, <c>sb_secret_</c>)는 JWT가 아니며,
        /// <c>apikey</c>와 같은 값을 Bearer에 넣으면 게이트웨이 뒤에서 거절될 수 있습니다.
        /// (<see href="https://supabase.com/docs/guides/api/api-keys">Supabase API keys</see>)
        /// </summary>
        private static bool IsLegacyJwtStyleApiKey(string key)
        {
            if (string.IsNullOrEmpty(key) || key.Length < 20)
                return false;
            if (!key.StartsWith("eyJ", StringComparison.Ordinal))
                return false;
            return key.IndexOf('.', StringComparison.Ordinal) > 0;
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
            string tableLabel)
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
            sb.AppendLine("using UnityEngine;");
            sb.AppendLine("using Truesoft.Supabase.Core.Data;");
            sb.AppendLine("using Truesoft.Supabase.Unity;");
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
            sb.AppendLine(indent + "/// <summary>유저 세이브 클래스.</summary>");
            sb.AppendLine(indent + "public sealed partial class " + className + " : StaticUserSave<" + className + ".Row>");
            sb.AppendLine(indent + "{");
            sb.AppendLine(indent + "    public static readonly " + className + " Instance = new();");
            sb.AppendLine(indent + "    private " + className + "() : base() { }");
            sb.AppendLine();
            sb.AppendLine(indent + "    [Serializable]");
            sb.AppendLine(indent + "    public sealed class Row");
            sb.AppendLine(indent + "    {");
            foreach (var c in columns)
            {
                var fieldName = LegalFieldName(c.Name);
                if (string.IsNullOrWhiteSpace(c.Comment) == false)
                    sb.AppendLine(indent + "        /// <summary>" + EscapeXml(c.Comment.Trim()) + "</summary>");
                sb.AppendLine(indent + "        [DataColumn(\"" + EscapeCSharpString(c.Name) + "\")] public " + c.ClrType + " " + fieldName + ";");
            }

            sb.AppendLine(indent + "    }");

            foreach (var c in columns)
            {
                var fieldName = LegalFieldName(c.Name);
                var propName = ToPascalCase(c.Name);
                sb.AppendLine();
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
                return "string /* array / composite — refine manually */";

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

        private static string LegalFieldName(string columnName)
        {
            return IsCSharpKeyword(columnName) ? "@" + columnName : columnName;
        }

        private static bool IsCSharpKeyword(string s)
        {
            switch (s)
            {
                case "abstract":
                case "as":
                case "base":
                case "bool":
                case "break":
                case "byte":
                case "case":
                case "catch":
                case "char":
                case "checked":
                case "class":
                case "const":
                case "continue":
                case "decimal":
                case "default":
                case "delegate":
                case "do":
                case "double":
                case "else":
                case "enum":
                case "event":
                case "explicit":
                case "extern":
                case "false":
                case "finally":
                case "fixed":
                case "float":
                case "for":
                case "foreach":
                case "goto":
                case "if":
                case "implicit":
                case "in":
                case "int":
                case "interface":
                case "internal":
                case "is":
                case "lock":
                case "long":
                case "namespace":
                case "new":
                case "null":
                case "object":
                case "operator":
                case "out":
                case "override":
                case "params":
                case "private":
                case "protected":
                case "public":
                case "readonly":
                case "ref":
                case "return":
                case "sbyte":
                case "sealed":
                case "short":
                case "sizeof":
                case "stackalloc":
                case "static":
                case "string":
                case "struct":
                case "switch":
                case "this":
                case "throw":
                case "true":
                case "try":
                case "typeof":
                case "uint":
                case "ulong":
                case "unchecked":
                case "unsafe":
                case "ushort":
                case "using":
                case "virtual":
                case "void":
                case "volatile":
                case "while":
                    return true;
                default:
                    return false;
            }
        }

        private static string ToPascalCase(string name)
        {
            var parts = name.Split('_');
            var sb = new StringBuilder();
            foreach (var part in parts)
            {
                if (part.Length == 0) continue;
                sb.Append(char.ToUpperInvariant(part[0]));
                if (part.Length > 1) sb.Append(part, 1, part.Length - 1);
            }
            return sb.Length > 0 ? sb.ToString() : name;
        }

        private static string EscapeXml(string s)
        {
            if (string.IsNullOrEmpty(s))
                return "";
            return s.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal);
        }

        private static string EscapeCSharpString(string s)
        {
            if (string.IsNullOrEmpty(s))
                return string.Empty;

            return s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
        }
    }

    internal readonly struct OpenApiColumn
    {
        public OpenApiColumn(string name, string clrType, string comment)
        {
            Name = name;
            ClrType = clrType;
            Comment = comment;
        }

        public string Name { get; }
        public string ClrType { get; }
        public string Comment { get; }
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
