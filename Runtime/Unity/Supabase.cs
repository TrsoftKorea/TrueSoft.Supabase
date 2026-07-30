using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using TrueBase.Core.Auth;
using TrueBase.Core.Common;
using TrueBase.Core.Data;
using TrueBase.Core.Models;

namespace TrueBase.Unity
{
    /// <summary>
    /// 게임 코드에서 쓰기 위한 정적 진입점입니다. 실제 구현은 <see cref="SupabaseSDK"/>에 있습니다.
    /// <para>
    /// <b>이 클래스에는 게임에 공개하는 API만 둡니다.</b> SDK 내부끼리 주고받는 호출은
    /// <see cref="SupabaseSDK"/>를 직접 쓰세요. 여기에 <c>internal</c> 멤버를 추가하면
    /// 게임이 볼 API와 내부 배선이 한 파일에 섞여 공개 표면이 흐려집니다.
    /// </para>
    /// </summary>
    /// <remarks>
    /// 공개 비동기 메서드는 이름에 <c>Try</c>를 붙이지 않으며 항상 <see cref="SupabaseResult"/>(액션) 또는
    /// <see cref="SupabaseResult{T}"/>(데이터)를 반환합니다. <c>SupabaseResult</c>는 암묵적 <c>bool</c> 변환을 제공하므로
    /// <c>if (await Supabase.SignInAnonymouslyAsync())</c> 형태로 바로 분기할 수 있습니다.
    /// 로그는 <c>SupabaseSettings.enableApiResultLogs</c>에 따라 API별 고정 태그로 자동 출력됩니다.
    /// </remarks>
    public static class Supabase
    {
        /// <summary>현재 로그인 여부.</summary>
        public static bool IsLoggedIn => SupabaseSDK.IsLoggedIn;

        /// <summary>현재 로그인 계정의 ID(<c>auth.users.id</c>). 비로그인 시 빈 문자열.</summary>
        public static string UserId => SupabaseSDK.UserId;

        /// <summary>현재 세션이 익명 로그인이면 true. 비로그인 시 false.</summary>
        public static bool IsAnonymous => SupabaseSDK.IsAnonymous;

        /// <summary>현재 계정에 Google이 연동되어 있으면 true.</summary>
        public static bool IsLinkedWithGoogle => SupabaseSDK.IsLinkedWithGoogle;

        /// <summary>현재 계정에 Apple이 연동되어 있으면 true.</summary>
        public static bool IsLinkedWithApple => SupabaseSDK.IsLinkedWithApple;

        /// <summary>로그인 결과(<see cref="SupabaseResult"/>)에 로그인 시 조회된 내 프로필을 실어 <see cref="SupabaseSignInResult"/>로 변환합니다.</summary>
        private static async Task<SupabaseSignInResult> ToSignInResultAsync(Task<SupabaseResult> loginTask)
        {
            var r = await loginTask;
            // 성공/실패 무관하게 전달 슬롯을 1회 소비해 다음 로그인으로 잔여 값이 새지 않게 한다.
            var profile = SupabaseSDK.ConsumePendingSignInProfile();
            if (r.IsSuccess)
                return SupabaseSignInResult.Success(profile);

            // 탈퇴 예약 게이트로 막힌 경우, 게이트가 저장해 둔 삭제 예정 시각·취소 토큰을 결과에 실어준다.
            if (r.Reason == SupabaseReason.WithdrawalGateBlocked)
            {
                DateTimeOffset? withdrawnAt = null;
                var gate = SupabaseSDK.GetStoredWithdrawalGateStatus();
                if (gate != null && !string.IsNullOrWhiteSpace(gate.WithdrawnAtIso)
                    && DateTimeOffset.TryParse(gate.WithdrawnAtIso, System.Globalization.CultureInfo.InvariantCulture,
                                               System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed))
                    withdrawnAt = parsed;

                return SupabaseSignInResult.Fail(
                    r.ErrorCode, r.BanInfo, withdrawnAt, SupabaseSDK.ReadStoredWithdrawalCancelToken());
            }

            return SupabaseSignInResult.Fail(r.ErrorCode, r.BanInfo);
        }

        /// <inheritdoc cref="SupabaseSDK.TrySignInWithGoogleAsync"/>
        public static Task<SupabaseSignInResult> SignInWithGoogleAsync() =>
            ToSignInResultAsync(SupabaseSDK.TrySignInWithGoogleAsync());

