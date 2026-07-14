namespace TrueBase.Core.Common
{
    /// <summary>
    /// <see cref="SupabaseResult.Reason"/>의 실패 사유 코드(enum). <see cref="SupabaseResult.ErrorCode"/> 문자열에서 매핑됩니다.
    /// <para>
    /// 게임 코드는 <c>if (result.Reason == SupabaseFailCode.UserBanned)</c>처럼 enum으로 분기하세요.
    /// 카탈로그에 없는 동적 사유(예외 메시지·서버 RPC reason·네이티브 SDK 오류 등)는 <see cref="Unknown"/>이며,
    /// 원문은 <see cref="SupabaseResult.ErrorCode"/> 문자열에서 확인합니다.
    /// </para>
    /// <para>
    /// 각 멤버는 동일 이름의 <c>SupabaseFailReason</c> 문자열 상수와 1:1 대응합니다.
    /// 새 사유를 추가할 때는 <c>SupabaseFailReason</c> 상수·이 enum·<see cref="SupabaseFailCodeMap.FromErrorCode"/> 세 곳을 함께 갱신하세요.
    /// </para>
    /// </summary>
    public enum SupabaseFailCode
    {
        /// <summary>성공했거나 실패 사유가 없습니다(<see cref="SupabaseResult.ErrorCode"/>가 null·빈 문자열).</summary>
        None = 0,

        /// <summary>실패 사유는 있으나 카탈로그에 없는 동적/서버/네이티브 문자열입니다. 원문은 <see cref="SupabaseResult.ErrorCode"/> 참조.</summary>
        Unknown,

        NotInitialized,
        NotSignedIn,
        SessionRequired,
        SignedInNonAnonymous,
        GoogleSignInCancelled,
        UserBanned,
        DuplicateLogin,
        AnonymousRequired,
        AnonymousRequiresLink,
        IdentityNotLinked,
        CannotUnlinkLastIdentity,
        UnlinkFailed,
        AnonymousSessionTokenMissing,
        RestoreSessionFailed,
        AutoLoginNoToken,
        AutoLoginFailed,
        AfterAutoLoginFailed,
        AccessTokenEmpty,
        WithdrawalDeleted,
        GoogleWebClientIdEmpty,
        GoogleSignInFailed,
        GoogleIdTokenEmpty,
        GoogleLinkFailed,
        GoogleLinkNotCleared,
        GoogleProviderNull,
        GoogleResultNull,
        AppleIdTokenEmpty,
        AppleSignInCancelled,
        AppleSignInIosOnly,
        AppleSignInUnsupportedPlatform,
        OAuthRefreshTokenMissing,
        OAuthRedirectSchemeEmpty,
        OAuthLoginInProgress,
        PlayNanooBrowserAppleUnsupported,
        AppleLinkFailed,
        AppleLinkNotCleared,
        NetworkError,
        SelectColumnsEmpty,
        DisplayNameTaken,
        DisplayNameTooLong,
        WithdrawalGateBlocked,
        WithdrawalCancelTokenEmpty,
        WithdrawalCancelJwtVerifyMustBeOff,
        WithdrawalCancelIssueFailed,
        WithdrawalDeletedRecreateFailed,
        WithdrawnAtEmpty,
        InvalidSignInMethod,
        UserSaveFlushFailed,
        UserSaveLoadFailed,
        UserSaveDeleteFailed,
        MailItemHandlerInvalid,
        IapProductIdsEmpty,
        IapDisposed,
        IapServicesInitFailed,
        IapInitTimeout,
        IapInitFailed,
    }

    /// <summary>실패 사유 문자열(<see cref="SupabaseResult.ErrorCode"/>)을 <see cref="SupabaseFailCode"/>로 매핑합니다.</summary>
    public static class SupabaseFailCodeMap
    {
        /// <summary>
        /// <see cref="SupabaseResult.ErrorCode"/> 문자열을 enum 코드로 변환합니다.
        /// null·빈 문자열은 <see cref="SupabaseFailCode.None"/>, 카탈로그에 없는 값은 <see cref="SupabaseFailCode.Unknown"/>.
        /// 문자열 값 기준 매핑이므로 호출부가 <c>SupabaseFailReason</c> 상수를 썼든 raw 문자열을 썼든 동일하게 인식됩니다.
        /// 아래 문자열은 <c>SupabaseFailReason</c> 상수 값과 반드시 일치해야 합니다.
        /// </summary>
        public static SupabaseFailCode FromErrorCode(string errorCode) => errorCode switch
        {
            null or "" => SupabaseFailCode.None,
            "sdk_not_initialized" => SupabaseFailCode.NotInitialized,
            "auth_not_signed_in" => SupabaseFailCode.NotSignedIn,
            "session_required" => SupabaseFailCode.SessionRequired,
            "signed_in_non_anonymous_sign_out_first" => SupabaseFailCode.SignedInNonAnonymous,
            "google_signin_cancelled" => SupabaseFailCode.GoogleSignInCancelled,
            "user_banned" => SupabaseFailCode.UserBanned,
            "duplicate_login_detected" => SupabaseFailCode.DuplicateLogin,
            "anonymous_session_required" => SupabaseFailCode.AnonymousRequired,
            "anonymous_session_requires_explicit_link" => SupabaseFailCode.AnonymousRequiresLink,
            "identity_not_linked" => SupabaseFailCode.IdentityNotLinked,
            "cannot_unlink_last_identity" => SupabaseFailCode.CannotUnlinkLastIdentity,
            "unlink_failed" => SupabaseFailCode.UnlinkFailed,
            "anonymous_session_token_missing" => SupabaseFailCode.AnonymousSessionTokenMissing,
            "restore_session_failed" => SupabaseFailCode.RestoreSessionFailed,
            "auto_login_blocked_or_no_token" => SupabaseFailCode.AutoLoginNoToken,
            "auto_login_on_start_failed" => SupabaseFailCode.AutoLoginFailed,
            "after_auto_login_failed" => SupabaseFailCode.AfterAutoLoginFailed,
            "access_token_empty" => SupabaseFailCode.AccessTokenEmpty,
            "withdrawal_deleted_manual_login_required" => SupabaseFailCode.WithdrawalDeleted,
            "google_web_client_id_empty" => SupabaseFailCode.GoogleWebClientIdEmpty,
            "google_signin_failed" => SupabaseFailCode.GoogleSignInFailed,
            "google_id_token_empty" => SupabaseFailCode.GoogleIdTokenEmpty,
            "google_link_failed" => SupabaseFailCode.GoogleLinkFailed,
            "google_link_anonymous_not_cleared" => SupabaseFailCode.GoogleLinkNotCleared,
            "google_provider_null" => SupabaseFailCode.GoogleProviderNull,
            "google_result_null" => SupabaseFailCode.GoogleResultNull,
            "apple_id_token_empty" => SupabaseFailCode.AppleIdTokenEmpty,
            "apple_signin_cancelled" => SupabaseFailCode.AppleSignInCancelled,
            "apple_login_ios_only" => SupabaseFailCode.AppleSignInIosOnly,
            "apple_signin_unsupported_platform" => SupabaseFailCode.AppleSignInUnsupportedPlatform,
            "oauth_refresh_token_missing" => SupabaseFailCode.OAuthRefreshTokenMissing,
            "oauth_redirect_scheme_empty" => SupabaseFailCode.OAuthRedirectSchemeEmpty,
            "oauth_login_already_in_progress" => SupabaseFailCode.OAuthLoginInProgress,
            "playnanoo_active_browser_apple_unsupported" => SupabaseFailCode.PlayNanooBrowserAppleUnsupported,
            "apple_link_failed" => SupabaseFailCode.AppleLinkFailed,
            "apple_link_anonymous_not_cleared" => SupabaseFailCode.AppleLinkNotCleared,
            "http_response_null" => SupabaseFailCode.NetworkError,
            "select_columns_empty" => SupabaseFailCode.SelectColumnsEmpty,
            "display_name_taken" => SupabaseFailCode.DisplayNameTaken,
            "display_name_too_long" => SupabaseFailCode.DisplayNameTooLong,
            "withdrawal_scheduled_gate_blocked" => SupabaseFailCode.WithdrawalGateBlocked,
            "withdrawal_cancel_token_empty" => SupabaseFailCode.WithdrawalCancelTokenEmpty,
            "withdrawal_cancel_redeem_verify_jwt_must_be_off" => SupabaseFailCode.WithdrawalCancelJwtVerifyMustBeOff,
            "withdrawal_scheduled_cancel_token_issue_failed" => SupabaseFailCode.WithdrawalCancelIssueFailed,
            "withdrawal_deleted_recreate_failed" => SupabaseFailCode.WithdrawalDeletedRecreateFailed,
            "withdrawn_at_empty" => SupabaseFailCode.WithdrawnAtEmpty,
            "invalid_signin_method" => SupabaseFailCode.InvalidSignInMethod,
            "user_save_flush_failed" => SupabaseFailCode.UserSaveFlushFailed,
            "user_save_load_failed" => SupabaseFailCode.UserSaveLoadFailed,
            "user_save_delete_failed" => SupabaseFailCode.UserSaveDeleteFailed,
            "mail_item_handler_invalid" => SupabaseFailCode.MailItemHandlerInvalid,
            "iap_product_ids_empty" => SupabaseFailCode.IapProductIdsEmpty,
            "iap_disposed" => SupabaseFailCode.IapDisposed,
            "iap_services_init_failed" => SupabaseFailCode.IapServicesInitFailed,
            "iap_init_timeout" => SupabaseFailCode.IapInitTimeout,
            "iap_init_failed" => SupabaseFailCode.IapInitFailed,
            _ => SupabaseFailCode.Unknown,
        };
    }
}
