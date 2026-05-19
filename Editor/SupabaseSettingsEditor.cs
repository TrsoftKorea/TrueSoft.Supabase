using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Truesoft.Supabase.Unity;
using UnityEditor;
using UnityEngine;

namespace Truesoft.Supabase.Editor
{
    [CustomEditor(typeof(SupabaseSettings))]
    public sealed class SupabaseSettingsEditor : UnityEditor.Editor
    {
        private const string PrefsKeySecret = "Truesoft.Supabase.UserSaveClassGenerator.SecretKey";
        private const string ClassName = "PlayerSave";
        private const string SkipColumns = "id,user_id,account_id,server_id,updated_at";
        private const string DialogTitle = "유저 데이터 클래스";

        private static readonly string[] s_typeOptions =
            { "bool", "int", "short", "long", "ulong", "float", "double", "string", "커스텀..." };
        private const int CustomTypeIndex = 8;

        private static bool _foldout;
        private static string _secretKey = "";
        private static List<EditableColumn> _editableColumns = new List<EditableColumn>();
        private static bool _columnsFetched;
        private static List<string> _warnings = new List<string>();
        private static Vector2 _columnScroll;
        private static string _previewText = "";
        private static Vector2 _previewScroll;
        private static GUIStyle _ambiguousStyle;

        private static GUIStyle AmbiguousStyle
        {
            get
            {
                if (_ambiguousStyle == null)
                    _ambiguousStyle = new GUIStyle(EditorStyles.label)
                    {
                        normal =
                        {
                            textColor = EditorGUIUtility.isProSkin
                                ? new Color(1f, 0.75f, 0.1f)   // 다크 테마: 밝은 노란색
                                : new Color(0.65f, 0.35f, 0f)  // 라이트 테마: 진한 황갈색
                        }
                    };
                return _ambiguousStyle;
            }
        }

        private void OnEnable()
        {
            _secretKey = EditorPrefs.GetString(PrefsKeySecret, "");
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);

            _foldout = EditorGUILayout.Foldout(_foldout, "유저 데이터 클래스 생성", true, EditorStyles.foldoutHeader);
            if (!_foldout) return;

            EditorGUILayout.HelpBox(
                "게임 데이터 스키마를 OpenAPI로 읽어 PlayerSave 클래스를 생성합니다.",
                MessageType.Info);

            EditorGUI.BeginChangeCheck();
            _secretKey = EditorGUILayout.PasswordField("Secret 키", _secretKey);
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetString(PrefsKeySecret, _secretKey);
                if (!string.IsNullOrWhiteSpace(_secretKey))
                    Debug.LogWarning("[Supabase] Secret 키가 EditorPrefs에 저장됩니다. 공유 PC 환경에서는 주의하세요.");
            }