        /// <inheritdoc cref="SupabaseSDK.TrySignInWithGoogleIdTokenAsync(string)"/>
        public static Task<SupabaseSignInResult> SignInWithGoogleIdTokenAsync(string idToken) =>
            ToSignInResultAsync(SupabaseSDK.TrySignInWithGoogleIdTokenAsync(idToken));

        /// <inheritdoc cref="SupabaseSDK.TrySignInWithAppleIdTokenAsync(string, string)"/>
        public static Task<SupabaseSignInResult> SignInWithAppleIdTokenAsync(
            string idToken, string rawNonce = null) =>
            ToSignInResultAsync(SupabaseSDK.TrySignInWithAppleIdTokenAsync(idToken, rawNonce));

        /// <inheritdoc cref="SupabaseSDK.TrySignInWithAppleAsync"/>
        public static Task<SupabaseSignInResult> SignInWithAppleAsync() =>
            ToSignInResultAsync(SupabaseSDK.TrySignInWithAppleAsync());

        /// <inheritdoc cref="SupabaseSDK.TryLinkAppleToGuestAsync"/>
        public static Task<SupabaseResult> LinkAppleToGuestAsync() =>
            SupabaseSDK.TryLinkAppleToGuestAsync();

        /// <inheritdoc cref="SupabaseSDK.TryLinkAppleNativeAsync"/>
        public static Task<SupabaseResult> LinkAppleNativeAsync() =>
            SupabaseSDK.TryLinkAppleNativeAsync();

        /// <inheritdoc cref="SupabaseSDK.TryLinkAppleToGuestWithIdTokenAsync(string, string)"/>
        public static Task<SupabaseResult> LinkAppleToGuestWithIdTokenAsync(
            string idToken, string rawNonce = null) =>
            SupabaseSDK.TryLinkAppleToGuestWithIdTokenAsync(idToken, rawNonce);

        /// <inheritdoc cref="SupabaseSDK.TryLinkGoogleWithIdTokenAsync(string, string)"/>
        public static Task<SupabaseResult> LinkGoogleWithIdTokenAsync(
            string idToken, string googleAccessToken = null) =>
            SupabaseSDK.TryLinkGoogleWithIdTokenAsync(idToken, googleAccessToken);

        /// <inheritdoc cref="SupabaseSDK.TryLinkAppleWithIdTokenAsync(string, string)"/>
        public static Task<SupabaseResult> LinkAppleWithIdTokenAsync(
            string idToken, string rawNonce = null) =>
            SupabaseSDK.TryLinkAppleWithIdTokenAsync(idToken, rawNonce);

        /// <inheritdoc cref="SupabaseSDK.TryLinkGoogleNativeAsync"/>
        public static Task<SupabaseResult> LinkGoogleNativeAsync() =>
            SupabaseSDK.TryLinkGoogleNativeAsync();

        /// <inheritdoc cref="SupabaseSDK.TryLinkGoogleToGuestAsync"/>
        public static Task<SupabaseResult> LinkGoogleToGuestAsync() =>
            SupabaseSDK.TryLinkGoogleToGuestAsync();

        /// <inheritdoc cref="SupabaseSDK.TryLinkGoogleToGuestWithIdTokenAsync(string, string)"/>
        public static Task<SupabaseResult> LinkGoogleToGuestWithIdTokenAsync(
            string idToken,
            string googleAccessToken = null) =>
            SupabaseSDK.TryLinkGoogleToGuestWithIdTokenAsync(idToken, googleAccessToken);

        /// <inheritdoc cref="SupabaseSDK.TrySignInAnonymouslyAsync"/>
        public static Task<SupabaseSignInResult> SignInAnonymouslyAsync() =>
            ToSignInResultAsync(SupabaseSDK.TrySignInAnonymouslyAsync());

        /// <inheritdoc cref="SupabaseSDK.TryRevokeGoogleAccessAsync"/>
        public static Task<SupabaseResult> RevokeGoogleAccessAsync() =>
            SupabaseSDK.TryRevokeGoogleAccessAsync();

        /// <inheritdoc cref="SupabaseSDK.TryUnlinkGoogleAsync"/>
        public static Task<SupabaseResult> UnlinkGoogleAsync() =>
            SupabaseSDK.TryUnlinkGoogleAsync();

