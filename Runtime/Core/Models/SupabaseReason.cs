namespace TrueBase.Core.Common
{
    /// <summary>
    /// <see cref="SupabaseResult.Reason"/>의 실패 사유 코드(enum). <see cref="SupabaseResult.ErrorCode"/> 문자열에서 매핑됩니다.
    /// <para>
    /// 게임 코드는 <c>if (result.Reason == SupabaseReason.UserBanned)</c>처럼 enum으로 분기하세요.
    /// 카탈로그에 없는 동적 사유(예외 메시지·서버 RPC reason·네이티브 SDK 오류 등)는 <see cref="Unknown"/>이며,
    /// 원문은 <see cref="SupabaseResult.ErrorCode"/> 문자열에서 확인합니다.
    /// </para>
    /// <para>
    /// 각 멤버는 동일 이름의 <c>SupabaseErrorCode</c> 문자열 상수와 1:1 대응합니다(둘 다 Core).
    /// 새 사유는 <c>SupabaseErrorCode</c> 상수와 이 enum 멤버만 추가하면 됩니다.
    /// <see cref="SupabaseReasonMap.FromErrorCode"/>는 그 상수를 직접 참조하므로 에러코드 문자열은 상수에만 존재합니다.
    /// </para>
    /// </summary>
    public enum SupabaseReason
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
        NameTaken,
        NameTooLong,
        WithdrawalGateBlocked,
        WithdrawalCancelTokenEmpty,
        WithdrawalCancelJwtVerifyMustBeOff,
        WithdrawalCancelIssueFailed,
        WithdrawalDeletedRecreateFailed,
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

    /// <summary>실패 사유 문자열(<see cref="SupabaseResult.ErrorCode"/>)을 <see cref="SupabaseReason"/>로 매핑합니다.</summary>
    public static class SupabaseReasonMap
    {
        /// <summary>
        /// <see cref="SupabaseResult.ErrorCode"/> 문자열을 enum 코드로 변환합니다.
        /// null·빈 문자열은 <see cref="SupabaseReason.None"/>, 카탈로그에 없는 값은 <see cref="SupabaseReason.Unknown"/>.
        /// 문자열 값 기준 매핑이므로 호출부가 <c>SupabaseErrorCode</c> 상수를 썼든 raw 문자열을 썼든 동일하게 인식됩니다.
        /// 아래 case는 <c>SupabaseErrorCode</c> 상수를 직접 참조하므로 에러코드 문자열은 상수 파일에만 정의됩니다.
        /// </summary>
        public static SupabaseReason FromErrorCode(string errorCode) => errorCode switch
        {
            null or "" => SupabaseReason.None,
            SupabaseErrorCode.NotInitialized => SupabaseReason.NotInitialized,
            SupabaseErrorCode.NotSignedIn => SupabaseReason.NotSignedIn,
            SupabaseErrorCode.SessionRequired => SupabaseReason.SessionRequired,
            SupabaseErrorCode.SignedInNonAnonymous => SupabaseReason.SignedInNonAnonymous,
            SupabaseErrorCode.GoogleSignInCancelled => SupabaseReason.GoogleSignInCancelled,
            SupabaseErrorCode.UserBanned => SupabaseReason.UserBanned,
            SupabaseErrorCode.DuplicateLogin => SupabaseReason.DuplicateLogin,
            SupabaseErrorCode.AnonymousRequired => SupabaseReason.AnonymousRequired,
            SupabaseErrorCode.AnonymousRequiresLink => SupabaseReason.AnonymousRequiresLink,
            SupabaseErrorCode.IdentityNotLinked => SupabaseReason.IdentityNotLinked,
            SupabaseErrorCode.CannotUnlinkLastIdentity => SupabaseReason.CannotUnlinkLastIdentity,
            SupabaseErrorCode.UnlinkFailed => SupabaseReason.UnlinkFailed,
            SupabaseErrorCode.AnonymousSessionTokenMissing => SupabaseReason.AnonymousSessionTokenMissing,
            SupabaseErrorCode.RestoreSessionFailed => SupabaseReason.RestoreSessionFailed,
            SupabaseErrorCode.AutoLoginNoToken => SupabaseReason.AutoLoginNoToken,
            SupabaseErrorCode.AutoLoginFailed => SupabaseReason.AutoLoginFailed,
            SupabaseErrorCode.AfterAutoLoginFailed => SupabaseReason.AfterAutoLoginFailed,
            SupabaseErrorCode.AccessTokenEmpty => SupabaseReason.AccessTokenEmpty,
            SupabaseErrorCode.WithdrawalDeleted => SupabaseReason.WithdrawalDeleted,
            SupabaseErrorCode.GoogleWebClientIdEmpty => SupabaseReason.GoogleWebClientIdEmpty,
            SupabaseErrorCode.GoogleSignInFailed => SupabaseReason.GoogleSignInFailed,
            SupabaseErrorCode.GoogleIdTokenEmpty => SupabaseReason.GoogleIdTokenEmpty,
            SupabaseErrorCode.GoogleLinkFailed => SupabaseReason.GoogleLinkFailed,
            SupabaseErrorCode.GoogleLinkNotCleared => SupabaseReason.GoogleLinkNotCleared,
            SupabaseErrorCode.GoogleProviderNull => SupabaseReason.GoogleProviderNull,
            SupabaseErrorCode.GoogleResultNull => SupabaseReason.GoogleResultNull,
            SupabaseErrorCode.AppleIdTokenEmpty => SupabaseReason.AppleIdTokenEmpty,
            SupabaseErrorCode.AppleSignInCancelled => SupabaseReason.AppleSignInCancelled,
            SupabaseErrorCode.AppleSignInIosOnly => SupabaseReason.AppleSignInIosOnly,
            SupabaseErrorCode.AppleSignInUnsupportedPlatform => SupabaseReason.AppleSignInUnsupportedPlatform,
            SupabaseErrorCode.OAuthRefreshTokenMissing => SupabaseReason.OAuthRefreshTokenMissing,
            SupabaseErrorCode.OAuthRedirectSchemeEmpty => SupabaseReason.OAuthRedirectSchemeEmpty,
            SupabaseErrorCode.OAuthLoginInProgress => SupabaseReason.OAuthLoginInProgress,
            SupabaseErrorCode.PlayNanooBrowserAppleUnsupported => SupabaseReason.PlayNanooBrowserAppleUnsupported,
            SupabaseErrorCode.AppleLinkFailed => SupabaseReason.AppleLinkFailed,
            SupabaseErrorCode.AppleLinkNotCleared => SupabaseReason.AppleLinkNotCleared,
            SupabaseErrorCode.NetworkError => SupabaseReason.NetworkError,
            SupabaseErrorCode.SelectColumnsEmpty => SupabaseReason.SelectColumnsEmpty,
            SupabaseErrorCode.NameTaken => SupabaseReason.NameTaken,
            SupabaseErrorCode.NameTooLong => SupabaseReason.NameTooLong,
            SupabaseErrorCode.WithdrawalGateBlocked => SupabaseReason.WithdrawalGateBlocked,
            SupabaseErrorCode.WithdrawalCancelTokenEmpty => SupabaseReason.WithdrawalCancelTokenEmpty,
            SupabaseErrorCode.WithdrawalCancelJwtVerifyMustBeOff => SupabaseReason.WithdrawalCancelJwtVerifyMustBeOff,
            SupabaseErrorCode.WithdrawalCancelIssueFailed => SupabaseReason.WithdrawalCancelIssueFailed,
            SupabaseErrorCode.WithdrawalDeletedRecreateFailed => SupabaseReason.WithdrawalDeletedRecreateFailed,
            SupabaseErrorCode.InvalidSignInMethod => SupabaseReason.InvalidSignInMethod,
            SupabaseErrorCode.UserSaveFlushFailed => SupabaseReason.UserSaveFlushFailed,
            SupabaseErrorCode.UserSaveLoadFailed => SupabaseReason.UserSaveLoadFailed,
            SupabaseErrorCode.UserSaveDeleteFailed => SupabaseReason.UserSaveDeleteFailed,
            SupabaseErrorCode.MailItemHandlerInvalid => SupabaseReason.MailItemHandlerInvalid,
            SupabaseErrorCode.IapProductIdsEmpty => SupabaseReason.IapProductIdsEmpty,
            SupabaseErrorCode.IapDisposed => SupabaseReason.IapDisposed,
            SupabaseErrorCode.IapServicesInitFailed => SupabaseReason.IapServicesInitFailed,
            SupabaseErrorCode.IapInitTimeout => SupabaseReason.IapInitTimeout,
            SupabaseErrorCode.IapInitFailed => SupabaseReason.IapInitFailed,
            _ => SupabaseReason.Unknown,
        };
    }
}
