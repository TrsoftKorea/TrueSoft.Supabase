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
    /// 각 멤버는 동일 이름의 <c>SupabaseErrorCode</c> 문자열 상수와 1:1 대응합니다(둘 다 Core).
    /// 새 사유는 <c>SupabaseErrorCode</c> 상수와 이 enum 멤버만 추가하면 됩니다.
    /// <see cref="SupabaseFailCodeMap.FromErrorCode"/>는 그 상수를 직접 참조하므로 에러코드 문자열은 상수에만 존재합니다.
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

    /// <summary>실패 사유 문자열(<see cref="SupabaseResult.ErrorCode"/>)을 <see cref="SupabaseFailCode"/>로 매핑합니다.</summary>
    public static class SupabaseFailCodeMap
    {
        /// <summary>
        /// <see cref="SupabaseResult.ErrorCode"/> 문자열을 enum 코드로 변환합니다.
        /// null·빈 문자열은 <see cref="SupabaseFailCode.None"/>, 카탈로그에 없는 값은 <see cref="SupabaseFailCode.Unknown"/>.
        /// 문자열 값 기준 매핑이므로 호출부가 <c>SupabaseErrorCode</c> 상수를 썼든 raw 문자열을 썼든 동일하게 인식됩니다.
        /// 아래 case는 <c>SupabaseErrorCode</c> 상수를 직접 참조하므로 에러코드 문자열은 상수 파일에만 정의됩니다.
        /// </summary>
        public static SupabaseFailCode FromErrorCode(string errorCode) => errorCode switch
        {
            null or "" => SupabaseFailCode.None,
            SupabaseErrorCode.NotInitialized => SupabaseFailCode.NotInitialized,
            SupabaseErrorCode.NotSignedIn => SupabaseFailCode.NotSignedIn,
            SupabaseErrorCode.SessionRequired => SupabaseFailCode.SessionRequired,
            SupabaseErrorCode.SignedInNonAnonymous => SupabaseFailCode.SignedInNonAnonymous,
            SupabaseErrorCode.GoogleSignInCancelled => SupabaseFailCode.GoogleSignInCancelled,
            SupabaseErrorCode.UserBanned => SupabaseFailCode.UserBanned,
            SupabaseErrorCode.DuplicateLogin => SupabaseFailCode.DuplicateLogin,
            SupabaseErrorCode.AnonymousRequired => SupabaseFailCode.AnonymousRequired,
            SupabaseErrorCode.AnonymousRequiresLink => SupabaseFailCode.AnonymousRequiresLink,
            SupabaseErrorCode.IdentityNotLinked => SupabaseFailCode.IdentityNotLinked,
            SupabaseErrorCode.CannotUnlinkLastIdentity => SupabaseFailCode.CannotUnlinkLastIdentity,
            SupabaseErrorCode.UnlinkFailed => SupabaseFailCode.UnlinkFailed,
            SupabaseErrorCode.AnonymousSessionTokenMissing => SupabaseFailCode.AnonymousSessionTokenMissing,
            SupabaseErrorCode.RestoreSessionFailed => SupabaseFailCode.RestoreSessionFailed,
            SupabaseErrorCode.AutoLoginNoToken => SupabaseFailCode.AutoLoginNoToken,
            SupabaseErrorCode.AutoLoginFailed => SupabaseFailCode.AutoLoginFailed,
            SupabaseErrorCode.AfterAutoLoginFailed => SupabaseFailCode.AfterAutoLoginFailed,
            SupabaseErrorCode.AccessTokenEmpty => SupabaseFailCode.AccessTokenEmpty,
            SupabaseErrorCode.WithdrawalDeleted => SupabaseFailCode.WithdrawalDeleted,
            SupabaseErrorCode.GoogleWebClientIdEmpty => SupabaseFailCode.GoogleWebClientIdEmpty,
            SupabaseErrorCode.GoogleSignInFailed => SupabaseFailCode.GoogleSignInFailed,
            SupabaseErrorCode.GoogleIdTokenEmpty => SupabaseFailCode.GoogleIdTokenEmpty,
            SupabaseErrorCode.GoogleLinkFailed => SupabaseFailCode.GoogleLinkFailed,
            SupabaseErrorCode.GoogleLinkNotCleared => SupabaseFailCode.GoogleLinkNotCleared,
            SupabaseErrorCode.GoogleProviderNull => SupabaseFailCode.GoogleProviderNull,
            SupabaseErrorCode.GoogleResultNull => SupabaseFailCode.GoogleResultNull,
            SupabaseErrorCode.AppleIdTokenEmpty => SupabaseFailCode.AppleIdTokenEmpty,
            SupabaseErrorCode.AppleSignInCancelled => SupabaseFailCode.AppleSignInCancelled,
            SupabaseErrorCode.AppleSignInIosOnly => SupabaseFailCode.AppleSignInIosOnly,
            SupabaseErrorCode.AppleSignInUnsupportedPlatform => SupabaseFailCode.AppleSignInUnsupportedPlatform,
            SupabaseErrorCode.OAuthRefreshTokenMissing => SupabaseFailCode.OAuthRefreshTokenMissing,
            SupabaseErrorCode.OAuthRedirectSchemeEmpty => SupabaseFailCode.OAuthRedirectSchemeEmpty,
            SupabaseErrorCode.OAuthLoginInProgress => SupabaseFailCode.OAuthLoginInProgress,
            SupabaseErrorCode.PlayNanooBrowserAppleUnsupported => SupabaseFailCode.PlayNanooBrowserAppleUnsupported,
            SupabaseErrorCode.AppleLinkFailed => SupabaseFailCode.AppleLinkFailed,
            SupabaseErrorCode.AppleLinkNotCleared => SupabaseFailCode.AppleLinkNotCleared,
            SupabaseErrorCode.NetworkError => SupabaseFailCode.NetworkError,
            SupabaseErrorCode.SelectColumnsEmpty => SupabaseFailCode.SelectColumnsEmpty,
            SupabaseErrorCode.NameTaken => SupabaseFailCode.NameTaken,
            SupabaseErrorCode.NameTooLong => SupabaseFailCode.NameTooLong,
            SupabaseErrorCode.WithdrawalGateBlocked => SupabaseFailCode.WithdrawalGateBlocked,
            SupabaseErrorCode.WithdrawalCancelTokenEmpty => SupabaseFailCode.WithdrawalCancelTokenEmpty,
            SupabaseErrorCode.WithdrawalCancelJwtVerifyMustBeOff => SupabaseFailCode.WithdrawalCancelJwtVerifyMustBeOff,
            SupabaseErrorCode.WithdrawalCancelIssueFailed => SupabaseFailCode.WithdrawalCancelIssueFailed,
            SupabaseErrorCode.WithdrawalDeletedRecreateFailed => SupabaseFailCode.WithdrawalDeletedRecreateFailed,
            SupabaseErrorCode.InvalidSignInMethod => SupabaseFailCode.InvalidSignInMethod,
            SupabaseErrorCode.UserSaveFlushFailed => SupabaseFailCode.UserSaveFlushFailed,
            SupabaseErrorCode.UserSaveLoadFailed => SupabaseFailCode.UserSaveLoadFailed,
            SupabaseErrorCode.UserSaveDeleteFailed => SupabaseFailCode.UserSaveDeleteFailed,
            SupabaseErrorCode.MailItemHandlerInvalid => SupabaseFailCode.MailItemHandlerInvalid,
            SupabaseErrorCode.IapProductIdsEmpty => SupabaseFailCode.IapProductIdsEmpty,
            SupabaseErrorCode.IapDisposed => SupabaseFailCode.IapDisposed,
            SupabaseErrorCode.IapServicesInitFailed => SupabaseFailCode.IapServicesInitFailed,
            SupabaseErrorCode.IapInitTimeout => SupabaseFailCode.IapInitTimeout,
            SupabaseErrorCode.IapInitFailed => SupabaseFailCode.IapInitFailed,
            _ => SupabaseFailCode.Unknown,
        };
    }
}