        /// <inheritdoc cref="SupabaseSDK.TryUnlinkAppleAsync"/>
        public static Task<SupabaseResult> UnlinkAppleAsync() =>
            SupabaseSDK.TryUnlinkAppleAsync();

        /// <summary>
        /// 지정한 계정의 차단 정보를 조회합니다. 성공 시 <c>.Data</c>가 차단 정보(차단 상태가 아니면 null)입니다.
        /// </summary>
        /// <remarks>
        /// 주로 로그인 실패 결과의 <c>result.BanInfo</c>를 통해 자동으로 채워집니다.
        /// 별도로 조회가 필요한 경우에만 직접 호출하세요.
        /// </remarks>
        public static Task<SupabaseResult<SupabaseBanInfo>> GetBanInfoAsync(string accountId) =>
            SupabaseSDK.TryGetBanInfoAsync(accountId);

        /// <summary>
        /// 등록된 유저 세이브. 생성된 세이브 클래스가 아직 초기화되지 않았으면 null입니다.
        /// <c>StaticUserSave</c> 서브클래스는 프로젝트당 하나이므로 대상이 모호하지 않습니다.
        /// </summary>
        private static IUserSaveOperations UserSave => SupabaseSDK._userSave;

        /// <summary>
        /// DB에서 유저 세이브를 로드해 생성된 세이브 클래스에 적용합니다. 행이 없으면 생성 후 재로드합니다.
        /// 반환값의 <see cref="SupabaseLoadResult.IsNewUser"/>로 신규 유저 여부를 확인합니다.
        /// </summary>
        public static Task<SupabaseLoadResult> LoadUserSaveAsync() =>
            UserSave != null
                ? UserSave.LoadAsync(includeUpdatedAt: true)
                : Task.FromResult(SupabaseLoadResult.Fail(SupabaseErrorCode.UserSaveNotReady));

        /// <summary>
        /// 쿨다운을 무시하고 변경분을 즉시 전송한 뒤 완료까지 대기합니다.
        /// <para>보낼 변경분이 없으면 네트워크 요청 없이 <see cref="SupabaseReason.UserSaveNoChanges"/> 사유의 실패를 반환합니다.</para>
        /// </summary>
        /// <param name="timeoutMs">전송 완료를 기다리는 최대 시간(밀리초). 초과 시 실패를 반환합니다.</param>
        public static Task<SupabaseResult> SaveNowAsync(int timeoutMs = 5000) =>
            UserSave != null
                ? UserSave.SaveNowAsync(timeoutMs)
                : Task.FromResult(SupabaseResult.Fail(SupabaseErrorCode.UserSaveNotReady));

        /// <summary>
        /// 쿨다운을 무시하고 즉시 전송을 요청합니다. 완료를 기다리지 않습니다(fire-and-forget).
        /// 여러 번 호출해도 안전하며, 전송 중이면 완료 후 1회 재전송이 예약됩니다.
        /// <para>보낼 변경분이 없으면 요청하지 않고 <see cref="SupabaseReason.UserSaveNoChanges"/> 사유의 실패를 반환합니다.</para>
        /// </summary>
        public static SupabaseResult RequestSave() =>
            UserSave != null
                ? UserSave.RequestSave()
                : SupabaseResult.Fail(SupabaseErrorCode.UserSaveNotReady);

        /// <summary>
        /// 마지막 동기화 이후 변경된 필드만 즉시 PATCH합니다.
        /// <para>변경이 없으면 네트워크 요청 없이 <see cref="SupabaseReason.UserSaveNoChanges"/> 사유의 실패를 반환합니다.</para>
        /// </summary>
        public static Task<SupabaseResult> SaveIfChangedAsync() =>
            UserSave != null
                ? UserSave.SaveIfChangedAsync()
                : Task.FromResult(SupabaseResult.Fail(SupabaseErrorCode.UserSaveNotReady));

        /// <summary>
        /// 유저 세이브를 삭제합니다(서버 행 DELETE + 로컬 상태를 기본값으로 리셋). 계정 탈퇴가 아닙니다.
        /// 다음 <see cref="LoadUserSaveAsync"/> 시 기본 행이 재생성되므로 실질적으로 "기본값 리셋"입니다.
        /// </summary>
        public static Task<SupabaseResult> DeleteUserSaveAsync() =>
            UserSave != null
                ? UserSave.DeleteAsync()
                : Task.FromResult(SupabaseResult.Fail(SupabaseErrorCode.UserSaveNotReady));

