using TrueBase.Unity;
using UnityEditor;
using UnityEngine;

namespace TrueBase.Editor
{
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
