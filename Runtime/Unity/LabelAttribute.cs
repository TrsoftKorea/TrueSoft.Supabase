using UnityEngine;

namespace Truesoft.Supabase.Unity
{
    /// <summary>
    /// Unity 인스펙터에서 표시되는 필드 레이블을 지정합니다.
    /// </summary>
    public sealed class LabelAttribute : PropertyAttribute
    {
        public readonly string Text;
        public LabelAttribute(string text) => Text = text;
    }
}
