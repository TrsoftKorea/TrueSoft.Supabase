using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine.Networking;
using static TrueBase.Editor.GeneratorTypeCatalog;

namespace TrueBase.Editor
{
    /// <summary>remote_config 테이블에서 키·JSON을 읽어 DTO + 접근자 클래스 소스를 생성합니다.</summary>
    internal static class RemoteConfigClassGenerator
    {

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
            if (IsLegacyJwtStyleApiKey(key))
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
                // value_json은 컬럼이 text면 문자열, jsonb면 JObject로 반환될 수 있어 JSON 문자열로 정규화
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


        /// <summary>
        /// value_json 문자열 → 평탄화된 RcEditableField 목록.
        /// 중첩 객체는 IsObjectNode 항목(Depth N) + 자식 필드(Depth N+1)로 삽입됩니다.
        /// <c>__meta</c> 키는 건너뜁니다. <c>__meta</c>에 타입 힌트가 있으면 해당 필드의 TypeIndex를 설정합니다.
        /// </summary>
        public static List<RcEditableField> ParseJsonToFields(string valueJson)
        {
            var result = new List<RcEditableField>();
            if (string.IsNullOrWhiteSpace(valueJson))
                return result;

            JObject root;
            try { root = JObject.Parse(valueJson); }
            catch { return result; }

            // __meta 에서 타입 힌트를 추출하여 필드 파싱에 전달
            var metaHints = root["__meta"] as JObject;
            AppendFields(result, root, depth: 0, pathPrefix: "", metaHints: metaHints);
            return result;
        }

        private static void AppendFields(List<RcEditableField> list, JObject obj, int depth, string pathPrefix,
            JObject metaHints = null)
        {
            foreach (var prop in obj.Properties())
            {
                var jsonKey  = prop.Name;

                // __meta 키는 타입 힌트 저장 전용이므로 필드 목록에서 제외
                if (jsonKey == "__meta") continue;

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
                        // 중첩 객체는 자체 __meta가 없으므로 metaHints 전달하지 않음
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
                        // __meta 힌트가 있으면 해당 타입 우선 사용
                        var metaType = metaHints?[jsonKey]?.Value<string>();
                        var (typeIndex, ambiguous) = MapPrimitiveTypeWithMeta(prop.Value.Type, metaType);
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

        private static (int typeIndex, bool ambiguous) MapPrimitiveTypeWithMeta(JTokenType t, string metaType)
        {
            // __meta 힌트가 있으면 TypeOptions에서 찾아 사용 (ambiguity 없음)
            if (!string.IsNullOrEmpty(metaType))
            {
                for (var i = 0; i < TypeOptions.Length; i++)
                    if (TypeOptions[i] == metaType)
                        return (i, false);
            }

            return MapPrimitiveType(t);
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


        /// <summary>
        /// <c>[RemoteConfigKey]</c> 어트리뷰트가 포함된 DTO 클래스 C# 소스를 생성합니다.
        /// 접근자 클래스는 생성하지 않습니다. 게임 코드에서 <c>RemoteConfig&lt;T&gt;</c>를 직접 사용하세요.
        /// </summary>
        /// <param name="description">DB remote_config.description 값. 클래스 주석에 포함됩니다.</param>
        public static string GenerateSource(
            IReadOnlyList<RcEditableField> fields,
            string configClassName,
            string keyName,
            string namespaceName,
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
            sb.AppendLine("using TrueBase.Unity;");
            sb.AppendLine();

            if (useNs)
            {
                sb.AppendLine("namespace " + namespaceName.Trim());
                sb.AppendLine("{");
            }

            // DTO 클래스 ([RemoteConfigKey] 포함)
            AppendConfigClass(sb, ind, configClassName, fields, depth: 0, keyName: keyName, description: description);

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
            string keyName = null,
            string description = null)
        {
            // 이 depth에 속하는 필드만 (자식 depth의 필드는 재귀 호출에서 처리)
            if (depth == 0 && !string.IsNullOrWhiteSpace(keyName))
            {
                var descPart = string.IsNullOrWhiteSpace(description) ? "" : " — " + EscapeXml(description.Trim());
                sb.AppendLine(ind + "/// <summary>");
                sb.AppendLine(ind + "/// Remote Config 키 <c>\"" + EscapeCSharpString(keyName) + "\"</c>" + descPart + " 의 JSON 구조에 대응하는 데이터 클래스입니다.<br/>");
                sb.AppendLine(ind + "/// <c>RemoteConfig&lt;" + className + "&gt;.CreateListener/Binding/Reader()</c> 로 사용하세요.");
                sb.AppendLine(ind + "/// <para><b>이 파일은 자동 생성됩니다. 직접 수정하지 마세요.</b></para>");
                sb.AppendLine(ind + "/// </summary>");
            }
            else if (depth > 0)
            {
                sb.AppendLine(ind + "/// <summary>중첩 설정 구조체입니다.</summary>");
            }

            if (depth == 0 && !string.IsNullOrWhiteSpace(keyName))
                sb.AppendLine(ind + "[RemoteConfigKey(\"" + EscapeCSharpString(keyName) + "\")]");

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
                    sb.AppendLine(ind + "    [JsonProperty(\"" + EscapeCSharpString(f.JsonKey) + "\")] public " + f.NestedClassName + " " + LegalFieldName(f.JsonKey) + ";");
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
                    sb.AppendLine(ind + "    [JsonProperty(\"" + EscapeCSharpString(f.JsonKey) + "\")] public " + clr + " " + LegalFieldName(f.JsonKey) + ";");
                    i++;
                }
            }

            sb.AppendLine(ind + "}");
        }


        /// <summary>depth보다 깊은 자식 항목들을 건너뜁니다 (i를 다음 같은/낮은 depth로 이동).</summary>
        private static void SkipSubtree(IReadOnlyList<RcEditableField> fields, ref int i, int parentDepth)
        {
            while (i < fields.Count && fields[i].Depth > parentDepth)
                i++;
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

        private static string ResolveClrType(RcEditableField f)
        {
            if (f.TypeIndex == CustomTypeIndex)
                return string.IsNullOrWhiteSpace(f.CustomType) ? "string" : f.CustomType.Trim();
            return TypeOptions[f.TypeIndex];
        }


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
                var src = File.ReadAllText(assetPath, Encoding.UTF8);
                return ExtractAttributedFieldTypes(src, "JsonProperty");
            }
            catch { /* 파싱 실패 시 무시 */ }

            return result;
        }

        // 문자열 유틸(ToPascalCase·LegalFieldName·EscapeXml 등)은 GeneratorTypeCatalog가 단일 소스이며
        // 파일 상단의 using static으로 참조합니다.


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

}
