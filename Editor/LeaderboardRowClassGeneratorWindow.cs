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
        private const string DialogTitle = "리더보드 클래스";
        private const string PrefsKeyLastPath = "TrueBase.Leaderboard.LastSavePath";

        private string _className = "";
        private string _preview = "";
        private Vector2 _scroll;

        private string[] _codes = Array.Empty<string>();     // 드롭다운 항목 코드
        private string[] _labels = Array.Empty<string>();    // 드롭다운 표시명 ("이름 (코드)")
        private int _selected = -1;
        private bool _listLoaded;
        private string _listError;

        /// <summary>조회한 등록 필드 하나의 이름과 C# 타입.</summary>
        private readonly struct ColMeta
        {
            public readonly string Name;
            public readonly string ClrType;
            public ColMeta(string name, string clrType) { Name = name; ClrType = clrType; }
        }

        [MenuItem("TrueSoft/Supabase/클래스 생성/리더보드")]
        private static void Open()
        {
            var win = GetWindow<LeaderboardRowClassGeneratorWindow>(true, "리더보드 클래스 생성", true);
            win.minSize = new Vector2(560, 480);
            win.Show();
        }

        private void OnEnable() => ReloadList();

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "리더보드를 선택하면 Retool 필드 탭에서 등록(사용 켬)한 필드를 자동으로 불러옵니다.",
                MessageType.Info);

            EditorGUILayout.Space(4);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (_listLoaded && _codes.Length > 0)
                {
                    var newSel = EditorGUILayout.Popup("리더보드", _selected, _labels);
                    if (newSel != _selected)
                    {
                        _selected = newSel;
                        _className = ToPascalCase(_codes[_selected]) + "LeaderboardRow";
                        _preview = "";
                    }
                }
                else
                {
                    using (new EditorGUI.DisabledScope(true))
                        EditorGUILayout.Popup("리더보드",
                            0, new[] { _listError ?? "불러오는 중…" });
                }

                if (GUILayout.Button("새로고침", GUILayout.Width(72)))
                    ReloadList();
            }

            if (_listLoaded && _codes.Length == 0 && _listError == null)
                EditorGUILayout.HelpBox("등록된 리더보드가 없습니다. Retool에서 먼저 만드세요.", MessageType.Warning);
            if (_listError != null)
                EditorGUILayout.HelpBox(_listError, MessageType.Error);

            _className = EditorGUILayout.TextField("클래스명", _className);

            EditorGUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(CanGenerate() == false))
                {
                    if (GUILayout.Button("소스 생성", GUILayout.Height(26)))
                        Generate();
                }
                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_preview)))
                {
                    if (GUILayout.Button("저장", GUILayout.Height(26)))
                        Save();
                }
            }

            if (!string.IsNullOrEmpty(_preview))
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("미리보기", EditorStyles.boldLabel);
                using (var sv = new EditorGUILayout.ScrollViewScope(_scroll, GUILayout.ExpandHeight(true)))
                {
                    _scroll = sv.scrollPosition;
                    var w = EditorGUIUtility.currentViewWidth - 32f;
                    var h = EditorStyles.textArea.CalcHeight(new GUIContent(_preview), w);
                    EditorGUILayout.SelectableLabel(_preview, EditorStyles.textArea,
                        GUILayout.Width(w), GUILayout.Height(Mathf.Max(h, 48f)));
                }
            }
        }

        private bool CanGenerate() =>
            _selected >= 0 && _selected < _codes.Length
            && string.IsNullOrWhiteSpace(_className) == false;

        /// <summary>리더보드 목록을 조회해 드롭다운을 채웁니다.</summary>
        private void ReloadList()
        {
            _listLoaded = false;
            _listError = null;
            var settings = LoadSettings();
            if (settings == null)
            {
                _listError = "SupabaseSettings.asset 을 찾지 못했습니다. TrueSoft > Supabase > 설정 에셋 만들기 로 먼저 생성하세요.";
                _listLoaded = true;
                return;
            }

            try
            {
                var list = FetchLeaderboardList(settings);
                _codes = list.Select(x => x.code).ToArray();
                _labels = list.Select(x =>
                    (string.IsNullOrWhiteSpace(x.name) ? x.code : x.name) + " (" + x.code + ")").ToArray();
                _selected = _codes.Length > 0 ? 0 : -1;
                if (_selected >= 0 && string.IsNullOrWhiteSpace(_className))
                    _className = ToPascalCase(_codes[0]) + "LeaderboardRow";
            }
            catch (Exception e)
            {
                _listError = "리더보드 목록을 불러오지 못했습니다.\n" + e.Message;
                _codes = Array.Empty<string>();
                _labels = Array.Empty<string>();
                _selected = -1;
            }
            _listLoaded = true;
            Repaint();
        }

        private void Generate()
        {
            var settings = LoadSettings();
            if (settings == null || _selected < 0)
                return;

            var code = _codes[_selected];
            List<ColMeta> cols;
            try
            {
                cols = FetchRegisteredColumns(settings, code);
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
            _preview = BuildSource(cols, _className.Trim(), ns, code);
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
        /// RPC를 publishable(anon) 키로 POST 호출하고 JSON 배열 응답을 반환합니다.
        /// 에디터에는 로그인 세션이 없어 anon 키를 쓰며, 대상 함수(<c>ts_leaderboard_*_meta</c>)는 무인증 허용입니다.
        /// 에디터 UI 전용 동기 호출이라 완료까지 블로킹합니다.
        /// </summary>
        private static JArray CallRpcArray(SupabaseSettings settings, string fn, JObject args)
        {
            var restRoot = PostgrestOpenApiUserSaveClass.BuildRestRootUrl(settings.projectUrl);
            var apiKey = PostgrestOpenApiUserSaveClass.NormalizeApiKey(settings.publishableKey);
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("SupabaseSettings 의 publishableKey 가 비어 있습니다.");

            var url = restRoot + "rpc/" + fn;
            var body = (args ?? new JObject()).ToString(Newtonsoft.Json.Formatting.None);

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

            return JArray.Parse(string.IsNullOrWhiteSpace(text) ? "[]" : text);
        }

        /// <summary><c>ts_leaderboard_list_meta()</c>로 전체 리더보드 코드+이름을 조회합니다(비활성 포함).</summary>
        private static List<(string code, string name)> FetchLeaderboardList(SupabaseSettings settings)
        {
            var arr = CallRpcArray(settings, "ts_leaderboard_list_meta", null);
            var list = new List<(string, string)>(arr.Count);
            foreach (var item in arr)
            {
                var code = item["code"]?.Value<string>();
                if (string.IsNullOrWhiteSpace(code))
                    continue;
                list.Add((code, item["display_name"]?.Value<string>()));
            }
            return list;
        }

        /// <summary><c>ts_leaderboard_columns_meta(code)</c>로 등록 필드의 이름·타입을 조회합니다.</summary>
        private static List<ColMeta> FetchRegisteredColumns(SupabaseSettings settings, string code)
        {
            var arr = CallRpcArray(settings, "ts_leaderboard_columns_meta", new JObject { ["p_code"] = code });
            var list = new List<ColMeta>(arr.Count);
            foreach (var item in arr)
            {
                var name = item["name"]?.Value<string>();
                if (string.IsNullOrWhiteSpace(name))
                    continue;
                list.Add(new ColMeta(name, MapPgTypeToClr(item["type"]?.Value<string>())));
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
            sb.AppendLine("// Menu: TrueSoft/Supabase/클래스 생성/리더보드");
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
