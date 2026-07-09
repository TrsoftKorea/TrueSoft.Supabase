namespace TrueBase.Unity
{
    /// <summary>
    /// <see cref="SupabaseCallResult.Reason"/>에서 자주 분기하는 케이스의 문자열 상수 모음.
    /// 목록에 없는 값은 실제 <see cref="SupabaseCallResult.Reason"/> 문자열과 직접 비교할 수 있습니다.
    /// </summary>
    public static class SupabaseFailReason
    {
        /// <summary>SDK가 초기화되지 않았습니다.</summary>
        public const string NotInitialized = "sdk_not_initialized";

        /// <summary>로그인 상태가 아닙니다.</summary>
        public const string NotSignedIn = "auth_not_signed_in";

        /// <summary>로그인 세션이 필요합니다.</summary>
        public const string SessionRequired = "session_required";

        /// <summary>비익명 계정으로 로그인 중입니다. 먼저 <c>TrySignOutFullyAsync</c>를 호출하세요.</summary>
        public const string SignedInNonAnonymous = "signed_in_non_anonymous_sign_out_first";

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

        /// <summary>해제하려는 provider가 현재 계정에 연동되어 있지 않습니다.</summary>
        public const string IdentityNotLinked = "identity_not_linked";

        /// <summary>마지막 남은 연동은 해제할 수 없습니다(계정에 로그인 수단이 하나도 남지 않게 됨).</summary>
        public const string CannotUnlinkLastIdentity = "cannot_unlink_last_identity";

        /// <summary>연동 해제에 실패했습니다.</summary>
        public const string UnlinkFailed = "unlink_failed";

        /// <summary>익명 세션 토큰이 없습니다. 재로그인이 필요합니다.</summary>
        public const string AnonymousSessionTokenMissing = "anonymous_session_token_missing";

        /// <summary>저장된 세션 복원에 실패했습니다.</summary>
        public const string RestoreSessionFailed = "restore_session_failed";

        /// <summary>자동 로그인이 차단되었거나 저장된 토큰이 없습니다.</summary>
        public const string AutoLoginNoToken = "auto_login_blocked_or_no_token";

        /// <summary>앱 시작 자동 로그인에 실패했습니다.</summary>
        public const string AutoLoginFailed = "auto_login_on_start_failed";

        /// <summary>액세스 토큰이 비어있습니다.</summary>
        public const string AccessTokenEmpty = "access_token_empty";

        /// <summary>계정이 탈퇴 처리되어 재로그인이 필요합니다.</summary>
        public const string WithdrawalDeleted = "withdrawal_deleted_manual_login_required";

        /// <summary><c>SupabaseSettings.googleWebClientId</c>가 설정되지 않았습니다.</summary>
        public const string GoogleWebClientIdEmpty = "google_web_client_id_empty";

        /// <summary>Play Services 내부 오류입니다.</summary>
        public const string GoogleSignInFailed = "google_signin_failed";

        /// <summary>Google ID 토큰을 획득하지 못했습니다.</summary>
        public const string GoogleIdTokenEmpty = "google_id_token_empty";

        /// <summary>Supabase Google identity 연동에 실패했습니다.</summary>
        public const string GoogleLinkFailed = "google_link_failed";

        /// <summary>Google 연동 후 익명 플래그가 해제되지 않았습니다.</summary>
        public const string GoogleLinkNotCleared = "google_link_anonymous_not_cleared";

        /// <summary>Google 로그인 프로바이더가 null입니다(내부 오류).</summary>
        public const string GoogleProviderNull = "google_provider_null";

        /// <summary>Google 로그인 결과가 null입니다(내부 오류).</summary>
        public const string GoogleResultNull = "google_result_null";

        /// <summary>전달된 Apple ID 토큰이 비어있습니다.</summary>
        public const string AppleIdTokenEmpty = "apple_id_token_empty";

        /// <summary>사용자가 Apple 로그인 화면을 직접 취소했습니다.</summary>
        public const string AppleSignInCancelled = "apple_signin_cancelled";

        /// <summary>네이티브 Apple 로그인은 iOS에서만 지원됩니다.</summary>
        public const string AppleSignInIosOnly = "apple_login_ios_only";

        /// <summary>현재 플랫폼(에디터 등)에서는 Apple 로그인을 지원하지 않습니다. iOS·Android 실기기 빌드에서 동작합니다.</summary>
        public const string AppleSignInUnsupportedPlatform = "apple_signin_unsupported_platform";

        /// <summary>웹 OAuth 리다이렉트에 세션 토큰(refresh_token)이 없습니다.</summary>
        public const string OAuthRefreshTokenMissing = "oauth_refresh_token_missing";

        /// <summary>OAuth 리다이렉트 스킴이 비어있습니다.</summary>
        public const string OAuthRedirectSchemeEmpty = "oauth_redirect_scheme_empty";

        /// <summary>이미 진행 중인 웹 OAuth 로그인이 있습니다.</summary>
        public const string OAuthLoginInProgress = "oauth_login_already_in_progress";

        /// <summary>PlayNANOO 연동 중에는 브라우저 기반 Apple 로그인을 쓸 수 없습니다. PlayNANOO WebView로 받은 토큰을 <c>TrySignInWithAppleIdTokenAsync</c>에 전달하세요.</summary>
        public const string PlayNanooBrowserAppleUnsupported = "playnanoo_active_browser_apple_unsupported";

        /// <summary>Supabase Apple identity 연동에 실패했습니다.</summary>
        public const string AppleLinkFailed = "apple_link_failed";

        /// <summary>Apple 연동 후 익명 플래그가 해제되지 않았습니다.</summary>
        public const string AppleLinkNotCleared = "apple_link_anonymous_not_cleared";

        /// <summary>HTTP 요청 자체가 실패했습니다(네트워크 없음 또는 타임아웃).</summary>
        public const string NetworkError = "http_response_null";

        /// <summary>SELECT 컬럼이 지정되지 않았습니다.</summary>
        public const string SelectColumnsEmpty = "select_columns_empty";

        /// <summary>이미 사용 중인 닉네임입니다.</summary>
        public const string DisplayNameTaken = "display_name_taken";

        /// <summary>닉네임이 허용 길이를 초과합니다.</summary>
        public const string DisplayNameTooLong = "display_name_too_long";

        /// <summary>탈퇴 예약 게이트에 의해 로그인이 차단되었습니다.</summary>
        public const string WithdrawalGateBlocked = "withdrawal_scheduled_gate_blocked";

        /// <summary>탈퇴 취소 토큰이 비어있습니다.</summary>
        public const string WithdrawalCancelTokenEmpty = "withdrawal_cancel_token_empty";

        /// <summary>탈퇴 취소 재사용 검증을 위해 Supabase JWT 검증 설정이 꺼져 있어야 합니다.</summary>
        public const string WithdrawalCancelJwtVerifyMustBeOff = "withdrawal_cancel_redeem_verify_jwt_must_be_off";

        /// <summary>탈퇴 취소 토큰 발급에 실패했습니다.</summary>
        public const string WithdrawalCancelIssueFailed = "withdrawal_scheduled_cancel_token_issue_failed";

        /// <summary>탈퇴 계정 재가입에 실패했습니다.</summary>
        public const string WithdrawalDeletedRecreateFailed = "withdrawal_deleted_recreate_failed";

        /// <summary>탈퇴 시각 값이 비어있습니다.</summary>
        public const string WithdrawnAtEmpty = "withdrawn_at_empty";

        /// <summary>지원하지 않는 로그인 방식입니다(내부 오류).</summary>
        public const string InvalidSignInMethod = "invalid_signin_method";

        /// <summary>유저 세이브 저장(플러시)에 실패했습니다(네트워크 오류 또는 타임아웃).</summary>
        public const string UserSaveFlushFailed = "user_save_flush_failed";

        /// <summary>유저 세이브 로드에 실패했습니다.</summary>
        public const string UserSaveLoadFailed = "user_save_load_failed";
    }
}
