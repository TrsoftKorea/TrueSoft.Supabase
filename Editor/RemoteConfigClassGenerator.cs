using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEngine.Networking;

namespace Truesoft.Supabase.Editor
{
    /// <summary>remote_config 테이블에서 키·JSON을 읽어 DTO + 접근자 클래스 소스를 생성합니다.</summary>
    internal static class RemoteConfigClassGenerator
    {
        // ── DB fetch ─────────────────────────────────────────────────────────

        /// <summary>
        /// remote_config 테이블 전체 행을 가져옵니다.
        /// GET /rest/v1/remote_config?select=key,value_json,description
        /// </summary>
        public static List<RcKeyRow> FetchConfigRows(string projectUrl, string secretKey, int timeoutSeconds)
        {
            var key = PostgrestOpenApiUserSaveClass.NormalizeApiKey(secretKey);
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Secret 키가 비어 있습니다.", nameof(secretKey));

            var baseUrl = PostgrestOpenApiUserSaveClass.NormalizeProjectUrl(projectUrl);
            if (string.IsNullOrWhiteSpace(baseUrl))
                throw new ArgumentException("프로젝트 URL이 비어 있습니다.", nameof(projectUrl));

            var url = baseUrl.TrimEnd('/') + "/rest/v1/remote_config?select=key,value_json,description&order=key.asc";

            using var req = UnityWebRequest.Get(url);
            req.timeout = Math.Max(5, timeoutSeconds);
            req.SetRequestHeader("apikey", key);
            if (IsLegacyJwt(key))
                req.SetRequestHeader("Authorization", "Bearer " + key);

            var op = req.SendWebRequest();
            while (!op.isDone)
                System.Threading.Thread.Sleep(16);

            var body = req.downloadHandler?.text ?? string.Empty;

#if UNITY_2020_2_OR_NEWER
            var ok = req.result == UnityWebRequest.Result.Success;
#else
            var ok = !req.isNetworkError && !req.isHttpError;
#endif
            if (!ok)
            {
                var snippet = body.Length > 600 ? body.Substring(0, 600) + "…" : body;
                throw new IOException($"{req.error} (HTTP {req.responseCode})\n{snippet}");
            }

            var arr = JArray.Parse(body);
            var result = new List<RcKeyRow>(arr.Count);
            foreach (var item in arr)
            {
                // value_json は text なら文字列、jsonb なら JObject で返る場合があるので正規化
                var vjToken = item["value_json"];
                string vj;
                if (vjToken == null || vjToken.Type == JTokenType.Null)
                    vj = "{}";
                else if (vjToken.Type == JTokenType.String)
                    vj = vjToken.Value<string>() ?? "{}";
                else
                    vj = vjToken.ToString(Newtonsoft.Json.Formatting.None);

                result.Add(new RcKeyRow
                {
                    Key         = item["key"]?.Value<string>() ?? "",
                    ValueJson   = vj,
                    Description = item["description"]?.Value<string>() ?? ""
                });
            }
            return result;
        }

        // ── JSON parsing ─────────────────────────────────────────────────────

        /// <summary>
        /// value_json 문자열 → 평탄화된 RcEditableField 목록.
        /// 중첩 객체는 IsObjectNode 항목(Depth N) + 자식 필드(Depth N+1)로 삽입됩니다.
        /// </summary>
        public static List<RcEditableField> ParseJsonToFields(string valueJson)
        {
            var result = new List<RcEditableField>();
            if (string.IsNullOrWhiteSpace(valueJson))
                return result;

            JObject root;
            try { root = JObject.Parse(valueJson); }
            catch { return result; }

            AppendFields(result, root, depth: 0, pathPrefix: "");
            return result;
        }

