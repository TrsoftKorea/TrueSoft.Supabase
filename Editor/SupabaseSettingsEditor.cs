using System;
using System.Collections.Generic;
using System.IO;
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
            { "bool", "int", "short", "long", "ulong", "float", "double", "string" };

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
                        normal = { textColor = new Color(1f, 0.75f, 0.1f) }
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
                "user_data 테이블 스키마를 OpenAPI로 읽어 StaticUserSave<TRow>를 상속하는 PlayerSave 클래스 초안을 생성합니다.",
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
                        if (GUILayout.Button(".cs 저장…", GUILayout.Height(26)))
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
                            GUILayout.MinWidth(100));
                        col.TypeIndex = EditorGUILayout.Popup(col.TypeIndex, s_typeOptions, GUILayout.Width(80));
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

                _columnsFetched = true;
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog(DialogTitle, "가져오기에 실패했습니다.\n" + e.Message, "확인");
            }
        }

        private static void BuildPreviewFromColumns()
        {
            var cols = new List<OpenApiColumn>();
            foreach (var ec in _editableColumns)
            {
                if (!ec.Include) continue;
                cols.Add(new OpenApiColumn(ec.Name, s_typeOptions[ec.TypeIndex], ec.Comment));
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
        }
    }
}
