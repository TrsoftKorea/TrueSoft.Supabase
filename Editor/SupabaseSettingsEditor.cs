using TrueBase.Unity;
using UnityEditor;

namespace TrueBase.Editor
{
    /// <summary>
    /// <c>SupabaseSettings</c> 커스텀 인스펙터.
    /// 기본 설정 필드에 더해 클래스 생성기가 공유하는 생성기 열쇠 입력을 제공합니다.
    /// <para>
    /// 클래스 생성 UI는 별도 창으로 분리되어 있습니다 — <c>TrueSoft > Supabase > 클래스 생성 > …</c>.
    /// 생성기 열쇠는 여기서 입력하면 EditorPrefs에 저장되어 각 생성기 창이 공유합니다.
    /// </para>
    /// </summary>
    [CustomEditor(typeof(SupabaseSettings))]
    public sealed class SupabaseSettingsEditor : UnityEditor.Editor
    {
        private static string _token = "";

        private void OnEnable()
        {
            _token = GeneratorEditorCommon.GetGeneratorToken();
        }

        public override void OnInspectorGUI()
        {
            // DrawDefaultInspector 대신 직접 이터레이션 — publishableKey 뒤에 생성기 열쇠 삽입
            serializedObject.Update();
            var prop = serializedObject.GetIterator();
            var enterChildren = true;
            while (prop.NextVisible(enterChildren))
            {
                enterChildren = false;
                using (new EditorGUI.DisabledScope(prop.propertyPath == "m_Script"))
                    EditorGUILayout.PropertyField(prop, true);

                if (prop.name == "publishableKey")
                    DrawGeneratorTokenField();
            }
            serializedObject.ApplyModifiedProperties();
        }

        private static void DrawGeneratorTokenField()
        {
            EditorGUI.BeginChangeCheck();
            _token = EditorGUILayout.PasswordField("생성기 열쇠", _token);
            if (EditorGUI.EndChangeCheck())
                GeneratorEditorCommon.SetGeneratorToken(_token);
        }
    }
}
