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

        private static bool _foldout;
        private static string _secretKey = "";
        private static string _previewText = "";
        private static Vector2 _scroll;
        private static List<string> _warnings = new List<string>();

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
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_secretKey)))
                {
                    if (GUILayout.Button("미리보기", GUILayout.Height(26)))
                        FetchPreview((SupabaseSettings)target);
                }

                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_previewText)))
                {
                    if (GUILayout.Button(".cs 저장…", GUILayout.Height(26)))
                        SaveToProject();
                }
            }

            foreach (var w in _warnings)
                EditorGUILayout.HelpBox(w, MessageType.Warning);

            if (!string.IsNullOrEmpty(_previewText))
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("미리보기", EditorStyles.boldLabel);
                using (var sv = new EditorGUILayout.ScrollViewScope(_scroll, GUILayout.Height(220)))
                {
                    _scroll = sv.scrollPosition;
                    var w = EditorGUIUtility.currentViewWidth - 32f;
                    var h = EditorStyles.textArea.CalcHeight(new GUIContent(_previewText), w);
                    EditorGUILayout.SelectableLabel(_previewText, EditorStyles.textArea,
                        GUILayout.Width(w), GUILayout.Height(Mathf.Max(h, 48f)));
                }
            }
        }

        private static void FetchPreview(SupabaseSettings settings)
        {
            _warnings.Clear();
            _previewText = "";

            try
            {
                var url = PostgrestOpenApiUserSaveClass.BuildRestRootUrl(settings.projectUrl);
                var json = PostgrestOpenApiUserSaveClass.FetchOpenApiJson(url, _secretKey, settings.timeoutSeconds);
                BuildPreview(json);
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog(DialogTitle, "가져오기에 실패했습니다.\n" + e.Message, "확인");
            }
        }

        private static void BuildPreview(string openApiJson)
        {
            var skip = ParseSkip(SkipColumns);
            var parsed = PostgrestOpenApiUserSaveClass.ParseTableColumns(openApiJson, "user_data", skip);
            if (!parsed.IsSuccess)
            {
                EditorUtility.DisplayDialog(DialogTitle, parsed.ErrorMessage, "확인");
                return;
            }

            _warnings = new List<string>(parsed.Warnings);

            if (parsed.Columns == null || parsed.Columns.Count == 0)
            {
                EditorUtility.DisplayDialog(DialogTitle, "생성할 컬럼이 없습니다.", "확인");
                return;
            }

            var ns = EditorSettings.projectGenerationRootNamespace?.Trim() ?? "";
            _previewText = PostgrestOpenApiUserSaveClass.GenerateSource(parsed.Columns, ClassName, ns, "user_data");
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
    }
}