        /// <inheritdoc cref="SupabaseSDK.TryGetPublicNameAsync(string)"/>
        public static Task<SupabaseResult<string>> GetPublicNameAsync(string userId) =>
            SupabaseSDK.TryGetPublicNameAsync(userId);

        /// <inheritdoc cref="SupabaseSDK.TrySetNameAsync"/>
        public static Task<SupabaseResult<string>> SetNameAsync(string displayName) =>
            SupabaseSDK.TrySetNameAsync(displayName);

        /// <inheritdoc cref="SupabaseSDK.TryIsNameAvailableAsync"/>
        public static Task<SupabaseResult> IsNameAvailableAsync(string displayName) =>
            SupabaseSDK.TryIsNameAvailableAsync(displayName);

        /// <inheritdoc cref="SupabaseSDK.TryGetServerInfoAsync"/>
        public static Task<SupabaseResult<ServerInfo>> GetServerInfoAsync() =>
            SupabaseSDK.TryGetServerInfoAsync();

        /// <summary>
        /// 이 기기에서 접속할 서버 코드. 설정한 적이 없으면 <c>SupabaseSettings</c>의 기본 서버 코드입니다.
        /// </summary>
        public static string ServerCode => SupabaseSDK.GetCurrentServerCode();

        /// <inheritdoc cref="SupabaseSDK.TrySetServerCode"/>
        public static SupabaseResult SetServerCode(string serverCode) =>
            SupabaseSDK.TrySetServerCode(serverCode);

        /// <inheritdoc cref="SupabaseSDK.TryGetPublicProfileAsync"/>
        public static Task<SupabaseResult<PublicProfile>> GetPublicProfileAsync(string userId) =>
            SupabaseSDK.TryGetPublicProfileAsync(userId);

        /// <inheritdoc cref="SupabaseSDK.TryRequestWithdrawalAsync"/>
        public static Task<SupabaseResult> RequestWithdrawalAsync() =>
            SupabaseSDK.TryRequestWithdrawalAsync();

        /// <inheritdoc cref="SupabaseSDK.TryRedeemWithdrawalCancelAsync(string)"/>
        public static Task<SupabaseResult> RedeemWithdrawalCancelAsync(string cancelToken = null) =>
            SupabaseSDK.TryRedeemWithdrawalCancelAsync(cancelToken);

        /// <summary>다른 기기에서 같은 계정으로 로그인해 이 기기 세션이 무효화된 경우(이미 로그아웃 처리 후). UI 팝업에 구독하세요.</summary>
        public static event Action OnDuplicateLoginDetected
        {
            add => SupabaseSDK.OnDuplicateLoginDetected += value;
            remove => SupabaseSDK.OnDuplicateLoginDetected -= value;
        }

        /// <inheritdoc cref="SupabaseSDK.TrySignOutFullyAsync"/>
        public static Task<SupabaseResult> SignOutFullyAsync() => SupabaseSDK.TrySignOutFullyAsync();

        /// <summary>
        /// 저장된 세션으로 자동 로그인을 시도하고, 성공 시 <c>SupabaseRuntime</c> 후처리 훅을 수행합니다.
        /// <b>UserSave 로드는 포함하지 않으므로</b>, 수동 로그인과 동일하게 성공 후 <c>Supabase.LoadUserSaveAsync()</c>를 직접 호출하세요.
        /// 자동 실행되지 않으므로 원하는 타이밍(인트로 완료 후, 로그인 화면 등)에 직접 호출합니다.
        /// </summary>
        public static Task<SupabaseSignInResult> TriggerAutoLoginAsync() =>
            ToSignInResultAsync(SupabaseSDK.TryTriggerAutoLoginAsync());

        /// <inheritdoc cref="SupabaseSDK.TryRestoreSessionAsync"/>
        public static Task<SupabaseSignInResult> RestoreSessionAsync() =>
            ToSignInResultAsync(SupabaseSDK.TryRestoreSessionAsync());

        /// <inheritdoc cref="SupabaseSDK.TryGetMailsAsync"/>
        public static Task<SupabaseResult<IReadOnlyList<Mail>>> GetMailsAsync(int limit = 50, int offset = 0, string category = null) =>
            SupabaseSDK.TryGetMailsAsync(limit, offset, category);

