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

            list.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
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
            sb.AppendLine("// Table: " + tableLabel);
            sb.AppendLine("// Generated (UTC): " + DateTime.UtcNow.ToString("O"));
            sb.AppendLine("// Menu: TrueSoft/Supabase/유저 데이터 클래스 생성");
            sb.AppendLine("// </auto-generated>");
            sb.AppendLine();
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Threading.Tasks;");
            sb.AppendLine("using UnityEngine;");
            sb.AppendLine("using Truesoft.Supabase.Core.Data;");
            sb.AppendLine();

            var useNs = string.IsNullOrWhiteSpace(namespaceName) == false;
            var indent = useNs ? "    " : "";

            if (useNs)
            {
                sb.AppendLine("namespace " + namespaceName.Trim());
                sb.AppendLine("{");
            }

            AppendStaticApiClass(sb, indent, className.Trim(), namespaceName, tableLabel, columns);

            if (useNs)
                sb.AppendLine("}");

            return sb.ToString();
        }

        private static void AppendStaticApiClass(
            StringBuilder sb,
            string indent,
            string className,
            string namespaceName,
            string tableLabel,
            IReadOnlyList<OpenApiColumn> columns)
        {
            var rowTypeName = className + "Row";
            var fullKey = string.IsNullOrWhiteSpace(namespaceName)
                ? className
                : namespaceName.Trim() + "." + className;

            sb.AppendLine(indent + "/// <summary>");
            sb.AppendLine(indent + "/// <c>" + EscapeXml(tableLabel) + "</c> 정적 접근 모델.");
            sb.AppendLine(indent + "/// </summary>");
            sb.AppendLine(indent + "public static class " + className);
            sb.AppendLine(indent + "{");
            sb.AppendLine(indent + "    private const string SyncKey = \"" + EscapeCSharpString(fullKey) + "\";");
            sb.AppendLine(indent + "    private static readonly " + rowTypeName + " Current = new " + rowTypeName + "();");
            sb.AppendLine(indent + "    private static " + rowTypeName + " LastSynced = new " + rowTypeName + "();");
            sb.AppendLine(indent + "    private static bool IsDirty;");
            sb.AppendLine(indent + "    private static bool IsRegistered;");
            sb.AppendLine();
            sb.AppendLine(indent + "    static " + className + "()");
            sb.AppendLine(indent + "    {");
            sb.AppendLine(indent + "        EnsureRegistered();");
            sb.AppendLine(indent + "    }");
            sb.AppendLine();
            sb.AppendLine(indent + "    private static void EnsureRegistered()");
            sb.AppendLine(indent + "    {");
            sb.AppendLine(indent + "        if (IsRegistered)");
            sb.AppendLine(indent + "            return;");
            sb.AppendLine();
            sb.AppendLine(indent + "        global::Truesoft.Supabase.Unity.Supabase.RegisterUserSaveStaticSync(SyncKey, HasDirty, FlushDirtyAsync, ResetLocalState);");
            sb.AppendLine(indent + "        IsRegistered = true;");
            sb.AppendLine(indent + "    }");
            sb.AppendLine();
            sb.AppendLine(indent + "    public static void ConfigureCooldown(float seconds)");
            sb.AppendLine(indent + "    {");
            sb.AppendLine(indent + "        global::Truesoft.Supabase.Unity.Supabase.ConfigureUserSaveAutoSyncCooldown(seconds);");
            sb.AppendLine(indent + "    }");
            sb.AppendLine();
            sb.AppendLine(indent + "    public static bool TryRequestImmediateSave()");
            sb.AppendLine(indent + "    {");
            sb.AppendLine(indent + "        EnsureRegistered();");
            sb.AppendLine(indent + "        return global::Truesoft.Supabase.Unity.Supabase.RequestImmediateUserSaveStaticFlush(SyncKey);");
            sb.AppendLine(indent + "    }");
            sb.AppendLine();
            sb.AppendLine(indent + "    public static Task<bool> TryFlushNowAsync(int timeoutMs = 5000)");
            sb.AppendLine(indent + "    {");
            sb.AppendLine(indent + "        EnsureRegistered();");
            sb.AppendLine(indent + "        return global::Truesoft.Supabase.Unity.Supabase.TryFlushUserSaveImmediateAsync(SyncKey, timeoutMs);");
            sb.AppendLine(indent + "    }");
            sb.AppendLine();
            sb.AppendLine(indent + "    /// <summary>서버에서 로드합니다. 본인 행이 없으면(<c>HasRow == false</c>) <c>Row</c>는 타입 기본값이며, 게임 초기값은 로드 후 프로퍼티를 설정하세요.</summary>");
            sb.AppendLine(indent + "    public static async Task<bool> TryLoadAsync(bool includeUpdatedAt = true)");
            sb.AppendLine(indent + "    {");
            sb.AppendLine(indent + "        EnsureRegistered();");
            sb.AppendLine(indent + "        var (success, _, row) = await global::Truesoft.Supabase.Unity.Supabase.TryLoadUserSaveAttributedWithRowStateAsync<" + rowTypeName + ">(defaultWhenFailed: null, includeUpdatedAt: includeUpdatedAt);");
            sb.AppendLine(indent + "        if (!success)");
            sb.AppendLine(indent + "            return false;");
            sb.AppendLine();
            sb.AppendLine(indent + "        CopyInto(Current, row);");
            sb.AppendLine(indent + "        LastSynced = CloneRow(row);");
            sb.AppendLine(indent + "        IsDirty = false;");
            sb.AppendLine(indent + "        return true;");
            sb.AppendLine(indent + "    }");
            sb.AppendLine();
            sb.AppendLine(indent + "    /// <summary>마지막 서버 스냅샷 대비 변경된 컬럼만 PATCH합니다. 변경이 없으면 요청을 보내지 않습니다.</summary>");
            sb.AppendLine(indent + "    public static async Task<bool> TrySaveIfChangedAsync()");
            sb.AppendLine(indent + "    {");
            sb.AppendLine(indent + "        EnsureRegistered();");
            sb.AppendLine(indent + "        var ok = await global::Truesoft.Supabase.Unity.Supabase.TryPatchUserSaveDiffAsync(LastSynced, Current, ensureRowFirst: true, setUpdatedAtIsoUtc: true);");
            sb.AppendLine(indent + "        if (!ok)");
            sb.AppendLine(indent + "            return false;");
            sb.AppendLine(indent + "        LastSynced = CloneRow(Current);");
            sb.AppendLine(indent + "        IsDirty = false;");
            sb.AppendLine(indent + "        return true;");
            sb.AppendLine(indent + "    }");
            sb.AppendLine();

            var usedPropertyNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var c in columns)
            {
                var fieldName = LegalFieldName(c.Name);
                var propertyName = StaticPropertyName(c.Name, usedPropertyNames);
                if (string.IsNullOrWhiteSpace(c.Comment) == false)
                    sb.AppendLine(indent + "    /// <summary>" + EscapeXml(c.Comment.Trim()) + "</summary>");
                sb.AppendLine(indent + "    public static " + c.ClrType + " " + propertyName);
                sb.AppendLine(indent + "    {");
                sb.AppendLine(indent + "        get => Current." + fieldName + ";");
                sb.AppendLine(indent + "        set");
                sb.AppendLine(indent + "        {");
                sb.AppendLine(indent + "            if (Equals(Current." + fieldName + ", value))");
                sb.AppendLine(indent + "                return;");
                sb.AppendLine(indent + "            Current." + fieldName + " = value;");
                sb.AppendLine(indent + "            MarkDirty();");
                sb.AppendLine(indent + "        }");
                sb.AppendLine(indent + "    }");
                sb.AppendLine();
            }

            sb.AppendLine(indent + "    private static void MarkDirty()");
            sb.AppendLine(indent + "    {");
            sb.AppendLine(indent + "        EnsureRegistered();");
            sb.AppendLine(indent + "        IsDirty = true;");
            sb.AppendLine(indent + "        global::Truesoft.Supabase.Unity.Supabase.MarkUserSaveStaticDirty(SyncKey);");
            sb.AppendLine(indent + "    }");
            sb.AppendLine();
            sb.AppendLine(indent + "    private static bool HasDirty() => IsDirty;");
            sb.AppendLine();
            sb.AppendLine(indent + "    private static async Task<bool> FlushDirtyAsync()");
            sb.AppendLine(indent + "    {");
            sb.AppendLine(indent + "        if (!IsDirty)");
            sb.AppendLine(indent + "            return true;");
            sb.AppendLine();
            sb.AppendLine(indent + "        var ok = await global::Truesoft.Supabase.Unity.Supabase.TryPatchUserSaveDiffAsync(LastSynced, Current, ensureRowFirst: true, setUpdatedAtIsoUtc: true);");
            sb.AppendLine(indent + "        if (!ok)");
            sb.AppendLine(indent + "            return false;");
            sb.AppendLine();
            sb.AppendLine(indent + "        LastSynced = CloneRow(Current);");
            sb.AppendLine(indent + "        IsDirty = false;");
            sb.AppendLine(indent + "        return true;");
            sb.AppendLine(indent + "    }");
            sb.AppendLine();
            sb.AppendLine(indent + "    private static void ResetLocalState()");
            sb.AppendLine(indent + "    {");
            sb.AppendLine(indent + "        CopyInto(Current, new " + rowTypeName + "());");
            sb.AppendLine(indent + "        LastSynced = new " + rowTypeName + "();");
            sb.AppendLine(indent + "        IsDirty = false;");
            sb.AppendLine(indent + "    }");
            sb.AppendLine();
            sb.AppendLine(indent + "    private static " + rowTypeName + " CloneRow(" + rowTypeName + " src)");
            sb.AppendLine(indent + "    {");
            sb.AppendLine(indent + "        if (src == null)");
            sb.AppendLine(indent + "            return new " + rowTypeName + "();");
            sb.AppendLine();
            sb.AppendLine(indent + "        var copy = new " + rowTypeName + "();");
            foreach (var c in columns)
            {
                var fieldName = LegalFieldName(c.Name);
                sb.AppendLine(indent + "        copy." + fieldName + " = src." + fieldName + ";");
            }

            sb.AppendLine(indent + "        return copy;");
            sb.AppendLine(indent + "    }");
            sb.AppendLine();
            sb.AppendLine(indent + "    private static void CopyInto(" + rowTypeName + " dst, " + rowTypeName + " src)");
            sb.AppendLine(indent + "    {");
            sb.AppendLine(indent + "        if (dst == null || src == null)");
            sb.AppendLine(indent + "            return;");
            foreach (var c in columns)
            {
                var fieldName = LegalFieldName(c.Name);
                sb.AppendLine(indent + "        dst." + fieldName + " = src." + fieldName + ";");
            }

            sb.AppendLine(indent + "    }");
            sb.AppendLine();
            sb.AppendLine(indent + "    [Serializable]");
            sb.AppendLine(indent + "    private sealed class " + rowTypeName);
            sb.AppendLine(indent + "    {");
            foreach (var c in columns)
            {
                var fieldName = LegalFieldName(c.Name);
                sb.AppendLine(indent + "        [DataColumn(\"" + EscapeCSharpString(c.Name) + "\")] public " + c.ClrType + " " + fieldName + ";");
            }

            sb.AppendLine(indent + "    }");
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

        private static string StaticPropertyName(string columnName, HashSet<string> usedNames)
        {
            if (usedNames == null)
                usedNames = new HashSet<string>(StringComparer.Ordinal);

            var tokens = columnName.Split(new[] { '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var sb = new StringBuilder();
            if (tokens.Length == 0)
                tokens = new[] { columnName };

            foreach (var token in tokens)
            {
                if (string.IsNullOrEmpty(token))
                    continue;

                var start = -1;
                for (var i = 0; i < token.Length; i++)
                {
                    if (char.IsLetterOrDigit(token[i]) || token[i] == '_')
                    {
                        start = i;
                        break;
                    }
                }

                if (start < 0)
                    continue;

                var first = token[start];
                sb.Append(char.ToUpperInvariant(first));
                for (var i = start + 1; i < token.Length; i++)
                {
                    var ch = token[i];
                    if (char.IsLetterOrDigit(ch) || ch == '_')
                        sb.Append(ch);
                }
            }

            var name = sb.ToString();
            if (string.IsNullOrWhiteSpace(name))
                name = "Value";
            if (char.IsDigit(name[0]))
                name = "_" + name;
            if (IsCSharpKeyword(name))
                name = "@" + name;

            var unique = name;
            var suffix = 2;
            while (usedNames.Contains(unique))
            {
                unique = name + suffix.ToString();
                suffix++;
            }

            usedNames.Add(unique);
            return unique;
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
