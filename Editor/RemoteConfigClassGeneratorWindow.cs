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
    /// <c>remote_config</c> 테이블의 키·JSON을 읽어 Config 클래스를 생성하는 창.
    /// 이전에는 <c>SupabaseSettings</c> 인스펙터의 접이식 UI였으나 독립 창으로 분리했습니다.
    /// </summary>
    internal sealed class RemoteConfigClassGeneratorWindow : EditorWindow
    {
        private const string RcDialogTitle    = "원격 설정 클래스";
        private const string PrefsKeyRcClassName = "TrueBase.RemoteConfig.ClassName";
        private const string PrefsKeyRcCsvPath   = "TrueBase.RemoteConfig.CsvPath";

        private static List<RcKeyRow> _rcKeyList = new List<RcKeyRow>();
        private static int _rcKeyIndex;
        private static bool _rcKeysFetched;
        private static string _rcFetchError = "";
        private static List<RcEditableField> _rcFields = new List<RcEditableField>();
        private static bool _rcFieldsParsed;
        private static string _rcClassName = "";
        private static string _rcPreviewText = "";
        private static Vector2 _rcFieldScroll;
        private static Vector2 _rcPreviewScroll;

        [MenuItem("TrueSoft/Supabase/클래스 생성/원격 설정")]
        private static void Open()
        {
            var win = GetWindow<RemoteConfigClassGeneratorWindow>(true, "원격 설정 클래스 생성", true);
            win.minSize = new Vector2(560, 480);
            win.Show();
        }

        private void OnEnable()
        {
            _rcClassName    = EditorPrefs.GetString(PrefsKeyRcClassName, "");
            _rcKeyList.Clear();
            _rcKeysFetched  = false;
            _rcFetchError   = "";
            _rcFields.Clear();
            _rcFieldsParsed = false;
            _rcPreviewText  = "";
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "remote_config 테이블에서 키·JSON을 읽어 Config 클래스를 생성합니다.",
                MessageType.Info);

            var settings = LoadSettings();
            var ready = GC.DrawConnectionSetup(settings, needsSecret: true);

            EditorGUILayout.Space(4);
            using (new EditorGUI.DisabledScope(!ready))
            {
                if (GUILayout.Button("키 목록 가져오기", GUILayout.Height(26)))
                    FetchRcKeys(settings);
            }

            GC.DrawFetchError(_rcFetchError, () => { var s = LoadSettings(); if (s != null) FetchRcKeys(s); });

            if (_rcKeysFetched)
            {
                if (_rcKeyList.Count > 0)
                {
                    EditorGUILayout.Space(4);
                    var keyNames = _rcKeyList.Select(r => r.Key).ToArray();
                    var prevIdx  = _rcKeyIndex;
                    _rcKeyIndex  = EditorGUILayout.Popup("키 선택", _rcKeyIndex, keyNames);
                    if (_rcKeyIndex != prevIdx)
                    {
                        _rcFields.Clear();
                        _rcFieldsParsed = false;
                        _rcPreviewText  = "";
                        _rcClassName    = "";
                    }

                    EditorGUILayout.Space(2);
                    if (GUILayout.Button("필드 목록 가져오기", GUILayout.Height(24)))
                        ParseRcFields();
                }
                else
                {
                    EditorGUILayout.HelpBox("remote_config 테이블에 행이 없습니다.", MessageType.Warning);
                }
            }

            if (_rcFieldsParsed && _rcFields.Count > 0)
            {
                EditorGUILayout.Space(6);
                DrawRcFieldList();

                EditorGUILayout.Space(8);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(new GUIContent("CSV로 저장하기", "현재 필드 설정을 CSV로 씁니다 → 엑셀에서 일괄 편집"), GUILayout.Height(24)))
                        EditorApplication.delayCall += ExportRcFieldsCsv;
                    if (GUILayout.Button(new GUIContent("CSV 불러오기", "편집한 CSV를 필드 경로 기준으로 설정에 반영"), GUILayout.Height(24)))
                        EditorApplication.delayCall += ImportRcFieldsCsv;
                }
                EditorGUILayout.Space(2);
                using (new EditorGUILayout.HorizontalScope())
                {
                    var rcCsvPath = EditorPrefs.GetString(PrefsKeyRcCsvPath, "");
                    EditorGUILayout.LabelField("CSV 위치",
                        string.IsNullOrEmpty(rcCsvPath) ? "미지정 (저장 시 폴더 선택)" : rcCsvPath, EditorStyles.miniLabel);
                    if (GUILayout.Button(new GUIContent("위치 변경", "CSV 파일 위치를 지정/변경합니다."), GUILayout.Height(18), GUILayout.Width(70)))
                        EditorApplication.delayCall += PickRcCsvPath;
                    if (GUILayout.Button(new GUIContent("열기", "저장된 CSV를 기본 편집기로 엽니다."), GUILayout.Height(18), GUILayout.Width(48)))
                        EditorApplication.delayCall += OpenRcCsv;
                }

                var rcUnresolved = CollectUnresolvedRcFieldPaths();
                if (rcUnresolved.Count > 0)
                    EditorGUILayout.HelpBox(
                        "에디터에서 찾지 못한 타입: " + string.Join(", ", rcUnresolved)
                        + "\n철자가 맞다면 그대로 생성해도 됩니다. 오타라면 컴파일 시 에러가 납니다.",
                        MessageType.Warning);

                EditorGUILayout.Space(4);
                EditorGUI.BeginChangeCheck();
                _rcClassName = EditorGUILayout.TextField("클래스명", _rcClassName);
                if (EditorGUI.EndChangeCheck())
                    EditorPrefs.SetString(PrefsKeyRcClassName, _rcClassName);

                EditorGUILayout.Space(4);
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_rcClassName)))
                    {
                        if (GUILayout.Button("소스 생성", GUILayout.Height(26)))
                            BuildRcPreview();
                    }
                    using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_rcPreviewText)))
                    {
                        if (GUILayout.Button("저장", GUILayout.Height(26)))
                            SaveRcToProject();
                    }
                }
            }

            if (!string.IsNullOrEmpty(_rcPreviewText))
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("미리보기", EditorStyles.boldLabel);
                using (var sv = new EditorGUILayout.ScrollViewScope(_rcPreviewScroll, GUILayout.ExpandHeight(true)))
                {
                    _rcPreviewScroll = sv.scrollPosition;
                    var w = EditorGUIUtility.currentViewWidth - 32f;
                    var h = EditorStyles.textArea.CalcHeight(new GUIContent(_rcPreviewText), w);
                    EditorGUILayout.SelectableLabel(_rcPreviewText, EditorStyles.textArea,
                        GUILayout.Width(w), GUILayout.Height(Mathf.Max(h, 48f)));
                }
            }
        }

        private static SupabaseSettings LoadSettings() => Resources.Load<SupabaseSettings>("SupabaseSettings");

        private static string ResolveRcClrType(RcEditableField f)
            => f.TypeIndex == GeneratorTypeCatalog.CustomTypeIndex
                ? (string.IsNullOrWhiteSpace(f.CustomType) ? "string" : f.CustomType.Trim())
                : GeneratorTypeCatalog.TypeOptions[f.TypeIndex];

        private static void DrawRcFieldList()
        {
            float rcField = Mathf.Max(160f, (EditorGUIUtility.currentViewWidth - 42f) * 0.45f);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("필드", EditorStyles.miniLabel, GUILayout.MinWidth(rcField));
                EditorGUILayout.LabelField("포함", EditorStyles.miniLabel, GUILayout.Width(34));
                EditorGUILayout.LabelField("타입", EditorStyles.miniLabel, GUILayout.MinWidth(80), GUILayout.ExpandWidth(true));
            }

            var rowHeight  = EditorGUIUtility.singleLineHeight + 2f;
            var listHeight = Mathf.Min(_rcFields.Count * rowHeight + 4f, 280f);

            using (var sv = new EditorGUILayout.ScrollViewScope(_rcFieldScroll, GUILayout.Height(listHeight)))
            {
                _rcFieldScroll = sv.scrollPosition;
                foreach (var f in _rcFields)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    using (new EditorGUI.DisabledScope(!f.Include))
                    {
                        if (f.Depth > 0) GUILayout.Space(f.Depth * 12f);

                        if (f.IsObjectNode)
                        {
                            var nodeLabel = f.JsonKey + " → " + f.NestedClassName;
                            EditorGUILayout.LabelField(new GUIContent(nodeLabel, f.FullPath), EditorStyles.boldLabel, GUILayout.MinWidth(rcField));
                            using (new EditorGUI.DisabledScope(true))
                                EditorGUILayout.Toggle(f.Include, GUILayout.Width(20));
                            EditorGUILayout.LabelField("중첩 객체", EditorStyles.miniLabel, GUILayout.ExpandWidth(true));
                        }
                        else
                        {
                            var resolvedType = ResolveRcClrType(f);
                            var error = f.TypeUnresolved;
                            var warn  = !error && f.IsAmbiguous;
                            var style = error ? GC.ErrorStyle : warn ? GC.AmbiguousStyle : EditorStyles.label;

                            var fieldLabel = error ? "✕ " + f.JsonKey : warn ? "⚠ " + f.JsonKey : f.JsonKey;
                            EditorGUILayout.LabelField(new GUIContent(fieldLabel, f.FullPath), style, GUILayout.MinWidth(rcField));

                            using (new EditorGUI.DisabledScope(true))
                                EditorGUILayout.Toggle(f.Include, GUILayout.Width(20));

                            var typeTooltip = error ? resolvedType + " — 에디터가 찾지 못한 타입. 철자가 맞다면 생성 가능."
                                            : warn  ? resolvedType + " — 타입 추정이 불확실합니다. CSV에서 확인하세요."
                                            : resolvedType;
                            EditorGUILayout.LabelField(new GUIContent(resolvedType, typeTooltip), style, GUILayout.ExpandWidth(true));
                        }
                    }
                }
            }
        }

        private static void FetchRcKeys(SupabaseSettings settings)
        {
            _rcKeyList.Clear();
            _rcKeysFetched  = false;
            _rcFetchError   = "";
            _rcFields.Clear();
            _rcFieldsParsed = false;
            _rcPreviewText  = "";

            try
            {
                EditorUtility.DisplayProgressBar(RcDialogTitle, "remote_config 키 목록을 가져오는 중…", 0.4f);
                _rcKeyList     = RemoteConfigClassGenerator.FetchConfigRows(settings.projectUrl, GC.GetSecretKey(), settings.timeoutSeconds);
                _rcKeyIndex    = 0;
                _rcKeysFetched = true;
            }
            catch (Exception e)
            {
                _rcFetchError = GC.DescribeFetchError(e);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private static void ParseRcFields()
        {
            _rcFields.Clear();
            _rcFieldsParsed = false;
            _rcPreviewText  = "";

            if (_rcKeyList.Count == 0 || _rcKeyIndex < 0 || _rcKeyIndex >= _rcKeyList.Count) return;

            var row = _rcKeyList[_rcKeyIndex];

            if (string.IsNullOrWhiteSpace(_rcClassName))
            {
                _rcClassName = GeneratorTypeCatalog.ToPascalCase(row.Key) + "Config";
                EditorPrefs.SetString(PrefsKeyRcClassName, _rcClassName);
            }

            _rcFields = RemoteConfigClassGenerator.ParseJsonToFields(row.ValueJson);
            if (_rcFields.Count == 0)
            {
                EditorUtility.DisplayDialog(RcDialogTitle, "파싱할 필드가 없습니다. value_json을 확인하세요.", "확인");
                return;
            }

            var existing = RemoteConfigClassGenerator.TryLoadExistingFieldTypes(_rcClassName);
            if (existing.Count > 0)
            {
                var keyCount = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var f in _rcFields)
                {
                    if (f.IsObjectNode) continue;
                    keyCount[f.JsonKey] = keyCount.TryGetValue(f.JsonKey, out var c) ? c + 1 : 1;
                }

                foreach (var f in _rcFields)
                {
                    if (f.IsObjectNode) continue;
                    if (keyCount.TryGetValue(f.JsonKey, out var cnt) && cnt > 1) continue;
                    if (!existing.TryGetValue(f.JsonKey, out var et)) continue;

                    var idx = Array.IndexOf(GeneratorTypeCatalog.TypeOptions, et);
                    if (idx >= 0)
                    {
                        f.TypeIndex   = idx;
                        f.IsAmbiguous = false;
                    }
                    else
                    {
                        f.TypeIndex   = GeneratorTypeCatalog.CustomTypeIndex;
                        f.CustomType  = et;
                        f.IsAmbiguous = false;
                        if      (GC.TryParseDictionaryTypes(et, out _, out _)) f.JsonCategory = FieldTypeCategory.Json;
                        else if (GC.TryParseListType(et, out _) || GC.TryParseArrayType(et, out _)) f.JsonCategory = FieldTypeCategory.Array;
                    }
                }
            }

            ValidateRcFieldTypes();
            _rcFieldsParsed = true;
        }

        private static void BuildRcPreview()
        {
            if (_rcKeyList.Count == 0 || _rcKeyIndex < 0 || _rcKeyIndex >= _rcKeyList.Count) return;

            var row = _rcKeyList[_rcKeyIndex];
            var ns  = EditorSettings.projectGenerationRootNamespace?.Trim() ?? "";
            try
            {
                _rcPreviewText = RemoteConfigClassGenerator.GenerateSource(_rcFields, _rcClassName, row.Key, ns, row.Description);
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog(RcDialogTitle, e.Message, "확인");
            }
        }

        private static void SaveRcToProject()
        {
            var path = EditorUtility.SaveFilePanelInProject("원격 설정 클래스 저장", _rcClassName + ".cs", "cs", "");
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                File.WriteAllText(path, _rcPreviewText, new System.Text.UTF8Encoding(false));
                AssetDatabase.ImportAsset(path);
                var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                if (asset != null) EditorGUIUtility.PingObject(asset);
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog(RcDialogTitle, e.Message, "확인");
            }
        }

        private static void ExportRcFieldsCsv()
        {
            if (_rcFields.Count == 0)
            {
                EditorUtility.DisplayDialog(RcDialogTitle, "먼저 '필드 목록 가져오기'으로 필드를 불러오세요.", "확인");
                return;
            }

            var path = EditorPrefs.GetString(PrefsKeyRcCsvPath, "");
            if (string.IsNullOrEmpty(path))
            {
                path = EditorUtility.SaveFilePanel("RC 필드 설정 CSV 내보내기", "", "remote_config_fields.csv", "csv");
                if (string.IsNullOrEmpty(path)) return;
            }

            var sb = new System.Text.StringBuilder();
            sb.Append("필드,타입,포함\n");
            foreach (var f in _rcFields)
            {
                sb.Append(GC.CsvEscape(f.FullPath)).Append(',')
                  .Append(GC.CsvEscape(f.IsObjectNode ? "(중첩 객체)" : ResolveRcClrType(f))).Append(',')
                  .Append(f.Include ? "1" : "0").Append('\n');
            }

            try
            {
                // 한글 헤더가 엑셀에서 깨지지 않도록 BOM 포함 UTF-8로 저장
                File.WriteAllText(path, sb.ToString(), new System.Text.UTF8Encoding(true));
                EditorPrefs.SetString(PrefsKeyRcCsvPath, path);
                Debug.Log($"[Supabase] RC CSV 내보내기 완료: {_rcFields.Count}개 필드 → {path}");
            }
            catch (Exception e) { EditorUtility.DisplayDialog(RcDialogTitle, "내보내기 실패:\n" + e.Message, "확인"); }
        }

        private static void ImportRcFieldsCsv()
        {
            if (_rcFields.Count == 0)
            {
                EditorUtility.DisplayDialog(RcDialogTitle, "먼저 '필드 목록 가져오기'으로 필드를 불러오세요. CSV는 필드 경로로 매칭합니다.", "확인");
                return;
            }

            var path = EditorPrefs.GetString(PrefsKeyRcCsvPath, "");
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                path = EditorUtility.OpenFilePanel("RC 필드 설정 CSV 불러오기", "", "csv");
                if (string.IsNullOrEmpty(path)) return;
            }
            EditorPrefs.SetString(PrefsKeyRcCsvPath, path);

            string[] lines;
            try { lines = File.ReadAllLines(path); }
            catch (Exception e) { EditorUtility.DisplayDialog(RcDialogTitle, "읽기 실패:\n" + e.Message, "확인"); return; }

            var byPath = new Dictionary<string, RcEditableField>(StringComparer.Ordinal);
            foreach (var f in _rcFields) byPath[f.FullPath] = f;

            int applied = 0;
            var unknown = new List<string>();
            bool firstRow = true;

            foreach (var raw in lines)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var cells = GC.ParseCsvLine(raw);
                if (cells.Count == 0) continue;

                if (firstRow)
                {
                    firstRow = false;
                    if (GC.IsRcHeaderRow(cells[0])) continue;
                }

                var fieldPath = cells[0].Trim();
                if (string.IsNullOrEmpty(fieldPath)) continue;
                if (!byPath.TryGetValue(fieldPath, out var f)) { unknown.Add(fieldPath); continue; }

                if (!f.IsObjectNode && cells.Count > 1 && !string.IsNullOrWhiteSpace(cells[1]) && cells[1].Trim() != "(중첩 객체)")
                    ApplyClrTypeToRcField(f, cells[1].Trim());
                if (cells.Count > 2 && !string.IsNullOrWhiteSpace(cells[2])) f.Include = GC.ParseBool(cells[2].Trim(), f.Include);
                applied++;
            }

            ValidateRcFieldTypes();
            _rcPreviewText = "";

            Debug.Log($"[Supabase] RC CSV 불러오기 완료: {applied}개 필드 적용 ← {path}");
            GC.ReportImportIssues(RcDialogTitle, applied, unknown, CollectUnresolvedRcFieldPaths());
        }

        private static void PickRcCsvPath()
        {
            var remembered = EditorPrefs.GetString(PrefsKeyRcCsvPath, "");
            var dir  = string.IsNullOrEmpty(remembered) ? "" : (Path.GetDirectoryName(remembered) ?? "");
            var name = string.IsNullOrEmpty(remembered) ? "remote_config_fields.csv" : Path.GetFileName(remembered);
            var path = EditorUtility.SaveFilePanel("RC CSV 파일 위치 지정", dir, name, "csv");
            if (string.IsNullOrEmpty(path)) return;
            EditorPrefs.SetString(PrefsKeyRcCsvPath, path);
            Debug.Log($"[Supabase] RC CSV 위치 설정: {path}");
        }

        private static void OpenRcCsv()
        {
            var path = EditorPrefs.GetString(PrefsKeyRcCsvPath, "");
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                ExportRcFieldsCsv();
                path = EditorPrefs.GetString(PrefsKeyRcCsvPath, "");
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            }
            EditorUtility.OpenWithDefaultApp(path);
        }

        private static void ApplyClrTypeToRcField(RcEditableField f, string type)
        {
            var idx = Array.IndexOf(GeneratorTypeCatalog.TypeOptions, type);
            if (idx >= 0)
            {
                f.TypeIndex = idx;
            }
            else
            {
                f.TypeIndex  = GeneratorTypeCatalog.CustomTypeIndex;
                f.CustomType = type;
                if (GC.TryParseDictionaryTypes(type, out _, out _)) f.JsonCategory = FieldTypeCategory.Json;
                else if (GC.TryParseListType(type, out _) || GC.TryParseArrayType(type, out _)) f.JsonCategory = FieldTypeCategory.Array;
            }
            f.IsAmbiguous = false;
        }

        private static List<string> CollectUnresolvedRcFieldPaths()
        {
            var names = new List<string>();
            foreach (var f in _rcFields)
                if (f.Include && !f.IsObjectNode && f.TypeUnresolved)
                    names.Add(f.FullPath);
            return names;
        }

        private static void ValidateRcFieldTypes()
        {
            var cache = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (var f in _rcFields)
                f.TypeUnresolved = !f.IsObjectNode
                                   && f.TypeIndex == GeneratorTypeCatalog.CustomTypeIndex
                                   && !GeneratorTypeCatalog.IsResolvableClrType(f.CustomType, cache);
        }
    }
}