        /// <inheritdoc cref="SupabaseSDK.TryGetMailAsync"/>
        public static Task<SupabaseResult<Mail>> GetMailAsync(string mailId) =>
            SupabaseSDK.TryGetMailAsync(mailId);

        /// <inheritdoc cref="SupabaseSDK.TryClaimMailItemsAsync"/>
        public static Task<SupabaseResult<IReadOnlyList<ClaimResult>>> ClaimMailItemsAsync(string mailId) =>
            SupabaseSDK.TryClaimMailItemsAsync(mailId);

        /// <inheritdoc cref="SupabaseSDK.TryClaimAllMailItemsAsync"/>
        public static Task<SupabaseResult<IReadOnlyList<ClaimResult>>> ClaimAllMailItemsAsync(string category = null) =>
            SupabaseSDK.TryClaimAllMailItemsAsync(category);

        /// <inheritdoc cref="SupabaseSDK.TryDeleteMailAsync"/>
        public static Task<SupabaseResult> DeleteMailAsync(string mailId) =>
            SupabaseSDK.TryDeleteMailAsync(mailId);

        /// <inheritdoc cref="SupabaseSDK.TryDeleteClaimedMailsAsync"/>
        public static Task<SupabaseResult<int>> DeleteClaimedMailsAsync(string category = null) =>
            SupabaseSDK.TryDeleteClaimedMailsAsync(category);

        /// <inheritdoc cref="SupabaseSDK.TryGetMailInboxCountsAsync"/>
        public static Task<SupabaseResult<MailInboxCounts>> GetMailInboxCountsAsync() =>
            SupabaseSDK.TryGetMailInboxCountsAsync();

        // ── 리더보드 ──────────────────────────────────────────────────────────
        // 대상은 코드 문자열이 아니라 생성 클래스 타입으로 지정합니다:
        //   Supabase.SubmitScoreAsync<ArenaLeaderboard>(1250)
        //   Supabase.SubmitScoreAsync(1250, new GuildLeaderboard.Row { ... })
        // 생성은 TrueSoft > Supabase > 클래스 생성 > 리더보드.

        /// <inheritdoc cref="SupabaseSDK.TryGetLeaderboardTablesAsync"/>
        public static Task<SupabaseResult<IReadOnlyList<LeaderboardTable>>> GetLeaderboardsAsync() =>
            SupabaseSDK.TryGetLeaderboardTablesAsync();

        /// <inheritdoc cref="SupabaseSDK.TryGetLeaderboardTableAsync"/>
        public static Task<SupabaseResult<LeaderboardTable>> GetLeaderboardAsync<TLeaderboard>()
            where TLeaderboard : class, ILeaderboard =>
            SupabaseSDK.TryGetLeaderboardTableAsync(LeaderboardMeta.CodeOf(typeof(TLeaderboard)));

        /// <inheritdoc cref="SupabaseSDK.TrySubmitLeaderboardScoreAsync"/>
        public static Task<SupabaseResult<LeaderboardSubmitResult>> SubmitScoreAsync<TLeaderboard>(double score)
            where TLeaderboard : class, ILeaderboard =>
            SupabaseSDK.TrySubmitLeaderboardScoreAsync(LeaderboardMeta.CodeOf(typeof(TLeaderboard)), score);

        /// <summary>
        /// 생성 클래스의 행과 함께 점수를 기록합니다. 어느 리더보드인지는 행 타입에서 읽습니다.
        /// </summary>
        public static Task<SupabaseResult<LeaderboardSubmitResult>> SubmitScoreAsync<TRow>(double score, TRow row)
            where TRow : class, new()
        {
            if (row == null)
                return Task.FromResult(SupabaseResult<LeaderboardSubmitResult>.Fail(SupabaseErrorCode.LeaderboardRowRequired));
            return SupabaseSDK.TrySubmitLeaderboardScoreAsync(
                LeaderboardMeta.CodeOfRow(typeof(TRow)), score, DataSchema.BuildRow(row));
        }

        /// <inheritdoc cref="SupabaseSDK.TryGetLeaderboardRangeAsync"/>
        public static Task<SupabaseResult<IReadOnlyList<LeaderboardEntry>>> GetRanksAsync<TLeaderboard>(
            int start = 1, int end = 100, int? rotationCount = null)
            where TLeaderboard : class, ILeaderboard =>
            SupabaseSDK.TryGetLeaderboardRangeAsync(
                LeaderboardMeta.CodeOf(typeof(TLeaderboard)), start, end, rotationCount);