        private static void AppendFields(List<RcEditableField> list, JObject obj, int depth, string pathPrefix)
        {
            foreach (var prop in obj.Properties())
            {
                var jsonKey  = prop.Name;
                var fullPath = pathPrefix.Length > 0 ? pathPrefix + "." + jsonKey : jsonKey;

                switch (prop.Value.Type)
                {
                    case JTokenType.Object:
                    {
                        var nestedClassName = ToPascalCase(jsonKey) + "Config";
                        list.Add(new RcEditableField
                        {
                            JsonKey         = jsonKey,
                            FullPath        = fullPath,
                            Depth           = depth,
                            IsObjectNode    = true,
                            NestedClassName = nestedClassName,
                            Include         = true
                        });
                        AppendFields(list, (JObject)prop.Value, depth + 1, fullPath);
                        break;
                    }

                    case JTokenType.Array:
                    {
                        var (clrType, ambiguous) = MapArrayType((JArray)prop.Value);
                        list.Add(new RcEditableField
                        {
                            JsonKey      = jsonKey,
                            FullPath     = fullPath,
                            Depth        = depth,
                            IsObjectNode = false,
                            IsAmbiguous  = ambiguous,
                            TypeIndex    = CustomTypeIndex, // List 계열은 커스텀
                            CustomType   = clrType,
                            JsonCategory = FieldTypeCategory.Array,
                            Include      = true
                        });
                        break;
                    }

                    default:
                    {
                        var (typeIndex, ambiguous) = MapPrimitiveType(prop.Value.Type);
                        list.Add(new RcEditableField
                        {
                            JsonKey      = jsonKey,
                            FullPath     = fullPath,
                            Depth        = depth,
                            IsObjectNode = false,
                            IsAmbiguous  = ambiguous,
                            TypeIndex    = typeIndex,
                            JsonCategory = MapCategory(prop.Value.Type),
                            Include      = true
                        });
                        break;
                    }
                }
            }
        }

        private static (int typeIndex, bool ambiguous) MapPrimitiveType(JTokenType t)
        {
            switch (t)
            {
                case JTokenType.Boolean: return (IndexOf("bool"),   false);
                case JTokenType.Integer: return (IndexOf("int"),    true);   // ⚠ int/long
                case JTokenType.Float:   return (IndexOf("float"),  true);   // ⚠ float/double
                case JTokenType.String:  return (IndexOf("string"), false);
                default:                 return (IndexOf("string"), true);   // null 등
            }
        }

        private static (string clrType, bool ambiguous) MapArrayType(JArray arr)
        {
            if (arr.Count == 0)
                return ("List<string>", true);

            var first = arr[0];
            switch (first.Type)
            {
                case JTokenType.Boolean: return ("List<bool>",   false);
                case JTokenType.Integer: return ("List<int>",    true);
                case JTokenType.Float:   return ("List<float>",  true);
                case JTokenType.String:  return ("List<string>", false);
                case JTokenType.Object:  return ("string /* array of objects — refine manually */", true);
                default:                 return ("List<string>", true);
            }
        }

        // ── Source generation ────────────────────────────────────────────────

        /// <summary>DTO + 접근자 클래스 C# 소스를 생성합니다.</summary>
        /// <param name="description">DB remote_config.description 값. 클래스 주석에 포함됩니다.</param>
        public static string GenerateSource(
            IReadOnlyList<RcEditableField> fields,
            string configClassName,
            string accessorClassName,
            string keyName,
            string namespaceName,
            IReadOnlyList<string> extraUsings = null,
            string description = null)
        {
            if (fields == null || fields.Count == 0)
                throw new InvalidOperationException("생성할 필드가 없습니다.");

            var sb    = new StringBuilder();
            var useNs = !string.IsNullOrWhiteSpace(namespaceName);
            var ind   = useNs ? "    " : "";

            // 헤더
            sb.AppendLine("// <auto-generated>");
            sb.AppendLine("// Remote Config key: " + keyName);
            sb.AppendLine("// Generated (UTC): " + DateTime.UtcNow.ToString("O"));
            sb.AppendLine("// </auto-generated>");
            sb.AppendLine();
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using Newtonsoft.Json;");
            sb.AppendLine("using Truesoft.Supabase.Core.Data;");
            sb.AppendLine("using Truesoft.Supabase.Unity;");
            sb.AppendLine("using Truesoft.Supabase.Unity.RemoteConfig;");
            if (extraUsings != null)
                foreach (var ns in extraUsings)
                    if (!string.IsNullOrWhiteSpace(ns))
                        sb.AppendLine("using " + ns.Trim() + ";");
            sb.AppendLine();

            if (useNs)
            {
                sb.AppendLine("namespace " + namespaceName.Trim());
                sb.AppendLine("{");
            }

            // DTO 클래스
            AppendConfigClass(sb, ind, configClassName, fields, depth: 0, keyName: keyName);
            sb.AppendLine();

            // 접근자 클래스
            AppendAccessorClass(sb, ind, accessorClassName, configClassName, keyName, description);

            if (useNs)
                sb.AppendLine("}");

            return sb.ToString();
        }

