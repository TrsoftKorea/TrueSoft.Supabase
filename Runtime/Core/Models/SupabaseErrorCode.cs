using System.Runtime.CompilerServices;

// Core 내부 심볼(SupabaseErrorCode 등)을 SDK Unity 계층에만 노출한다. 게임 어셈블리에는 비공개.
[assembly: InternalsVisibleTo("TrueBase.Unity")]
[assembly: InternalsVisibleTo("TrueBase.Unity.IAP")]

namespace TrueBase.Core.Common
{
    /// <summary>
    /// 에러 코드 문자열 카탈로그. <b>SDK 내부 전용</b>(<c>internal</c>) — SDK가 <c>.Fail(...)</c> 방출과
    /// <see cref="SupabaseReasonMap.FromErrorCode"/> 매핑에 사용합니다.
    /// <para>
    /// 게임 코드는 이 문자열 대신 <see cref="SupabaseResult.Reason"/>(<see cref="SupabaseReason"/> enum)으로
    /// 분기하세요(예: <c>result.Reason == SupabaseReason.UserBanned</c>). 각 상수는 같은 이름의 enum 멤버와 1:1 대응합니다.
    /// 실패 원문이 필요하면 <see cref="SupabaseResult.ErrorCode"/> 문자열을 읽습니다.
    /// </para>
    /// </summary>
    internal static class SupabaseErrorCode
    {
        /// <summary>SDK가 초기화되지 않았습니다.</summary>
        public const string NotInitialized = "sdk_not_initialized";

        /// <summary>로그인 상태가 아닙니다. 클라이언트 선검사에서 세션이 없을 때 발생합니다.</summary>
        public const string NotSignedIn = "auth_not_signed_in";

        /// <summary>서버가 인증 토큰을 거부했습니다(토큰 없음·만료·무효). 세션이 있다고 판단해 호출했으나 서버가 반려한 경우로, 재로그인이 필요합니다.</summary>
        public const string NotAuthenticated = "not_authenticated";

        /// <summary>로그인 세션이 필요합니다.</summary>
        public const string SessionRequired = "session_required";

        /// <summary>비익명 계정으로 로그인 중입니다. 먼저 <c>SignOutFullyAsync</c>를 호출하세요.</summary>
        public const string SignedInNonAnonymous = "signed_in_non_anonymous_sign_out_first";

        /// <summary>사용자가 Google 로그인 화면을 직접 취소했습니다.</summary>
        public const string GoogleSignInCancelled = "google_signin_cancelled";

        /// <summary>계정이 차단되었습니다. <see cref="TrueBase.Core.Common.SupabaseResult.BanInfo"/>에서 상세 정보를 확인하세요.</summary>
        public const string UserBanned = "user_banned";

        /// <summary>다른 기기에서 동일 계정으로 로그인해 현재 세션이 무효화되었습니다.</summary>
        public const string DuplicateLogin = "duplicate_login_detected";

        /// <summary>익명(게스트) 세션이 필요한 작업입니다.</summary>
        public const string AnonymousRequired = "anonymous_session_required";

        /// <summary>익명 세션에서는 <c>Link*</c> 메서드를 사용해야 합니다.</summary>
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

        /// <summary>자동 로그인 후처리 훅이 실패를 반환했습니다.</summary>
        public const string AfterAutoLoginFailed = "after_auto_login_failed";

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

        /// <summary>PlayNANOO 연동 중에는 브라우저 기반 Apple 로그인을 쓸 수 없습니다. PlayNANOO WebView로 받은 토큰을 <c>SignInWithAppleIdTokenAsync</c>에 전달하세요.</summary>
        public const string PlayNanooBrowserAppleUnsupported = "playnanoo_active_browser_apple_unsupported";

        /// <summary>Supabase Apple identity 연동에 실패했습니다.</summary>
        public const string AppleLinkFailed = "apple_link_failed";

        /// <summary>Apple 연동 후 익명 플래그가 해제되지 않았습니다.</summary>
        public const string AppleLinkNotCleared = "apple_link_anonymous_not_cleared";

        /// <summary>HTTP 요청 자체가 실패했습니다(네트워크 없음 또는 타임아웃).</summary>
        public const string NetworkError = "http_response_null";

        /// <summary>SELECT 컬럼이 지정되지 않았습니다.</summary>
        public const string SelectColumnsEmpty = "select_columns_empty";

        /// <summary>서버 코드가 비어 있습니다.</summary>
        public const string ServerCodeEmpty = "server_code_empty";

        /// <summary>이미 사용 중인 닉네임입니다.</summary>
        public const string NameTaken = "display_name_taken";

        /// <summary>닉네임이 허용 길이를 초과합니다.</summary>
        public const string NameTooLong = "display_name_too_long";

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

