using UnityEngine;

namespace TrueBase.Unity
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
        [Label("Project URL")]
        [Tooltip("Supabase 대시보드 → Project Settings → API")]
        public string projectUrl;

        [Label("Publishable Key")]
        [Tooltip("Supabase 대시보드 → Project Settings → API")]
        public string publishableKey;

        [Header("인증")]
        [Label("Google 웹 클라이언트 ID")]
        [Tooltip("Google Cloud Console의 웹 애플리케이션 OAuth 클라이언트 ID. Google 로그인 미사용 시 공란.")]
        public string googleWebClientId;

        [Header("기본 옵션")]
        [Label("API 결과 로그 사용")]
        [Tooltip("Try API 호출 결과를 콘솔에 출력합니다.")]
        public bool enableApiResultLogs = true;

        [Label("HTTP 타임아웃")]
        [Tooltip("서버 요청 타임아웃(초).")]
        [Min(1)]
        public int timeoutSeconds = 30;

        [Header("유저 세이브 저장 주기")]
        [Label("높음 우선순위 쿨다운")]
        [Tooltip("레벨·튜토리얼 등 중요 데이터의 저장 대기 시간(초).")]
        [Min(0f)]
        public float urgentSaveCooldownSeconds = 1f;

        [Label("보통 우선순위 쿨다운")]
        [Tooltip("우선순위를 지정하지 않은 기본 필드의 저장 대기 시간(초).")]
        [Min(0f)]
        public float normalSaveCooldownSeconds = 5f;

        [Label("낮음 우선순위 쿨다운")]
        [Tooltip("골드 등 자주 변하고 중요도가 낮은 데이터의 저장 대기 시간(초).")]
        [Min(0f)]
        public float lazySaveCooldownSeconds = 30f;

        [Header("우편함")]
        [Label("메일 기본 만료 일수")]
        [Tooltip("수신된 메일의 기본 만료 일수.")]
        [Min(1)]
        public int defaultMailExpirationDays = 30;

        [Label("우편함 폴링 간격")]
        [Tooltip("새 메일 확인 주기(초). 0이면 자동 폴링을 사용하지 않습니다.")]
        [Min(0)]
        public int mailPollingIntervalSeconds = 0;

        [Header("게임 서버")]
        [Label("기본 서버 코드")]
        [Tooltip("게임 서버 배정에 사용할 기본 server_code.")]
        public string defaultServerCode = "GLOBAL";

        [Header("중복 로그인")]
        [Label("중복 감지 폴링 주기")]
        [Tooltip("중복 로그인 감지 확인 주기(초). 0이면 폴링하지 않습니다.")]
        [Min(0f)]
        public float duplicateSessionPollSeconds = 60f;

        [Label("중복 로그인 쿨다운")]
        [Tooltip("중복 로그인 감지 후 다음 검사까지의 대기 시간(초).")]
        [Min(0f)]
        public float duplicateSessionActionCheckCooldownSeconds = 5f;

        [Header("탈퇴")]
        [Label("탈퇴 유예 기간")]
        [Tooltip("탈퇴 신청 후 계정이 실제로 삭제되기까지의 유예 일수.")]
        [Min(0f)]
        public float withdrawalRequestDelayDays = 7f;


        /// <summary>
        /// 인스펙터 값을 Core 계층의 <see cref="SupabaseOptions"/>로 변환합니다. 범위를 벗어난 값은 최소값으로 보정합니다.
        /// </summary>
        public SupabaseOptions ToOptions()
        {
            return new SupabaseOptions
            {
                ProjectURL = projectUrl,
                PublishableKey = publishableKey,
                TimeoutSeconds = timeoutSeconds,
                DefaultMailExpirationDays = defaultMailExpirationDays < 1 ? 1 : defaultMailExpirationDays,
                MailPollingIntervalSeconds = mailPollingIntervalSeconds < 0 ? 0 : mailPollingIntervalSeconds,
                DefaultServerCode = string.IsNullOrWhiteSpace(defaultServerCode) ? "GLOBAL" : defaultServerCode.Trim(),
                DuplicateSessionActionCheckCooldownSeconds = duplicateSessionActionCheckCooldownSeconds < 0f
                    ? 0f
                    : duplicateSessionActionCheckCooldownSeconds,
                WithdrawalRequestDelayDays = withdrawalRequestDelayDays < 0f
                    ? 0f
                    : withdrawalRequestDelayDays,
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
