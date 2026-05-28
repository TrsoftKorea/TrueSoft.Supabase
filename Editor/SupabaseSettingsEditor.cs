using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TrueBase.Unity;
using UnityEditor;
using UnityEngine;

namespace TrueBase.Editor
{
    [CustomEditor(typeof(SupabaseSettings))]
    public sealed class SupabaseSettingsEditor : UnityEditor.Editor
    {
        private const string PrefsKeySecret            = "TrueBase.UserSaveClassGenerator.SecretKey";
        private const string PrefsKeyExtraUsings       = "TrueBase.PlayerSave.ExtraUsings";
        private const string PrefsKeyColumnTypes       = "TrueBase.PlayerSave.ColumnTypes";
        private const string PrefsKeyColumnPriorities  = "TrueBase.PlayerSave.ColumnPriorities";
        private const string PrefsKeyRcClassName       = "TrueBase.RemoteConfig.ClassName";
        private const string ClassName   = "PlayerSave";
        private const string SkipColumns = "id,user_id,account_id,server_id,updated_at";
        private const string DialogTitle = "유저 데이터 클래스";
        private const string RcDialogTitle = "Remote Config 클래스";

        // TypeOptions / RemoteConfigClassGenerator.CustomTypeIndex 는 RemoteConfigClassGenerator 에서 가져옴
        // (두 생성기가 같은 배열을 공유 → 중복 선언 제거)

        // ── UserSave 생성기 ──────────────────────────────────────────────────────
        private static bool                 _foldout;
        private static string               _secretKey       = "";
        private static string               _extraUsings     = "";
        private static List<EditableColumn> _editableColumns = new List<EditableColumn>();
        private static bool                 _columnsFetched;
        private static List<string>         _warnings        = new List<string>();
        private static Vector2              _columnScroll;
        private static string               _previewText     = "";
        private static Vector2              _previewScroll;
        private static GUIStyle             _ambiguousStyle;

        // ── Remote Config 생성기 ─────────────────────────────────────────────────
        private static bool                  _rcFoldout;
        private static List<RcKeyRow>        _rcKeyList     = new List<RcKeyRow>();
        private static int                   _rcKeyIndex;
        private static bool                  _rcKeysFetched;
        private static List<RcEditableField> _rcFields      = new List<RcEditableField>();
        private static bool                  _rcFieldsParsed;
        private static string                _rcClassName   = "";
        private static string                _rcPreviewText = "";
        private static Vector2               _rcFieldScroll;
        private static Vector2               _rcPreviewScroll;

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
            _secretKey   = EditorPrefs.GetString(PrefsKeySecret,      "");
            _extraUsings = EditorPrefs.GetString(PrefsKeyExtraUsings, "");

            // Settings 에셋이 바뀔 때 이전 워크플로 상태를 초기화
            // (static 필드는 에셋 변경 후에도 잔존하므로 OnEnable에서 명시적으로 클리어)
            _editableColumns.Clear();
            _columnsFetched = false;
            _warnings.Clear();
            _previewText    = "";

            _rcClassName    = EditorPrefs.GetString(PrefsKeyRcClassName, "");
            _rcKeyList.Clear();
            _rcKeysFetched  = false;
            _rcFields.Clear();
            _rcFieldsParsed = false;
            _rcPreviewText  = "";
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
                    DrawExtraUsingsField();

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

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);