        /// <inheritdoc cref="SupabaseSDK.TryGetLeaderboardPlayerAsync"/>
        public static Task<SupabaseResult<LeaderboardPlayerEntry>> GetRankAsync<TLeaderboard>(
            string accountId = null, int? rotationCount = null)
            where TLeaderboard : class, ILeaderboard =>
            SupabaseSDK.TryGetLeaderboardPlayerAsync(
                LeaderboardMeta.CodeOf(typeof(TLeaderboard)), accountId, rotationCount);

        // ToRow가 만든 행만 기억한다. 행에 값을 넣지 않은 필드는 기본값으로 전송되므로,
        // 조회로 현재 값을 채운 행이 아니면 다른 필드가 조용히 덮어써진다 — 그래서 아예 막는다.
        // 약참조라 게임이 행을 버리면 함께 정리된다.
        private static readonly ConditionalWeakTable<object, object> _loadedRows = new ConditionalWeakTable<object, object>();

        /// <summary>
        /// 이미 기록된 본인 항목의 등록 필드를 수정합니다. 점수는 바뀌지 않습니다.
        /// 어느 리더보드인지는 행 타입에서 읽으며, 현재 회차에만 적용됩니다.
        /// <para>
        /// 넘기는 행은 <see cref="ToRow{TRow}(LeaderboardEntry)"/>로 만든 것이어야 합니다.
        /// 직접 만든 행은 값을 넣지 않은 필드가 기본값으로 덮어써지므로 거부됩니다.
        /// </para>
        /// </summary>
        public static Task<SupabaseResult> SetRowAsync<TRow>(TRow row)
            where TRow : class, new()
        {
            if (row == null)
                return Task.FromResult(SupabaseResult.Fail(SupabaseErrorCode.LeaderboardRowRequired));
            if (!_loadedRows.TryGetValue(row, out _))
                return Task.FromResult(SupabaseResult.Fail(SupabaseErrorCode.LeaderboardRowNotLoaded));

            return SupabaseSDK.TrySetLeaderboardPlayerDataAsync(
                LeaderboardMeta.CodeOfRow(typeof(TRow)), DataSchema.BuildRow(row));
        }

        /// <inheritdoc cref="SupabaseSDK.TryDeleteMyLeaderboardScoreAsync"/>
        public static Task<SupabaseResult> DeleteMyScoreAsync<TLeaderboard>()
            where TLeaderboard : class, ILeaderboard =>
            SupabaseSDK.TryDeleteMyLeaderboardScoreAsync(LeaderboardMeta.CodeOf(typeof(TLeaderboard)));

        /// <summary>
        /// 순위 조회 결과의 추가 데이터를 생성 클래스의 행으로 변환합니다. 네트워크 호출이 없습니다.
        /// 이렇게 만든 행만 <see cref="SetRowAsync{TRow}"/>에 넘길 수 있습니다.
        /// </summary>
        public static TRow ToRow<TRow>(LeaderboardEntry entry) where TRow : class, new()
        {
            var row = new TRow();
            DataSchema.FillRow(row, entry?.Data);
            _loadedRows.Add(row, null);
            return row;
        }

        /// <summary>
        /// 플레이어 순위 조회 결과의 추가 데이터를 생성 클래스의 행으로 변환합니다. 네트워크 호출이 없습니다.
        /// 이렇게 만든 행만 <see cref="SetRowAsync{TRow}"/>에 넘길 수 있습니다.
        /// </summary>
        public static TRow ToRow<TRow>(LeaderboardPlayerEntry entry) where TRow : class, new()
        {
            var row = new TRow();
            DataSchema.FillRow(row, entry?.Data);
            _loadedRows.Add(row, null);
            return row;
        }

        // ── 쿠폰 ──────────────────────────────────────────────────────────────
        //
        // 쿠폰 정의·발급은 운영(Retool) 전용입니다. 게임은 코드를 보내 사용하는 것만 합니다.
        // 보상은 응답으로 오지 않고 우편으로 지급되므로, 사용 후 우편함을 새로 조회하세요.

        /// <inheritdoc cref="SupabaseSDK.TryRedeemCouponAsync"/>
        public static Task<SupabaseResult> RedeemCouponAsync(string code) =>
            SupabaseSDK.TryRedeemCouponAsync(code);