        /// <summary>지원하지 않는 로그인 방식입니다(내부 오류).</summary>
        public const string InvalidSignInMethod = "invalid_signin_method";

        /// <summary>유저 세이브 전송에 실패했습니다. 서버가 거부했거나 네트워크 오류입니다.</summary>
        public const string UserSaveFlushFailed = "user_save_flush_failed";

        /// <summary>세이브 클래스가 아직 초기화되지 않았습니다. 로그인·로드 전에 호출했을 때 발생합니다.</summary>
        public const string UserSaveNotReady = "user_save_not_ready";

        /// <summary>전송 완료를 기다리다 제한 시간을 넘겼습니다. 전송 자체는 계속 진행될 수 있습니다.</summary>
        public const string UserSaveTimeout = "user_save_timeout";

        /// <summary>유저 데이터 테이블 이름이 지정되지 않았습니다.</summary>
        public const string TableNameEmpty = "table_name_empty";

        /// <summary>세이브 값을 전송 형식으로 변환하지 못했습니다. 직렬화할 수 없는 타입이 섞였을 때 발생합니다.</summary>
        public const string UserSaveSerializeFailed = "user_save_serialize_failed";

        /// <summary>유저 세이브 로드에 실패했습니다.</summary>
        public const string UserSaveLoadFailed = "user_save_load_failed";

        /// <summary>유저 세이브 삭제에 실패했습니다.</summary>
        public const string UserSaveDeleteFailed = "user_save_delete_failed";

        /// <summary>변경된 값이 없어 저장을 건너뛰었습니다. 오류가 아니라 "보낼 것이 없었다"는 뜻입니다.</summary>
        public const string UserSaveNoChanges = "user_save_no_changes";

        /// <summary>우편 아이템 핸들러가 null이거나 <c>ItemKey</c>가 비어 있습니다.</summary>
        public const string MailItemHandlerInvalid = "mail_item_handler_invalid";

        /// <summary>해당 우편이 없습니다. 이미 삭제됐거나 기간이 지나 정리된 경우입니다.</summary>
        public const string MailNotFound = "mail_not_found";

        /// <summary>내 우편이 아닙니다.</summary>
        public const string Forbidden = "forbidden";

        /// <summary>지금 접속한 서버의 우편이 아닙니다.</summary>
        public const string ForbiddenServer = "forbidden_server";

        /// <summary>이미 삭제한 우편입니다.</summary>
        public const string MailDeleted = "mail_deleted";

        /// <summary>수령 기간이 지난 우편입니다.</summary>
        public const string MailExpired = "mail_expired";

        /// <summary>이미 보상을 수령한 우편입니다.</summary>
        public const string AlreadyClaimed = "already_claimed";

        /// <summary>우편의 보상 아이템 형식이 올바르지 않습니다.</summary>
        public const string InvalidItemsPayload = "invalid_items_payload";

        /// <summary>보상을 수령하지 않은 우편은 삭제할 수 없습니다.</summary>
        public const string CannotDeleteUnclaimed = "cannot_delete_unclaimed";

        /// <summary>IAP 초기화에 전달된 상품 ID 목록이 비어 있습니다.</summary>
        public const string IapProductIdsEmpty = "iap_product_ids_empty";

        /// <summary>이미 Dispose된 IAP 파사드입니다.</summary>
        public const string IapDisposed = "iap_disposed";

        /// <summary>Unity Services 초기화에 실패했습니다(IAP).</summary>
        public const string IapServicesInitFailed = "iap_services_init_failed";

        /// <summary>IAP 초기화가 제한 시간 내에 완료되지 않았습니다.</summary>
        public const string IapInitTimeout = "iap_init_timeout";

        /// <summary>IAP 초기화에 실패했습니다(스토어 연결·상품 조회 실패).</summary>
        public const string IapInitFailed = "iap_init_failed";

        /// <summary>지급 완료 기록에 전달된 주문 ID가 비어 있습니다.</summary>
        public const string IapOrderIdEmpty = "iap_order_id_empty";

        /// <summary>해당 코드의 리더보드가 없습니다.</summary>
        public const string LeaderboardTableNotFound = "leaderboard_table_not_found";

        /// <summary>리더보드가 종료되었거나 비활성이라 기록할 수 없습니다. 순위 조회는 계속 가능합니다.</summary>
        public const string LeaderboardEnded = "leaderboard_ended";

        /// <summary>존재하지 않는 회차입니다.</summary>
        public const string LeaderboardRotationNotFound = "leaderboard_rotation_not_found";

        /// <summary>이미 지난 회차라 기록을 고치거나 지울 수 없습니다.</summary>
        public const string LeaderboardRotationClosed = "leaderboard_rotation_closed";