            // ── Remote Config 클래스 생성 ────────────────────────────────────────
            _rcFoldout = EditorGUILayout.Foldout(_rcFoldout, "Remote Config 클래스 생성", true, EditorStyles.foldoutHeader);
            if (_rcFoldout)
            {
                EditorGUILayout.HelpBox(
                    "remote_config 테이블에서 키·JSON을 읽어 Config 클래스를 생성합니다.",
                    MessageType.Info);

                EditorGUILayout.Space(4);
                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_secretKey)))
                {
                    if (GUILayout.Button("키 목록 가져오기", GUILayout.Height(26)))
                        FetchRcKeys((SupabaseSettings)target);
                }

                if (_rcKeysFetched)
                {
                    if (_rcKeyList.Count > 0)
                    {
                        EditorGUILayout.Space(4);
                        var keyNames = _rcKeyList.Select(r => r.Key).ToArray();
                        var prevIdx  = _rcKeyIndex;
                        _rcKeyIndex  = EditorGUILayout.Popup("키 선택", _rcKeyIndex, keyNames);
                        // 키가 바뀌면 파싱 상태 초기화
                        if (_rcKeyIndex != prevIdx)
                        {
                            _rcFields.Clear();
                            _rcFieldsParsed = false;
                            _rcPreviewText  = "";
                            _rcClassName    = "";
                        }

                        EditorGUILayout.Space(2);
                        if (GUILayout.Button("필드 파싱", GUILayout.Height(24)))
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

                    EditorGUILayout.Space(4);
                    EditorGUI.BeginChangeCheck();
                    _rcClassName = EditorGUILayout.TextField("클래스명", _rcClassName);
                    if (EditorGUI.EndChangeCheck())
                        EditorPrefs.SetString(PrefsKeyRcClassName, _rcClassName);

                    EditorGUILayout.Space(4);
                    DrawExtraUsingsField();

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
                    using (var sv = new EditorGUILayout.ScrollViewScope(_rcPreviewScroll, GUILayout.Height(220)))
                    {
                        _rcPreviewScroll = sv.scrollPosition;
                        var w = EditorGUIUtility.currentViewWidth - 32f;
                        var h = EditorStyles.textArea.CalcHeight(new GUIContent(_rcPreviewText), w);
                        EditorGUILayout.SelectableLabel(_rcPreviewText, EditorStyles.textArea,
                            GUILayout.Width(w), GUILayout.Height(Mathf.Max(h, 48f)));
                    }
                }
            }

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

        // Json 카테고리 전용 드롭다운 옵션 (jsonb 컬럼: 어떤 JSON 값이든 가능)
        private static readonly string[] s_jsonTypeOptions =
            { "string", "Dictionary<K, V>", "List<T>", "T[]" };

        // Array 카테고리 전용 드롭다운 옵션 (컬렉션 종류)
        private static readonly string[] s_arrayTypeOptions = { "List<T>", "T[]" };

        // DataSavePriority 드롭다운 옵션 (label → int 매핑)
        // Normal=1(보통/5초), Urgent=0(짧게/1초), Lazy=2(길게/30초)
        private static readonly string[] s_priorityOptions = { "보통", "짧게", "길게" };
        private static readonly int[]    s_priorityValues  = {  1,      0,      2     };

        // Dictionary key 타입 선택지 (value 타입은 자유 텍스트)
        private static readonly string[] s_dictKeyOptions = { "string", "int" };

        /// <summary>
        /// 카테고리에 속하는 타입만 표시하는 Popup을 그립니다.
        /// Json 카테고리는 string / Dictionary 두 선택지를 제공합니다.
        /// Array 카테고리는 List&lt;T&gt; / T[] 두 선택지를 제공하며 요소 타입은 별도 컨트롤로 편집합니다.
        /// 현재 TypeIndex가 카테고리에 없으면 전체 목록을 표시합니다.
        /// </summary>
        private static int DrawTypePopup(int currentTypeIndex, FieldTypeCategory category, float width,
            ref string customType)
        {
            // Json 카테고리 전용 처리 (jsonb 컬럼: string / Dictionary / List<T> / T[])
            if (category == FieldTypeCategory.Json)
            {
                int selIdx;
                if (currentTypeIndex == RemoteConfigClassGenerator.CustomTypeIndex)
                {
                    if      (TryParseDictionaryTypes(customType, out _, out _)) selIdx = 1;
                    else if (TryParseListType(customType, out _))                selIdx = 2;
                    else if (TryParseArrayType(customType, out _))               selIdx = 3;
                    else                                                          selIdx = 0;
                }
                else selIdx = 0; // string

                var picked = EditorGUILayout.Popup(selIdx, s_jsonTypeOptions, GUILayout.Width(width));
                if (picked == 0) { customType = ""; return 7; }  // string
                if (picked == 1)
                {
                    if (!TryParseDictionaryTypes(customType, out _, out _))
                        customType = "Dictionary<string, object>";
                    return RemoteConfigClassGenerator.CustomTypeIndex;
                }
                if (picked == 2)
                {
                    if (!TryParseListType(customType, out _))
                        customType = "List<int>";
                    return RemoteConfigClassGenerator.CustomTypeIndex;
                }
                // picked == 3: T[]
                if (!TryParseArrayType(customType, out _))
                    customType = "int[]";
                return RemoteConfigClassGenerator.CustomTypeIndex;
            }

            // Array 카테고리 전용 처리
            if (category == FieldTypeCategory.Array)
            {
                var isList  = TryParseListType(customType, out _);
                var isArray = !isList && TryParseArrayType(customType, out _);

                // 둘 다 아니면 List<T>를 기본으로
                var selIdx = (isArray) ? 1 : 0;
                var picked = EditorGUILayout.Popup(selIdx, s_arrayTypeOptions, GUILayout.Width(width));

                if (picked == 0 && !isList)
                {
                    // T[] → List<T> 전환: 요소 타입 유지
                    TryParseArrayType(customType, out var elem);
                    customType = "List<" + (string.IsNullOrEmpty(elem) ? "int" : elem) + ">";
                }
                else if (picked == 1 && !isArray)
                {
                    // List<T> → T[] 전환: 요소 타입 유지
                    TryParseListType(customType, out var elem);
                    customType = (string.IsNullOrEmpty(elem) ? "int" : elem) + "[]";
                }
                else if (currentTypeIndex != RemoteConfigClassGenerator.CustomTypeIndex)
                {
                    // 다른 카테고리에서 Array로 처음 진입할 때 기본값 설정
                    customType = picked == 0 ? "List<int>" : "int[]";
                }
                return RemoteConfigClassGenerator.CustomTypeIndex;
            }

            var allowed = RemoteConfigClassGenerator.GetAllowedTypeIndices(category);

            // 기존 파일 복원 등으로 카테고리에 맞지 않는 타입이 들어있으면 전체 표시
            if (Array.IndexOf(allowed, currentTypeIndex) < 0)
                allowed = RemoteConfigClassGenerator.GetAllowedTypeIndices(FieldTypeCategory.Unknown);

            var options = new string[allowed.Length];
            for (var j = 0; j < allowed.Length; j++)
                options[j] = RemoteConfigClassGenerator.TypeOptions[allowed[j]];

            var selIdx2    = Math.Max(0, Array.IndexOf(allowed, currentTypeIndex));
            var newSelIdx2 = EditorGUILayout.Popup(selIdx2, options, GUILayout.Width(width));
            return allowed[newSelIdx2];
        }

        /// <summary>명확한 ClrType 문자열에서 FieldTypeCategory를 결정합니다 (isAmbiguous=false인 경우만 호출).</summary>
        private static FieldTypeCategory ResolveTypeCategory(string rawClrType)
        {
            switch (rawClrType?.Trim())
            {
                case "bool":                                         return FieldTypeCategory.Boolean;
                case "int": case "short": case "long": case "ulong": return FieldTypeCategory.Integer;
                case "float": case "double":                         return FieldTypeCategory.Float;
                case "string":                                       return FieldTypeCategory.String;
                default:                                             return FieldTypeCategory.Unknown;
            }
        }

        /// <summary>
        /// customType이 Dictionary&lt;K, V&gt; 형식이면 K와 V를 파싱합니다.
        /// 중첩 제네릭(e.g. Dictionary&lt;string, List&lt;int&gt;&gt;)도 처리합니다.
        /// </summary>
        private static bool TryParseDictionaryTypes(string customType, out string keyType, out string valueType)
        {
            keyType   = "string";
            valueType = "object";
            if (string.IsNullOrWhiteSpace(customType)) return false;

            var s = customType.Trim();
            if (!s.StartsWith("Dictionary<", StringComparison.Ordinal) ||
                !s.EndsWith(">", StringComparison.Ordinal))
                return false;

            var inner = s.Substring("Dictionary<".Length, s.Length - "Dictionary<".Length - 1);
            var depth = 0;
            for (var i = 0; i < inner.Length; i++)
            {
                if (inner[i] == '<')      depth++;
                else if (inner[i] == '>') depth--;
                else if (inner[i] == ',' && depth == 0)
                {
                    keyType   = inner.Substring(0, i).Trim();
                    valueType = inner.Substring(i + 1).Trim();
                    return true;
                }
            }
            return false;
        }

        /// <summary>customType이 List&lt;T&gt; 형식이면 요소 타입을 파싱합니다.</summary>
        private static bool TryParseListType(string customType, out string elementType)
        {
            elementType = "int";
            if (string.IsNullOrWhiteSpace(customType)) return false;
            var s = customType.Trim();
            if (!s.StartsWith("List<", StringComparison.Ordinal) || !s.EndsWith(">", StringComparison.Ordinal))
                return false;
            elementType = s.Substring(5, s.Length - 6).Trim();
            return !string.IsNullOrEmpty(elementType);
        }

        /// <summary>customType이 T[] 형식이면 요소 타입을 파싱합니다.</summary>
        private static bool TryParseArrayType(string customType, out string elementType)
        {
            elementType = "int";
            if (string.IsNullOrWhiteSpace(customType)) return false;
            var s = customType.Trim();
            if (!s.EndsWith("[]", StringComparison.Ordinal)) return false;
            elementType = s.Substring(0, s.Length - 2).Trim();
            return !string.IsNullOrEmpty(elementType);
        }

        /// <summary>List&lt;T&gt; 요소 타입 선택 드롭다운.</summary>
        private static void DrawListTypeControls(ref string customType)
        {
            TryParseListType(customType, out var elem);
            var idx    = Math.Max(0, Array.IndexOf(RemoteConfigClassGenerator.TypeOptions, elem));
            var newIdx = EditorGUILayout.Popup(idx, RemoteConfigClassGenerator.TypeOptions, GUILayout.ExpandWidth(true));
            customType = "List<" + RemoteConfigClassGenerator.TypeOptions[newIdx] + ">";
        }

        /// <summary>T[] 요소 타입 선택 드롭다운.</summary>
        private static void DrawArrayTypeControls(ref string customType)
        {
            TryParseArrayType(customType, out var elem);
            var idx    = Math.Max(0, Array.IndexOf(RemoteConfigClassGenerator.TypeOptions, elem));
            var newIdx = EditorGUILayout.Popup(idx, RemoteConfigClassGenerator.TypeOptions, GUILayout.ExpandWidth(true));
            customType = RemoteConfigClassGenerator.TypeOptions[newIdx] + "[]";
        }

        /// <summary>Dictionary 타입의 key / value 타입을 인라인으로 편집합니다.</summary>
        private static void DrawDictionaryTypeControls(ref string customType)
        {
            TryParseDictionaryTypes(customType, out var keyType, out var valueType);

            // Key 타입 popup (string / int)
            var keyIdx    = Math.Max(0, Array.IndexOf(s_dictKeyOptions, keyType));
            var newKeyIdx = EditorGUILayout.Popup(keyIdx, s_dictKeyOptions, GUILayout.Width(50));
            var newKey    = s_dictKeyOptions[newKeyIdx];

            EditorGUILayout.LabelField(",", GUILayout.Width(8));

            // Value 타입 자유 텍스트
            var newValue = EditorGUILayout.TextField(valueType, GUILayout.ExpandWidth(true));
            if (string.IsNullOrWhiteSpace(newValue)) newValue = "object";

            customType = "Dictionary<" + newKey + ", " + newValue + ">";
        }

        /// <summary>두 생성기가 공유하는 추가 네임스페이스 입력 영역.</summary>
        private static void DrawExtraUsingsField()
        {
            EditorGUILayout.LabelField("추가 네임스페이스  (줄 단위, using · ; 생략)", EditorStyles.miniLabel);
            EditorGUI.BeginChangeCheck();
            _extraUsings = EditorGUILayout.TextArea(_extraUsings, GUILayout.Height(40));
            if (EditorGUI.EndChangeCheck())
                EditorPrefs.SetString(PrefsKeyExtraUsings, _extraUsings);
        }

        /// <summary>추가 네임스페이스 텍스트 → 정규화된 네임스페이스 목록.</summary>
        private static List<string> ParseExtraUsings(string raw)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(raw)) return result;
            foreach (var line in raw.Split('\n'))
            {
                var ns = line.Trim().TrimEnd(';');
                if (ns.StartsWith("using ", StringComparison.Ordinal))
                    ns = ns.Substring("using ".Length).Trim();
                if (!string.IsNullOrWhiteSpace(ns))
                    result.Add(ns);
            }
            return result;
        }

        // ── Remote Config 생성기 메서드 ──────────────────────────────────────────

        private static void DrawRcFieldList()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("필드", EditorStyles.miniLabel, GUILayout.MinWidth(120));
                EditorGUILayout.LabelField("타입", EditorStyles.miniLabel, GUILayout.Width(80));
                EditorGUILayout.LabelField("포함", EditorStyles.miniLabel, GUILayout.Width(30));
            }

            var rowHeight  = EditorGUIUtility.singleLineHeight + 2f;
            var listHeight = Mathf.Min(_rcFields.Count * rowHeight + 4f, 220f);

            using (var sv = new EditorGUILayout.ScrollViewScope(_rcFieldScroll, GUILayout.Height(listHeight)))
            {
                _rcFieldScroll = sv.scrollPosition;
                foreach (var f in _rcFields)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        // 깊이만큼 들여쓰기
                        if (f.Depth > 0) GUILayout.Space(f.Depth * 12f);

                        if (f.IsObjectNode)
                        {
                            // 중첩 객체 헤더 행 — 타입 팝업 없음
                            var nodeLabel = f.JsonKey + " → " + f.NestedClassName;
                            EditorGUILayout.LabelField(nodeLabel, EditorStyles.boldLabel, GUILayout.ExpandWidth(true));
                            f.Include = EditorGUILayout.Toggle(f.Include, GUILayout.Width(20));
                        }
                        else
                        {
                            var fieldLabel = f.IsAmbiguous ? "⚠ " + f.JsonKey : f.JsonKey;
                            EditorGUILayout.LabelField(fieldLabel,
                                f.IsAmbiguous ? AmbiguousStyle : EditorStyles.label,
                                GUILayout.MinWidth(f.TypeIndex == RemoteConfigClassGenerator.CustomTypeIndex ? 60 : 120));

                            f.TypeIndex = DrawTypePopup(f.TypeIndex, f.JsonCategory, 80f, ref f.CustomType);

                            if (f.TypeIndex == RemoteConfigClassGenerator.CustomTypeIndex)
                            {
                                if      (TryParseDictionaryTypes(f.CustomType, out _, out _)) DrawDictionaryTypeControls(ref f.CustomType);
                                else if (TryParseListType(f.CustomType, out _))                DrawListTypeControls(ref f.CustomType);
                                else if (TryParseArrayType(f.CustomType, out _))               DrawArrayTypeControls(ref f.CustomType);
                                else EditorGUILayout.LabelField(f.CustomType, GUILayout.ExpandWidth(true));
                            }

                            f.Include = EditorGUILayout.Toggle(f.Include, GUILayout.Width(20));
                        }
                    }
                }
            }
        }

        private static void FetchRcKeys(SupabaseSettings settings)
        {
            _rcKeyList.Clear();
            _rcKeysFetched  = false;
            _rcFields.Clear();
            _rcFieldsParsed = false;
            _rcPreviewText  = "";

            try
            {
                _rcKeyList     = RemoteConfigClassGenerator.FetchConfigRows(settings.projectUrl, _secretKey, settings.timeoutSeconds);
                _rcKeyIndex    = 0;
                _rcKeysFetched = true;
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog(RcDialogTitle, "가져오기에 실패했습니다.\n" + e.Message, "확인");
            }
        }

        private static void ParseRcFields()
        {
            _rcFields.Clear();
            _rcFieldsParsed = false;
            _rcPreviewText  = "";

            var row = _rcKeyList[_rcKeyIndex];

            // 클래스명 자동 유도 (비어있을 때만)
            if (string.IsNullOrWhiteSpace(_rcClassName))
            {
                _rcClassName = RemoteConfigClassGenerator.ToPascalCase(row.Key) + "Config";
                EditorPrefs.SetString(PrefsKeyRcClassName, _rcClassName);
            }

            _rcFields = RemoteConfigClassGenerator.ParseJsonToFields(row.ValueJson);
            if (_rcFields.Count == 0)
            {
                EditorUtility.DisplayDialog(RcDialogTitle, "파싱할 필드가 없습니다. value_json을 확인하세요.", "확인");
                return;
            }

            // 기존 파일에서 타입 복원 (JsonProperty 기반)
            var existing = RemoteConfigClassGenerator.TryLoadExistingFieldTypes(_rcClassName);
            if (existing.Count > 0)
            {
                foreach (var f in _rcFields)
                {
                    if (f.IsObjectNode) continue;
                    if (!existing.TryGetValue(f.JsonKey, out var et)) continue;

                    var idx = Array.IndexOf(RemoteConfigClassGenerator.TypeOptions, et);
                    if (idx >= 0)
                    {
                        f.TypeIndex    = idx;
                        f.IsAmbiguous  = false;
                    }
                    else
                    {
                        f.TypeIndex    = RemoteConfigClassGenerator.CustomTypeIndex;
                        f.CustomType   = et;
                        f.IsAmbiguous  = false;
                        if      (TryParseDictionaryTypes(et, out _, out _)) f.JsonCategory = FieldTypeCategory.Json;
                        else if (TryParseListType(et, out _) || TryParseArrayType(et, out _)) f.JsonCategory = FieldTypeCategory.Array;
                    }
                }
            }

            _rcFieldsParsed = true;
        }

        private static void BuildRcPreview()
        {
            var row = _rcKeyList[_rcKeyIndex];
            var ns  = EditorSettings.projectGenerationRootNamespace?.Trim() ?? "";
            try
            {
                _rcPreviewText = RemoteConfigClassGenerator.GenerateSource(
                    _rcFields, _rcClassName, row.Key, ns,
                    ParseExtraUsings(_extraUsings), row.Description);
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog(RcDialogTitle, e.Message, "확인");
            }
        }

        private static void SaveRcToProject()
        {
            var path = EditorUtility.SaveFilePanelInProject("Remote Config 클래스 저장", _rcClassName + ".cs", "cs", "");
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

        // ── UserSave 생성기 메서드 ────────────────────────────────────────────────

        private static void DrawColumnList()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("필드",     EditorStyles.miniLabel, GUILayout.MinWidth(100));
                EditorGUILayout.LabelField("타입",     EditorStyles.miniLabel, GUILayout.Width(80));
                EditorGUILayout.LabelField("저장 주기", EditorStyles.miniLabel, GUILayout.Width(58));
                EditorGUILayout.LabelField("포함",     EditorStyles.miniLabel, GUILayout.Width(30));
            }

            var rowHeight  = EditorGUIUtility.singleLineHeight + 2f;
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
                            GUILayout.MinWidth(col.TypeIndex == RemoteConfigClassGenerator.CustomTypeIndex ? 60 : 100));
                        col.TypeIndex = DrawTypePopup(col.TypeIndex, col.TypeCategory, 80f, ref col.CustomType);
                        if (col.TypeIndex == RemoteConfigClassGenerator.CustomTypeIndex)
                        {
                            if (TryParseDictionaryTypes(col.CustomType, out _, out _))
                                DrawDictionaryTypeControls(ref col.CustomType);
                            else if (TryParseListType(col.CustomType, out _))
                                DrawListTypeControls(ref col.CustomType);
                            else if (TryParseArrayType(col.CustomType, out _))
                                DrawArrayTypeControls(ref col.CustomType);
                            else
                                EditorGUILayout.LabelField(col.CustomType, GUILayout.ExpandWidth(true));
                        }

                        // Priority 드롭다운
                        var prioLabelIdx    = Array.IndexOf(s_priorityValues, col.Priority);
                        if (prioLabelIdx < 0) prioLabelIdx = 0;
                        var newPrioLabelIdx = EditorGUILayout.Popup(prioLabelIdx, s_priorityOptions, GUILayout.Width(58));
                        col.Priority = s_priorityValues[newPrioLabelIdx];

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

                var stringIdx = Array.IndexOf(RemoteConfigClassGenerator.TypeOptions, "string");
                foreach (var col in parsed.Columns)
                {
                    var isAmbiguous = col.ClrType.Contains("/*");
                    var typeIdx = isAmbiguous
                        ? stringIdx
                        : Array.IndexOf(RemoteConfigClassGenerator.TypeOptions, col.ClrType);
                    if (typeIdx < 0) typeIdx = stringIdx;

                    _editableColumns.Add(new EditableColumn
                    {
                        Name         = col.Name,
                        Comment      = col.Comment,
                        TypeIndex    = typeIdx,
                        IsAmbiguous  = isAmbiguous,
                        // isAmbiguous(/* 포함) = 복잡한 타입(jsonb, array, $ref 등) → Json 카테고리 (Dictionary 프리셋 포함)
                        TypeCategory = isAmbiguous ? FieldTypeCategory.Json : ResolveTypeCategory(col.ClrType)
                    });
                }

                // EditorPrefs 우선, 파일 파싱 폴백으로 기존 타입 복원
                var prefsTypes = LoadColumnTypesFromPrefs();
                var fileTypes  = TryLoadExistingColumnTypes();

                // 두 소스 병합 (EditorPrefs가 더 최신이므로 덮어씀)
                var existing = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in fileTypes)
                    existing[kv.Key] = kv.Value;
                foreach (var kv in prefsTypes)
                    existing[kv.Key] = kv.Value;

                if (existing.Count > 0)
                {
                    foreach (var col in _editableColumns)
                    {
                        if (!existing.TryGetValue(col.Name, out var existingType)) continue;
                        var idx = Array.IndexOf(RemoteConfigClassGenerator.TypeOptions, existingType);
                        if (idx >= 0)
                        {
                            col.TypeIndex = idx;
                        }
                        else
                        {
                            col.TypeIndex  = RemoteConfigClassGenerator.CustomTypeIndex;
                            col.CustomType = existingType;
                            // 타입 패턴에 따라 전용 팝업이 열리도록 카테고리 설정
                            if (TryParseDictionaryTypes(existingType, out _, out _))
                                col.TypeCategory = FieldTypeCategory.Json;
                            else if (TryParseListType(existingType, out _) || TryParseArrayType(existingType, out _))
                                col.TypeCategory = FieldTypeCategory.Array;
                        }
                        col.IsAmbiguous = false;
                    }
                }

                // EditorPrefs에서 Priority 설정 복원
                var existingPriorities = LoadColumnPrioritiesFromPrefs();
                if (existingPriorities.Count > 0)
                {
                    foreach (var col in _editableColumns)
                    {
                        if (existingPriorities.TryGetValue(col.Name, out var p))
                            col.Priority = p;
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
                    @"\[DataColumn(?:\(""([^""]*)""\))?\]\s+public\s+(.+?)\s+@?(\w+)\s*;",
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
                var clrType = ec.TypeIndex == RemoteConfigClassGenerator.CustomTypeIndex
                    ? (string.IsNullOrWhiteSpace(ec.CustomType) ? "string" : ec.CustomType.Trim())
                    : RemoteConfigClassGenerator.TypeOptions[ec.TypeIndex];
                cols.Add(new OpenApiColumn(ec.Name, clrType, ec.Comment, ec.Priority));
            }

            if (cols.Count == 0)
            {
                EditorUtility.DisplayDialog(DialogTitle, "포함된 필드가 없습니다.", "확인");
                return;
            }

            var ns = EditorSettings.projectGenerationRootNamespace?.Trim() ?? "";
            _previewText = PostgrestOpenApiUserSaveClass.GenerateSource(cols, ClassName, ns, "user_data", ParseExtraUsings(_extraUsings));

            // 소스 생성 시점에 현재 타입·Priority 설정을 EditorPrefs에 저장 → 다음 "필드 목록 가져오기" 시 복원
            SaveColumnTypesToPrefs();
            SaveColumnPrioritiesToPrefs();
        }

        /// <summary>EditorPrefs에서 컬럼명→타입 매핑을 로드합니다.</summary>
        private static Dictionary<string, string> LoadColumnTypesFromPrefs()
        {
            var json = EditorPrefs.GetString(PrefsKeyColumnTypes, "");
            if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, string>();
            try
            {
                return Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(json)
                       ?? new Dictionary<string, string>();
            }
            catch
            {
                return new Dictionary<string, string>();
            }
        }

        /// <summary>현재 _editableColumns의 Priority 설정을 EditorPrefs에 직렬화해 저장합니다.</summary>
        private static void SaveColumnPrioritiesToPrefs()
        {
            var dict = new Dictionary<string, int>();
            foreach (var col in _editableColumns)
                dict[col.Name] = col.Priority;
            EditorPrefs.SetString(PrefsKeyColumnPriorities, Newtonsoft.Json.JsonConvert.SerializeObject(dict));
        }

        /// <summary>EditorPrefs에서 컬럼명→Priority 매핑을 로드합니다.</summary>
        private static Dictionary<string, int> LoadColumnPrioritiesFromPrefs()
        {
            var json = EditorPrefs.GetString(PrefsKeyColumnPriorities, "");
            if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, int>();
            try
            {
                return Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, int>>(json)
                       ?? new Dictionary<string, int>();
            }
            catch { return new Dictionary<string, int>(); }
        }

        /// <summary>현재 _editableColumns의 타입 설정을 EditorPrefs에 직렬화해 저장합니다.</summary>
        private static void SaveColumnTypesToPrefs()
        {
            var dict = new Dictionary<string, string>();
            foreach (var col in _editableColumns)
            {
                var type = col.TypeIndex == RemoteConfigClassGenerator.CustomTypeIndex
                    ? (string.IsNullOrWhiteSpace(col.CustomType) ? "string" : col.CustomType.Trim())
                    : RemoteConfigClassGenerator.TypeOptions[col.TypeIndex];
                dict[col.Name] = type;
            }
            EditorPrefs.SetString(PrefsKeyColumnTypes, Newtonsoft.Json.JsonConvert.SerializeObject(dict));
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

                // 저장 시점에도 타입·Priority 설정을 EditorPrefs에 기록
                SaveColumnTypesToPrefs();
                SaveColumnPrioritiesToPrefs();
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
            public string            CustomType   = "";  // TypeIndex == RemoteConfigClassGenerator.CustomTypeIndex 일 때 사용
            public FieldTypeCategory TypeCategory = FieldTypeCategory.Unknown;
            /// <summary>저장 우선순위. 0=Urgent, 1=Normal(기본), 2=Lazy.</summary>
            public int               Priority     = 1;
        }

    }
}
