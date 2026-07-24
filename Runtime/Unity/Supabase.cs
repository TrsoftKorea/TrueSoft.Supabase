using System;
using System.Collections.Generic;
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
        /// <param name="includeUpdatedAt">true면 select에 <c>updated_at</c> 컬럼을 포함합니다.</param>
        public static Task<SupabaseLoadResult> LoadUserSaveAsync(bool includeUpdatedAt = true) =>
            UserSave != null
                ? UserSave.LoadAsync(includeUpdatedAt)
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

        /// <inheritdoc cref="SupabaseSDK.TryGetLeaderboardTablesAsync"/>
        public static Task<SupabaseResult<IReadOnlyList<LeaderboardTable>>> GetLeaderboardTablesAsync() =>
            SupabaseSDK.TryGetLeaderboardTablesAsync();

        /// <inheritdoc cref="SupabaseSDK.TryGetLeaderboardTableAsync"/>
        public static Task<SupabaseResult<LeaderboardTable>> GetLeaderboardTableAsync(string code) =>
            SupabaseSDK.TryGetLeaderboardTableAsync(code);

        /// <inheritdoc cref="SupabaseSDK.TrySubmitLeaderboardScoreAsync"/>
        public static Task<SupabaseResult<LeaderboardSubmitResult>> SubmitLeaderboardScoreAsync(
            string code, double score, IReadOnlyDictionary<string, object> data = null) =>
            SupabaseSDK.TrySubmitLeaderboardScoreAsync(code, score, data);

        /// <summary>생성한 리더보드 행 타입으로 점수를 기록합니다. 리더보드 코드·데이터는 행에서 읽습니다.</summary>
        public static Task<SupabaseResult<LeaderboardSubmitResult>> SubmitLeaderboardScoreAsync(
            double score, ILeaderboardRow row) =>
            SupabaseSDK.TrySubmitLeaderboardScoreAsync(row.LeaderboardCode, score, row.ToData());

        /// <inheritdoc cref="SupabaseSDK.TryGetLeaderboardRangeAsync"/>
        public static Task<SupabaseResult<IReadOnlyList<LeaderboardEntry>>> GetLeaderboardRangeAsync(
            string code, int start = 1, int end = 100, int? rotationCount = null) =>
            SupabaseSDK.TryGetLeaderboardRangeAsync(code, start, end, rotationCount);

        /// <inheritdoc cref="SupabaseSDK.TryGetLeaderboardPlayerAsync"/>
        public static Task<SupabaseResult<LeaderboardPlayerEntry>> GetLeaderboardPlayerAsync(
            string code, string accountId = null, int? rotationCount = null) =>
            SupabaseSDK.TryGetLeaderboardPlayerAsync(code, accountId, rotationCount);

        /// <inheritdoc cref="SupabaseSDK.TrySetLeaderboardPlayerDataAsync"/>
        public static Task<SupabaseResult> SetLeaderboardPlayerDataAsync(
            string code, IReadOnlyDictionary<string, object> data = null, int? rotationCount = null) =>
            SupabaseSDK.TrySetLeaderboardPlayerDataAsync(code, data, rotationCount);

        /// <summary>생성한 리더보드 행 타입으로 등록 컬럼을 수정합니다. 점수는 바뀌지 않습니다.</summary>
        public static Task<SupabaseResult> SetLeaderboardPlayerDataAsync(
            ILeaderboardRow row, int? rotationCount = null) =>
            SupabaseSDK.TrySetLeaderboardPlayerDataAsync(row.LeaderboardCode, row.ToData(), rotationCount);

        /// <inheritdoc cref="SupabaseSDK.TryDeleteMyLeaderboardScoreAsync"/>
        public static Task<SupabaseResult> DeleteMyLeaderboardScoreAsync(string code, int? rotationCount = null) =>
            SupabaseSDK.TryDeleteMyLeaderboardScoreAsync(code, rotationCount);

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

        // PlayNANOO 이관 브릿지 전용
        // 게임 코드에서 직접 호출하지 마세요. PlayNanooRuntime이 내부적으로 사용합니다.

        /// <inheritdoc cref="SupabaseSDK.RegisterPlayNanooInterceptors"/>
        public static void RegisterPlayNanooInterceptors(
            Func<Func<Task<SupabaseResult>>, Task<SupabaseResult>>         signInAnonymously,
            Func<string, Func<Task<SupabaseResult>>, Task<SupabaseResult>> signInWithGoogleIdToken,
            Func<string, Func<Task<SupabaseResult>>, Task<SupabaseResult>> signInWithAppleIdToken,
            Func<Func<Task<SupabaseResult>>, Task<SupabaseResult>>         signOutFully,
            Func<Func<Task<SupabaseResult>>, Task<SupabaseResult>>         requestMyWithdrawal,
            Func<string, Func<Task<SupabaseResult>>, Task<SupabaseResult>> linkGoogleToGuestWithIdToken = null,
            Func<string, Func<Task<SupabaseResult>>, Task<SupabaseResult>> linkAppleToGuestWithIdToken  = null,
            Func<string, Func<Task<SupabaseResult>>, Task<SupabaseResult>> setMyName                       = null,
            Func<string, Func<Task<SupabaseResult>>, Task<SupabaseResult>> linkGoogleWithIdToken                  = null,
            Func<string, Func<Task<SupabaseResult>>, Task<SupabaseResult>> linkAppleWithIdToken                   = null,
            Func<Func<Task<SupabaseResult>>, Task<SupabaseResult>>         redeemWithdrawalCancel                 = null) =>
            SupabaseSDK.RegisterPlayNanooInterceptors(
                signInAnonymously, signInWithGoogleIdToken, signInWithAppleIdToken,
                signOutFully, requestMyWithdrawal,
                linkGoogleToGuestWithIdToken, linkAppleToGuestWithIdToken,
                setMyName, linkGoogleWithIdToken, linkAppleWithIdToken,
                redeemWithdrawalCancel);

        /// <inheritdoc cref="SupabaseSDK.UnregisterPlayNanooInterceptors"/>
        public static void UnregisterPlayNanooInterceptors() =>
            SupabaseSDK.UnregisterPlayNanooInterceptors();

        /// <inheritdoc cref="SupabaseSDK.RegisterNanooStorageReset"/>
        public static void RegisterNanooStorageReset(Func<string, Task> reset) =>
            SupabaseSDK.RegisterNanooStorageReset(reset);

        /// <inheritdoc cref="SupabaseSDK.RegisterIAPAppleInterceptor"/>
        public static void RegisterIAPAppleInterceptor(
            Func<string, string, Func<Task<SupabaseResult<AppleIAPPurchaseResponse>>>, Task<SupabaseResult<AppleIAPPurchaseResponse>>> interceptor) =>
            SupabaseSDK.RegisterIAPAppleInterceptor(interceptor);

        /// <inheritdoc cref="SupabaseSDK.RegisterIAPGoogleInterceptor"/>
        public static void RegisterIAPGoogleInterceptor(
            Func<string, string, long, string, Func<Task<SupabaseResult<GooglePlayPurchaseResponse>>>, Task<SupabaseResult<GooglePlayPurchaseResponse>>> interceptor) =>
            SupabaseSDK.RegisterIAPGoogleInterceptor(interceptor);

        /// <summary>
        /// PlayNanooRuntime 전용. 현재 등록된 StaticUserSave 인스턴스를 반환합니다.
        /// StaticUserSave&lt;TRow&gt; 생성 시 자동 등록되므로 게임 코드에서 직접 호출할 필요가 없습니다.
        /// </summary>
        public static INanooSaveSyncable GetNanooSaveBridge() => SupabaseSDK._nanooSaveBridge;
    }
}