        private static void AppendConfigClass(
            StringBuilder sb,
            string ind,
            string className,
            IReadOnlyList<RcEditableField> allFields,
            int depth,
            string keyName = null)
        {
            // 이 depth에 속하는 필드만 (자식 depth의 필드는 재귀 호출에서 처리)
            if (depth == 0 && !string.IsNullOrWhiteSpace(keyName))
            {
                sb.AppendLine(ind + "/// <summary>");
                sb.AppendLine(ind + "/// Remote Config 키 <c>\"" + EscapeCs(keyName) + "\"</c> 의 JSON 구조에 대응하는 데이터 클래스입니다.<br/>");
                sb.AppendLine(ind + "/// DB 값이 갱신되면 폴링 주기에 따라 자동 반영됩니다.");
                sb.AppendLine(ind + "/// <para><b>이 파일은 자동 생성됩니다. 직접 수정하지 마세요.</b></para>");
                sb.AppendLine(ind + "/// </summary>");
            }
            else if (depth > 0)
            {
                sb.AppendLine(ind + "/// <summary>중첩 설정 구조체입니다.</summary>");
            }

            sb.AppendLine(ind + "[Serializable]");
            sb.AppendLine(ind + "public sealed partial class " + className);
            sb.AppendLine(ind + "{");

            var i = 0;
            while (i < allFields.Count)
            {
                var f = allFields[i];
                if (f.Depth != depth) { i++; continue; }
                if (!f.Include)       { i++; SkipSubtree(allFields, ref i, depth); continue; }

                if (f.IsObjectNode)
                {
                    // 필드 선언 (자식 클래스 타입)
                    sb.AppendLine(ind + "    [JsonProperty(\"" + EscapeCs(f.JsonKey) + "\")] public " + f.NestedClassName + " " + LegalField(f.JsonKey) + ";");
                    i++;

                    // 자식 필드 범위를 수집해 재귀 생성
                    var childStart = i;
                    SkipSubtree(allFields, ref i, depth);
                    var childEnd = i;

                    sb.AppendLine();
                    AppendConfigClass(sb, ind + "    ", f.NestedClassName,
                        new SubList(allFields, childStart, childEnd), depth + 1);
                }
                else
                {
                    var clr = ResolveClrType(f);
                    sb.AppendLine(ind + "    [JsonProperty(\"" + EscapeCs(f.JsonKey) + "\")] public " + clr + " " + LegalField(f.JsonKey) + ";");
                    i++;
                }
            }

            sb.AppendLine(ind + "}");
        }

