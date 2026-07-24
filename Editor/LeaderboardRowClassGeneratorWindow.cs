using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using TrueBase.Unity;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace TrueBase.Editor
{
    /// <summary>
    /// 리더보드별 플레이어 데이터 행 클래스를 생성합니다.
    ///
    /// 리더보드 기록은 물리적으로 <c>leaderboard_scores</c> 한 테이블을 공유하지만,
    /// 어떤 필드를 쓰는지는 리더보드마다 다릅니다(Retool 리더보드 &gt; 필드 탭에서 등록).
    /// 이 창은 리더보드 코드만 받아 <c>ts_leaderboard_columns_meta</c> RPC로 등록 필드의 이름·타입을
    /// 조회한 뒤, 그 리더보드 전용 타입을 만듭니다. 필드를 손으로 입력할 필요가 없습니다.
    ///
    /// 유저 세이브 생성기와 달리 <c>StaticUserSave</c> 기반이 아닙니다. 리더보드는 diff 저장이 아니라
    /// 제출할 때마다 값을 통째로 보내므로, 사전(<c>Dictionary</c>)과 상호 변환하는 단순 DTO로 만듭니다.
    /// </summary>
    internal sealed class LeaderboardRowClassGeneratorWindow : EditorWindow
    {
        private const string DialogTitle = "리더보드 클래스 생성";
        private const string PrefsKeyLastPath = "TrueBase.Leaderboard.LastSavePath";

        private string _leaderboardCode = "";
        private string _className = "";
        private string _preview = "";
        private Vector2 _scroll;

        /// <summary>조회한 등록 필드 하나의 이름과 C# 타입.</summary>
        private readonly struct ColMeta
        {
            public readonly string Name;
            public readonly string ClrType;
            public ColMeta(string name, string clrType) { Name = name; ClrType = clrType; }
        }

        [MenuItem("TrueSoft/Supabase/리더보드 클래스 생성")]
        private static void Open()
        {
            var win = GetWindow<LeaderboardRowClassGeneratorWindow>(true, DialogTitle, true);
            win.minSize = new Vector2(520, 460);
            win.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "Retool 리더보드 > 필드 탭에서 이 리더보드에 등록(사용 켬)한 필드를 자동으로 불러옵니다.\n" +
                "리더보드 코드만 입력하고 아래 버튼을 누르세요.",
                MessageType.Info);

            EditorGUILayout.Space(4);
            _leaderboardCode = EditorGUILayout.TextField("리더보드 코드", _leaderboardCode);

            if (string.IsNullOrWhiteSpace(_className) && string.IsNullOrWhiteSpace(_leaderboardCode) == false)
                _className = ToPascalCase(_leaderboardCode) + "LeaderboardRow";

            _className = EditorGUILayout.TextField("클래스 이름", _className);

            EditorGUILayout.Space(6);

            using (new EditorGUI.DisabledScope(CanGenerate() == false))
            {
                if (GUILayout.Button("등록 필드 불러와 미리보기", GUILayout.Height(26)))
                    Generate();
            }

            EditorGUILayout.Space(6);

            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_preview)))
            {
                if (GUILayout.Button("C# 파일로 저장", GUILayout.Height(26)))
                    Save();
            }

            EditorGUILayout.Space(6);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.TextArea(_preview ?? "", GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        private bool CanGenerate() =>
            string.IsNullOrWhiteSpace(_leaderboardCode) == false
            && string.IsNullOrWhiteSpace(_className) == false;

        private void Generate()
        {
            var settings = LoadSettings();
            if (settings == null)
            {
                EditorUtility.DisplayDialog(DialogTitle,
                    "Assets/Resources/SupabaseSettings.asset 을 찾지 못했습니다.\n" +
                    "TrueSoft > Supabase > 설정 에셋 만들기 로 먼저 생성하세요.", "확인");
                return;
            }

            List<ColMeta> cols;
            try
            {
                cols = FetchRegisteredColumns(settings, _leaderboardCode.Trim());
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog(DialogTitle, "등록 필드를 불러오지 못했습니다.\n\n" + e.Message, "확인");
                return;
            }

            if (cols.Count == 0)
            {
                EditorUtility.DisplayDialog(DialogTitle,
                    "이 리더보드에 등록된 필드가 없습니다.\n" +
                    "Retool 리더보드 > 필드 탭에서 사용할 필드를 먼저 켜세요.\n" +
                    "(코드가 정확한지도 확인하세요.)", "확인");
                return;
            }

            var ns = EditorSettings.projectGenerationRootNamespace?.Trim() ?? "";
            _preview = BuildSource(cols, _className.Trim(), ns, _leaderboardCode.Trim());
            Repaint();
        }

        private void Save()
        {
            var suggested = _className.Trim() + ".cs";
            var lastDir = EditorPrefs.GetString(PrefsKeyLastPath, "Assets");
            var path = EditorUtility.SaveFilePanel("리더보드 클래스 저장", lastDir, suggested, "cs");
            if (string.IsNullOrWhiteSpace(path))
                return;

            File.WriteAllText(path, _preview, new UTF8Encoding(true));
            EditorPrefs.SetString(PrefsKeyLastPath, Path.GetDirectoryName(path) ?? "Assets");
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog(DialogTitle, "저장했습니다.\n" + path, "확인");
        }

        private static SupabaseSettings LoadSettings() =>
            Resources.Load<SupabaseSettings>("SupabaseSettings");

        /// <summary>
        /// <c>ts_leaderboard_columns_meta(code)</c> RPC로 등록 필드의 이름·타입을 조회합니다.
        /// 에디터에는 로그인 세션이 없어 publishable(anon) 키로 호출하며, 이 함수는 무인증 허용입니다.
        /// 에디터 UI에서 쓰는 동기 호출이라 완료까지 블로킹합니다.
        /// </summary>
        private static List<ColMeta> FetchRegisteredColumns(SupabaseSettings settings, string code)
        {
            var restRoot = PostgrestOpenApiUserSaveClass.BuildRestRootUrl(settings.projectUrl);
            var apiKey = PostgrestOpenApiUserSaveClass.NormalizeApiKey(settings.publishableKey);
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("SupabaseSettings 의 publishableKey 가 비어 있습니다.");

            var url = restRoot + "rpc/ts_leaderboard_columns_meta";
            var body = new JObject { ["p_code"] = code }.ToString(Newtonsoft.Json.Formatting.None);

            using var req = new UnityWebRequest(url, "POST")
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = 20,
            };
            req.SetRequestHeader("apikey", apiKey);
            req.SetRequestHeader("Authorization", "Bearer " + apiKey);
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Accept", "application/json");

            var op = req.SendWebRequest();
            while (op.isDone == false)
                System.Threading.Thread.Sleep(16);

            var text = req.downloadHandler?.text ?? "";

#if UNITY_2020_2_OR_NEWER
            var ok = req.result == UnityWebRequest.Result.Success;
#else
            var ok = req.isNetworkError == false && req.isHttpError == false;
#endif
            if (!ok)
            {
                var snippet = text.Length > 600 ? text.Substring(0, 600) + "…" : text;
                throw new IOException($"{req.error} (HTTP {req.responseCode})\n{snippet}");
            }

            var arr = JArray.Parse(string.IsNullOrWhiteSpace(text) ? "[]" : text);
            var list = new List<ColMeta>(arr.Count);
            foreach (var item in arr)
            {
                var name = item["name"]?.Value<string>();
                var pgType = item["type"]?.Value<string>();
                if (string.IsNullOrWhiteSpace(name))
                    continue;
                list.Add(new ColMeta(name, MapPgTypeToClr(pgType)));
            }
            return list;
        }

        /// <summary>
        /// <c>information_schema.columns.data_type</c> 문자열을 C# 타입으로 매핑합니다.
        /// 리더보드 필드는 대부분 스칼라입니다. 시각·JSON·배열은 문자열로 받아 게임에서 파싱하세요.
        /// </summary>
        private static string MapPgTypeToClr(string pgType)
        {
            switch ((pgType ?? "").Trim().ToLowerInvariant())
            {
                case "integer":                    return "int";
                case "bigint":                     return "long";
                case "smallint":                   return "short";
                case "boolean":                    return "bool";
                case "real":                       return "float";
                case "double precision":           return "double";
                case "numeric":                    return "double";
                case "text":
                case "character varying":
                case "varchar":
                case "character":
                case "citext":                     return "string";
                case "timestamp with time zone":
                case "timestamp without time zone":
                case "date":
                case "time without time zone":
                case "time with time zone":        return "string /* 시각 문자열 — 필요 시 DateTimeOffset 등으로 파싱 */";
                case "jsonb":
                case "json":                       return "string /* json — 필요 시 역직렬화 */";
                case "array":                      return "string /* 배열 — 필요 시 수동 정제 */";
                default:                            return "string /* " + pgType + " — 필요 시 수동 정제 */";
            }
        }

        private static string ToPascalCase(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            var parts = raw.Split(new[] { '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var sb = new StringBuilder();
            foreach (var p in parts)
                sb.Append(char.ToUpperInvariant(p[0])).Append(p.Length > 1 ? p.Substring(1) : "");
            return sb.ToString();
        }

        /// <summary>사전과 상호 변환하는 리더보드 행 DTO 소스를 만듭니다.</summary>
        private static string BuildSource(
            IReadOnlyList<ColMeta> columns,
            string className,
            string namespaceName,
            string leaderboardCode)
        {
            var useNs = string.IsNullOrWhiteSpace(namespaceName) == false;
            var ind = useNs ? "    " : "";
            var sb = new StringBuilder();

            sb.AppendLine("// <auto-generated>");
            sb.AppendLine("// ts_leaderboard_columns_meta → 리더보드 행 클래스");
            sb.AppendLine("// Leaderboard: " + leaderboardCode);
            sb.AppendLine("// Generated (UTC): " + DateTime.UtcNow.ToString("O"));
            sb.AppendLine("// Menu: TrueSoft/Supabase/리더보드 클래스 생성");
            sb.AppendLine("// </auto-generated>");
            sb.AppendLine();
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using TrueBase.Core.Data;");
            sb.AppendLine();

            if (useNs)
            {
                sb.AppendLine("namespace " + namespaceName.Trim());
                sb.AppendLine("{");
            }

            sb.AppendLine(ind + "/// <summary>리더보드 '" + leaderboardCode + "' 의 플레이어 데이터 필드.</summary>");
            // partial — 게임이 별도 파일에서 공통 인터페이스를 붙일 수 있게 합니다(생성 파일은 건드리지 않음).
            sb.AppendLine(ind + "public sealed partial class " + className);
            sb.AppendLine(ind + "{");
            sb.AppendLine(ind + "    /// <summary>이 클래스가 대응하는 리더보드 코드.</summary>");
            sb.AppendLine(ind + "    public const string LeaderboardCode = \"" + leaderboardCode + "\";");
            sb.AppendLine();

            foreach (var c in columns)
            {
                sb.AppendLine(ind + "    [DataColumn(\"" + c.Name + "\")]");
                sb.AppendLine(ind + "    public " + c.ClrType + " " + ToPascalCase(c.Name) + " { get; set; }");
                sb.AppendLine();
            }

            // 제출용 사전 변환
            sb.AppendLine(ind + "    /// <summary>점수 기록 시 넘길 사전으로 변환합니다.</summary>");
            sb.AppendLine(ind + "    public IReadOnlyDictionary<string, object> ToData()");
            sb.AppendLine(ind + "    {");
            sb.AppendLine(ind + "        return new Dictionary<string, object>");
            sb.AppendLine(ind + "        {");
            foreach (var c in columns)
                sb.AppendLine(ind + "            [\"" + c.Name + "\"] = " + ToPascalCase(c.Name) + ",");
            sb.AppendLine(ind + "        };");
            sb.AppendLine(ind + "    }");
            sb.AppendLine();

            // 조회 결과 사전에서 복원
            sb.AppendLine(ind + "    /// <summary>순위·플레이어 조회 결과의 <c>Data</c> 사전에서 복원합니다.</summary>");
            sb.AppendLine(ind + "    public static " + className + " FromData(IReadOnlyDictionary<string, object> data)");
            sb.AppendLine(ind + "    {");
            sb.AppendLine(ind + "        var row = new " + className + "();");
            sb.AppendLine(ind + "        if (data == null) return row;");
            foreach (var c in columns)
            {
                var prop = ToPascalCase(c.Name);
                var clr = StripComment(c.ClrType);
                sb.AppendLine(ind + "        if (data.TryGetValue(\"" + c.Name + "\", out var v_" + prop + ") && v_" + prop + " != null)");
                sb.AppendLine(ind + "            row." + prop + " = (" + clr + ")System.Convert.ChangeType(v_" + prop + ", typeof(" + clr + "));");
            }
            sb.AppendLine(ind + "        return row;");
            sb.AppendLine(ind + "    }");

            sb.AppendLine(ind + "}");

            if (useNs)
                sb.AppendLine("}");

            return sb.ToString();
        }

        /// <summary>타입 뒤에 붙은 <c>/* ... */</c> 안내 주석을 떼어 순수 타입명만 남깁니다.</summary>
        private static string StripComment(string clr)
        {
            var idx = clr.IndexOf("/*", StringComparison.Ordinal);
            return idx < 0 ? clr.Trim() : clr.Substring(0, idx).Trim();
        }
    }
}
