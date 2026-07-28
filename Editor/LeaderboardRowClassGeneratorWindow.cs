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
using GC = TrueBase.Editor.GeneratorEditorCommon;

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
    /// 생성 파일에는 <b>메서드가 없습니다</b> — <c>[Leaderboard]</c> 속성과 <c>[DataColumn]</c> 필드 선언만 담습니다.
    /// 사전 변환은 SDK가 그 속성을 읽어 처리하므로, SDK API가 바뀌어도 생성 파일은 깨지지 않습니다.
    /// 조작은 <c>Supabase.SubmitScoreAsync&lt;ArenaLeaderboard&gt;(score)</c>처럼 파사드에서 합니다.
    /// </summary>
    internal sealed class LeaderboardRowClassGeneratorWindow : EditorWindow
    {
        private const string DialogTitle = "리더보드 클래스";
        private const string PrefsKeyLastPath = "TrueBase.Leaderboard.LastSavePath";

        private string _className = "";
        private string _preview = "";
        private Vector2 _scroll;
        private Vector2 _fieldScroll;

        private string[] _codes = Array.Empty<string>();     // 드롭다운 항목 코드
        private string[] _labels = Array.Empty<string>();    // 드롭다운 표시명 ("이름 (코드)")
        private int _selected = -1;
        private bool _listLoaded;
        private string _listError;

        private List<ColMeta> _cols = new List<ColMeta>();   // 선택한 리더보드의 등록 필드
        private string _fieldError;                          // 등록 필드 가져오기 실패 메시지
        private bool _fieldEmpty;                            // 등록 필드가 0개로 조회됨
        private bool _fieldsFetched;                         // 조회를 끝냄(0개여도 생성 가능)

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
                "리더보드를 선택하고 '필드 목록 가져오기'를 누르면 Retool 필드 탭에서 등록한 필드를 불러옵니다.",
                MessageType.Info);

            GC.DrawConnectionSetup(LoadSettings(), needsSecret: false);

            EditorGUILayout.Space(4);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (_listLoaded && _codes.Length > 0)
                {
                    var newSel = EditorGUILayout.Popup("리더보드", _selected, _labels);
                    if (newSel != _selected)
                    {
                        _selected = newSel;
                        _className = ToPascalCase(_codes[_selected]) + "Leaderboard";
                        _cols.Clear();
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

            EditorGUILayout.Space(4);
            using (new EditorGUI.DisabledScope(_selected < 0 || _selected >= _codes.Length))
            {
                if (GUILayout.Button("필드 목록 가져오기", GUILayout.Height(26)))
                    FetchFields();
            }

            GC.DrawFetchError(_fieldError, FetchFields);
            if (_fieldEmpty && _fieldError == null)
                EditorGUILayout.HelpBox(
                    "등록된 필드가 없어 점수만 기록하는 리더보드입니다. 그대로 생성하면 됩니다.\n" +
                    "순위와 함께 값을 주고받으려면 Retool 리더보드 > 필드 탭에서 필드를 켜세요.", MessageType.Info);

            // 필드가 0개여도 생성한다 — 리더보드를 가리키는 타입 자체가 필요하기 때문.
            if (_fieldsFetched)
            {
                if (_cols.Count > 0)
                {
                    EditorGUILayout.Space(6);
                    DrawFieldList();
                }

                EditorGUILayout.Space(4);
                _className = EditorGUILayout.TextField("클래스명", _className);

                EditorGUILayout.Space(4);
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_className)))
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

        /// <summary>선택 리더보드에 등록된 필드를 읽기전용 목록으로 그립니다.</summary>
        private void DrawFieldList()
        {
            float wName = Mathf.Max(160f, (EditorGUIUtility.currentViewWidth - 42f) * 0.45f);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("필드", EditorStyles.miniLabel, GUILayout.MinWidth(wName));
                EditorGUILayout.LabelField("타입", EditorStyles.miniLabel, GUILayout.MinWidth(80), GUILayout.ExpandWidth(true));
            }

            var rowHeight  = EditorGUIUtility.singleLineHeight + 2f;
            var listHeight = Mathf.Min(_cols.Count * rowHeight + 4f, 280f);

            using (var sv = new EditorGUILayout.ScrollViewScope(_fieldScroll, GUILayout.Height(listHeight)))
            {
                _fieldScroll = sv.scrollPosition;
                foreach (var c in _cols)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        var commented = c.ClrType.IndexOf("/*", StringComparison.Ordinal) >= 0;
                        var style = commented ? GC.AmbiguousStyle : EditorStyles.label;

                        EditorGUILayout.LabelField(new GUIContent(c.Name, c.Name), style, GUILayout.MinWidth(wName));

                        var shown   = commented ? StripComment(c.ClrType) + "  ⚠" : c.ClrType;
                        var tooltip = commented
                            ? c.ClrType + " — 스칼라로 매핑하지 못한 타입입니다. 문자열로 받아 게임에서 파싱하세요."
                            : c.ClrType;
                        EditorGUILayout.LabelField(new GUIContent(shown, tooltip), style, GUILayout.MinWidth(80), GUILayout.ExpandWidth(true));
                    }
                }
            }
        }

        /// <summary>리더보드 목록을 조회해 드롭다운을 채웁니다.</summary>
        private void ReloadList()
        {
            _listLoaded = false;
            _listError = null;
            _cols.Clear();
            _preview = "";
            var settings = LoadSettings();
            if (settings == null)
            {
                _listError = "SupabaseSettings.asset 을 찾지 못했습니다. TrueSoft > Supabase > 설정 에셋 만들기 로 먼저 생성하세요.";
                _listLoaded = true;
                return;
            }

            try
            {
                EditorUtility.DisplayProgressBar(DialogTitle, "리더보드 목록을 가져오는 중…", 0.4f);
                var list = FetchLeaderboardList(settings);
                _codes = list.Select(x => x.code).ToArray();
                _labels = list.Select(x =>
                    (string.IsNullOrWhiteSpace(x.name) ? x.code : x.name) + " (" + x.code + ")").ToArray();
                _selected = _codes.Length > 0 ? 0 : -1;
                if (_selected >= 0 && string.IsNullOrWhiteSpace(_className))
                    _className = ToPascalCase(_codes[0]) + "Leaderboard";
            }
            catch (Exception e)
            {
                _listError = GC.DescribeFetchError(e);
                _codes = Array.Empty<string>();
                _labels = Array.Empty<string>();
                _selected = -1;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            _listLoaded = true;
            Repaint();
        }

        /// <summary>선택 리더보드의 등록 필드를 조회해 목록에 채웁니다(소스는 아직 생성하지 않음).</summary>
        private void FetchFields()
        {
            _cols.Clear();
            _preview = "";
            _fieldError = null;
            _fieldEmpty = false;
            _fieldsFetched = false;

            var settings = LoadSettings();
            if (settings == null || _selected < 0 || _selected >= _codes.Length)
                return;

            var code = _codes[_selected];
            try
            {
                EditorUtility.DisplayProgressBar(DialogTitle, "등록 필드를 가져오는 중…", 0.4f);
                _cols = FetchRegisteredColumns(settings, code);
            }
            catch (Exception e)
            {
                _fieldError = GC.DescribeFetchError(e);
                Repaint();
                return;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            _fieldEmpty = _cols.Count == 0;
            _fieldsFetched = true;
            Repaint();
        }

        /// <summary>이미 불러온 등록 필드로 소스를 생성합니다.</summary>
        private void Generate()
        {
            if (!_fieldsFetched || _selected < 0 || _selected >= _codes.Length)
                return;

            var code = _codes[_selected];
            var ns   = EditorSettings.projectGenerationRootNamespace?.Trim() ?? "";
            _preview = BuildSource(_cols, _className.Trim(), ns, code);
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

        /// <summary>
        /// 리더보드를 가리키는 타입 소스를 만듭니다.
        /// <para>
        /// <b>메서드를 emit하지 않습니다</b> — 속성·필드 선언만 담습니다. 사전 변환은 SDK가
        /// <c>[DataColumn]</c>을 읽어 처리하므로, SDK API가 바뀌어도 이 파일은 깨지지 않습니다.
        /// DB 컬럼이 바뀔 때만 다시 생성하면 됩니다.
        /// </para>
        /// </summary>
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
            sb.AppendLine("// ts_leaderboard_columns_meta → 리더보드 타입");
            sb.AppendLine("// Leaderboard: " + leaderboardCode);
            sb.AppendLine("// Generated (UTC): " + DateTime.UtcNow.ToString("O"));
            sb.AppendLine("// Menu: TrueSoft/Supabase/클래스 생성/리더보드");
            sb.AppendLine("// </auto-generated>");
            sb.AppendLine();
            sb.AppendLine("using TrueBase.Core.Data;");
            sb.AppendLine();

            if (useNs)
            {
                sb.AppendLine("namespace " + namespaceName.Trim());
                sb.AppendLine("{");
            }

            sb.AppendLine(ind + "/// <summary>");
            sb.AppendLine(ind + "/// 리더보드 '" + leaderboardCode + "'. 조작은 Supabase 파사드에 있습니다 —");
            sb.AppendLine(ind + "/// Supabase.SubmitScoreAsync&lt;" + className + "&gt;(score) / GetRanksAsync&lt;" + className + "&gt;() 등.");
            sb.AppendLine(ind + "/// </summary>");
            // partial — 게임이 별도 파일에서 멤버를 덧붙일 수 있게 합니다(생성 파일은 건드리지 않음).
            sb.AppendLine(ind + "[Leaderboard(\"" + leaderboardCode + "\")]");
            sb.AppendLine(ind + "public sealed partial class " + className + " : ILeaderboard");
            sb.AppendLine(ind + "{");

            if (columns.Count == 0)
            {
                sb.AppendLine(ind + "    // 등록된 플레이어 데이터 필드가 없습니다. 점수만 기록하는 리더보드입니다.");
            }
            else
            {
                sb.AppendLine(ind + "    /// <summary>순위와 함께 주고받는 플레이어 데이터. Retool 리더보드 &gt; 필드 탭에서 등록한 것만 있습니다.</summary>");
                sb.AppendLine(ind + "    public sealed partial class Row");
                sb.AppendLine(ind + "    {");
                foreach (var c in columns)
                    sb.AppendLine(ind + "        [DataColumn(\"" + c.Name + "\")] public " + StripComment(c.ClrType) + " " + ToPascalCase(c.Name) + ";");
                sb.AppendLine(ind + "    }");
            }

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
