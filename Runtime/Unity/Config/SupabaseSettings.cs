using UnityEngine;

namespace Truesoft.Supabase.Unity
{
    /// <summary>
    /// Supabase 프로젝트의 "공통 설정값"을 담는 에셋입니다.
    /// </summary>
    /// <remarks>
    /// 이 에셋은 프로젝트 전역에서 재사용되는 정적 값(서버 주소, 키, REST 테이블명, 기본 옵션)만 정의합니다.
    /// 씬 실행 정책(자동 복원, 폴링 주기 등)은 <see cref="Config.SupabaseRuntime"/>에서 제어합니다.
    /// 런타임에서는 <c>Resources/SupabaseSettings</c> 이름으로 로드되므로 경로·파일명을 맞춰야 합니다.
    /// <see cref="Supabase.TrySignInWithGoogleAsync(bool)"/> 호출 시 <see cref="googleWebClientId"/>를 읽습니다.
    /// </remarks>
    [CreateAssetMenu(fileName = "SupabaseSettings", menuName = "TrueSoft/Supabase/Supabase 설정")]
    public sealed class SupabaseSettings : ScriptableObject
    {
        [Header("공통")]
        [Tooltip("프로젝트 URL.")]
        public string projectUrl;

        [Tooltip("Publishable API 키.")]
        public string publishableKey;

        [Tooltip("Google OAuth Web Client ID. Android 네이티브 로그인.")]
        public string googleWebClientId;

        [Header("기본 옵션")]
        [Tooltip("Try API 결과를 콘솔에 로그합니다.")]
        public bool enableApiResultLogs = true;

        [Tooltip("HTTP 타임아웃(초).")]
        public int timeoutSeconds = 30;

        [Header("테이블")]
        [Tooltip("RemoteConfig 테이블.")]
        public string remoteConfigTable = "remote_config";

        [Tooltip("우편함 테이블 (mails).")]
        public string mailsTable = "mails";

        [Tooltip("메일 만료 일수(클라이언트·발송 보조 참고).")]
        public int defaultMailExpirationDays = 30;

        [Tooltip("우편함 폴링 간격(초). 0이면 비활성.")]
        public int mailPollingIntervalSeconds = 0;

        [Tooltip("공개 프로필 테이블.")]
        public string publicProfilesTable = "user_profiles";

        [Header("서버 샤드")]
        [Tooltip("기본 server_code. DB game_servers와 맞출 것.")]
        public string defaultServerCode = "GLOBAL";

        [Header("중복 로그인")]
        [Tooltip("다른 기기 로그인 시 이쪽 세션 해제 및 이벤트.")]
        public bool enableDuplicateSessionMonitor = true;

        [Tooltip("세션 폴링 주기(초). 0이면 1회만.")]
        public float duplicateSessionPollSeconds = 0f;

        [Tooltip("중복 로그인 검사 쿨다운(초).")]
        public float duplicateSessionActionCheckCooldownSeconds = 5f;

        [Tooltip("중복 로그인 감지 테이블.")]
        public string userSessionsTable = "user_sessions";

        [Header("탈퇴")]
        [Tooltip("탈퇴 유예 일수.")]
        public float withdrawalRequestDelayDays = 7f;

        [Tooltip("로그인 시 탈퇴 가드 Edge 호출.")]
        public bool enableWithdrawalGuardOnLogin = true;

        [Tooltip("탈퇴 가드 Edge 함수 이름.")]
        public string withdrawalGuardFunctionName = "withdrawal-guard";

        [Tooltip("구글 신규 가입 시 익명형 displayName 적용.")]
        public bool applyAnonymousDisplayNameOnNewGoogleSignUp = true;

        [Header("인앱 결제")]
        [Tooltip("구글 플레이 영수증 검증 Edge 함수 이름.")]
        public string purchaseVerifyGoogleFunctionName = "purchase-verify-google";

        [Tooltip("안드로이드 패키지명 (예: com.studio.mygame). 비워두면 호출 시 직접 넘겨야 합니다.")]
        public string defaultPackageName = "";

        public SupabaseOptions ToOptions()
        {
            return new SupabaseOptions
            {
                ProjectURL = projectUrl,
                PublishableKey = publishableKey,
                TimeoutSeconds = timeoutSeconds,
                RemoteConfigTable = string.IsNullOrWhiteSpace(remoteConfigTable) ? "remote_config" : remoteConfigTable.Trim(),
                MailsTable = string.IsNullOrWhiteSpace(mailsTable) ? "mails" : mailsTable.Trim(),
                DefaultMailExpirationDays = defaultMailExpirationDays < 1 ? 1 : defaultMailExpirationDays,
                MailPollingIntervalSeconds = mailPollingIntervalSeconds < 0 ? 0 : mailPollingIntervalSeconds,
                PublicProfilesTable = string.IsNullOrWhiteSpace(publicProfilesTable) ? "user_profiles" : publicProfilesTable.Trim(),
                UserSessionsTable = string.IsNullOrWhiteSpace(userSessionsTable) ? "user_sessions" : userSessionsTable.Trim(),
                DefaultServerCode = string.IsNullOrWhiteSpace(defaultServerCode) ? "GLOBAL" : defaultServerCode.Trim(),
                DuplicateSessionActionCheckCooldownSeconds = duplicateSessionActionCheckCooldownSeconds < 0f
                    ? 0f
                    : duplicateSessionActionCheckCooldownSeconds,
                WithdrawalRequestDelayDays = withdrawalRequestDelayDays < 0f
                    ? 0f
                    : withdrawalRequestDelayDays,
                EnableWithdrawalGuardOnLogin = enableWithdrawalGuardOnLogin,
                WithdrawalGuardFunctionName = string.IsNullOrWhiteSpace(withdrawalGuardFunctionName)
                    ? "withdrawal-guard"
                    : withdrawalGuardFunctionName.Trim(),
                ApplyAnonymousDisplayNameOnNewGoogleSignUp = applyAnonymousDisplayNameOnNewGoogleSignUp,
                PurchaseVerifyGoogleFunctionName = string.IsNullOrWhiteSpace(purchaseVerifyGoogleFunctionName)
                    ? "purchase-verify-google"
                    : purchaseVerifyGoogleFunctionName.Trim(),
                DefaultPackageName = defaultPackageName?.Trim() ?? "",
            };
        }
    }
}