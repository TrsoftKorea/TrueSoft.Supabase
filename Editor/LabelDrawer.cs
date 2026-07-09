using TrueBase.Unity;
using UnityEditor;
using UnityEngine;

namespace TrueBase.Editor
{
    /// <summary><c>[Label("...")]</c>이 붙은 직렬화 필드의 인스펙터 라벨을 지정한 텍스트로 바꿔 그립니다.</summary>
    [CustomPropertyDrawer(typeof(LabelAttribute))]
    internal sealed class LabelDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            label.text = ((LabelAttribute)attribute).Text;
            EditorGUI.PropertyField(position, property, label, true);
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUI.GetPropertyHeight(property, label, true);
        }
    }
}
