namespace TrueBase
{
    public sealed class SupabaseOptions
    {
        public string ProjectURL;
        public string PublishableKey;
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