        /// <summary>해당 회차에 이 플레이어의 리더보드 기록이 없습니다.</summary>
        public const string LeaderboardScoreNotFound = "leaderboard_score_not_found";

        /// <summary>이 리더보드에 등록되지 않은 플레이어 데이터 컬럼입니다.</summary>
        public const string LeaderboardColumnNotAllowed = "leaderboard_column_not_allowed";

        /// <summary>기록할 점수가 전달되지 않았습니다.</summary>
        public const string LeaderboardScoreRequired = "leaderboard_score_required";

        /// <summary>리더보드 행 객체가 null입니다. 생성한 행 타입 인스턴스를 넘겨야 합니다.</summary>
        public const string LeaderboardRowRequired = "leaderboard_row_required";

        /// <summary>
        /// 조회 결과에서 만들지 않은 행입니다. 직접 <c>new</c>한 행을 수정에 쓰면 값을 넣지 않은 필드가
        /// 기본값으로 덮어써지므로, <c>Supabase.ToRow</c>로 현재 값을 받아 고쳐서 넘겨야 합니다.
        /// </summary>
        public const string LeaderboardRowNotLoaded = "leaderboard_row_not_loaded";

        /// <summary>없는 쿠폰 코드입니다.</summary>
        public const string CouponNotFound = "coupon_not_found";

        /// <summary>운영이 사용을 중지한 쿠폰입니다.</summary>
        public const string CouponInactive = "coupon_inactive";

        /// <summary>사용 기한이 지난 쿠폰입니다.</summary>
        public const string CouponExpired = "coupon_expired";

        /// <summary>이미 사용한 쿠폰입니다. 일반 쿠폰은 코드가 소진된 경우, 키워드 쿠폰은 본인이 이미 쓴 경우입니다.</summary>
        public const string CouponAlreadyUsed = "coupon_already_used";

        /// <summary>키워드 쿠폰의 최대 사용 횟수가 모두 소진되었습니다.</summary>
        public const string CouponExhausted = "coupon_exhausted";

        /// <summary>없는 채팅 채널 코드입니다.</summary>
        public const string ChatChannelNotFound = "chat_channel_not_found";

        /// <summary>운영이 채팅을 중지한 채널입니다. 읽기는 계속 가능합니다.</summary>
        public const string ChatChannelInactive = "chat_channel_inactive";

        /// <summary>보낼 내용이 비어 있습니다.</summary>
        public const string ChatMessageEmpty = "chat_message_empty";

        /// <summary>채널에 설정된 최대 길이를 넘었습니다.</summary>
        public const string ChatMessageTooLong = "chat_message_too_long";

        /// <summary>채팅이 차단된 계정입니다.</summary>
        public const string ChatMuted = "chat_muted";

        /// <summary>채널에 설정된 연속 채팅 간격을 지키지 않았습니다.</summary>
        public const string ChatTooFast = "chat_too_fast";

        /// <summary>서버 채팅을 쓸 수 없는 상태입니다. 프로필에 서버가 지정되지 않았습니다.</summary>
        public const string ChatScopeUnavailable = "chat_scope_unavailable";

        /// <summary>조회 커서 형식이 올바르지 않습니다.</summary>
        public const string ChatCursorsInvalid = "chat_cursors_invalid";

        /// <summary>지원하지 않는 채널 종류입니다.</summary>
        public const string ChatKindUnsupported = "chat_kind_unsupported";

        /// <summary>구독 채널 목록이 비어 있습니다.</summary>
        public const string ChatChannelsEmpty = "chat_channels_empty";

        /// <summary>서버 시각을 아직 한 번도 맞추지 못했습니다. 동기 조회에서만 나오며, 곧 맞춰집니다.</summary>
        public const string ServerTimeNotSynced = "server_time_not_synced";

        /// <summary>매치 세션 ID가 비어 있습니다.</summary>
        public const string MatchSessionIdEmpty = "match_session_id_empty";

        /// <summary>게임 코드가 비어 있습니다.</summary>
        public const string MatchGameCodeEmpty = "match_game_code_empty";

        /// <summary>상대 계정이 지정되지 않았습니다.</summary>
        public const string MatchOpponentRequired = "match_opponent_required";

        /// <summary>상대 계정으로 본인을 지목했습니다.</summary>
        public const string MatchOpponentInvalid = "match_opponent_invalid";

        /// <summary>해당 게임 코드에 등록된 보상 설정이 없습니다. 운영이 먼저 설정해야 합니다.</summary>
        public const string MatchRewardConfigNotFound = "match_reward_config_not_found";

        /// <summary>양쪽이 신고한 결과가 서로 맞지 않습니다(상호 지목 불일치 또는 승패 불일치). 지급되지 않습니다.</summary>
        public const string MatchResultMismatch = "match_result_mismatch";
    }
}