        // ── 채팅 ──────────────────────────────────────────────────────────────
        //
        // 채널 생성·설정과 메시지 숨김·채팅 차단은 운영(Retool) 전용입니다.
        // 새 메시지는 구독해서 받습니다 — 채팅창을 열 때 Subscribe, 닫을 때 Dispose.

        /// <inheritdoc cref="SupabaseSDK.TryGetChatChannelsAsync"/>
        public static Task<SupabaseResult<IReadOnlyList<ChatChannelInfo>>> GetChatChannelsAsync(bool forceRefresh = false) =>
            SupabaseSDK.TryGetChatChannelsAsync(forceRefresh);

        /// <inheritdoc cref="SupabaseSDK.TrySendChatAsync"/>
        public static Task<SupabaseResult<ChatSendResult>> SendChatAsync(string channelCode, string content) =>
            SupabaseSDK.TrySendChatAsync(channelCode, content);

        /// <summary>
        /// 채널들을 구독해 새 메시지를 콜백으로 받습니다. 채팅창을 닫을 때 반환된 구독을 Dispose 하세요.
        /// 대화가 없으면 조회 간격이 <paramref name="maxIntervalSeconds"/>까지 늘어납니다.
        /// </summary>
        /// <param name="channelCodes">구독할 채널 코드. 여러 개를 넘겨도 조회는 한 번에 묶여 나갑니다.</param>
        /// <param name="onMessages">
        /// 새로 도착한 메시지를 받습니다. 채널이 여럿이어도 <b>시간순으로 합쳐</b> 한 번만 호출되며,
        /// 각 메시지의 <see cref="ChatMessage.ChannelCode"/>로 어느 채널인지 구분합니다.
        /// </param>
        /// <param name="minIntervalSeconds">대화가 오갈 때의 조회 간격. (기본값: 2)</param>
        /// <param name="maxIntervalSeconds">조용할 때 늘어나는 상한. (기본값: 10)</param>
        public static SupabaseResult<ChatSubscription> SubscribeChat(
            IEnumerable<string> channelCodes,
            Action<IReadOnlyList<ChatMessage>> onMessages,
            float minIntervalSeconds = 2f,
            float maxIntervalSeconds = 10f) =>
            SupabaseSDK.Chat.Subscribe(channelCodes, onMessages, minIntervalSeconds, maxIntervalSeconds);

        /// <inheritdoc cref="SupabaseSDK.TryGetUnclaimedMailCountAsync"/>
        public static Task<SupabaseResult<int>> GetUnclaimedMailCountAsync(string userId = null, string category = null) =>
            SupabaseSDK.TryGetUnclaimedMailCountAsync(userId, category);

        /// <summary>
        /// 우편 보상 아이템(<c>items[].key</c>)을 게임에 지급하는 핸들러를 등록합니다. 앱 시작 시 키마다 1회 등록하세요.
        /// 수령 RPC 성공 후 등록된 핸들러가 순서대로 호출됩니다.
        /// </summary>
        public static SupabaseResult RegisterMailItemHandler(IMailItemHandler handler)
        {
            if (handler == null || string.IsNullOrWhiteSpace(handler.ItemKey))
                return SupabaseResult.Fail(SupabaseErrorCode.MailItemHandlerInvalid);
            MailItemHandlerRegistry.Register(handler);
            return SupabaseResult.Ok;
        }

        /// <summary>등록된 우편 아이템 핸들러를 해제합니다.</summary>
        public static SupabaseResult UnregisterMailItemHandler(string itemKey)
        {
            if (string.IsNullOrWhiteSpace(itemKey))
                return SupabaseResult.Fail(SupabaseErrorCode.MailItemHandlerInvalid);
            MailItemHandlerRegistry.Unregister(itemKey);
            return SupabaseResult.Ok;
        }

        /// <inheritdoc cref="SupabaseSDK.TryGetServerNowAsync"/>
        public static Task<SupabaseResult<DateTimeOffset>> GetServerNowAsync() =>
            SupabaseSDK.TryGetServerNowAsync();

        /// <inheritdoc cref="SupabaseSDK.GetServerNow"/>
        public static SupabaseResult<DateTimeOffset> GetServerNow() =>
            SupabaseSDK.GetServerNow();
    }
}