        private static void AppendAccessorClass(
            StringBuilder sb, string ind,
            string accessorClass, string configClass, string keyName, string description = null)
        {
            var descLine = string.IsNullOrWhiteSpace(description)
                ? ""
                : " — " + description.Trim();

            // ─ 클래스 XML doc ────────────────────────────────────────────────
            sb.AppendLine(ind + "/// <summary>");
            sb.AppendLine(ind + "/// Remote Config 키 <c>\"" + EscapeCs(keyName) + "\"</c>" + EscapeXml(descLine) + " 에 대한 정적 접근자입니다.");
            sb.AppendLine(ind + "/// <list type=\"bullet\">");
            sb.AppendLine(ind + "/// <item><description>");
            sb.AppendLine(ind + "///   <see cref=\"CreateReader\"/> — 비동기로 값을 한 번 읽습니다.");
            sb.AppendLine(ind + "/// </description></item>");
            sb.AppendLine(ind + "/// <item><description>");
            sb.AppendLine(ind + "///   <see cref=\"CreateBinding\"/> — 폴링 자동 갱신 바인딩 객체를 만듭니다. (<c>binding.Value</c> 로 동기 접근)");
            sb.AppendLine(ind + "/// </description></item>");
            sb.AppendLine(ind + "/// <item><description>");
            sb.AppendLine(ind + "///   <see cref=\"CreateListener\"/> — 값이 갱신될 때마다 콜백을 받습니다.");
            sb.AppendLine(ind + "/// </description></item>");
            sb.AppendLine(ind + "/// </list>");
            sb.AppendLine(ind + "/// </summary>");
            sb.AppendLine(ind + "public static partial class " + accessorClass);
            sb.AppendLine(ind + "{");

            // ─ Key 상수 ──────────────────────────────────────────────────────
            sb.AppendLine(ind + "    /// <summary>DB <c>remote_config</c> 테이블의 키 이름입니다.</summary>");
            sb.AppendLine(ind + "    public const string Key = \"" + EscapeCs(keyName) + "\";");
            sb.AppendLine();

            // ─ CreateReader ───────────────────────────────────────────────────
            sb.AppendLine(ind + "    /// <summary>");
            sb.AppendLine(ind + "    /// 값을 비동기로 한 번 읽습니다.<br/>");
            sb.AppendLine(ind + "    /// 캐시가 신선하면 즉시 반환하고, 만료됐으면 서버에서 갱신합니다.");
            sb.AppendLine(ind + "    /// </summary>");
            sb.AppendLine(ind + "    /// <param name=\"maxStaleSeconds\">");
            sb.AppendLine(ind + "    ///   유효로 간주할 최대 캐시 경과 시간(초).<br/>");
            sb.AppendLine(ind + "    ///   0이면 DB의 <c>max_stale_seconds</c> 설정을 따릅니다.");
            sb.AppendLine(ind + "    /// </param>");
            sb.AppendLine(ind + "    /// <example><code>");
            sb.AppendLine(ind + "    /// var config = await " + accessorClass + ".CreateReader()();");
            sb.AppendLine(ind + "    /// </code></example>");
            sb.AppendLine(ind + "    public static System.Func<System.Threading.Tasks.Task<" + configClass + ">> CreateReader(");
            sb.AppendLine(ind + "        int maxStaleSeconds = 0) =>");
            sb.AppendLine(ind + "        Supabase.CreateRemoteConfigReader<" + configClass + ">(Key, maxStaleSeconds);");
            sb.AppendLine();

            // ─ CreateBinding ─────────────────────────────────────────────────
            sb.AppendLine(ind + "    /// <summary>");
            sb.AppendLine(ind + "    /// 폴링으로 값을 자동 갱신하는 바인딩 객체를 만듭니다.<br/>");
            sb.AppendLine(ind + "    /// <c>binding.Value</c> 로 최신 값을 동기적으로 읽을 수 있습니다.<br/>");
            sb.AppendLine(ind + "    /// 객체 파괴 시 <see cref=\"IDisposable.Dispose\"/> 를 호출하세요.");
            sb.AppendLine(ind + "    /// </summary>");
            sb.AppendLine(ind + "    /// <param name=\"pollIntervalSeconds\">갱신 확인 주기(초). 0이면 자동 폴링 없음.</param>");
            sb.AppendLine(ind + "    /// <example><code>");
            sb.AppendLine(ind + "    /// _binding = " + accessorClass + ".CreateBinding();");
            sb.AppendLine(ind + "    /// var config = _binding.Value; // 매 프레임 최신 값 읽기");
            sb.AppendLine(ind + "    /// </code></example>");
            sb.AppendLine(ind + "    public static RemoteConfigBinding<" + configClass + "> CreateBinding(");
            sb.AppendLine(ind + "        float pollIntervalSeconds = 60f) =>");
            sb.AppendLine(ind + "        Supabase.CreateRemoteConfigBinding<" + configClass + ">(Key, pollIntervalSeconds);");
            sb.AppendLine();

            // ─ CreateListener ────────────────────────────────────────────────
            sb.AppendLine(ind + "    /// <summary>");
            sb.AppendLine(ind + "    /// 값이 갱신될 때마다 콜백을 호출하는 리스너를 만듭니다.<br/>");
            sb.AppendLine(ind + "    /// 객체 파괴 시 <see cref=\"IDisposable.Dispose\"/> 를 호출하세요.");
            sb.AppendLine(ind + "    /// </summary>");
            sb.AppendLine(ind + "    /// <param name=\"onChange\">값이 갱신될 때 호출되는 콜백.</param>");
            sb.AppendLine(ind + "    /// <param name=\"pollIntervalSeconds\">갱신 확인 주기(초). 0이면 자동 폴링 없음.</param>");
            sb.AppendLine(ind + "    /// <example><code>");
            sb.AppendLine(ind + "    /// _listener = " + accessorClass + ".CreateListener(cfg => ApplyConfig(cfg));");
            sb.AppendLine(ind + "    /// </code></example>");
            sb.AppendLine(ind + "    public static RemoteConfigListener<" + configClass + "> CreateListener(");
            sb.AppendLine(ind + "        System.Action<" + configClass + "> onChange, float pollIntervalSeconds = 60f) =>");
            sb.AppendLine(ind + "        Supabase.CreateRemoteConfigListener<" + configClass + ">(Key, pollIntervalSeconds, onChange);");
            sb.AppendLine(ind + "}");
        }

