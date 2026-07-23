using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TrueBase.Unity;
using UnityEditor;
using UnityEngine;

namespace TrueBase.Editor
{
    /// <summary>
    /// 리더보드별 플레이어 데이터 행 클래스를 생성합니다.
    ///
    /// 리더보드 기록은 물리적으로 <c>leaderboard_scores</c> 한 테이블을 공유하지만,
    /// 어떤 컬럼을 쓰는지는 리더보드마다 다릅니다(Retool 리더보드 &gt; 컬럼 탭에서 등록).
    /// 이 창은 그중 지정한 컬럼만 뽑아 리더보드 전용 타입을 만듭니다.
    ///
    /// 유저 세이브 생성기와 달리 <c>StaticUserSave</c> 기반이 아닙니다. 리더보드는 diff 저장이 아니라
    /// 제출할 때마다 값을 통째로 보내므로, 사전(<c>Dictionary</c>)과 상호 변환하는 단순 DTO로 만듭니다.
    /// </summary>
    internal sealed class LeaderboardRowClassGeneratorWindow : EditorWindow
    {
        private const string DialogTitle = "리더보드 클래스 생성";
        private const string ScoresTable = "leaderboard_scores";
        private const string PrefsKeyLastPath = "TrueBase.Leaderboard.LastSavePath";

        /// <summary>리더보드가 공유하는 인프라 컬럼. 플레이어 데이터가 아니므로 생성에서 제외합니다.</summary>
        private static readonly HashSet<string> ReservedColumns = new HashSet<string>(StringComparer.Ordinal)
        {
            "id", "table_code", "rotation_count", "account_id", "user_id", "server_id",
            "score", "extra_data", "score_achieved_at", "first_recorded_at", "score_count", "updated_at"
        };

        private string _leaderboardCode = "";
        private string _columnsCsv = "";
        private string _className = "";
        private string _preview = "";
        private Vector2 _scroll;

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
                "Retool의 리더보드 > 컬럼 탭에서 이 리더보드에 등록한 컬럼 이름을 쉼표로 구분해 입력하세요.\n" +
                "입력한 컬럼만 클래스에 포함됩니다.",
                MessageType.Info);

            EditorGUILayout.Space(4);
            _leaderboardCode = EditorGUILayout.TextField("리더보드 코드", _leaderboardCode);
            _columnsCsv = EditorGUILayout.TextField("컬럼 (쉼표 구분)", _columnsCsv);
            _className = EditorGUILayout.TextField("클래스 이름", _className);

            if (string.IsNullOrWhiteSpace(_className) && string.IsNullOrWhiteSpace(_leaderboardCode) == false)
                _className = ToPascalCase(_leaderboardCode) + "LeaderboardRow";

            EditorGUILayout.Space(6);

            using (new EditorGUI.DisabledScope(CanGenerate() == false))
            {
                if (GUILayout.Button("컬럼 불러와 미리보기", GUILayout.Height(26)))
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
            && string.IsNullOrWhiteSpace(_columnsCsv) == false
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

            var wanted = ParseColumnList(_columnsCsv);
            if (wanted.Count == 0)
            {
                EditorUtility.DisplayDialog(DialogTitle, "컬럼을 하나 이상 입력하세요.", "확인");
                return;
            }

            var reservedHit = wanted.Where(ReservedColumns.Contains).ToList();
            if (reservedHit.Count > 0)
            {
                EditorUtility.DisplayDialog(DialogTitle,
                    "다음은 리더보드 인프라 컬럼이라 플레이어 데이터로 쓸 수 없습니다:\n\n• "
                    + string.Join("\n• ", reservedHit), "확인");
                return;
            }

            try
            {
                var restRoot = PostgrestOpenApiUserSaveClass.BuildRestRootUrl(settings.projectUrl);
                var apiKey = PostgrestOpenApiUserSaveClass.NormalizeApiKey(settings.publishableKey);
                var json = PostgrestOpenApiUserSaveClass.FetchOpenApiJson(restRoot, apiKey, 20);

                var parsed = PostgrestOpenApiUserSaveClass.ParseTableColumns(
                    json, ScoresTable, ReservedColumns, wanted);

                if (parsed.IsSuccess == false)
                {
                    EditorUtility.DisplayDialog(DialogTitle, parsed.ErrorMessage, "확인");
                    return;
                }

                var found = new HashSet<string>(parsed.Columns.Select(c => c.Name), StringComparer.Ordinal);
                var missing = wanted.Where(w => found.Contains(w) == false).ToList();
                if (missing.Count > 0)
                {
                    EditorUtility.DisplayDialog(DialogTitle,
                        "다음 컬럼을 leaderboard_scores 에서 찾지 못했습니다.\n" +
                        "Retool 리더보드 > 컬럼 탭에서 먼저 추가했는지 확인하세요:\n\n• "
                        + string.Join("\n• ", missing), "확인");
                    return;
                }

                var ns = EditorSettings.projectGenerationRootNamespace?.Trim() ?? "";
                _preview = BuildSource(parsed.Columns, _className.Trim(), ns, _leaderboardCode.Trim());
                Repaint();
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog(DialogTitle, "컬럼을 불러오지 못했습니다.\n\n" + e.Message, "확인");
            }
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

        private static HashSet<string> ParseColumnList(string csv) =>
            new HashSet<string>(
                (csv ?? "")
                    .Split(new[] { ',', '\n', '\r', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => s.Length > 0),
                StringComparer.Ordinal);

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
            IReadOnlyList<OpenApiColumn> columns,
            string className,
            string namespaceName,
            string leaderboardCode)
        {
            var useNs = string.IsNullOrWhiteSpace(namespaceName) == false;
            var ind = useNs ? "    " : "";
            var sb = new StringBuilder();

            sb.AppendLine("// <auto-generated>");
            sb.AppendLine("// PostgREST OpenAPI → 리더보드 행 클래스");
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

            sb.AppendLine(ind + "/// <summary>리더보드 '" + leaderboardCode + "' 의 플레이어 데이터 컬럼.</summary>");
            // partial — 게임이 별도 파일에서 공통 인터페이스를 붙일 수 있게 합니다(생성 파일은 건드리지 않음).
            sb.AppendLine(ind + "public sealed partial class " + className);
            sb.AppendLine(ind + "{");
            sb.AppendLine(ind + "    /// <summary>이 클래스가 대응하는 리더보드 코드.</summary>");
            sb.AppendLine(ind + "    public const string LeaderboardCode = \"" + leaderboardCode + "\";");
            sb.AppendLine();

            foreach (var c in columns)
            {
                if (string.IsNullOrWhiteSpace(c.Comment) == false)
                    sb.AppendLine(ind + "    /// <summary>" + c.Comment.Replace("\n", " ") + "</summary>");

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
                sb.AppendLine(ind + "        if (data.TryGetValue(\"" + c.Name + "\", out var v_" + prop + ") && v_" + prop + " != null)");
                sb.AppendLine(ind + "            row." + prop + " = (" + c.ClrType + ")System.Convert.ChangeType(v_" + prop + ", typeof(" + c.ClrType + "));");
            }
            sb.AppendLine(ind + "        return row;");
            sb.AppendLine(ind + "    }");

            sb.AppendLine(ind + "}");

            if (useNs)
                sb.AppendLine("}");

            return sb.ToString();
        }
    }
}
