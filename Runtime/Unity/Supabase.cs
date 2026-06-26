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
    /// </summary>
    /// <remarks>
    /// • 구글: <see cref="TrySignInWithGoogleAsync"/>는 Android 네이티브 전체 플로우(설정의 Web Client ID), <see cref="TrySignInWithGoogleIdTokenAsync"/>는 ID 토큰 문자열만 넘길 때.<br/>
    /// • 공개 프로필: <see cref="TryGetPublicProfileAsync"/>, displayName <see cref="TryIsDisplayNameAvailableAsync"/> → <see cref="TrySetMyDisplayNameAsync"/>, 탈퇴 표시 <see cref="TryMarkMyWithdrawnAsync"/> 등 (DB <c>profiles</c>, README).<br/>
    /// • Try API들은 <c>SupabaseSettings.enableApiResultLogs</c>에 따라 API별 고정 태그로 성공/실패 로그를 자동 출력합니다.
    /// </remarks>
    public static class Supabase
    {
        /// <summary>SDK가 초기화되었는지 여부.</summary>
        internal static bool IsInitialized => SupabaseSDK.IsInitialized;

        /// <summary>현재 로그인된 세션.</summary>
        internal static SupabaseSession Session => SupabaseSDK.Session;

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

        /// <summary>
        /// 로그인 직후 자동으로 조회·캐시된 내 프로필.
        /// 닉네임·탈퇴 상태 등 로그인 시 1회 조회로 충분한 정보를 담습니다.
        /// 로그아웃 후 또는 조회 전에는 <see cref="PublicProfileSnapshot.Empty"/>를 반환합니다.
        /// </summary>
        public static PublicProfileSnapshot MyProfile => SupabaseSDK.MyProfile;

        /// <summary>
        /// 씬의 SupabaseRuntime 초기화를 잠시 대기한 뒤, 필요 시 Resources의 SupabaseSettings로 부트스트랩합니다.
        /// 대부분의 API가 내부에서 호출하므로, 게임 코드에서는 생략해도 됩니다.
        /// </summary>
        internal static Task<bool> EnsureInitializedAsync(int timeoutMs = SupabaseSDK.DefaultEnsureInitTimeoutMs) =>
            SupabaseSDK.EnsureInitializedAsync(timeoutMs);

        /// <inheritdoc cref="SupabaseSDK.TrySignInWithGoogleAsync"/>
        public static Task<SupabaseCallResult> TrySignInWithGoogleAsync() =>
            SupabaseSDK.TrySignInWithGoogleAsync();

        /// <inheritdoc cref="SupabaseSDK.TrySignInWithGoogleIdTokenAsync(string)"/>
        public static Task<SupabaseCallResult> TrySignInWithGoogleIdTokenAsync(string idToken) =>
            SupabaseSDK.TrySignInWithGoogleIdTokenAsync(idToken);

        /// <inheritdoc cref="SupabaseSDK.TrySignInWithAppleIdTokenAsync(string, string)"/>
        public static Task<SupabaseCallResult> TrySignInWithAppleIdTokenAsync(
            string idToken, string rawNonce = null) =>
            SupabaseSDK.TrySignInWithAppleIdTokenAsync(idToken, rawNonce);

        /// <inheritdoc cref="SupabaseSDK.TryLinkAppleToCurrentAnonymousWithIdTokenAsync(string, string)"/>
        public static Task<SupabaseCallResult> TryLinkAppleToCurrentAnonymousWithIdTokenAsync(
            string idToken, string rawNonce = null) =>
            SupabaseSDK.TryLinkAppleToCurrentAnonymousWithIdTokenAsync(idToken, rawNonce);

        /// <inheritdoc cref="SupabaseSDK.TryLinkGoogleWithIdTokenAsync(string, string)"/>
        public static Task<SupabaseCallResult> TryLinkGoogleWithIdTokenAsync(
            string idToken, string googleAccessToken = null) =>
            SupabaseSDK.TryLinkGoogleWithIdTokenAsync(idToken, googleAccessToken);

        /// <inheritdoc cref="SupabaseSDK.TryLinkAppleWithIdTokenAsync(string, string)"/>
        public static Task<SupabaseCallResult> TryLinkAppleWithIdTokenAsync(
            string idToken, string rawNonce = null) =>
            SupabaseSDK.TryLinkAppleWithIdTokenAsync(idToken, rawNonce);

        /// <inheritdoc cref="SupabaseSDK.TryLinkGoogleNativeAsync"/>
        public static Task<SupabaseCallResult> TryLinkGoogleNativeAsync() =>
            SupabaseSDK.TryLinkGoogleNativeAsync();

        /// <inheritdoc cref="SupabaseSDK.TryLinkGoogleToCurrentAnonymousAsync"/>
        public static Task<SupabaseCallResult> TryLinkGoogleToCurrentAnonymousAsync() =>
            SupabaseSDK.TryLinkGoogleToCurrentAnonymousAsync();

        /// <inheritdoc cref="SupabaseSDK.TryLinkGoogleToCurrentAnonymousWithIdTokenAsync(string, string)"/>
        public static Task<SupabaseCallResult> TryLinkGoogleToCurrentAnonymousWithIdTokenAsync(
            string idToken,
            string googleAccessToken = null) =>
            SupabaseSDK.TryLinkGoogleToCurrentAnonymousWithIdTokenAsync(idToken, googleAccessToken);

        /// <inheritdoc cref="SupabaseSDK.TrySignInAnonymouslyAsync"/>
        public static Task<SupabaseCallResult> TrySignInAnonymouslyAsync() =>
            SupabaseSDK.TrySignInAnonymouslyAsync();

        /// <inheritdoc cref="SupabaseSDK.TrySignOutFromGoogleAsync"/>
        internal static Task<SupabaseCallResult> TrySignOutFromGoogleAsync() =>
            SupabaseSDK.TrySignOutFromGoogleAsync();

        /// <inheritdoc cref="SupabaseSDK.TryRevokeGoogleAccessAsync"/>
        public static Task<SupabaseCallResult> TryRevokeGoogleAccessAsync() =>
            SupabaseSDK.TryRevokeGoogleAccessAsync();

        /// <inheritdoc cref="SupabaseSDK.TryUnlinkGoogleAsync"/>
        public static Task<SupabaseCallResult> TryUnlinkGoogleAsync() =>
            SupabaseSDK.TryUnlinkGoogleAsync();

        /// <inheritdoc cref="SupabaseSDK.TryUnlinkAppleAsync"/>
        public static Task<SupabaseCallResult> TryUnlinkAppleAsync() =>
            SupabaseSDK.TryUnlinkAppleAsync();

        /// <summary>
        /// 지정한 계정의 차단 정보를 조회합니다.
        /// 차단 상태가 아니거나 조회 실패 시 <see langword="null"/>을 반환합니다.
        /// </summary>
        /// <remarks>
        /// 주로 로그인 실패 결과의 <c>result.BanInfo</c>를 통해 자동으로 채워집니다.
        /// 별도로 조회가 필요한 경우에만 직접 호출하세요.
        /// </remarks>
        public static Task<SupabaseBanInfo> TryGetBanInfoAsync(string accountId) =>
            SupabaseSDK.TryGetBanInfoAsync(accountId);

        /// <inheritdoc cref="SupabaseSDK.TryRefreshSessionAsync"/>
        internal static Task<SupabaseCallResult> TryRefreshSessionAsync(string refreshToken) =>
            SupabaseSDK.TryRefreshSessionAsync(refreshToken);

        /// <summary>
        /// 앱 시작 시 자주 필요한 준비를 한 번에 수행합니다.
        /// 초기화 → (선택) 자동 로그인.
        /// </summary>
        internal static Task<bool> StartAsync(bool restoreSessionFirst = true) =>
            SupabaseSDK.StartAsync(restoreSessionFirst);

        /// <summary>로그인 직후 <typeparamref name="T"/>의 테이블에 본인 행이 존재하도록 보장합니다. 행이 없으면 DB 기본값으로 생성합니다.</summary>
        internal static Task<SupabaseResult<bool>> EnsureMyRowAsync<T>() =>
            SupabaseSDK.EnsureMyRowAsync<T>();

        /// <summary>변경된 컬럼만 부분 저장(PATCH) (내부 Result API).</summary>
        internal static Task<SupabaseResult<bool>> PatchUserDataAsync(
            string tableName,
            System.Collections.Generic.Dictionary<string, object> patch,
            bool ensureRowFirst = true,
            bool setUpdatedAtIsoUtc = true) =>
            SupabaseSDK.PatchUserDataAsync(tableName, patch, ensureRowFirst, setUpdatedAtIsoUtc);

        /// <summary>프로젝트별 select 컬럼으로 로드 (내부 Result API).</summary>
        internal static Task<SupabaseResult<T>> LoadUserDataColumnsAsync<T>(
            string tableName,
            string selectColumnsCsv) where T : class, new() =>
            SupabaseSDK.LoadUserDataColumnsAsync<T>(tableName, selectColumnsCsv);

        /// <inheritdoc cref="SupabaseSDK.PatchUserDataAsync"/>
        internal static async Task<SupabaseCallResult> TryPatchUserDataAsync(
            string tableName,
            System.Collections.Generic.Dictionary<string, object> patch,
            bool ensureRowFirst = true,
            bool setUpdatedAtIsoUtc = true)
        {
            var r = await PatchUserDataAsync(tableName, patch, ensureRowFirst, setUpdatedAtIsoUtc);
            return r != null && r.IsSuccess
                ? SupabaseCallResult.Ok
                : SupabaseCallResult.Fail(r?.ErrorMessage);
        }

        /// <inheritdoc cref="SupabaseSDK.LoadUserDataColumnsAsync{T}(string, string)"/>
        internal static async Task<T> TryLoadUserDataColumnsAsync<T>(
            string tableName,
            string selectColumnsCsv,
            T defaultValue = default) where T : class, new()
        {
            var r = await LoadUserDataColumnsAsync<T>(tableName, selectColumnsCsv);
            return r != null && r.IsSuccess ? r.Data : defaultValue;
        }

        /// <inheritdoc cref="SupabaseSDK.TryLoadUserDataAttributedAsync{T}(T, bool)"/>
        internal static Task<T> TryLoadUserDataAttributedAsync<T>(T defaultValue = default, bool includeUpdatedAt = true) where T : class, new() =>
            SupabaseSDK.TryLoadUserDataAttributedAsync(defaultValue, includeUpdatedAt);

        /// <inheritdoc cref="SupabaseSDK.TryLoadUserDataAttributedWithRowStateAsync{T}(T, bool)"/>
        internal static Task<(bool success, bool hasRow, T row)> TryLoadUserDataAttributedWithRowStateAsync<T>(
            T defaultWhenFailed = default,
            bool includeUpdatedAt = true) where T : class, new() =>
            SupabaseSDK.TryLoadUserDataAttributedWithRowStateAsync(defaultWhenFailed, includeUpdatedAt);

        /// <inheritdoc cref="SupabaseSDK.TryLoadUserDataColumnsWithRowStateAsync{T}(string, string, T)"/>
        internal static Task<(bool success, bool hasRow, T row)> TryLoadUserDataColumnsWithRowStateAsync<T>(
            string tableName,
            string selectColumnsCsv,
            T defaultWhenFailed = default) where T : class, new() =>
            SupabaseSDK.TryLoadUserDataColumnsWithRowStateAsync(tableName, selectColumnsCsv, defaultWhenFailed);

        /// <inheritdoc cref="SupabaseSDK.TryPatchUserDataDiffAsync{T}(T, T, bool, bool)"/>
        internal static Task<SupabaseCallResult> TryPatchUserDataDiffAsync<T>(
            T previous,
            T current,
            bool ensureRowFirst = true,
            bool setUpdatedAtIsoUtc = true) =>
            SupabaseSDK.TryPatchUserDataDiffAsync(previous, current, ensureRowFirst, setUpdatedAtIsoUtc);

        /// <summary>정적 세이브 자동 동기화 쿨타임(초)을 설정합니다.</summary>
        internal static void ConfigureUserSaveAutoSyncCooldown(float seconds) =>
            SupabaseSDK.ConfigureUserSaveAutoSyncCooldown(seconds);

        /// <summary>
        /// 우선순위별 유저 세이브 쿨다운(초)을 전역으로 설정합니다.
        /// 인스턴스별 <c>ConfigureCooldown</c> 오버라이드가 있으면 그 값이 우선합니다.
        /// </summary>
        internal static void ConfigureUserSavePriorityCooldowns(float urgent, float normal, float lazy) =>
            SupabaseSDK.ConfigureUserSavePriorityCooldowns(urgent, normal, lazy);

        /// <summary>생성된 정적 세이브 타입을 자동 동기화 레지스트리에 등록합니다.</summary>
        internal static void RegisterUserSaveStaticSync(
            string key,
            Func<bool> hasDirty,
            Func<Task<bool>> flushAsync,
            Action resetLocalState = null,
            Func<Task<bool>> loadAsync = null,
            Func<float> getDirtyCooldown = null) =>
            SupabaseSDK.RegisterUserSaveStaticSync(key, hasDirty, flushAsync, resetLocalState, loadAsync, getDirtyCooldown);

        /// <summary>정적 세이브 값이 바뀌었음을 알립니다(쿨타임 스케줄).</summary>
        internal static void MarkUserSaveStaticDirty(string key) =>
            SupabaseSDK.MarkUserSaveStaticDirty(key);

        /// <summary>특정 정적 세이브의 즉시 전송을 요청합니다. 전송 중이면 완료 후 1회 재시도됩니다.</summary>
        internal static bool RequestImmediateUserSaveStaticFlush(string key) =>
            SupabaseSDK.RequestImmediateUserSaveStaticFlush(key);

        /// <summary>특정 정적 세이브를 즉시 전송하고 완료까지 대기합니다.</summary>
        internal static Task<bool> TryFlushUserSaveImmediateAsync(string key, int timeoutMs = 5000) =>
            SupabaseSDK.TryFlushUserSaveImmediateAsync(key, timeoutMs);

        /// <summary>등록된 모든 정적 세이브를 로드합니다. 하나라도 실패하면 실패를 반환합니다.</summary>
        public static async Task<SupabaseCallResult> TryLoadAllUserSavesAsync() =>
            await SupabaseSDK.TryLoadAllUserSavesAsync()
                ? SupabaseCallResult.Ok
                : SupabaseCallResult.Fail(SupabaseFailReason.UserSaveLoadFailed);

        /// <summary>등록된 모든 정적 세이브에 즉시 전송을 요청합니다.</summary>
        internal static void RequestImmediateUserSaveStaticFlushAll() =>
            SupabaseSDK.RequestImmediateUserSaveStaticFlushAll();

        /// <summary>등록된 모든 정적 세이브를 즉시 전송하고 완료까지 대기합니다.</summary>
        public static async Task<SupabaseCallResult> TrySaveAllAsync(int timeoutMs = 5000) =>
            await SupabaseSDK.TrySaveAllAsync(timeoutMs)
                ? SupabaseCallResult.Ok
                : SupabaseCallResult.Fail(SupabaseFailReason.UserSaveFlushFailed);

        /// <inheritdoc cref="SupabaseSDK.TryGetPublicDisplayNameAsync(string, string)"/>
        public static Task<string> TryGetPublicDisplayNameAsync(string userId, string defaultValue = "") =>
            SupabaseSDK.TryGetPublicDisplayNameAsync(userId, defaultValue);

        /// <inheritdoc cref="SupabaseSDK.TrySetMyDisplayNameAsync"/>
        public static Task<SupabaseCallResult> TrySetMyDisplayNameAsync(string displayName) =>
            SupabaseSDK.TrySetMyDisplayNameAsync(displayName);

        /// <inheritdoc cref="SupabaseSDK.TryIsDisplayNameAvailableAsync"/>
        public static Task<SupabaseCallResult> TryIsDisplayNameAvailableAsync(string displayName) =>
            SupabaseSDK.TryIsDisplayNameAvailableAsync(displayName);

        /// <inheritdoc cref="SupabaseSDK.TryTransferMyServerAsync"/>
        public static Task<SupabaseCallResult> TryTransferMyServerAsync(string targetServerCode, string reason = null) =>
            SupabaseSDK.TryTransferMyServerAsync(targetServerCode, reason);

        /// <summary>로컬에 선택한 서버 코드를 저장합니다.</summary>
        internal static void SetCurrentServerCode(string serverCode) =>
            SupabaseSDK.SetCurrentServerCode(serverCode);

        /// <summary>로컬에 저장된 현재 서버 코드를 반환합니다.</summary>
        internal static string GetCurrentServerCode() =>
            SupabaseSDK.GetCurrentServerCode();

        /// <inheritdoc cref="SupabaseSDK.TryGetMyServerInfoAsync"/>
        public static Task<MyServerInfo> TryGetMyServerInfoAsync(MyServerInfo defaultValue = default) =>
            SupabaseSDK.TryGetMyServerInfoAsync(defaultValue);

        /// <inheritdoc cref="SupabaseSDK.TryGetPublicProfileAsync"/>
        public static Task<PublicProfileSnapshot> TryGetPublicProfileAsync(string userId) =>
            SupabaseSDK.TryGetPublicProfileAsync(userId);

        /// <inheritdoc cref="SupabaseSDK.TryMarkMyWithdrawnAsync"/>
        public static Task<SupabaseCallResult> TryMarkMyWithdrawnAsync() =>
            SupabaseSDK.TryMarkMyWithdrawnAsync();

        /// <inheritdoc cref="SupabaseSDK.TryRequestMyWithdrawalAsync"/>
        public static Task<SupabaseCallResult> TryRequestMyWithdrawalAsync() =>
            SupabaseSDK.TryRequestMyWithdrawalAsync();

        /// <inheritdoc cref="SupabaseSDK.TryClearMyWithdrawalAsync"/>
        public static Task<SupabaseCallResult> TryClearMyWithdrawalAsync() =>
            SupabaseSDK.TryClearMyWithdrawalAsync();

        /// <inheritdoc cref="SupabaseSDK.TryGetMyWithdrawalStatusAsync"/>
        public static Task<MyWithdrawalStatus> TryGetMyWithdrawalStatusAsync() =>
            SupabaseSDK.TryGetMyWithdrawalStatusAsync();

        /// <inheritdoc cref="SupabaseSDK.TryRequestWithdrawalCancelTokenAsync(string)"/>
        public static Task<string> TryRequestWithdrawalCancelTokenAsync(string defaultValue = null) =>
            SupabaseSDK.TryRequestWithdrawalCancelTokenAsync(defaultValue);

        /// <inheritdoc cref="SupabaseSDK.TryRedeemWithdrawalCancelAsync(string)"/>
        public static Task<SupabaseCallResult> TryRedeemWithdrawalCancelAsync(string cancelToken = null) =>
            SupabaseSDK.TryRedeemWithdrawalCancelAsync(cancelToken);

        /// <summary>로컬에 저장된 탈퇴 게이트 상태를 반환합니다(로그아웃 안내 UI용).</summary>
        internal static MyWithdrawalStatus GetStoredWithdrawalGateStatus() =>
            SupabaseSDK.GetStoredWithdrawalGateStatus();

        /// <inheritdoc cref="SupabaseSDK.TrySetMyWithdrawnAtAsync"/>
        internal static Task<SupabaseCallResult> TrySetMyWithdrawnAtAsync(string withdrawnAtIsoUtc) =>
            SupabaseSDK.TrySetMyWithdrawnAtAsync(withdrawnAtIsoUtc);

        /// <inheritdoc cref="SupabaseSDK.TryUpdateLastActivityAtAsync"/>
        internal static Task<SupabaseCallResult> TryUpdateLastActivityAtAsync() =>
            SupabaseSDK.TryUpdateLastActivityAtAsync();

        /// <summary>특정 key가 갱신될 때마다 콜백 (코드 연결, 실제 JSON 문자열 전달).</summary>
        internal static void SubscribeRemoteConfig(string key, Action<string> onValueChanged, bool invokeIfCached = true) =>
            SupabaseSDK.SubscribeRemoteConfig(key, onValueChanged, invokeIfCached);

        internal static void UnsubscribeRemoteConfig(string key, Action<string> onValueChanged) =>
            SupabaseSDK.UnsubscribeRemoteConfig(key, onValueChanged);

        internal static T GetRemoteConfig<T>(string key, T defaultValue = default) =>
            SupabaseSDK.GetRemoteConfig(key, defaultValue);

        /// <inheritdoc cref="SupabaseSDK.TryGetRemoteConfigAsync{T}(string, int)"/>
        internal static Task<(bool success, T value)> TryGetRemoteConfigAsync<T>(string key, int maxStale = 0) where T : class, new() =>
            SupabaseSDK.TryGetRemoteConfigAsync<T>(key, maxStale);

        internal static Func<Task<T>> CreateRemoteConfigReader<T>(string key, int maxStale = 0) where T : class, new() =>
            SupabaseSDK.CreateRemoteConfigReader<T>(key, maxStale);

        internal static RemoteConfigBinding<T> CreateRemoteConfigBinding<T>(string key, float pollInterval)
            where T : class, new() =>
            SupabaseSDK.CreateRemoteConfigBinding<T>(key, pollInterval);

        internal static RemoteConfigListener<T> CreateRemoteConfigListener<T>(
            string key, float pollInterval, Action<T> onChange, bool invokeIfCached = true)
            where T : class, new() =>
            SupabaseSDK.CreateRemoteConfigListener<T>(key, pollInterval, onChange, invokeIfCached);

        internal static bool TryGetRemoteConfigRaw(string key, out string valueJson) =>
            SupabaseSDK.TryGetRemoteConfigRaw(key, out valueJson);

        /// <inheritdoc cref="SupabaseSDK.TryVerifyGooglePlayPurchaseAsync"/>
        internal static Task<(bool success, GooglePlayPurchaseResponse value)> TryVerifyGooglePlayPurchaseAsync(
            string purchaseToken,
            string productId,
            string packageName   = null,
            long   priceAmount   = 0,
            string priceCurrency = null) =>
            SupabaseSDK.TryVerifyGooglePlayPurchaseAsync(purchaseToken, productId, packageName, priceAmount, priceCurrency);

        /// <inheritdoc cref="SupabaseSDK.TryVerifyApplePurchaseAsync"/>
        internal static Task<(bool success, AppleIAPPurchaseResponse value)> TryVerifyApplePurchaseAsync(
            string jwsToken,
            string productId,
            string bundleId = null) =>
            SupabaseSDK.TryVerifyApplePurchaseAsync(jwsToken, productId, bundleId);

        /// <inheritdoc cref="SupabaseSDK.TryVerifyApplePurchaseLegacyAsync"/>
        internal static Task<(bool success, AppleIAPPurchaseResponse value)> TryVerifyApplePurchaseLegacyAsync(
            string receipt,
            string productId,
            string bundleId = null) =>
            SupabaseSDK.TryVerifyApplePurchaseLegacyAsync(receipt, productId, bundleId);

        /// <summary>로그인 성공 시 세션을 SDK에 설정. 이후 Patch/LoadColumns API는 세션 인자 없이 사용 가능.</summary>
        internal static void SetSession(SupabaseSession session) => SupabaseSDK.SetSession(session);

        /// <inheritdoc cref="SupabaseSDK.SetSession(SupabaseSession, SupabaseSessionChangeKind)"/>
        internal static void SetSession(SupabaseSession session, SupabaseSessionChangeKind kind) =>
            SupabaseSDK.SetSession(session, kind);

        /// <summary>다른 기기에서 같은 계정으로 로그인해 이 기기 세션이 무효화된 경우(이미 로그아웃 처리 후). UI 팝업에 구독하세요.</summary>
        public static event Action OnDuplicateLoginDetected
        {
            add => SupabaseSDK.OnDuplicateLoginDetected += value;
            remove => SupabaseSDK.OnDuplicateLoginDetected -= value;
        }

        /// <summary>로그아웃 시 호출. clearStorage가 true면 저장된 refresh_token도 삭제.</summary>
        internal static void ClearSession(bool clearStorage = true) => SupabaseSDK.ClearSession(clearStorage);

        /// <inheritdoc cref="SupabaseSDK.ClearSession(bool, bool)"/>
        internal static void ClearSession(bool clearStorage, bool deleteUserSessionRow) =>
            SupabaseSDK.ClearSession(clearStorage, deleteUserSessionRow);

        /// <inheritdoc cref="SupabaseSDK.ClearLocalStorage"/>
        internal static void ClearLocalStorage() => SupabaseSDK.ClearLocalStorage();

        /// <inheritdoc cref="SupabaseSDK.SignOutAsync"/>
        internal static Task SignOutAsync() => SupabaseSDK.SignOutAsync();

        /// <inheritdoc cref="SupabaseSDK.SignOutFullyAsync"/>
        internal static Task SignOutFullyAsync() => SupabaseSDK.SignOutFullyAsync();

        /// <inheritdoc cref="SupabaseSDK.TrySignOutFullyAsync"/>
        public static Task<SupabaseCallResult> TrySignOutFullyAsync() => SupabaseSDK.TrySignOutFullyAsync();

        /// <summary>현재 세션을 기기에 저장. 앱 재시작 후 RestoreSessionAsync로 복원 가능.</summary>
        public static void SaveSessionToStorage() => SupabaseSDK.SaveSessionToStorage();

        /// <summary>저장된 refresh_token으로 세션 복원 (내부 API).</summary>
        internal static Task<bool> RestoreSessionAsync() => SupabaseSDK.RestoreSessionAsync();

        /// <summary>앱 시작 자동 로그인 정책(로그아웃/이전 계정 정보 여부)을 적용해 자동 로그인을 시도합니다(내부 API).</summary>
        internal static Task<SupabaseCallResult> TryAutoLoginOnStartAsync() => SupabaseSDK.TryAutoLoginOnStartAsync();

        /// <inheritdoc cref="SupabaseSDK.TryRestoreSessionAsync"/>
        public static Task<SupabaseCallResult> TryRestoreSessionAsync() => SupabaseSDK.TryRestoreSessionAsync();

        /// <inheritdoc cref="SupabaseSDK.TryGetMyMailsAsync"/>
        internal static Task<IReadOnlyList<Mail>> TryGetMyMailsAsync(int limit = 50, int offset = 0) =>
            SupabaseSDK.TryGetMyMailsAsync(limit, offset);

        /// <inheritdoc cref="SupabaseSDK.TryGetMailDetailAsync"/>
        internal static Task<Mail> TryGetMailDetailAsync(string mailId) =>
            SupabaseSDK.TryGetMailDetailAsync(mailId);

        /// <inheritdoc cref="SupabaseSDK.TryClaimMailItemsAsync"/>
        internal static Task<IReadOnlyList<ClaimResult>> TryClaimMailItemsAsync(string mailId) =>
            SupabaseSDK.TryClaimMailItemsAsync(mailId);

        /// <inheritdoc cref="SupabaseSDK.TryClaimAllMailItemsAsync"/>
        internal static Task<IReadOnlyList<ClaimResult>> TryClaimAllMailItemsAsync() =>
            SupabaseSDK.TryClaimAllMailItemsAsync();

        /// <inheritdoc cref="SupabaseSDK.TryDeleteMailAsync"/>
        internal static Task<SupabaseCallResult> TryDeleteMailAsync(string mailId) =>
            SupabaseSDK.TryDeleteMailAsync(mailId);

        /// <inheritdoc cref="SupabaseSDK.TryDeleteReadMailsAsync"/>
        internal static Task<int?> TryDeleteReadMailsAsync() =>
            SupabaseSDK.TryDeleteReadMailsAsync();

        /// <inheritdoc cref="SupabaseSDK.TryGetUnreadMailCountAsync"/>
        internal static Task<int?> TryGetUnreadMailCountAsync(string userId = null) =>
            SupabaseSDK.TryGetUnreadMailCountAsync(userId);

        /// <inheritdoc cref="SupabaseSDK.TryGetUnclaimedItemMailCountAsync"/>
        internal static Task<int?> TryGetUnclaimedItemMailCountAsync(string userId = null) =>
            SupabaseSDK.TryGetUnclaimedItemMailCountAsync(userId);

        /// <summary>우편함 파사드(<c>SupabaseSDK.Mailbox</c>와 동일 인스턴스).</summary>
        internal static MailboxFacade Mailbox => SupabaseSDK.Mailbox;

        /// <inheritdoc cref="SupabaseSDK.TryGetServerUtcNowAsync"/>
        public static Task<DateTime> TryGetServerUtcNowAsync(DateTime defaultValue = default) =>
            SupabaseSDK.TryGetServerUtcNowAsync(defaultValue);

        // ── PlayNANOO 이관 브릿지 전용 ─────────────────────────────────────────
        // 게임 코드에서 직접 호출하지 마세요. PlayNanooRuntime이 내부적으로 사용합니다.

        /// <inheritdoc cref="SupabaseSDK.RegisterPlayNanooInterceptors"/>
        public static void RegisterPlayNanooInterceptors(
            Func<Func<Task<SupabaseCallResult>>, Task<SupabaseCallResult>>         signInAnonymously,
            Func<string, Func<Task<SupabaseCallResult>>, Task<SupabaseCallResult>> signInWithGoogleIdToken,
            Func<string, Func<Task<SupabaseCallResult>>, Task<SupabaseCallResult>> signInWithAppleIdToken,
            Func<Func<Task<SupabaseCallResult>>, Task<SupabaseCallResult>>         signOutFully,
            Func<Func<Task<SupabaseCallResult>>, Task<SupabaseCallResult>>         requestMyWithdrawal,
            Func<string, Func<Task<SupabaseCallResult>>, Task<SupabaseCallResult>> linkGoogleToCurrentAnonymousWithIdToken = null,
            Func<string, Func<Task<SupabaseCallResult>>, Task<SupabaseCallResult>> linkAppleToCurrentAnonymousWithIdToken  = null,
            Func<string, Func<Task<SupabaseCallResult>>, Task<SupabaseCallResult>> setMyDisplayName                       = null,
            Func<string, Func<Task<SupabaseCallResult>>, Task<SupabaseCallResult>> linkGoogleWithIdToken                  = null,
            Func<string, Func<Task<SupabaseCallResult>>, Task<SupabaseCallResult>> linkAppleWithIdToken                   = null) =>
            SupabaseSDK.RegisterPlayNanooInterceptors(
                signInAnonymously, signInWithGoogleIdToken, signInWithAppleIdToken,
                signOutFully, requestMyWithdrawal,
                linkGoogleToCurrentAnonymousWithIdToken, linkAppleToCurrentAnonymousWithIdToken,
                setMyDisplayName, linkGoogleWithIdToken, linkAppleWithIdToken);

        /// <inheritdoc cref="SupabaseSDK.UnregisterPlayNanooInterceptors"/>
        public static void UnregisterPlayNanooInterceptors() =>
            SupabaseSDK.UnregisterPlayNanooInterceptors();

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
