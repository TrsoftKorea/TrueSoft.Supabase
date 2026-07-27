using TrueBase.Unity;
using UnityEditor;
using UnityEngine;

namespace TrueBase.Editor
{
    /// <summary>
    /// <c>SupabaseSettings</c> 커스텀 인스펙터.
    /// 기본 설정 필드에 더해 클래스 생성기가 공유하는 Secret 키 입력을 제공합니다.
    /// <para>
    /// 클래스 생성 UI는 별도 창으로 분리되어 있습니다 — <c>TrueSoft > Supabase > 클래스 생성 > …</c>.
    /// Secret 키는 여기서 입력하면 EditorPrefs에 저장되어 각 생성기 창이 공유합니다.
    /// </para>
    /// </summary>
    [CustomEditor(typeof(SupabaseSettings))]
    public sealed class SupabaseSettingsEditor : UnityEditor.Editor
    {
        private static string _secretKey = "";

        private void OnEnable()
        {
            _secretKey = GeneratorEditorCommon.GetSecretKey();
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
                    DrawSecretKeyField();
            }
            serializedObject.ApplyModifiedProperties();
        }

        private static void DrawSecretKeyField()
        {
            EditorGUI.BeginChangeCheck();
            _secretKey = EditorGUILayout.PasswordField("Secret 키", _secretKey);
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetString(GeneratorEditorCommon.PrefsKeySecret, _secretKey);
                if (!string.IsNullOrWhiteSpace(_secretKey))
                    Debug.LogWarning("[Supabase] Secret 키가 EditorPrefs에 저장됩니다. 공유 PC 환경에서는 주의하세요.");
            }
        }
    }
}
