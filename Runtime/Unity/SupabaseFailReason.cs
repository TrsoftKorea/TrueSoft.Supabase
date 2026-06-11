namespace TrueBase.Unity
{
    /// <summary>
    /// <see cref="SupabaseCallResult.Reason"/>에서 자주 분기하는 케이스의 문자열 상수 모음.
    /// 목록에 없는 값은 실제 <see cref="SupabaseCallResult.Reason"/> 문자열과 직접 비교할 수 있습니다.
    /// </summary>
    public static class SupabaseFailReason
    {
        // ── SDK ──────────────────────────────────────────────────────────────────
        /// <summary>SDK가 초기화되지 않았습니다.</summary>
        public const string NotInitialized = "sdk_not_initialized";

        // ── Auth ─────────────────────────────────────────────────────────────────
        /// <summary>로그인 상태가 아닙니다.</summary>
        public const string NotSignedIn = "auth_not_signed_in";

        /// <summary>사용자가 Google 로그인 화면을 직접 취소했습니다.</summary>
        public const string GoogleSignInCancelled = "google_signin_cancelled";

        /// <summary>계정이 차단되었습니다. <see cref="SupabaseCallResult.BanInfo"/>에서 상세 정보를 확인하세요.</summary>
        public const string UserBanned = "user_banned";

        /// <summary>다른 기기에서 동일 계정으로 로그인해 현재 세션이 무효화되었습니다.</summary>
        public const string DuplicateLogin = "duplicate_login_detected";

        /// <summary>익명(게스트) 세션이 필요한 작업입니다.</summary>
        public const string AnonymousRequired = "anonymous_session_required";

        /// <summary>익명 세션에서는 <c>TryLink*</c> 메서드를 사용해야 합니다.</summary>
        public const string AnonymousRequiresLink = "anonymous_session_requires_explicit_link";

        /// <summary>계정이 탈퇴 처리되어 재로그인이 필요합니다.</summary>
        public const string WithdrawalDeleted = "withdrawal_deleted_manual_login_required";

        // ── Network ──────────────────────────────────────────────────────────────
        /// <summary>HTTP 요청 자체가 실패했습니다(네트워크 없음 또는 타임아웃).</summary>
        public const string NetworkError = "http_response_null";

        // ── Profile ──────────────────────────────────────────────────────────────
        /// <summary>이미 사용 중인 닉네임입니다.</summary>
        public const string DisplayNameTaken = "display_name_taken";

        /// <summary>닉네임이 허용 길이를 초과합니다.</summary>
        public const string DisplayNameTooLong = "display_name_too_long";
    }
}
