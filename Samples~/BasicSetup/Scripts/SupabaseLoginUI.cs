using Truesoft.Supabase.Unity;
using UnityEngine;
using UnityEngine.UI;

namespace Truesoft.Supabase.Samples.BasicSetup
{
    /// <summary>
    /// 구글/익명 로그인 버튼을 처리하는 로그인 UI 컴포넌트입니다.
    /// SupabaseCanvas 하위에 배치하세요.
    /// </summary>
    /// <remarks>
    /// 버튼 OnClick에 각 메서드를 연결하세요:
    /// - GoogleLoginButton → OnGoogleLoginButtonClicked
    /// - AnonymousLoginButton → OnAnonymousLoginButtonClicked
    /// </remarks>
    public class SupabaseLoginUI : MonoBehaviour
    {
        [SerializeField] private SupabaseCanvas supabaseCanvas;
        [SerializeField] private Button googleLoginButton;
        [SerializeField] private Button anonymousLoginButton;
        [SerializeField] private GameObject loadingIndicator;

        public async void OnGoogleLoginButtonClicked()
        {
            SetInteractable(false);
            var ok = await Supabase.TrySignInWithGoogleAsync();
            if (ok)
                supabaseCanvas.OnLoginSuccess();
            else
                SetInteractable(true);
        }

        public async void OnAnonymousLoginButtonClicked()
        {
            SetInteractable(false);
            var ok = await Supabase.TrySignInAnonymouslyAsync();
            if (ok)
                supabaseCanvas.OnLoginSuccess();
            else
                SetInteractable(true);
        }

        private void SetInteractable(bool value)
        {
            if (googleLoginButton != null) googleLoginButton.interactable = value;
            if (anonymousLoginButton != null) anonymousLoginButton.interactable = value;
            if (loadingIndicator != null) loadingIndicator.SetActive(!value);
        }
    }
}
