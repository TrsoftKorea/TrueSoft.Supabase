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
        private const string PrefsKeyColumnFieldNames  = "TrueBase.PlayerSave.ColumnFieldNames";
        private const string PrefsKeyLastSaveDir       = "TrueBase.PlayerSave.LastSaveDir";
        private const string PrefsKeyRcClassName       = "TrueBase.RemoteConfig.ClassName";
        private const string PrefsKeyRcExtraUsings     = "TrueBase.RemoteConfig.ExtraUsings";
        private const string ClassName   = "PlayerSave";
        private const string SkipColumns = "id,user_id,account_id,server_id,updated_at";
        private const string DialogTitle = "유저 데이터 클래스";
        private const string RcDialogTitle = "Remote Config 클래스";

        // TypeOptions / GeneratorTypeCatalog.CustomTypeIndex 는 RemoteConfigClassGenerator 에서 가져옴
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
        private static string                _rcExtraUsings = "";
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

            _rcClassName    = EditorPrefs.GetString(PrefsKeyRcClassName,   "");
            _rcExtraUsings  = EditorPrefs.GetString(PrefsKeyRcExtraUsings, "");
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

                    // 미지정(정제 안 된 jsonb) 필드가 있으면 경고 + 생성 차단.
                    var unspecifiedNames = CollectUnspecifiedColumnNames();
                    if (unspecifiedNames.Count > 0)
                        EditorGUILayout.HelpBox(
                            "타입 미지정 필드: " + string.Join(", ", unspecifiedNames)
                            + "\njsonb 컬럼은 Dictionary value 또는 리스트 요소 타입을 지정해야 소스를 생성할 수 있습니다.",
                            MessageType.Warning);

                    EditorGUILayout.Space(4);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        using (new EditorGUI.DisabledScope(unspecifiedNames.Count > 0))
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
                        EditorGUILayout.LabelField(
                            "저장 위치",
                            string.IsNullOrEmpty(autoPath) ? "최초 저장 시 폴더 선택" : autoPath,
                            EditorStyles.miniLabel);
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
                    DrawRcExtraUsingsField();

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

        // Json 카테고리 전용 드롭다운 옵션 (jsonb 컬럼). string은 jsonb↔C# string 타입 불일치라 제외(text 컬럼 전용).
        // 배열(T[])도 생성 시 AutoList로 치환돼 List와 결과가 같으므로 제외 — jsonb는 Dictionary/List 중 선택.
        private static readonly string[] s_jsonTypeOptions =
            { "Dictionary<K, V>", "List<T>" };

        // Array 카테고리 전용 드롭다운 옵션 (컬렉션 종류) — RemoteConfig 필드 등에서 사용
        private static readonly string[] s_arrayTypeOptions = { "List<T>", "T[]" };

        // DataSavePriority 드롭다운 옵션 (label → int 매핑)
        // Normal=1(보통/5초), Urgent=0(짧게/1초), Lazy=2(길게/30초)
        private static readonly string[] s_priorityOptions = { "보통", "짧게", "길게" };
        private static readonly int[]    s_priorityValues  = {  1,      0,      2     };

        // Dictionary key 타입 선택지
        private static readonly string[] s_dictKeyOptions = { "string", "int" };

        // 컬렉션 요소 값 타입 12종 — AutoList/AutoDict 변환 대상(= 기본값 지정 가능 타입).
        // GeneratorTypeCatalog의 AutoList leaf 집합과 동일. 기본값을 못 박는 타입(struct·tuple·커스텀 클래스)은
        // 자동 확장 컬렉션에서 무의미하므로 노출하지 않습니다.
        private static readonly string[] s_valueTypeOptions =
        {
            "int", "long", "short", "ulong", "uint", "byte", "sbyte",
            "float", "double", "decimal", "bool", "string"
        };

        // List/배열 요소 드롭다운: 값 타입 + "2차원"(→ List<값타입>, 생성 시 AutoList2D로 치환·셀 기본값 지정 가능).
        private const string ElementTwoDimLabel = "2차원(List)";
        private static readonly string[] s_listElementOptions = AppendOne(s_valueTypeOptions, ElementTwoDimLabel);

        // Dictionary value 드롭다운: 값 타입(→ AutoDict) + "미지정"(value=object).
        // object는 정상 타입이 아니라 "아직 타입을 안 정한 jsonb" 센티넬 — 경고로 표시하고 소스 생성을 막습니다.
        private const string DictValueRawLabel = "⚠ 미지정(타입 선택)";
        private static readonly string[] s_dictValueOptions = AppendOne(s_valueTypeOptions, DictValueRawLabel);

        private static string[] AppendOne(string[] src, string tail)
        {
            var arr = new string[src.Length + 1];
            Array.Copy(src, arr, src.Length);
            arr[src.Length] = tail;
            return arr;
        }

        /// <summary>
        /// 카테고리에 속하는 타입만 표시하는 Popup을 그립니다.
        /// Json 카테고리(유저 세이브 jsonb 등)는 Dictionary / List 선택지를 제공합니다(string은 text 컬럼 전용, 배열은 List로 통일).
        /// Array 카테고리(RemoteConfig 필드 등)는 List&lt;T&gt; / T[] 두 선택지를 제공하며 요소 타입은 별도 컨트롤로 편집합니다.
        /// 현재 TypeIndex가 카테고리에 없으면 전체 목록을 표시합니다.
        /// </summary>
        private static int DrawTypePopup(int currentTypeIndex, FieldTypeCategory category, float width,
            ref string customType)
        {
            // Json 카테고리 전용 처리 (jsonb 컬럼: Dictionary / List<T>). string은 text 전용이라 제외, 배열은 List로 통일.
            if (category == FieldTypeCategory.Json)
            {
                int selIdx;
                if (currentTypeIndex == GeneratorTypeCatalog.CustomTypeIndex && TryParseListType(customType, out _))
                {
                    selIdx = 1; // List
                }
                else if (currentTypeIndex == GeneratorTypeCatalog.CustomTypeIndex && TryParseArrayType(customType, out var arrElem))
                {
                    customType = "List<" + arrElem + ">"; // 기존 배열 → List로 정규화
                    selIdx = 1;
                }
                else
                {
                    selIdx = 0; // Dictionary (미정 jsonb의 기본 — 모든 JSON 객체를 담을 수 있음)
                }

                var picked = EditorGUILayout.Popup(selIdx, s_jsonTypeOptions, GUILayout.Width(width));
                if (picked == 0)
                {
                    if (!TryParseDictionaryTypes(customType, out _, out _))
                        customType = "Dictionary<string, object>";
                    return GeneratorTypeCatalog.CustomTypeIndex;
                }
                // picked == 1: List<T> (jsonb의 리스트/배열은 모두 List로, 생성 시 AutoList로 치환됨)
                if (!TryParseListType(customType, out _))
                    customType = "List<int>";
                return GeneratorTypeCatalog.CustomTypeIndex;
            }

            // Array 카테고리 전용 처리 (RemoteConfig 필드 등). 유저 세이브 jsonb 컬럼은 Json 카테고리를 사용합니다.
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
                else if (currentTypeIndex != GeneratorTypeCatalog.CustomTypeIndex)
                {
                    // 다른 카테고리에서 Array로 처음 진입할 때 기본값 설정
                    customType = picked == 0 ? "List<int>" : "int[]";
                }
                return GeneratorTypeCatalog.CustomTypeIndex;
            }

            var allowed = GeneratorTypeCatalog.GetAllowedTypeIndices(category);

            // 기존 파일 복원 등으로 카테고리에 맞지 않는 타입이 들어있으면 전체 표시
            if (Array.IndexOf(allowed, currentTypeIndex) < 0)
                allowed = GeneratorTypeCatalog.GetAllowedTypeIndices(FieldTypeCategory.Unknown);

            var options = new string[allowed.Length];
            for (var j = 0; j < allowed.Length; j++)
                options[j] = GeneratorTypeCatalog.TypeOptions[allowed[j]];

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
                case "DateTimeOffset": case "DateTime":
                case "DateOnly": case "TimeOnly":                    return FieldTypeCategory.String;
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

        /// <summary>
        /// List&lt;T&gt;·배열 요소 타입을 선택합니다 — 값 타입 12종 또는 "2차원"(→ List&lt;값타입&gt;, 생성 시 AutoList2D).
        /// 모두 기본값 지정이 가능한 타입만 노출합니다. 인식 못 하는 기존 타입은 첫 값 타입(int)으로 정규화됩니다.
        /// </summary>
        private static string DrawListElementPopup(string current, float width)
        {
            var cur       = (current ?? string.Empty).Trim();
            var twoDimIdx = s_listElementOptions.Length - 1; // "2차원(List)"

            var isTwoDim = TryParseListType(cur, out var inner); // 현재가 List<X>면 2차원
            var selIdx   = isTwoDim ? twoDimIdx : Math.Max(0, Array.IndexOf(s_valueTypeOptions, cur));

            var picked = EditorGUILayout.Popup(selIdx, s_listElementOptions, GUILayout.Width(width));
            if (picked != twoDimIdx)
                return s_valueTypeOptions[picked]; // 값 타입(1D)

            // 2차원: 안쪽 값 타입 드롭다운 → List<값타입> 반환(AutoList2D로 치환됨)
            var innerSel = Math.Max(0, Array.IndexOf(s_valueTypeOptions, isTwoDim ? inner : "int"));
            var newInner = EditorGUILayout.Popup(innerSel, s_valueTypeOptions, GUILayout.Width(width));
            return "List<" + s_valueTypeOptions[newInner] + ">";
        }

        /// <summary>값 타입 12종 드롭다운. 인식 못 하는 값은 int로 정규화.</summary>
        private static string DrawValueTypePopup(string current, float width)
        {
            var vi     = Math.Max(0, Array.IndexOf(s_valueTypeOptions, (current ?? string.Empty).Trim()));
            var picked = EditorGUILayout.Popup(vi, s_valueTypeOptions, GUILayout.Width(width));
            return s_valueTypeOptions[picked];
        }

        /// <summary>Dictionary value 타입 드롭다운 — 값 타입(→ AutoDict) 또는 object(원시 JSON 컨테이너 유지).</summary>
        private static string DrawDictValuePopup(string current, float width)
        {
            var rawIdx = s_dictValueOptions.Length - 1; // "object(원시 JSON)"
            var vi     = Array.IndexOf(s_valueTypeOptions, (current ?? string.Empty).Trim());
            var selIdx = vi >= 0 ? vi : rawIdx; // 값 타입이 아니면(object 등) 원시 JSON
            var picked = EditorGUILayout.Popup(selIdx, s_dictValueOptions, GUILayout.Width(width));
            return picked == rawIdx ? "object" : s_valueTypeOptions[picked];
        }

        /// <summary>List&lt;T&gt; 요소 타입 선택(값 타입 / 2차원).</summary>
        private static void DrawListTypeControls(ref string customType)
        {
            TryParseListType(customType, out var elem);
            customType = "List<" + DrawListElementPopup(elem, 90) + ">";
        }

        /// <summary>T[] 요소 타입 선택(값 타입). 예: int → int[].</summary>
        private static void DrawArrayTypeControls(ref string customType)
        {
            TryParseArrayType(customType, out var elem);
            customType = DrawValueTypePopup(elem, 90) + "[]";
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

            // Value 타입 선택(값 타입 / object)
            var newValue = DrawDictValuePopup(valueType, 90);

            customType = "Dictionary<" + newKey + ", " + newValue + ">";
        }

        /// <summary>UserSave 생성기용 추가 네임스페이스 입력 영역.</summary>
        private static void DrawExtraUsingsField()
        {
            EditorGUILayout.LabelField("추가 네임스페이스  (줄 단위, using · ; 생략)", EditorStyles.miniLabel);
            EditorGUI.BeginChangeCheck();
            _extraUsings = EditorGUILayout.TextArea(_extraUsings, GUILayout.Height(40));
            if (EditorGUI.EndChangeCheck())
                EditorPrefs.SetString(PrefsKeyExtraUsings, _extraUsings);
        }

        /// <summary>Remote Config 생성기 전용 추가 네임스페이스 입력 영역.</summary>
        private static void DrawRcExtraUsingsField()
        {
            EditorGUILayout.LabelField("추가 네임스페이스  (줄 단위, using · ; 생략)", EditorStyles.miniLabel);
            EditorGUI.BeginChangeCheck();
            _rcExtraUsings = EditorGUILayout.TextArea(_rcExtraUsings, GUILayout.Height(40));
            if (EditorGUI.EndChangeCheck())
                EditorPrefs.SetString(PrefsKeyRcExtraUsings, _rcExtraUsings);
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
                                GUILayout.MinWidth(f.TypeIndex == GeneratorTypeCatalog.CustomTypeIndex ? 60 : 120));

                            f.TypeIndex = DrawTypePopup(f.TypeIndex, f.JsonCategory, 80f, ref f.CustomType);

                            if (f.TypeIndex == GeneratorTypeCatalog.CustomTypeIndex)
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

            // ③ 방어: 인덱스 유효성 확인
            if (_rcKeyList.Count == 0 || _rcKeyIndex < 0 || _rcKeyIndex >= _rcKeyList.Count) return;

            var row = _rcKeyList[_rcKeyIndex];

            // 클래스명 자동 유도 (비어있을 때만)
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

            // 기존 파일에서 타입 복원 (JsonProperty 기반)
            var existing = RemoteConfigClassGenerator.TryLoadExistingFieldTypes(_rcClassName);
            if (existing.Count > 0)
            {
                // ② 중복 JsonKey 감지: 서로 다른 depth에 같은 키가 있으면 복원 스킵 (오작동 방지)
                var keyCount = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var f in _rcFields)
                {
                    if (f.IsObjectNode) continue;
                    keyCount[f.JsonKey] = keyCount.TryGetValue(f.JsonKey, out var c) ? c + 1 : 1;
                }

                foreach (var f in _rcFields)
                {
                    if (f.IsObjectNode) continue;
                    if (keyCount.TryGetValue(f.JsonKey, out var cnt) && cnt > 1) continue; // 중복 키 스킵
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
                        if      (TryParseDictionaryTypes(et, out _, out _)) f.JsonCategory = FieldTypeCategory.Json;
                        else if (TryParseListType(et, out _) || TryParseArrayType(et, out _)) f.JsonCategory = FieldTypeCategory.Array;
                    }
                }
            }

            _rcFieldsParsed = true;
        }

        private static void BuildRcPreview()
        {
            // ③ 방어: 인덱스 유효성 확인
            if (_rcKeyList.Count == 0 || _rcKeyIndex < 0 || _rcKeyIndex >= _rcKeyList.Count) return;

            var row = _rcKeyList[_rcKeyIndex];
            var ns  = EditorSettings.projectGenerationRootNamespace?.Trim() ?? "";
            try
            {
                _rcPreviewText = RemoteConfigClassGenerator.GenerateSource(
                    _rcFields, _rcClassName, row.Key, ns,
                    ParseExtraUsings(_rcExtraUsings), row.Description);  // ① RC 전용 네임스페이스 사용
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
                EditorGUILayout.LabelField("컬럼",     EditorStyles.miniLabel, GUILayout.Width(92));
                EditorGUILayout.LabelField("필드명",   EditorStyles.miniLabel, GUILayout.Width(110));
                EditorGUILayout.LabelField("타입",     EditorStyles.miniLabel, GUILayout.Width(80));
                GUILayout.FlexibleSpace(); // 저장 주기·포함을 행과 동일하게 오른쪽 끝에 정렬
                EditorGUILayout.LabelField("저장 주기", EditorStyles.miniLabel, GUILayout.Width(58));
                EditorGUILayout.LabelField("포함",     EditorStyles.miniLabel, GUILayout.Width(30));
            }

            var rowHeight  = EditorGUIUtility.singleLineHeight + 2f;
            // 커스텀(List/Dictionary 등) 타입은 요소 선택을 다음 줄에 그리므로 그만큼 줄 수를 더해 높이를 잡는다.
            var customRows = 0;
            foreach (var c in _editableColumns)
                if (c.TypeIndex == GeneratorTypeCatalog.CustomTypeIndex) customRows++;
            var listHeight = Mathf.Min((_editableColumns.Count + customRows) * rowHeight + 4f, 260f);

            using (var sv = new EditorGUILayout.ScrollViewScope(_columnScroll, GUILayout.Height(listHeight)))
            {
                _columnScroll = sv.scrollPosition;
                foreach (var col in _editableColumns)
                {
                    // 메인 행: 컬럼·필드명·타입 + (오른쪽 정렬) 저장 주기·포함. 행마다 동일하게 정렬.
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        var warn  = col.IsAmbiguous || IsUnspecifiedType(ResolveColumnClrType(col));
                        var label = warn ? "⚠ " + col.Name : col.Name;
                        // 컬럼명: DB 식별자(읽기 전용). 전체 이름은 툴팁으로 노출.
                        EditorGUILayout.LabelField(new GUIContent(label, col.Name),
                            warn ? AmbiguousStyle : EditorStyles.label,
                            GUILayout.Width(92));
                        // 필드명: C# 식별자(편집 가능, 기본=컬럼명). 컬럼명과 다르면 생성기가 [JsonProperty]로 직렬화를 보존.
                        col.FieldName = EditorGUILayout.TextField(col.FieldName, GUILayout.Width(110));
                        col.TypeIndex = DrawTypePopup(col.TypeIndex, col.TypeCategory, 80f, ref col.CustomType);

                        // 저장 주기·포함은 항상 오른쪽 끝에 정렬(요소 컨트롤은 다음 줄로 분리됨).
                        GUILayout.FlexibleSpace();

                        var prioLabelIdx    = Array.IndexOf(s_priorityValues, col.Priority);
                        if (prioLabelIdx < 0) prioLabelIdx = 0;
                        var newPrioLabelIdx = EditorGUILayout.Popup(prioLabelIdx, s_priorityOptions, GUILayout.Width(58));
                        col.Priority = s_priorityValues[newPrioLabelIdx];

                        col.Include = EditorGUILayout.Toggle(col.Include, GUILayout.Width(20));
                    }

                    // 커스텀 타입(List·Dictionary 등)은 요소 선택을 다음 줄에 들여써서 표시 → 메인 행이 밀리지 않음.
                    if (col.TypeIndex == GeneratorTypeCatalog.CustomTypeIndex)
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            GUILayout.Space(16);
                            EditorGUILayout.LabelField("└ 요소 타입", EditorStyles.miniLabel, GUILayout.Width(64));
                            if (TryParseDictionaryTypes(col.CustomType, out _, out _))
                                DrawDictionaryTypeControls(ref col.CustomType);
                            else if (TryParseListType(col.CustomType, out _))
                                DrawListTypeControls(ref col.CustomType);
                            else if (TryParseArrayType(col.CustomType, out _))
                                DrawArrayTypeControls(ref col.CustomType);
                            else
                                EditorGUILayout.LabelField(col.CustomType);
                            GUILayout.FlexibleSpace();
                        }
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

                var stringIdx = Array.IndexOf(GeneratorTypeCatalog.TypeOptions, "string");
                foreach (var col in parsed.Columns)
                {
                    var isAmbiguous = col.ClrType.Contains("/*");
                    var typeIdx = isAmbiguous
                        ? stringIdx
                        : Array.IndexOf(GeneratorTypeCatalog.TypeOptions, col.ClrType);
                    if (typeIdx < 0) typeIdx = stringIdx;

                    _editableColumns.Add(new EditableColumn
                    {
                        Name         = col.Name,
                        FieldName    = col.Name, // 기본 필드명 = 컬럼명 (아래에서 커스텀 복원)
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
                        var idx = Array.IndexOf(GeneratorTypeCatalog.TypeOptions, existingType);
                        if (idx >= 0)
                        {
                            col.TypeIndex = idx;
                        }
                        else
                        {
                            col.TypeIndex  = GeneratorTypeCatalog.CustomTypeIndex;
                            col.CustomType = existingType;
                            // jsonb 컬럼(List·배열·Dictionary)은 항상 전체 선택지(string/Dictionary/List/T[])를
                            // 제공하도록 Json 카테고리로 둡니다. (Array로 두면 Dictionary 선택지가 사라짐)
                            if (TryParseDictionaryTypes(existingType, out _, out _)
                                || TryParseListType(existingType, out _)
                                || TryParseArrayType(existingType, out _))
                                col.TypeCategory = FieldTypeCategory.Json;
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

                // 커스텀 필드명 복원 — 기존 파일 폴백, EditorPrefs 우선
                var existingFieldNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in TryLoadExistingFieldNames())
                    existingFieldNames[kv.Key] = kv.Value;
                foreach (var kv in LoadColumnFieldNamesFromPrefs())
                    existingFieldNames[kv.Key] = kv.Value;
                foreach (var col in _editableColumns)
                    if (existingFieldNames.TryGetValue(col.Name, out var fn) && !string.IsNullOrWhiteSpace(fn))
                        col.FieldName = fn;

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
            var assetPath = FindExistingClassAssetPath();
            if (assetPath == null) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var source = File.ReadAllText(assetPath, System.Text.Encoding.UTF8);
                return GeneratorTypeCatalog.ExtractAttributedFieldTypes(source, "DataColumn");
            }
            catch
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        /// <summary>컬럼의 최종 CLR 타입을 해석합니다(커스텀이면 CustomType, 아니면 TypeOptions).</summary>
        private static string ResolveColumnClrType(EditableColumn ec)
            => ec.TypeIndex == GeneratorTypeCatalog.CustomTypeIndex
                ? (string.IsNullOrWhiteSpace(ec.CustomType) ? "string" : ec.CustomType.Trim())
                : GeneratorTypeCatalog.TypeOptions[ec.TypeIndex];

        /// <summary>
        /// 타입이 "미지정"인지 — 정제하지 않은 jsonb 상태. Dictionary value가 object이거나
        /// <c>/* refine manually */</c> 플레이스홀더가 남아 있으면 true. 이 상태로는 소스 생성을 막습니다.
        /// </summary>
        private static bool IsUnspecifiedType(string clrType)
        {
            if (string.IsNullOrWhiteSpace(clrType)) return true;
            var t = clrType.Trim();
            if (t.IndexOf("/*", StringComparison.Ordinal) >= 0) return true; // 미해결 플레이스홀더
            if (TryParseDictionaryTypes(t, out _, out var valueType)
                && string.Equals(valueType?.Trim(), "object", StringComparison.Ordinal))
                return true; // Dictionary<…, object> = value 타입 미지정
            return false;
        }

        /// <summary>포함(Include)된 컬럼 중 타입이 미지정인 것들의 이름 목록.</summary>
        private static List<string> CollectUnspecifiedColumnNames()
        {
            var names = new List<string>();
            foreach (var ec in _editableColumns)
                if (ec.Include && IsUnspecifiedType(ResolveColumnClrType(ec)))
                    names.Add(ec.Name);
            return names;
        }

        private static void BuildPreviewFromColumns()
        {
            var cols = new List<OpenApiColumn>();
            foreach (var ec in _editableColumns)
            {
                if (!ec.Include) continue;
                cols.Add(new OpenApiColumn(ec.Name, ResolveColumnClrType(ec), ec.Comment, ec.Priority, ec.FieldName));
            }

            if (cols.Count == 0)
            {
                EditorUtility.DisplayDialog(DialogTitle, "포함된 필드가 없습니다.", "확인");
                return;
            }

            // 미지정(정제 안 된 jsonb) 필드가 있으면 생성 차단 — 타입을 반드시 지정하도록 강제.
            var unspecified = CollectUnspecifiedColumnNames();
            if (unspecified.Count > 0)
            {
                EditorUtility.DisplayDialog(DialogTitle,
                    "다음 필드의 타입이 미지정 상태입니다(jsonb). Dictionary value 또는 리스트 요소 타입을 지정한 뒤 다시 생성하세요:\n\n• "
                    + string.Join("\n• ", unspecified), "확인");
                return;
            }

            var ns = EditorSettings.projectGenerationRootNamespace?.Trim() ?? "";
            _previewText = PostgrestOpenApiUserSaveClass.GenerateSource(cols, ClassName, ns, "user_data", ParseExtraUsings(_extraUsings));

            // 기존 파일에 수동으로 붙인 [AutoDefault]를 보존(생성기는 커스텀 기본값을 모름)
            var existingPath = FindExistingClassAssetPath();
            if (existingPath != null && File.Exists(existingPath))
                _previewText = PostgrestOpenApiUserSaveClass.MergePreservedAutoDefaults(_previewText, File.ReadAllText(existingPath));

            // 소스 생성 시점에 현재 타입·Priority·필드명 설정을 EditorPrefs에 저장 → 다음 "필드 목록 가져오기" 시 복원
            SaveColumnTypesToPrefs();
            SaveColumnPrioritiesToPrefs();
            SaveColumnFieldNamesToPrefs();
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
                var type = col.TypeIndex == GeneratorTypeCatalog.CustomTypeIndex
                    ? (string.IsNullOrWhiteSpace(col.CustomType) ? "string" : col.CustomType.Trim())
                    : GeneratorTypeCatalog.TypeOptions[col.TypeIndex];
                dict[col.Name] = type;
            }
            EditorPrefs.SetString(PrefsKeyColumnTypes, Newtonsoft.Json.JsonConvert.SerializeObject(dict));
        }

        /// <summary>현재 _editableColumns의 커스텀 필드명을 EditorPrefs에 저장합니다(컬럼명과 같으면 생략).</summary>
        private static void SaveColumnFieldNamesToPrefs()
        {
            var dict = new Dictionary<string, string>();
            foreach (var col in _editableColumns)
            {
                var fn = col.FieldName?.Trim();
                if (!string.IsNullOrEmpty(fn) && fn != col.Name) // 기본값(=컬럼명)은 저장 불필요
                    dict[col.Name] = fn;
            }
            EditorPrefs.SetString(PrefsKeyColumnFieldNames, Newtonsoft.Json.JsonConvert.SerializeObject(dict));
        }

        /// <summary>EditorPrefs에서 컬럼명→커스텀 필드명 매핑을 로드합니다.</summary>
        private static Dictionary<string, string> LoadColumnFieldNamesFromPrefs()
        {
            var json = EditorPrefs.GetString(PrefsKeyColumnFieldNames, "");
            if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, string>();
            try
            {
                return Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(json)
                       ?? new Dictionary<string, string>();
            }
            catch { return new Dictionary<string, string>(); }
        }

        /// <summary>기존 PlayerSave.cs에서 컬럼명→C# 필드 식별자 매핑을 읽습니다. 없으면 빈 딕셔너리.</summary>
        private static Dictionary<string, string> TryLoadExistingFieldNames()
        {
            var assetPath = FindExistingClassAssetPath();
            if (assetPath == null) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var source = File.ReadAllText(assetPath, System.Text.Encoding.UTF8);
                return GeneratorTypeCatalog.ExtractDataColumnFieldNames(source);
            }
            catch
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// 프로젝트에서 기존 <see cref="ClassName"/>.cs 의 에셋 경로(Assets/... 상대)를 찾습니다. 없으면 null.
        /// </summary>
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

        /// <summary>
        /// 저장 대상 경로를 자동 결정합니다. 우선순위:
        /// ① 프로젝트에 이미 있는 PlayerSave.cs → 같은 위치(덮어쓰기)
        /// ② 마지막으로 저장한 폴더(EditorPrefs) → 그 폴더의 PlayerSave.cs
        /// 둘 다 없으면 null(최초 1회는 폴더 선택 다이얼로그 필요).
        /// </summary>
        private static string ResolveAutoSavePath()
        {
            var existing = FindExistingClassAssetPath();
            if (!string.IsNullOrEmpty(existing)) return existing;

            var lastDir = EditorPrefs.GetString(PrefsKeyLastSaveDir, "");
            if (!string.IsNullOrEmpty(lastDir) && AssetDatabase.IsValidFolder(lastDir))
                return lastDir.TrimEnd('/') + "/" + ClassName + ".cs";

            return null;
        }

        /// <summary>저장 대상을 자동 결정해 저장합니다. 경로를 못 정하면 폴더 선택 다이얼로그로 폴백합니다.</summary>
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
                // 저장 대상 파일에 수동 추가된 [AutoDefault]가 있으면 보존(미리보기와 다른 경로로 저장하는 경우 대비)
                var sourceToWrite = _previewText;
                if (File.Exists(path))
                    sourceToWrite = PostgrestOpenApiUserSaveClass.MergePreservedAutoDefaults(_previewText, File.ReadAllText(path));

                File.WriteAllText(path, sourceToWrite, new System.Text.UTF8Encoding(false));
                AssetDatabase.ImportAsset(path);
                var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                if (asset != null) EditorGUIUtility.PingObject(asset);

                // 다음 자동 저장을 위해 저장 폴더를 기억
                var dir = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
                if (!string.IsNullOrEmpty(dir))
                    EditorPrefs.SetString(PrefsKeyLastSaveDir, dir);

                // 저장 시점에도 타입·Priority·필드명 설정을 EditorPrefs에 기록
                SaveColumnTypesToPrefs();
                SaveColumnPrioritiesToPrefs();
                SaveColumnFieldNamesToPrefs();
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
            /// <summary>C# 필드명(커스텀). 기본=컬럼명(Name). 컬럼명과 다르면 생성기가 [JsonProperty]로 직렬화 보존.</summary>
            public string            FieldName    = "";
            public string            Comment;
            public bool              Include      = true;
            public int               TypeIndex;
            public bool              IsAmbiguous;
            public string            CustomType   = "";  // TypeIndex == GeneratorTypeCatalog.CustomTypeIndex 일 때 사용
            public FieldTypeCategory TypeCategory = FieldTypeCategory.Unknown;
            /// <summary>저장 우선순위. 0=Urgent, 1=Normal(기본), 2=Lazy.</summary>
            public int               Priority     = 1;
        }

    }
}