            EditorGUILayout.Space(4);
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_secretKey)))
            {
                if (GUILayout.Button("스키마 가져오기", GUILayout.Height(26)))
                    FetchColumns((SupabaseSettings)target);
            }

            if (_columnsFetched && _editableColumns.Count > 0)
            {
                EditorGUILayout.Space(6);
                DrawColumnList();

                EditorGUILayout.Space(4);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("소스 생성", GUILayout.Height(26)))
                        BuildPreviewFromColumns();

                    using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_previewText)))
                    {
                        if (GUILayout.Button("저장", GUILayout.Height(26)))
                            SaveToProject();
                    }
                }
            }

            foreach (var w in _warnings)
                EditorGUILayout.HelpBox(w, MessageType.Warning);

            if (!string.IsNullOrEmpty(_previewText))
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("미리보기", EditorStyles.boldLabel);
                using (var sv = new EditorGUILayout.ScrollViewScope(_previewScroll, GUILayout.Height(220)))
                {
                    _previewScroll = sv.scrollPosition;
                    var w = EditorGUIUtility.currentViewWidth - 32f;
                    var h = EditorStyles.textArea.CalcHeight(new GUIContent(_previewText), w);
                    EditorGUILayout.SelectableLabel(_previewText, EditorStyles.textArea,
                        GUILayout.Width(w), GUILayout.Height(Mathf.Max(h, 48f)));
                }
            }
        }

        private static void DrawColumnList()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("컬럼", EditorStyles.miniLabel, GUILayout.MinWidth(100));
                EditorGUILayout.LabelField("타입", EditorStyles.miniLabel, GUILayout.Width(80));
                EditorGUILayout.LabelField("포함", EditorStyles.miniLabel, GUILayout.Width(30));
            }

            var rowHeight = EditorGUIUtility.singleLineHeight + 2f;
            var listHeight = Mathf.Min(_editableColumns.Count * rowHeight + 4f, 200f);

            using (var sv = new EditorGUILayout.ScrollViewScope(_columnScroll, GUILayout.Height(listHeight)))
            {
                _columnScroll = sv.scrollPosition;
                foreach (var col in _editableColumns)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        var label = col.IsAmbiguous ? "⚠ " + col.Name : col.Name;
                        EditorGUILayout.LabelField(label,
                            col.IsAmbiguous ? AmbiguousStyle : EditorStyles.label,
                            GUILayout.MinWidth(col.TypeIndex == CustomTypeIndex ? 60 : 100));
                        col.TypeIndex = EditorGUILayout.Popup(col.TypeIndex, s_typeOptions, GUILayout.Width(80));
                        if (col.TypeIndex == CustomTypeIndex)
                            col.CustomType = EditorGUILayout.TextField(col.CustomType, GUILayout.ExpandWidth(true));
                        col.Include = EditorGUILayout.Toggle(col.Include, GUILayout.Width(20));
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
                var json = PostgrestOpenApiUserSaveClass.FetchOpenApiJson(url, _secretKey, settings.timeoutSeconds);

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
                    EditorUtility.DisplayDialog(DialogTitle, "가져온 컬럼이 없습니다. 제외 목록·스키마를 확인하세요.", "확인");
                    return;
                }

                var stringIdx = Array.IndexOf(s_typeOptions, "string");
                foreach (var col in parsed.Columns)
                {
                    var isAmbiguous = col.ClrType.Contains("/*");
                    var typeIdx = isAmbiguous
                        ? stringIdx
                        : Array.IndexOf(s_typeOptions, col.ClrType);
                    if (typeIdx < 0) typeIdx = stringIdx;

                    _editableColumns.Add(new EditableColumn
                    {
                        Name = col.Name,
                        Comment = col.Comment,
                        TypeIndex = typeIdx,
                        IsAmbiguous = isAmbiguous
                    });
                }

                // 기존 PlayerSave.cs가 있으면 타입 덮어쓰기
                var existing = TryLoadExistingColumnTypes();
                if (existing.Count > 0)
                {
                    foreach (var col in _editableColumns)
                    {
                        if (!existing.TryGetValue(col.Name, out var existingType)) continue;
                        var idx = Array.IndexOf(s_typeOptions, existingType);
                        if (idx >= 0)
                        {
                            col.TypeIndex = idx;
                        }
                        else
                        {
                            col.TypeIndex = CustomTypeIndex;
                            col.CustomType = existingType;
                        }
                        col.IsAmbiguous = false;
                    }
                }

                _columnsFetched = true;
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog(DialogTitle, "가져오기에 실패했습니다.\n" + e.Message, "확인");
            }
        }

        /// <summary>
        /// 프로젝트에서 PlayerSave.cs를 찾아 [DataColumn] 필드의 컬럼명→타입 매핑을 반환합니다.
        /// 파일이 없거나 파싱 실패 시 빈 딕셔너리를 반환합니다.
        /// </summary>
        private static Dictionary<string, string> TryLoadExistingColumnTypes()
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            string assetPath = null;
            foreach (var guid in AssetDatabase.FindAssets(ClassName))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith(ClassName + ".cs", StringComparison.OrdinalIgnoreCase))
                {
                    assetPath = path;
                    break;
                }
            }

            if (assetPath == null) return result;

            try
            {
                var source = File.ReadAllText(assetPath, System.Text.Encoding.UTF8);
                // [DataColumn("col_name")] public TYPE field;
                // [DataColumn] public TYPE field;
                var pattern = new Regex(
                    @"\[DataColumn(?:\(""([^""]*)""\))?\]\s+public\s+(.+?)\s+(\w+)\s*;",
                    RegexOptions.Multiline);

                foreach (Match m in pattern.Matches(source))
                {
                    var colName = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[3].Value;
                    var typeName = m.Groups[2].Value.Trim();
                    if (!string.IsNullOrEmpty(colName) && !string.IsNullOrEmpty(typeName))
                        result[colName] = typeName;
                }
            }
            catch
            {
                // 파싱 실패 시 무시
            }

            return result;
        }

        private static void BuildPreviewFromColumns()
        {
            var cols = new List<OpenApiColumn>();
            foreach (var ec in _editableColumns)
            {
                if (!ec.Include) continue;
                var clrType = ec.TypeIndex == CustomTypeIndex
                    ? (string.IsNullOrWhiteSpace(ec.CustomType) ? "string" : ec.CustomType.Trim())
                    : s_typeOptions[ec.TypeIndex];
                cols.Add(new OpenApiColumn(ec.Name, clrType, ec.Comment));
            }

            if (cols.Count == 0)
            {
                EditorUtility.DisplayDialog(DialogTitle, "포함된 컬럼이 없습니다.", "확인");
                return;
            }

            var ns = EditorSettings.projectGenerationRootNamespace?.Trim() ?? "";
            _previewText = PostgrestOpenApiUserSaveClass.GenerateSource(cols, ClassName, ns, "user_data");
        }

        private static void SaveToProject()
        {
            var path = EditorUtility.SaveFilePanelInProject("유저 데이터 클래스 저장", ClassName + ".cs", "cs", "");
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                File.WriteAllText(path, _previewText, new System.Text.UTF8Encoding(false));
                AssetDatabase.ImportAsset(path);
                var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                if (asset != null) EditorGUIUtility.PingObject(asset);
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog(DialogTitle, e.Message, "확인");
            }
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
            public string Name;
            public string Comment;
            public bool Include = true;
            public int TypeIndex;
            public bool IsAmbiguous;
            public string CustomType = "";  // TypeIndex == CustomTypeIndex 일 때 사용
        }
    }
}
