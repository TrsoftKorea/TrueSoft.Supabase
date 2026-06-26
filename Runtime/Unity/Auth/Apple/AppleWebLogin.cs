using System;
using System.Threading.Tasks;
using UnityEngine;

namespace TrueBase.Unity.Auth.Apple
{
    /// <summary>
    /// Android 등 네이티브 Sign in with Apple을 쓸 수 없는 플랫폼에서, Supabase 호스팅 OAuth(브라우저) +
    /// 딥링크로 Apple 로그인을 수행하는 헬퍼입니다.
    ///
    /// 흐름: 시스템 브라우저로 Supabase authorize URL을 열고 → Apple 로그인 →
    /// Supabase가 앱 딥링크(<c>{scheme}://login-callback</c>)로 세션을 붙여 돌려보내면 →
    /// <see cref="Supabase.TryCompleteOAuthRedirectAsync"/>로 로그인을 완료합니다.
    ///
    /// 사전 준비: Supabase 대시보드 Apple provider 설정, Redirect URL 허용목록에 딥링크 등록,
    /// AndroidManifest에 해당 스킴의 deep link intent-filter 추가.
    /// </summary>
    public static class AppleWebLogin
    {
        private static TaskCompletionSource<SupabaseCallResult> _pending;
        private static string _expectedScheme;

        /// <summary>
        /// 브라우저로 Apple 로그인을 띄우고, 딥링크로 돌아온 세션으로 로그인을 완료합니다.
        /// </summary>
        /// <param name="redirectScheme">앱 딥링크 스킴(예: <c>mygame</c>). AndroidManifest에 등록된 값과 같아야 합니다.</param>
        /// <param name="redirectHost">딥링크 호스트(기본값: <c>login-callback</c>). Supabase Redirect URL 허용목록과 일치해야 합니다.</param>
        public static Task<SupabaseCallResult> TrySignInAsync(string redirectScheme, string redirectHost = "login-callback")
        {
            if (string.IsNullOrWhiteSpace(redirectScheme))
                return Task.FromResult(SupabaseCallResult.Fail(SupabaseFailReason.OAuthRedirectSchemeEmpty));

            // PlayNANOO 연동 중에는 브라우저 흐름이 PlayNANOO를 태우지 못합니다(Apple id_token이 앱에 없음).
            // 데이터 분기를 막기 위해 조용히 진행하지 않고 실패시킵니다. PlayNANOO WebView 토큰을 TrySignInWithAppleIdTokenAsync에 전달하세요.
            if (SupabaseSDK.IsPlayNanooAppleInterceptionActive)
            {
                Debug.LogError("[Supabase.Auth.Apple] PlayNANOO 연동 중에는 브라우저 기반 Apple 로그인을 쓸 수 없습니다. " +
                               "PlayNANOO WebView로 받은 Apple id_token을 Supabase.TrySignInWithAppleIdTokenAsync에 전달하세요.");
                return Task.FromResult(SupabaseCallResult.Fail(SupabaseFailReason.PlayNanooBrowserAppleUnsupported));
            }

            // 진행 중이던 흐름이 있으면(사용자가 브라우저에서 취소 후 재시도 등) 교체합니다.
            if (_pending != null && !_pending.Task.IsCompleted)
            {
                Application.deepLinkActivated -= OnDeepLink;
                _pending.TrySetResult(SupabaseCallResult.Fail(SupabaseFailReason.OAuthLoginInProgress));
            }

            _expectedScheme = redirectScheme.Trim();
            _pending = new TaskCompletionSource<SupabaseCallResult>(TaskCreationOptions.RunContinuationsAsynchronously);

            var redirectTo = $"{_expectedScheme}://{redirectHost}";
            var url = Supabase.BuildOAuthAuthorizeUrl("apple", redirectTo);

            Application.deepLinkActivated += OnDeepLink;
            Application.OpenURL(url);

            return _pending.Task;
        }

        private static async void OnDeepLink(string url)
        {
            if (string.IsNullOrEmpty(url))
                return;

            if (!string.IsNullOrEmpty(_expectedScheme) &&
                !url.StartsWith(_expectedScheme + "://", StringComparison.OrdinalIgnoreCase))
                return;

            Application.deepLinkActivated -= OnDeepLink;
            var tcs = _pending;
            _pending = null;
            if (tcs == null)
                return;

            var result = await Supabase.TryCompleteOAuthRedirectAsync(url);
            tcs.TrySetResult(result);
        }
    }
}
