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
        [Header("프로젝트 (필수)")]
        [Label("프로젝트 URL")]
        [Tooltip("Supabase 대시보드 → Project Settings → API → Project URL")]
        public string projectUrl;

        [Label("Publishable Key")]
        [Tooltip("Supabase 대시보드 → Project Settings → API → Publishable Key")]
        public string publishableKey;

        [Header("인증")]
        [Label("Google 웹 클라이언트 ID")]
        [Tooltip("Google Cloud Console → OAuth 2.0 클라이언트 ID → 웹 애플리케이션 클라이언트 ID\nAndroid 네이티브 로그인 전용. Google 로그인 미사용 시 공란.")]
        public string googleWebClientId;

        [Header("기본 옵션")]
        [Label("API 결과 로그 사용")]
        [Tooltip("Try API 결과를 콘솔에 로그합니다.")]
        public bool enableApiResultLogs = true;

        [Label("HTTP 타임아웃 (초)")]
        [Tooltip("HTTP 타임아웃(초).")]
        [Min(1)]
        public int timeoutSeconds = 30;

        [Header("Remote Config")]
        [Label("원격 설정 테이블")]
        [Tooltip("RemoteConfig 테이블.")]
        public string remoteConfigTable = "remote_config";

        [Header("우편함")]
        [Label("우편함 테이블")]
        [Tooltip("우편함 테이블 (mails).")]
        public string mailsTable = "mails";

        [Label("메일 기본 만료 일수")]
        [Tooltip("메일 만료 일수(클라이언트·발송 보조 참고).")]
        [Min(1)]
        public int defaultMailExpirationDays = 30;

        [Label("우편함 폴링 간격 (초, 0=비활성)")]
        [Tooltip("우편함 폴링 간격(초). 0이면 비활성.")]
        [Min(0)]
        public int mailPollingIntervalSeconds = 0;

        [Header("공개 프로필")]
        [Label("공개 프로필 테이블")]
        [Tooltip("공개 프로필 테이블.")]
        public string publicProfilesTable = "user_profiles";

        [Header("서버 샤드")]
        [Label("기본 서버 코드")]
        [Tooltip("기본 server_code. DB game_servers 테이블의 server_code 값과 일치해야 합니다.")]
        public string defaultServerCode = "GLOBAL";

        [Header("중복 로그인")]
        [Label("중복 로그인 감지 사용")]
        [Tooltip("다른 기기 로그인 시 이쪽 세션 해제 및 이벤트.")]
        public bool enableDuplicateSessionMonitor = true;

        [Label("세션 폴링 주기 (초, 0=1회)")]
        [Tooltip("세션 폴링 주기(초). 0이면 1회만.")]
        [Min(0f)]
        public float duplicateSessionPollSeconds = 0f;

        [Label("중복 로그인 쿨다운 (초)")]
        [Tooltip("중복 로그인 검사 쿨다운(초).")]
        [Min(0f)]
        public float duplicateSessionActionCheckCooldownSeconds = 5f;

        [Label("중복 로그인 감지 테이블")]
        [Tooltip("중복 로그인 감지 테이블.")]
        public string userSessionsTable = "user_sessions";

        [Header("탈퇴")]
        [Label("탈퇴 유예 기간 (일)")]
        [Tooltip("탈퇴 신청 후 실제 삭제까지 유예 일수.")]
        [Min(0f)]
        public float withdrawalRequestDelayDays = 7f;

        [Label("로그인 시 탈퇴 가드 실행")]
        [Tooltip("로그인 시 탈퇴 가드 Edge 호출.")]
        public bool enableWithdrawalGuardOnLogin = true;

        [Label("탈퇴 가드 함수 이름")]
        [Tooltip("탈퇴 가드 Edge 함수 이름.")]
        public string withdrawalGuardFunctionName = "withdrawal-guard";

        [Header("인앱 결제")]
        [Label("구글 플레이 영수증 검증 함수 이름")]
        [Tooltip("구글 플레이 영수증 검증 Edge 함수 이름.")]
        public string purchaseVerifyGoogleFunctionName = "purchase-verify-google";

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
                PurchaseVerifyGoogleFunctionName = string.IsNullOrWhiteSpace(purchaseVerifyGoogleFunctionName)
                    ? "purchase-verify-google"
                    : purchaseVerifyGoogleFunctionName.Trim(),
            };
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(projectUrl))
                Debug.LogWarning("[SupabaseSettings] projectUrl이 비어 있습니다.", this);
            if (string.IsNullOrWhiteSpace(publishableKey))
                Debug.LogWarning("[SupabaseSettings] publishableKey가 비어 있습니다.", this);
        }
#endif
    }
}
