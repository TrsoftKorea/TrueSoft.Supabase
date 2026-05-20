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

        // ── Remote Config 클래스 생성 섹션 ──────────────────────────────────
        private const string RcDialogTitle = "Remote Config 클래스";
        private static bool _rcFoldout;
        private static List<RcKeyRow> _rcKeyList = new List<RcKeyRow>();
        private static int _rcKeyIndex;
        private static bool _rcKeysFetched;
        private static List<RcEditableField> _rcFields = new List<RcEditableField>();
        private static bool _rcFieldsParsed;
        private static string _rcClassName = "";
        private static string _rcPreviewText = "";
        private static Vector2 _rcFieldScroll;
        private static Vector2 _rcPreviewScroll;
        private static List<string> _rcWarnings = new List<string>();

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
            // DrawDefaultInspector 대신 직접 이터레이션 — publishableKey 뒤에 Secret 키 삽입
            serializedObject.Update();
            var prop = serializedObject.GetIterator();
            var enterChildren = true;
            while (prop.NextVisible(enterChildren))
            {
                enterChildren = false;
                using (new EditorGUI.DisabledScope(prop.propertyPath == "m_Script"))
                    EditorGUILayout.PropertyField(prop, true);

                if (prop.name == "publishableKey")
                {
                    DrawSecretKeyField();
                }
            }
            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);

            // ── 유저 데이터 클래스 생성 ─────────────────────────────────────
            _foldout = EditorGUILayout.Foldout(_foldout, "유저 데이터 클래스 생성", true, EditorStyles.foldoutHeader);
            if (_foldout)
            {
                EditorGUILayout.HelpBox(
                    "DB에서 유저 데이터 필드 목록을 읽어 PlayerSave 클래스를 생성합니다.",
                    MessageType.Info);

                EditorGUILayout.Space(4);
                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_secretKey)))
                {
                    if (GUILayout.Button("필드 목록 가져오기", GUILayout.Height(26)))
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

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);

            // ── Remote Config 클래스 생성 ────────────────────────────────────
            _rcFoldout = EditorGUILayout.Foldout(_rcFoldout, "Remote Config 클래스 생성", true, EditorStyles.foldoutHeader);
            if (_rcFoldout)
                DrawRemoteConfigSection((SupabaseSettings)target);
        }

        private static void DrawSecretKeyField()
        {
            EditorGUI.BeginChangeCheck();
            _secretKey = EditorGUILayout.PasswordField("Secret 키", _secretKey);
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetString(PrefsKeySecret, _secretKey);
                if (!string.IsNullOrWhiteSpace(_secretKey))
                    Debug.LogWarning("[Supabase] Secret 키가 EditorPrefs에 저장됩니다. 공유 PC 환경에서는 주의하세요.");
            }
        }

        /// <summary>
        /// 카테고리에 속하는 타입만 표시하는 Popup을 그립니다.
        /// 현재 TypeIndex가 카테고리에 없으면 전체 목록을 표시합니다.
        /// </summary>
        private static int DrawTypePopup(int currentTypeIndex, FieldTypeCategory category, float width)
        {
            var allowed = RemoteConfigClassGenerator.GetAllowedTypeIndices(category);

            // 기존 파일 복원 등으로 카테고리에 맞지 않는 타입이 들어있으면 전체 표시
            if (Array.IndexOf(allowed, currentTypeIndex) < 0)
                allowed = RemoteConfigClassGenerator.GetAllowedTypeIndices(FieldTypeCategory.Unknown);

            var options = new string[allowed.Length];
            for (var j = 0; j < allowed.Length; j++)
                options[j] = RemoteConfigClassGenerator.TypeOptions[allowed[j]];

            var selIdx    = Math.Max(0, Array.IndexOf(allowed, currentTypeIndex));
            var newSelIdx = EditorGUILayout.Popup(selIdx, options, GUILayout.Width(width));
            return allowed[newSelIdx];
        }

        /// <summary>ClrType 문자열에서 FieldTypeCategory를 결정합니다.</summary>
        private static FieldTypeCategory ResolveTypeCategory(string rawClrType)
        {
            // "/* ... */" 포함 → 수동 수정 필요 케이스, 전체 표시
            if (rawClrType?.Contains("/*") == true)
                return FieldTypeCategory.Unknown;

            switch (rawClrType?.Trim())
            {
                case "bool":                                         return FieldTypeCategory.Boolean;
                case "int": case "short": case "long": case "ulong": return FieldTypeCategory.Integer;
                case "float": case "double":                         return FieldTypeCategory.Float;
                case "string":                                       return FieldTypeCategory.String;
                default:                                             return FieldTypeCategory.Unknown;
            }
        }

        private static void DrawColumnList()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("필드", EditorStyles.miniLabel, GUILayout.MinWidth(100));
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
                        col.TypeIndex = DrawTypePopup(col.TypeIndex, col.TypeCategory, 80f);
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
                    EditorUtility.DisplayDialog(DialogTitle, "가져온 필드가 없습니다. 제외 목록을 확인하세요.", "확인");
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
                        Name         = col.Name,
                        Comment      = col.Comment,
                        TypeIndex    = typeIdx,
                        IsAmbiguous  = isAmbiguous,
                        TypeCategory = ResolveTypeCategory(col.ClrType)
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
                EditorUtility.DisplayDialog(DialogTitle, "포함된 필드가 없습니다.", "확인");
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
            public string            Name;
            public string            Comment;
            public bool              Include      = true;
            public int               TypeIndex;
            public bool              IsAmbiguous;
            public string            CustomType   = "";  // TypeIndex == CustomTypeIndex 일 때 사용
            public FieldTypeCategory TypeCategory = FieldTypeCategory.Unknown;
        }

        // ══════════════════════════════════════════════════════════════════════
        // Remote Config 클래스 생성
        // ══════════════════════════════════════════════════════════════════════

        private static void DrawRemoteConfigSection(SupabaseSettings settings)
        {
            EditorGUILayout.HelpBox(
                "DB에서 Remote Config 키 목록을 읽어 설정 클래스를 생성합니다.",
                MessageType.Info);

            EditorGUILayout.Space(4);
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_secretKey)))
            {
                if (GUILayout.Button("키 목록 가져오기", GUILayout.Height(26)))
                    RcFetchKeys(settings);
            }

            if (!_rcKeysFetched || _rcKeyList.Count == 0)
            {
                foreach (var w in _rcWarnings)
                    EditorGUILayout.HelpBox(w, MessageType.Warning);
                return;
            }

            EditorGUILayout.Space(4);

            // 키 선택 드롭다운
            var keyNames = new string[_rcKeyList.Count];
            for (var i = 0; i < _rcKeyList.Count; i++)
                keyNames[i] = string.IsNullOrEmpty(_rcKeyList[i].Description)
                    ? _rcKeyList[i].Key
                    : _rcKeyList[i].Key + "  — " + _rcKeyList[i].Description;

            var prevIndex = _rcKeyIndex;
            _rcKeyIndex = EditorGUILayout.Popup("키 선택", _rcKeyIndex, keyNames);
            if (_rcKeyIndex != prevIndex)
            {
                _rcFieldsParsed = false;
                _rcFields.Clear();
                _rcPreviewText = "";
                _rcClassName   = RcDefaultClassName(_rcKeyList[_rcKeyIndex].Key);
            }

            EditorGUILayout.Space(2);
            if (GUILayout.Button("필드 파싱", GUILayout.Height(26)))
                RcParseFields();

            if (!_rcFieldsParsed || _rcFields.Count == 0) return;

            // 필드 목록
            EditorGUILayout.Space(6);
            DrawRcFieldList();

            // 클래스명 입력
            EditorGUILayout.Space(4);
            _rcClassName = EditorGUILayout.TextField("클래스명", _rcClassName);

            EditorGUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_rcClassName)))
                {
                    if (GUILayout.Button("소스 생성", GUILayout.Height(26)))
                        RcBuildPreview();
                }

                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_rcPreviewText)))
                {
                    if (GUILayout.Button("저장", GUILayout.Height(26)))
                        RcSaveToProject();
                }
            }

            foreach (var w in _rcWarnings)
                EditorGUILayout.HelpBox(w, MessageType.Warning);

            if (!string.IsNullOrEmpty(_rcPreviewText))
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("미리보기", EditorStyles.boldLabel);
                using (var sv = new EditorGUILayout.ScrollViewScope(_rcPreviewScroll, GUILayout.Height(240)))
                {
                    _rcPreviewScroll = sv.scrollPosition;
                    var w = EditorGUIUtility.currentViewWidth - 32f;
                    var h = EditorStyles.textArea.CalcHeight(new GUIContent(_rcPreviewText), w);
                    EditorGUILayout.SelectableLabel(_rcPreviewText, EditorStyles.textArea,
                        GUILayout.Width(w), GUILayout.Height(Mathf.Max(h, 48f)));
                }
            }
        }

        private static void DrawRcFieldList()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("필드", EditorStyles.miniLabel, GUILayout.MinWidth(100));
                EditorGUILayout.LabelField("타입", EditorStyles.miniLabel, GUILayout.Width(80));
                EditorGUILayout.LabelField("포함", EditorStyles.miniLabel, GUILayout.Width(30));
            }

            var rowH    = EditorGUIUtility.singleLineHeight + 2f;
            var listH   = Mathf.Min(_rcFields.Count * rowH + 4f, 220f);
            var indentW = 12f;

            using (var sv = new EditorGUILayout.ScrollViewScope(_rcFieldScroll, GUILayout.Height(listH)))
            {
                _rcFieldScroll = sv.scrollPosition;
                foreach (var f in _rcFields)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        // 인덴트
                        if (f.Depth > 0)
                            GUILayout.Space(f.Depth * indentW);

                        if (f.IsObjectNode)
                        {
                            // 객체 노드: 클래스명 표시, 타입 변경 불가
                            var label = (f.IsAmbiguous ? "⚠ " : "") + f.JsonKey + "  →  " + f.NestedClassName;
                            EditorGUILayout.LabelField(label,
                                f.IsAmbiguous ? AmbiguousStyle : EditorStyles.label,
                                GUILayout.MinWidth(100));
                            GUILayout.FlexibleSpace();
                            f.Include = EditorGUILayout.Toggle(f.Include, GUILayout.Width(20));
                        }
                        else
                        {
                            // 일반 필드
                            var label = f.IsAmbiguous ? "⚠ " + f.JsonKey : f.JsonKey;
                            EditorGUILayout.LabelField(label,
                                f.IsAmbiguous ? AmbiguousStyle : EditorStyles.label,
                                GUILayout.MinWidth(f.TypeIndex == RemoteConfigClassGenerator.CustomTypeIndex ? 60 : 100));

                            f.TypeIndex = DrawTypePopup(f.TypeIndex, f.JsonCategory, 80f);
                            if (f.TypeIndex == RemoteConfigClassGenerator.CustomTypeIndex)
                                f.CustomType = EditorGUILayout.TextField(f.CustomType, GUILayout.ExpandWidth(true));

                            f.Include = EditorGUILayout.Toggle(f.Include, GUILayout.Width(20));
                        }
                    }
                }
            }
        }

        // ── RC actions ────────────────────────────────────────────────────────

        private static void RcFetchKeys(SupabaseSettings settings)
        {
            _rcKeyList.Clear();
            _rcKeysFetched  = false;
            _rcFieldsParsed = false;
            _rcFields.Clear();
            _rcPreviewText = "";
            _rcWarnings.Clear();

            try
            {
                _rcKeyList     = RemoteConfigClassGenerator.FetchConfigRows(settings.projectUrl, _secretKey, settings.timeoutSeconds);
                _rcKeysFetched = true;
                _rcKeyIndex    = 0;

                if (_rcKeyList.Count == 0)
                {
                    EditorUtility.DisplayDialog(RcDialogTitle, "Remote Config 키가 없습니다. 대시보드에서 먼저 키를 추가하세요.", "확인");
                    _rcKeysFetched = false;
                    return;
                }

                _rcClassName = RcDefaultClassName(_rcKeyList[0].Key);
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog(RcDialogTitle, "가져오기에 실패했습니다.\n" + e.Message, "확인");
            }
        }

        private static void RcParseFields()
        {
            _rcFields.Clear();
            _rcFieldsParsed = false;
            _rcPreviewText  = "";
            _rcWarnings.Clear();

            if (_rcKeyList.Count == 0 || _rcKeyIndex >= _rcKeyList.Count) return;

            var row = _rcKeyList[_rcKeyIndex];
            _rcFields = RemoteConfigClassGenerator.ParseJsonToFields(row.ValueJson);

            if (_rcFields.Count == 0)
            {
                EditorUtility.DisplayDialog(RcDialogTitle, "JSON 파싱에 실패하거나 필드가 없습니다.\nvalue_json이 객체 형식인지 확인하세요.", "확인");
                return;
            }

            // 기존 파일 타입 복원
            var existing = RemoteConfigClassGenerator.TryLoadExistingFieldTypes(_rcClassName);
            if (existing.Count > 0)
            {
                foreach (var f in _rcFields)
                {
                    if (f.IsObjectNode) continue;
                    if (!existing.TryGetValue(f.JsonKey, out var existType)) continue;

                    var idx = Array.IndexOf(RemoteConfigClassGenerator.TypeOptions, existType);
                    if (idx >= 0 && idx < RemoteConfigClassGenerator.CustomTypeIndex)
                    {
                        f.TypeIndex  = idx;
                        f.IsAmbiguous = false;
                    }
                    else
                    {
                        f.TypeIndex  = RemoteConfigClassGenerator.CustomTypeIndex;
                        f.CustomType = existType;
                        f.IsAmbiguous = false;
                    }
                }
            }

            _rcFieldsParsed = true;
        }

        private static void RcBuildPreview()
        {
            _rcWarnings.Clear();
            if (string.IsNullOrWhiteSpace(_rcClassName))
            {
                EditorUtility.DisplayDialog(RcDialogTitle, "클래스명을 입력하세요.", "확인");
                return;
            }

            var includedFields = new List<RcEditableField>();
            var excludedPaths  = new System.Collections.Generic.HashSet<string>();

            foreach (var f in _rcFields)
            {
                // 부모가 제외됐으면 자식도 제외
                var parentPath = f.Depth > 0 && f.FullPath.Contains(".")
                    ? f.FullPath.Substring(0, f.FullPath.LastIndexOf('.'))
                    : null;
                var parentExcluded = parentPath != null && excludedPaths.Contains(parentPath);

                if (!f.Include || parentExcluded)
                {
                    excludedPaths.Add(f.FullPath);
                    continue;
                }

                includedFields.Add(f);
            }

            if (includedFields.Count == 0)
            {
                EditorUtility.DisplayDialog(RcDialogTitle, "포함된 필드가 없습니다.", "확인");
                return;
            }

            var key          = _rcKeyList[_rcKeyIndex].Key;
            var configCls    = _rcClassName.Trim();
            var accessorCls  = RcAccessorClassName(configCls);
            var ns           = EditorSettings.projectGenerationRootNamespace?.Trim() ?? "";

            try
            {
                _rcPreviewText = RemoteConfigClassGenerator.GenerateSource(
                    includedFields, configCls, accessorCls, key, ns);
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog(RcDialogTitle, "소스 생성 실패:\n" + e.Message, "확인");
            }
        }

        private static void RcSaveToProject()
        {
            var defaultName = string.IsNullOrWhiteSpace(_rcClassName) ? "RemoteConfig" : _rcClassName.Trim();
            var path = EditorUtility.SaveFilePanelInProject("Remote Config 클래스 저장", defaultName + ".cs", "cs", "");
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

        // ── RC helpers ────────────────────────────────────────────────────────

        private static string RcDefaultClassName(string key)
        {
            // gameplay_v1 → GameplayV1Config
            var pascal = RemoteConfigClassGenerator.ToPascalCase(key);
            return pascal.EndsWith("Config", StringComparison.OrdinalIgnoreCase) ? pascal : pascal + "Config";
        }

        private static string RcAccessorClassName(string configClassName)
        {
            // GameplayV1Config → GameplayV1RemoteConfig
            var stem = configClassName.EndsWith("Config", StringComparison.OrdinalIgnoreCase)
                ? configClassName.Substring(0, configClassName.Length - "Config".Length)
                : configClassName;
            return stem + "RemoteConfig";
        }
    }
}
