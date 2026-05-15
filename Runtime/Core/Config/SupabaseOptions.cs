namespace Truesoft.Supabase
{
    public sealed class SupabaseOptions
    {
        public string ProjectURL;
        public string PublishableKey;
        public int TimeoutSeconds = 30;

        /// <summary>Remote Config REST 테이블 (기본 <c>remote_config</c>).</summary>
        public string RemoteConfigTable = "remote_config";

        /// <summary>우편함 REST 테이블 (기본 <c>mails</c>).</summary>
        public string MailsTable = "mails";

        /// <summary>기본 메일 만료 일수(클라이언트·Edge 발송 보조용, DB 기본값과 별개).</summary>
        public int DefaultMailExpirationDays = 30;

        /// <summary>우편함 폴링 권장 간격(초). 0이면 폴링 비활성 안내용.</summary>
        public int MailPollingIntervalSeconds = 0;

        /// <summary>공개 프로필 REST 테이블 — <c>id</c>(row PK), <c>user_id</c>, <c>account_id</c>, <c>withdrawn_at</c> (기본 <c>user_profiles</c>).</summary>
        public string PublicProfilesTable = "user_profiles";

        /// <summary>중복 로그인 감지용 세션 토큰 테이블 (기본 <c>user_sessions</c>).</summary>
        public string UserSessionsTable = "user_sessions";

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

        /// <summary>
        /// 로그인 직후 호출할 Edge Function 이름 (기본 <c>withdrawal-guard</c>).
        /// </summary>
        public string WithdrawalGuardFunctionName = "withdrawal-guard";

        /// <summary>구글 플레이 영수증 검증 Edge Function 이름 (기본 <c>purchase-verify-google</c>).</summary>
        public string PurchaseVerifyGoogleFunctionName = "purchase-verify-google";
    }
}