        // ── Subtree helpers ───────────────────────────────────────────────────

        /// <summary>depth보다 깊은 자식 항목들을 건너뜁니다 (i를 다음 같은/낮은 depth로 이동).</summary>
        private static void SkipSubtree(IReadOnlyList<RcEditableField> fields, ref int i, int parentDepth)
        {
            while (i < fields.Count && fields[i].Depth > parentDepth)
                i++;
        }

        // ── Type helpers ──────────────────────────────────────────────────────

        internal static readonly string[] TypeOptions =
            { "bool", "int", "short", "long", "ulong", "float", "double", "string", "커스텀..." };

        internal const int CustomTypeIndex = 8;

        /// <summary>카테고리에서 허용하는 TypeOptions 인덱스 배열을 반환합니다.</summary>
        public static int[] GetAllowedTypeIndices(FieldTypeCategory cat)
        {
            // TypeOptions = { bool(0), int(1), short(2), long(3), ulong(4), float(5), double(6), string(7), 커스텀(8) }
            switch (cat)
            {
                case FieldTypeCategory.Boolean: return new[] { 0, 8 };             // bool, 커스텀
                case FieldTypeCategory.Integer: return new[] { 1, 2, 3, 4, 8 };   // int, short, long, ulong, 커스텀
                case FieldTypeCategory.Float:   return new[] { 5, 6, 8 };          // float, double, 커스텀
                case FieldTypeCategory.String:  return new[] { 7, 8 };             // string, 커스텀
                case FieldTypeCategory.Json:    return new[] { 7, 8 };             // string, 커스텀 (Dictionary 프리셋은 별도 처리)
                case FieldTypeCategory.Array:   return new[] { 8 };                // 커스텀 전용
                default:                        return new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 }; // 전체
            }
        }

        private static FieldTypeCategory MapCategory(JTokenType t)
        {
            switch (t)
            {
                case JTokenType.Boolean: return FieldTypeCategory.Boolean;
                case JTokenType.Integer: return FieldTypeCategory.Integer;
                case JTokenType.Float:   return FieldTypeCategory.Float;
                case JTokenType.String:  return FieldTypeCategory.String;
                default:                 return FieldTypeCategory.Unknown;
            }
        }

        private static int IndexOf(string type)
        {
            for (var i = 0; i < TypeOptions.Length - 1; i++)
                if (TypeOptions[i] == type) return i;
            return IndexOf("string");
        }

        private static string ResolveClrType(RcEditableField f)
        {
            if (f.TypeIndex == CustomTypeIndex)
                return string.IsNullOrWhiteSpace(f.CustomType) ? "string" : f.CustomType.Trim();
            return TypeOptions[f.TypeIndex];
        }

        // ── Existing file type restoration ────────────────────────────────────

