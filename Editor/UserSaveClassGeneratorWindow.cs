using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TrueBase.Unity;
using UnityEditor;
using UnityEngine;
using GC = TrueBase.Editor.GeneratorEditorCommon;

namespace TrueBase.Editor
{
    /// <summary>
    /// <c>user_data</c> 스키마를 읽어 <c>PlayerSave</c> 클래스를 생성하는 창.
    /// 이전에는 <c>SupabaseSettings</c> 인스펙터의 접이식 UI였으나 독립 창으로 분리했습니다.
    /// </summary>
    internal sealed class UserSaveClassGeneratorWindow : EditorWindow
    {
        private const string ClassName   = "PlayerSave";
        private const string SkipColumns = "id,user_id,account_id,server_id,updated_at";
        private const string DialogTitle = "유저 데이터 클래스";

        private const string PrefsKeyColumnTypes      = "TrueBase.PlayerSave.ColumnTypes";
        private const string PrefsKeyColumnPriorities = "TrueBase.PlayerSave.ColumnPriorities";
        private const string PrefsKeyColumnFieldNames = "TrueBase.PlayerSave.ColumnFieldNames";
        private const string PrefsKeyColumnDefaults   = "TrueBase.PlayerSave.ColumnDefaults";
        private const string PrefsKeyCsvPath          = "TrueBase.PlayerSave.CsvPath";
        private const string PrefsKeyLastSaveDir      = "TrueBase.PlayerSave.LastSaveDir";

        private static List<EditableColumn> _editableColumns = new List<EditableColumn>();
        private static bool _columnsFetched;
        private static List<string> _warnings = new List<string>();
        private static Vector2 _columnScroll;
        private static string _columnFilter = "";
        private static string _previewText = "";
        private static Vector2 _previewScroll;

        [MenuItem("TrueSoft/Supabase/클래스 생성/유저 데이터")]
        private static void Open()
        {
            var win = GetWindow<UserSaveClassGeneratorWindow>(true, "유저 데이터 클래스 생성", true);
            win.minSize = new Vector2(560, 480);
            win.Show();
        }

        private void OnEnable()
        {
            _editableColumns.Clear();
            _columnsFetched = false;
            _warnings.Clear();
            _previewText = "";
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "DB에서 user_data 필드 목록을 읽어 PlayerSave 클래스를 생성합니다.\n" +
                "Secret 키는 SupabaseSettings 인스펙터에서 입력합니다.",
                MessageType.Info);

            var secret = GC.GetSecretKey();
            if (string.IsNullOrWhiteSpace(secret))
                EditorGUILayout.HelpBox("Secret 키가 설정되지 않았습니다. SupabaseSettings.asset 인스펙터에서 먼저 입력하세요.", MessageType.Warning);

