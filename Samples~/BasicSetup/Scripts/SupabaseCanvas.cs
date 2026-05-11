using Truesoft.Supabase.Unity.Config;
using UnityEngine;

namespace Truesoft.Supabase.Samples.BasicSetup
{
    /// <summary>
    /// Supabase SDK 전용 Canvas 루트입니다. SDK UI(로그인, 알림 등)를 최상단에서 관리합니다.
    /// 씬에 배치하고 Canvas SortOrder를 높게 설정하세요 (예: 999).
    /// </summary>
    /// <remarks>
    /// SupabaseRuntime과 독립적으로 동작합니다.
    /// 자동 로그인 실패 시 LoginUI를 자동으로 표시합니다.
    /// </remarks>
    [RequireComponent(typeof(Canvas))]
    public class SupabaseCanvas : MonoBehaviour
    {
        [SerializeField] private SupabaseLoginUI loginUI;

        private void Awake()
        {
            loginUI.gameObject.SetActive(false);

            SupabaseRuntime.OnSessionRestored += OnSessionRestored;

            if (SupabaseRuntime.IsSessionRestoreCompleted)
                OnSessionRestored(SupabaseRuntime.SessionRestoreResult);
        }

        private void OnDisable()
        {
            SupabaseRuntime.OnSessionRestored -= OnSessionRestored;
        }

        private void OnSessionRestored(bool success)
        {
            loginUI.gameObject.SetActive(!success);
        }

        /// <summary>로그인 성공 시 SupabaseLoginUI에서 호출합니다.</summary>
        internal void OnLoginSuccess()
        {
            loginUI.gameObject.SetActive(false);
        }
    }
}
