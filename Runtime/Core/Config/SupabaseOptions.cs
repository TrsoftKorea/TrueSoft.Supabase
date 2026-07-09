namespace TrueBase
{
    /// <summary>
    /// Core 서비스 구성 값. Unity 레이어에서는 <c>SupabaseSettings</c> ScriptableObject의 값이 여기에 채워집니다.
    /// </summary>
    public sealed class SupabaseOptions
    {
        /// <summary>Supabase 프로젝트 URL(<c>https://xxxx.supabase.co</c>).</summary>
        public string ProjectURL;

        /// <summary>Publishable(anon) API 키. 모든 요청의 <c>apikey</c> 헤더에 사용됩니다.</summary>
        public string PublishableKey;

        /// <summary>HTTP 요청 타임아웃(초).</summary>
        public int TimeoutSeconds = 30;

        /// <summary>기본 메일 만료 일수(클라이언트·Edge 발송 보조용, DB 기본값과 별개).</summary>
        public int DefaultMailExpirationDays = 30;

        /// <summary>우편함 폴링 권장 간격(초). 0이면 폴링 비활성 안내용.</summary>
        public int MailPollingIntervalSeconds = 0;

        /// <summary>로그인 후 기본으로 묶을 서버 코드(기본 <c>GLOBAL</c>).</summary>
        public string DefaultServerCode = "GLOBAL";

        /// <summary>
        /// 행동 시점 중복 로그인 검사 최소 간격(초). 0 이하면 행동마다 검사합니다.
        /// </summary>
        public float DuplicateSessionActionCheckCooldownSeconds = 5f;

        /// <summary>
        /// 탈퇴 요청 시 실제 탈퇴 시각(<c>profiles.withdrawn_at</c>)으로 예약할 유예 기간(일).
        /// </summary>
        public float WithdrawalRequestDelayDays = 7f;

    }
}