            EditorGUILayout.Space(4);
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(secret)))
            {
                if (GUILayout.Button("필드 목록 가져오기", GUILayout.Height(26)))
                {
                    var settings = LoadSettings();
                    if (settings != null) FetchColumns(settings);
                    else EditorUtility.DisplayDialog(DialogTitle, "SupabaseSettings.asset 을 찾지 못했습니다.", "확인");
                }
            }

            if (_columnsFetched && _editableColumns.Count > 0)
            {
                EditorGUILayout.Space(6);
                DrawColumnList();

                EditorGUILayout.Space(8);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(new GUIContent("CSV로 저장하기", "현재 컬럼 설정을 CSV로 씁니다 → 엑셀에서 일괄 편집"), GUILayout.Height(24)))
                        EditorApplication.delayCall += ExportColumnsCsv;
                    if (GUILayout.Button(new GUIContent("CSV 불러오기", "편집한 CSV를 컬럼명 기준으로 설정에 반영"), GUILayout.Height(24)))
                        EditorApplication.delayCall += ImportColumnsCsv;
                }
                EditorGUILayout.Space(2);
                using (new EditorGUILayout.HorizontalScope())
                {
                    var csvPath = EditorPrefs.GetString(PrefsKeyCsvPath, "");
                    EditorGUILayout.LabelField("CSV 위치",
                        string.IsNullOrEmpty(csvPath) ? "미지정 (저장 시 폴더 선택)" : csvPath, EditorStyles.miniLabel);
                    if (GUILayout.Button(new GUIContent("위치 변경", "CSV 파일 위치를 지정/변경합니다."), GUILayout.Height(18), GUILayout.Width(70)))
                        EditorApplication.delayCall += PickCsvPath;
                    if (GUILayout.Button(new GUIContent("열기", "저장된 CSV를 기본 편집기로 엽니다."), GUILayout.Height(18), GUILayout.Width(48)))
                        EditorApplication.delayCall += OpenColumnsCsv;
                }

                var unspecifiedNames = CollectUnspecifiedColumnNames();
                if (unspecifiedNames.Count > 0)
                    EditorGUILayout.HelpBox(
                        "타입 미지정 필드: " + string.Join(", ", unspecifiedNames)
                        + "\njsonb 컬럼은 CSV에서 Dictionary value 또는 리스트 요소 타입을 지정해야 소스를 생성할 수 있습니다.",
                        MessageType.Warning);

                var defaultMissing = CollectDefaultMissingColumnNames();
                if (defaultMissing.Count > 0)
                    EditorGUILayout.HelpBox(
                        "기본값 미지정 필드: " + string.Join(", ", defaultMissing)
                        + "\n모든 컬럼은 CSV의 default 열에 기본값을 지정해야 소스를 생성할 수 있습니다.",
                        MessageType.Warning);

                var unresolvedNames = CollectUnresolvedColumnNames();
                if (unresolvedNames.Count > 0)
                    EditorGUILayout.HelpBox(
                        "에디터에서 찾지 못한 타입: " + string.Join(", ", unresolvedNames)
                        + "\n철자가 맞다면 그대로 생성해도 됩니다. 오타라면 컴파일 시 에러가 납니다."
                        + " 다른 네임스페이스의 타입은 정규화 이름으로 쓰세요.",
                        MessageType.Warning);

                EditorGUILayout.Space(4);
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(unspecifiedNames.Count > 0 || defaultMissing.Count > 0))
                    {
                        if (GUILayout.Button("소스 생성", GUILayout.Height(26)))
                            BuildPreviewFromColumns();
                    }
                    using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_previewText)))
                    {
                        if (GUILayout.Button("저장", GUILayout.Height(26)))
                            SaveToProject();
                        if (GUILayout.Button("다른 위치에 저장…", GUILayout.Height(26), GUILayout.Width(120)))
                            SaveToProject(forcePicker: true);
                    }
                }

                if (!string.IsNullOrWhiteSpace(_previewText))
                {
                    var autoPath = ResolveAutoSavePath();
                    EditorGUILayout.LabelField("저장 위치",
                        string.IsNullOrEmpty(autoPath) ? "최초 저장 시 폴더 선택" : autoPath, EditorStyles.miniLabel);
                }
            }

            foreach (var w in _warnings)
                EditorGUILayout.HelpBox(w, MessageType.Warning);

            if (!string.IsNullOrEmpty(_previewText))
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("미리보기", EditorStyles.boldLabel);
                using (var sv = new EditorGUILayout.ScrollViewScope(_previewScroll, GUILayout.ExpandHeight(true)))
                {
                    _previewScroll = sv.scrollPosition;
                    var w = EditorGUIUtility.currentViewWidth - 32f;
                    var h = EditorStyles.textArea.CalcHeight(new GUIContent(_previewText), w);
                    EditorGUILayout.SelectableLabel(_previewText, EditorStyles.textArea,
                        GUILayout.Width(w), GUILayout.Height(Mathf.Max(h, 48f)));
                }
            }
        }

        private static SupabaseSettings LoadSettings() => Resources.Load<SupabaseSettings>("SupabaseSettings");

        private static void DrawColumnList()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                _columnFilter = EditorGUILayout.TextField(new GUIContent("검색", "컬럼명·필드명으로 표시를 거릅니다. 생성 대상엔 영향 없습니다."), _columnFilter);
                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_columnFilter)))
                    if (GUILayout.Button("×", GUILayout.Width(22))) { _columnFilter = ""; GUI.FocusControl(null); }
            }

            var visible = _editableColumns;
            if (!string.IsNullOrWhiteSpace(_columnFilter))
            {
                var f = _columnFilter.Trim();
                visible = _editableColumns.Where(c =>
                    (c.Name != null && c.Name.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (c.FieldName != null && c.FieldName.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
                EditorGUILayout.LabelField($"필터 표시: {visible.Count} / {_editableColumns.Count}", EditorStyles.miniLabel);
            }

            float avail    = Mathf.Max(360f, EditorGUIUtility.currentViewWidth - 42f);
            float wCycle   = 64f;
            float wInclude = 34f;
            float flex     = avail - wCycle - wInclude;
            float wName    = Mathf.Round(flex * 0.22f);
            float wField   = Mathf.Round(flex * 0.26f);
            float wDefault = Mathf.Round(flex * 0.20f);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("컬럼",     EditorStyles.miniLabel, GUILayout.Width(wName));
                EditorGUILayout.LabelField("필드명",   EditorStyles.miniLabel, GUILayout.Width(wField));
                EditorGUILayout.LabelField("저장 주기", EditorStyles.miniLabel, GUILayout.Width(wCycle));
                EditorGUILayout.LabelField(new GUIContent("기본값", "새 유저 시작값. 스칼라는 = 초기화, Auto 컬렉션은 [AutoDefault]로 생성됩니다."),
                    EditorStyles.miniLabel, GUILayout.Width(wDefault));
                EditorGUILayout.LabelField("포함",     EditorStyles.miniLabel, GUILayout.Width(wInclude));
                EditorGUILayout.LabelField("타입",     EditorStyles.miniLabel, GUILayout.MinWidth(80), GUILayout.ExpandWidth(true));
            }

            var rowHeight  = EditorGUIUtility.singleLineHeight + 2f;
            var listHeight = Mathf.Min(Mathf.Max(1, visible.Count) * rowHeight + 4f, 300f);

            using (var sv = new EditorGUILayout.ScrollViewScope(_columnScroll, GUILayout.Height(listHeight)))
            {
                _columnScroll = sv.scrollPosition;
                foreach (var col in visible)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    using (new EditorGUI.DisabledScope(!col.Include))
                    {
                        var resolvedType = ResolveColumnClrType(col);
                        var error = col.TypeUnresolved;
                        var warn  = !error && (col.IsAmbiguous || GC.IsUnspecifiedType(resolvedType));
                        var style = error ? GC.ErrorStyle : warn ? GC.AmbiguousStyle : EditorStyles.label;

                        var label = error ? "✕ " + col.Name : warn ? "⚠ " + col.Name : col.Name;
                        EditorGUILayout.LabelField(new GUIContent(label, col.Name), style, GUILayout.Width(wName));

                        EditorGUILayout.LabelField(new GUIContent(col.FieldName, col.FieldName), GUILayout.Width(wField));
                        EditorGUILayout.LabelField(GC.PriorityLabel(col.Priority), GUILayout.Width(wCycle));

                        var needsDefault = string.IsNullOrWhiteSpace(col.DefaultValue);
                        if (needsDefault)
                            EditorGUILayout.LabelField(
                                new GUIContent("⚠ 필요", "모든 필드는 기본값이 필요합니다. CSV의 default 열에 지정하세요."),
                                GC.AmbiguousStyle, GUILayout.Width(wDefault));
                        else
                            EditorGUILayout.LabelField(new GUIContent(col.DefaultValue ?? "", col.DefaultValue), GUILayout.Width(wDefault));

                        using (new EditorGUI.DisabledScope(true))
                            EditorGUILayout.Toggle(col.Include, GUILayout.Width(wInclude));

                        var typeTooltip = error ? resolvedType + " — 에디터가 찾지 못한 타입. 철자가 맞다면 생성 가능."
                                        : warn  ? resolvedType + " — 타입 미지정. CSV에서 지정하세요."
                                        : resolvedType;
                        EditorGUILayout.LabelField(new GUIContent(resolvedType, typeTooltip), style, GUILayout.MinWidth(80), GUILayout.ExpandWidth(true));
                    }
                }
            }
        }

        private static void FetchColumns(SupabaseSettings settings)
        {
            _editableColumns.Clear();
            _columnsFetched = false;
            _warnings.Clear();
            _previewText = "";

            try
            {
                var url = PostgrestOpenApiUserSaveClass.BuildRestRootUrl(settings.projectUrl);
                var json = PostgrestOpenApiUserSaveClass.FetchOpenApiJson(url, GC.GetSecretKey(), settings.timeoutSeconds);

                var skip = ParseSkip(SkipColumns);
                var parsed = PostgrestOpenApiUserSaveClass.ParseTableColumns(json, "user_data", skip);
                if (!parsed.IsSuccess)
                {
                    EditorUtility.DisplayDialog(DialogTitle, parsed.ErrorMessage, "확인");
                    return;
                }

                _warnings = new List<string>(parsed.Warnings);

                if (parsed.Columns == null || parsed.Columns.Count == 0)
                {
                    EditorUtility.DisplayDialog(DialogTitle, "가져온 필드가 없습니다. 제외 목록을 확인하세요.", "확인");
                    return;
                }

                var stringIdx = Array.IndexOf(GeneratorTypeCatalog.TypeOptions, "string");
                foreach (var col in parsed.Columns)
                {
                    var isAmbiguous = col.ClrType.Contains("/*");
                    var typeIdx = isAmbiguous
                        ? GeneratorTypeCatalog.CustomTypeIndex
                        : Array.IndexOf(GeneratorTypeCatalog.TypeOptions, col.ClrType);
                    if (typeIdx < 0) typeIdx = stringIdx;

                    _editableColumns.Add(new EditableColumn
                    {
                        Name         = col.Name,
                        FieldName    = col.Name,
                        Comment      = col.Comment,
                        DefaultValue = col.DefaultValue ?? "",
                        TypeIndex    = typeIdx,
                        CustomType   = isAmbiguous ? "Dictionary<string, object>" : "",
                        IsAmbiguous  = isAmbiguous,
                        TypeCategory = isAmbiguous ? FieldTypeCategory.Json : GC.ResolveTypeCategory(col.ClrType)
                    });
                }

                var prefsTypes = LoadColumnTypesFromPrefs();
                var fileTypes  = TryLoadExistingColumnTypes();

                var existing = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in fileTypes) existing[kv.Key] = kv.Value;
                foreach (var kv in prefsTypes) existing[kv.Key] = kv.Value;

                if (existing.Count > 0)
                {
                    foreach (var col in _editableColumns)
                    {
                        if (!existing.TryGetValue(col.Name, out var existingType)) continue;
                        var idx = Array.IndexOf(GeneratorTypeCatalog.TypeOptions, existingType);
                        if (idx >= 0)
                        {
                            col.TypeIndex = idx;
                        }
                        else
                        {
                            col.TypeIndex  = GeneratorTypeCatalog.CustomTypeIndex;
                            col.CustomType = existingType;
                            if (GC.TryParseDictionaryTypes(existingType, out _, out _)
                                || GC.TryParseListType(existingType, out _)
                                || GC.TryParseArrayType(existingType, out _))
                                col.TypeCategory = FieldTypeCategory.Json;
                        }
                        col.IsAmbiguous = false;
                    }
                }

                var existingPriorities = LoadColumnPrioritiesFromPrefs();
                if (existingPriorities.Count > 0)
                    foreach (var col in _editableColumns)
                        if (existingPriorities.TryGetValue(col.Name, out var p))
                            col.Priority = p;

                var existingFieldNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in TryLoadExistingFieldNames()) existingFieldNames[kv.Key] = kv.Value;
                foreach (var kv in LoadColumnFieldNamesFromPrefs()) existingFieldNames[kv.Key] = kv.Value;
                foreach (var col in _editableColumns)
                    if (existingFieldNames.TryGetValue(col.Name, out var fn) && !string.IsNullOrWhiteSpace(fn))
                        col.FieldName = fn;

                var csvDefaults = LoadColumnDefaultsFromCsv();
                var fallbackDefaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in TryLoadExistingDefaults()) fallbackDefaults[kv.Key] = kv.Value;
                foreach (var kv in LoadColumnDefaultsFromPrefs()) fallbackDefaults[kv.Key] = kv.Value;
                foreach (var col in _editableColumns)
                {
                    if (csvDefaults.TryGetValue(col.Name, out var cv) && !string.IsNullOrWhiteSpace(cv))
                        col.DefaultValue = cv;
                    else if (string.IsNullOrWhiteSpace(col.DefaultValue)
                             && fallbackDefaults.TryGetValue(col.Name, out var fv) && !string.IsNullOrWhiteSpace(fv))
                        col.DefaultValue = fv;
                }

                ValidateColumnTypes();
                _columnsFetched = true;
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog(DialogTitle, "가져오기에 실패했습니다.\n" + e.Message, "확인");
            }
        }

        private static Dictionary<string, string> TryLoadExistingColumnTypes()
        {
            var assetPath = FindExistingClassAssetPath();
            if (assetPath == null) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try { return GeneratorTypeCatalog.ExtractAttributedFieldTypes(File.ReadAllText(assetPath, System.Text.Encoding.UTF8), "DataColumn"); }
            catch { return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); }
        }

        private static string ResolveColumnClrType(EditableColumn ec)
            => ec.TypeIndex == GeneratorTypeCatalog.CustomTypeIndex
                ? (string.IsNullOrWhiteSpace(ec.CustomType) ? "string" : ec.CustomType.Trim())
                : GeneratorTypeCatalog.TypeOptions[ec.TypeIndex];

        private static void ExportColumnsCsv()
        {
            if (_editableColumns.Count == 0)
            {
                EditorUtility.DisplayDialog(DialogTitle, "먼저 '필드 목록 가져오기'로 컬럼을 불러오세요.", "확인");
                return;
            }

            var path = EditorPrefs.GetString(PrefsKeyCsvPath, "");
            if (string.IsNullOrEmpty(path))
            {
                path = EditorUtility.SaveFilePanel("컬럼 설정 CSV 내보내기", "", "user_data_columns.csv", "csv");
                if (string.IsNullOrEmpty(path)) return;
            }

            var sb = new System.Text.StringBuilder();
            sb.Append("컬럼,필드명,타입,저장주기,기본값,포함\n");
            foreach (var c in _editableColumns)
            {
                sb.Append(GC.CsvEscape(c.Name)).Append(',')
                  .Append(GC.CsvEscape(c.FieldName)).Append(',')
                  .Append(GC.CsvEscape(ResolveColumnClrType(c))).Append(',')
                  .Append(c.Priority).Append(',')
                  .Append(GC.CsvEscape(c.DefaultValue ?? "")).Append(',')
                  .Append(c.Include ? "1" : "0").Append('\n');
            }

            try
            {
                // 한글 헤더가 엑셀에서 깨지지 않도록 BOM 포함 UTF-8로 저장
                File.WriteAllText(path, sb.ToString(), new System.Text.UTF8Encoding(true));
                EditorPrefs.SetString(PrefsKeyCsvPath, path);
                Debug.Log($"[Supabase] CSV 내보내기 완료: {_editableColumns.Count}개 컬럼 → {path}");
            }
            catch (Exception e) { EditorUtility.DisplayDialog(DialogTitle, "내보내기 실패:\n" + e.Message, "확인"); }
        }

        private static void ImportColumnsCsv()
        {
            if (_editableColumns.Count == 0)
            {
                EditorUtility.DisplayDialog(DialogTitle, "먼저 '필드 목록 가져오기'로 컬럼을 불러오세요. CSV는 컬럼명으로 매칭합니다.", "확인");
                return;
            }

            var path = EditorPrefs.GetString(PrefsKeyCsvPath, "");
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                path = EditorUtility.OpenFilePanel("컬럼 설정 CSV 불러오기", "", "csv");
                if (string.IsNullOrEmpty(path)) return;
            }
            EditorPrefs.SetString(PrefsKeyCsvPath, path);

            string[] lines;
            try { lines = File.ReadAllLines(path); }
            catch (Exception e) { EditorUtility.DisplayDialog(DialogTitle, "읽기 실패:\n" + e.Message, "확인"); return; }

            var byName = new Dictionary<string, EditableColumn>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in _editableColumns) byName[c.Name] = c;

            int applied = 0;
            var unknown = new List<string>();
            bool firstRow = true;
            bool legacyConverted = false; // 옛 형식(영문 헤더·문구 저장주기)을 만나 최신 형식으로 변환했는지

            foreach (var raw in lines)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var cells = GC.ParseCsvLine(raw);
                if (cells.Count == 0) continue;

                if (firstRow)
                {
                    firstRow = false;
                    if (GC.IsUserSaveHeaderRow(cells[0]))
                    {
                        if (GC.IsLegacyHeaderRow(cells[0])) legacyConverted = true;
                        continue;
                    }
                }

                var name = cells[0].Trim();
                if (string.IsNullOrEmpty(name)) continue;
                if (!byName.TryGetValue(name, out var col)) { unknown.Add(name); continue; }

                if (cells.Count > 1 && !string.IsNullOrWhiteSpace(cells[1])) col.FieldName = cells[1].Trim();
                if (cells.Count > 2 && !string.IsNullOrWhiteSpace(cells[2])) ApplyClrTypeToColumn(col, cells[2].Trim());
                if (cells.Count > 3 && !string.IsNullOrWhiteSpace(cells[3]))
                {
                    col.Priority = GC.ParsePriority(cells[3].Trim(), out var legacyPri);
                    if (legacyPri) legacyConverted = true;
                }
                if (cells.Count > 4) col.DefaultValue = cells[4];
                if (cells.Count > 5 && !string.IsNullOrWhiteSpace(cells[5])) col.Include = GC.ParseBool(cells[5].Trim(), col.Include);
                applied++;
            }

            ValidateColumnTypes();
            _previewText = "";

            Debug.Log($"[Supabase] CSV 불러오기 완료: {applied}개 컬럼 적용 ← {path}");
            GC.ReportImportIssues(DialogTitle, applied, unknown, CollectUnresolvedColumnNames());

            if (legacyConverted)
                EditorUtility.DisplayDialog(DialogTitle,
                    "예전 형식의 CSV를 인식해 최신 형식으로 변환해 불러왔습니다.\n" +
                    "'CSV로 저장하기'로 다시 저장하면 파일이 최신 형식(한글 헤더·숫자 저장주기)으로 갱신됩니다.",
                    "확인");
        }

        private static void PickCsvPath()
        {
            var remembered = EditorPrefs.GetString(PrefsKeyCsvPath, "");
            var dir  = string.IsNullOrEmpty(remembered) ? "" : (Path.GetDirectoryName(remembered) ?? "");
            var name = string.IsNullOrEmpty(remembered) ? "user_data_columns.csv" : Path.GetFileName(remembered);
            var path = EditorUtility.SaveFilePanel("CSV 파일 위치 지정", dir, name, "csv");
            if (string.IsNullOrEmpty(path)) return;
            EditorPrefs.SetString(PrefsKeyCsvPath, path);
            Debug.Log($"[Supabase] CSV 위치 설정: {path}");
        }

        private static void OpenColumnsCsv()
        {
            var path = EditorPrefs.GetString(PrefsKeyCsvPath, "");
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                ExportColumnsCsv();
                path = EditorPrefs.GetString(PrefsKeyCsvPath, "");
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            }
            EditorUtility.OpenWithDefaultApp(path);
        }

        private static void ApplyClrTypeToColumn(EditableColumn col, string type)
        {
            var idx = Array.IndexOf(GeneratorTypeCatalog.TypeOptions, type);
            if (idx >= 0)
            {
                col.TypeIndex    = idx;
                col.TypeCategory = GC.ResolveTypeCategory(type);
            }
            else
            {
                col.TypeIndex  = GeneratorTypeCatalog.CustomTypeIndex;
                col.CustomType = type;
                if (GC.TryParseDictionaryTypes(type, out _, out _)
                    || GC.TryParseListType(type, out _)
                    || GC.TryParseArrayType(type, out _))
                    col.TypeCategory = FieldTypeCategory.Json;
            }
            col.IsAmbiguous = false;
        }

        private static List<string> CollectUnspecifiedColumnNames()
        {
            var names = new List<string>();
            foreach (var ec in _editableColumns)
                if (ec.Include && GC.IsUnspecifiedType(ResolveColumnClrType(ec)))
                    names.Add(ec.Name);
            return names;
        }

        private static List<string> CollectDefaultMissingColumnNames()
        {
            var names = new List<string>();
            foreach (var ec in _editableColumns)
                if (ec.Include && string.IsNullOrWhiteSpace(ec.DefaultValue))
                    names.Add(ec.Name);
            return names;
        }

        private static List<string> CollectUnresolvedColumnNames()
        {
            var names = new List<string>();
            foreach (var ec in _editableColumns)
                if (ec.Include && ec.TypeUnresolved)
                    names.Add(ec.Name);
            return names;
        }

        private static void ValidateColumnTypes()
        {
            var cache = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (var ec in _editableColumns)
                ec.TypeUnresolved = ec.TypeIndex == GeneratorTypeCatalog.CustomTypeIndex
                                    && !GeneratorTypeCatalog.IsResolvableClrType(ec.CustomType, cache);
        }

        private static void BuildPreviewFromColumns()
        {
            var cols = new List<OpenApiColumn>();
            foreach (var ec in _editableColumns)
            {
                if (!ec.Include) continue;
                cols.Add(new OpenApiColumn(ec.Name, ResolveColumnClrType(ec), ec.Comment, ec.Priority, ec.FieldName, ec.DefaultValue));
            }

            if (cols.Count == 0)
            {
                EditorUtility.DisplayDialog(DialogTitle, "포함된 필드가 없습니다.", "확인");
                return;
            }

            var unspecified = CollectUnspecifiedColumnNames();
            if (unspecified.Count > 0)
            {
                EditorUtility.DisplayDialog(DialogTitle,
                    "다음 필드의 타입이 미지정 상태입니다(jsonb). CSV에서 Dictionary value 또는 리스트 요소 타입을 지정한 뒤 다시 생성하세요:\n\n• "
                    + string.Join("\n• ", unspecified), "확인");
                return;
            }

            var defaultMissing = CollectDefaultMissingColumnNames();
            if (defaultMissing.Count > 0)
            {
                EditorUtility.DisplayDialog(DialogTitle,
                    "다음 컬럼에 기본값이 없습니다. CSV의 default 열에 기본값을 지정한 뒤 다시 생성하세요:\n\n• "
                    + string.Join("\n• ", defaultMissing), "확인");
                return;
            }

            var ns = EditorSettings.projectGenerationRootNamespace?.Trim() ?? "";
            _previewText = PostgrestOpenApiUserSaveClass.GenerateSource(cols, ClassName, ns, "user_data");

            var existingPath = FindExistingClassAssetPath();
            if (existingPath != null && File.Exists(existingPath))
                _previewText = PostgrestOpenApiUserSaveClass.MergePreservedAutoDefaults(_previewText, File.ReadAllText(existingPath));

            SaveColumnTypesToPrefs();
            SaveColumnPrioritiesToPrefs();
            SaveColumnFieldNamesToPrefs();
            SaveColumnDefaultsToPrefs();
        }

        private static Dictionary<string, string> LoadColumnTypesFromPrefs()
        {
            var json = EditorPrefs.GetString(PrefsKeyColumnTypes, "");
            if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, string>();
            try { return Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(json) ?? new Dictionary<string, string>(); }
            catch { return new Dictionary<string, string>(); }
        }

        private static void SaveColumnPrioritiesToPrefs()
        {
            var dict = new Dictionary<string, int>();
            foreach (var col in _editableColumns) dict[col.Name] = col.Priority;
            EditorPrefs.SetString(PrefsKeyColumnPriorities, Newtonsoft.Json.JsonConvert.SerializeObject(dict));
        }

        private static Dictionary<string, int> LoadColumnPrioritiesFromPrefs()
        {
            var json = EditorPrefs.GetString(PrefsKeyColumnPriorities, "");
            if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, int>();
            try { return Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, int>>(json) ?? new Dictionary<string, int>(); }
            catch { return new Dictionary<string, int>(); }
        }

        private static void SaveColumnTypesToPrefs()
        {
            var dict = new Dictionary<string, string>();
            foreach (var col in _editableColumns)
            {
                var type = col.TypeIndex == GeneratorTypeCatalog.CustomTypeIndex
                    ? (string.IsNullOrWhiteSpace(col.CustomType) ? "string" : col.CustomType.Trim())
                    : GeneratorTypeCatalog.TypeOptions[col.TypeIndex];
                dict[col.Name] = type;
            }
            EditorPrefs.SetString(PrefsKeyColumnTypes, Newtonsoft.Json.JsonConvert.SerializeObject(dict));
        }

        private static void SaveColumnFieldNamesToPrefs()
        {
            var dict = new Dictionary<string, string>();
            foreach (var col in _editableColumns)
            {
                var fn = col.FieldName?.Trim();
                if (!string.IsNullOrEmpty(fn) && fn != col.Name) dict[col.Name] = fn;
            }
            EditorPrefs.SetString(PrefsKeyColumnFieldNames, Newtonsoft.Json.JsonConvert.SerializeObject(dict));
        }

        private static Dictionary<string, string> LoadColumnFieldNamesFromPrefs()
        {
            var json = EditorPrefs.GetString(PrefsKeyColumnFieldNames, "");
            if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, string>();
            try { return Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(json) ?? new Dictionary<string, string>(); }
            catch { return new Dictionary<string, string>(); }
        }

        private static void SaveColumnDefaultsToPrefs()
        {
            var dict = new Dictionary<string, string>();
            foreach (var col in _editableColumns)
            {
                var dv = col.DefaultValue?.Trim();
                if (!string.IsNullOrEmpty(dv)) dict[col.Name] = dv;
            }
            EditorPrefs.SetString(PrefsKeyColumnDefaults, Newtonsoft.Json.JsonConvert.SerializeObject(dict));
        }

        private static Dictionary<string, string> LoadColumnDefaultsFromPrefs()
        {
            var json = EditorPrefs.GetString(PrefsKeyColumnDefaults, "");
            if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, string>();
            try { return Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(json) ?? new Dictionary<string, string>(); }
            catch { return new Dictionary<string, string>(); }
        }

        private static Dictionary<string, string> LoadColumnDefaultsFromCsv()
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var path = EditorPrefs.GetString(PrefsKeyCsvPath, "");
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return result;

            string[] lines;
            try { lines = File.ReadAllLines(path); }
            catch { return result; }

            var firstRow = true;
            foreach (var raw in lines)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var cells = GC.ParseCsvLine(raw);
                if (cells.Count == 0) continue;

                if (firstRow)
                {
                    firstRow = false;
                    if (GC.IsUserSaveHeaderRow(cells[0])) continue;
                }

                var name = cells[0].Trim();
                if (string.IsNullOrEmpty(name)) continue;
                if (cells.Count > 4 && !string.IsNullOrWhiteSpace(cells[4]))
                    result[name] = cells[4];
            }
            return result;
        }

        private static Dictionary<string, string> TryLoadExistingDefaults()
        {
            var assetPath = FindExistingClassAssetPath();
            if (assetPath == null) return new Dictionary<string, string>(StringComparer.Ordinal);
            try { return PostgrestOpenApiUserSaveClass.ExtractDefaultsByColumn(File.ReadAllText(assetPath, System.Text.Encoding.UTF8)); }
            catch { return new Dictionary<string, string>(StringComparer.Ordinal); }
        }

        private static Dictionary<string, string> TryLoadExistingFieldNames()
        {
            var assetPath = FindExistingClassAssetPath();
            if (assetPath == null) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try { return GeneratorTypeCatalog.ExtractDataColumnFieldNames(File.ReadAllText(assetPath, System.Text.Encoding.UTF8)); }
            catch { return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); }
        }

        private static string FindExistingClassAssetPath()
        {
            foreach (var guid in AssetDatabase.FindAssets(ClassName))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith(ClassName + ".cs", StringComparison.OrdinalIgnoreCase))
                    return path;
            }
            return null;
        }

        private static string ResolveAutoSavePath()
        {
            var existing = FindExistingClassAssetPath();
            if (!string.IsNullOrEmpty(existing)) return existing;

            var lastDir = EditorPrefs.GetString(PrefsKeyLastSaveDir, "");
            if (!string.IsNullOrEmpty(lastDir) && AssetDatabase.IsValidFolder(lastDir))
                return lastDir.TrimEnd('/') + "/" + ClassName + ".cs";

            return null;
        }

        private static void SaveToProject() => SaveToProject(forcePicker: false);

        private static void SaveToProject(bool forcePicker)
        {
            var path = forcePicker ? null : ResolveAutoSavePath();
            if (string.IsNullOrEmpty(path))
            {
                path = EditorUtility.SaveFilePanelInProject("유저 데이터 클래스 저장", ClassName + ".cs", "cs", "");
                if (string.IsNullOrEmpty(path)) return;
            }

            try
            {
                var sourceToWrite = _previewText;
                if (File.Exists(path))
                    sourceToWrite = PostgrestOpenApiUserSaveClass.MergePreservedAutoDefaults(_previewText, File.ReadAllText(path));

                File.WriteAllText(path, sourceToWrite, new System.Text.UTF8Encoding(false));
                AssetDatabase.ImportAsset(path);
                var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                if (asset != null) EditorGUIUtility.PingObject(asset);

                var dir = Path.GetDirectoryName(path)?.Replace('\\', '/');
                if (!string.IsNullOrEmpty(dir)) EditorPrefs.SetString(PrefsKeyLastSaveDir, dir);

                SaveColumnTypesToPrefs();
                SaveColumnPrioritiesToPrefs();
                SaveColumnFieldNamesToPrefs();
                SaveColumnDefaultsToPrefs();
            }
            catch (Exception e) { EditorUtility.DisplayDialog(DialogTitle, e.Message, "확인"); }
        }

        private static HashSet<string> ParseSkip(string csv)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in csv.Split(','))
            {
                var t = p.Trim();
                if (t.Length > 0) set.Add(t);
            }
            return set;
        }

        private sealed class EditableColumn
        {
            public string            Name;
            public string            FieldName    = "";
            public string            DefaultValue = "";
            public string            Comment;
            public bool              Include      = true;
            public int               TypeIndex;
            public bool              IsAmbiguous;
            public string            CustomType   = "";
            public bool              TypeUnresolved;
            public FieldTypeCategory TypeCategory = FieldTypeCategory.Unknown;
            public int               Priority     = 1;
        }
    }
}