        /// <summary>
        /// 기존 Config.cs 파일이 있으면 [JsonProperty("key")] 필드의 타입을 읽어 반환합니다.
        /// key = JSON 키, value = C# 타입명
        /// </summary>
        public static Dictionary<string, string> TryLoadExistingFieldTypes(string configClassName)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            string assetPath = null;
            foreach (var guid in UnityEditor.AssetDatabase.FindAssets(configClassName))
            {
                var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith(configClassName + ".cs", StringComparison.OrdinalIgnoreCase))
                {
                    assetPath = path;
                    break;
                }
            }
            if (assetPath == null) return result;

            try
            {
                var src     = File.ReadAllText(assetPath, Encoding.UTF8);
                var pattern = new Regex(
                    @"\[JsonProperty\(""([^""]*)""\)\]\s+public\s+(.+?)\s+\w+\s*;",
                    RegexOptions.Multiline);
                foreach (Match m in pattern.Matches(src))
                {
                    var jsonKey  = m.Groups[1].Value;
                    var typeName = m.Groups[2].Value.Trim();
                    if (!string.IsNullOrEmpty(jsonKey) && !string.IsNullOrEmpty(typeName))
                        result[jsonKey] = typeName;
                }
            }
            catch { /* 파싱 실패 시 무시 */ }

            return result;
        }

        // ── String utilities ──────────────────────────────────────────────────

        internal static string ToPascalCase(string name)
        {
            var parts = name.Split('_', '-', ' ');
            var sb    = new StringBuilder();
            foreach (var part in parts)
            {
                if (part.Length == 0) continue;
                sb.Append(char.ToUpperInvariant(part[0]));
                if (part.Length > 1) sb.Append(part, 1, part.Length - 1);
            }
            return sb.Length > 0 ? sb.ToString() : name;
        }

        private static string LegalField(string key)
        {
            // C# 예약어 충돌 방지
            switch (key)
            {
                case "bool": case "int": case "float": case "double": case "string":
                case "object": case "class": case "namespace": case "public":
                case "private": case "protected": case "static": case "new":
                case "override": case "virtual": case "abstract": case "sealed":
                case "event": case "delegate": case "void": case "null":
                case "true": case "false": case "base": case "this":
                    return "@" + key;
                default:
                    return key;
            }
        }

        private static string EscapeCs(string s) =>
            s?.Replace("\\", "\\\\").Replace("\"", "\\\"") ?? "";

        /// <summary>XML 문서 주석 안에 넣을 수 있도록 특수문자를 이스케이프합니다.</summary>
        private static string EscapeXml(string s) =>
            s?.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;") ?? "";

        private static bool IsLegacyJwt(string key) =>
            !string.IsNullOrEmpty(key) && key.Length >= 20
            && key.StartsWith("eyJ", StringComparison.Ordinal)
            && key.IndexOf('.') > 0;

        // ── SubList helper ────────────────────────────────────────────────────

        private sealed class SubList : IReadOnlyList<RcEditableField>
        {
            private readonly IReadOnlyList<RcEditableField> _src;
            private readonly int _start, _end;
            public SubList(IReadOnlyList<RcEditableField> src, int start, int end)
            { _src = src; _start = start; _end = end; }
            public int Count => _end - _start;
            public RcEditableField this[int i] => _src[_start + i];
            public IEnumerator<RcEditableField> GetEnumerator()
            { for (var i = _start; i < _end; i++) yield return _src[i]; }
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }
    }

    // ── Data classes ──────────────────────────────────────────────────────────

    internal sealed class RcKeyRow
    {
        public string Key;
        public string ValueJson;
        public string Description;
    }

    internal sealed class RcEditableField
    {
        public string           JsonKey;
        public string           FullPath;
        public int              Depth;
        public bool             IsObjectNode;
        public string           NestedClassName;          // IsObjectNode일 때만 사용
        public bool             Include = true;
        public int              TypeIndex;
        public bool             IsAmbiguous;
        public string           CustomType = "";          // TypeIndex == CustomTypeIndex 일 때
        public FieldTypeCategory JsonCategory = FieldTypeCategory.Unknown;
    }

    /// <summary>필드의 JSON 타입 카테고리 — Inspector 드롭다운 필터링에 사용합니다.</summary>
    internal enum FieldTypeCategory
    {
        Boolean,  // bool, 커스텀
        Integer,  // int, short, long, ulong, 커스텀
        Float,    // float, double, 커스텀
        String,   // string, 커스텀
        Json,     // string, Dictionary<string,object>(프리셋), 커스텀  ← jsonb/$ref/allOf 등 복잡한 DB 타입
        Array,    // 커스텀 전용
        Unknown,  // 전체 표시
    }
}
