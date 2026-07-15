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
    /// 공개 비동기 메서드는 이름에 <c>Try</c>를 붙이지 않으며 항상 <see cref="SupabaseResult"/>(액션) 또는
    /// <see cref="SupabaseResult{T}"/>(데이터)를 반환합니다. <c>SupabaseResult</c>는 암묵적 <c>bool</c> 변환을 제공하므로
    /// <c>if (await Supabase.SignInAnonymouslyAsync())</c> 형태로 바로 분기할 수 있습니다.
    /// 로그는 <c>SupabaseSettings.enableApiResultLogs</c>에 따라 API별 고정 태그로 자동 출력됩니다.
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

        /// <summary>로그인 결과(<see cref="SupabaseResult"/>)에 로그인 시 조회된 내 프로필을 실어 <see cref="SupabaseSignInResult"/>로 변환합니다.</summary>
        private static async Task<SupabaseSignInResult> ToSignInResultAsync(Task<SupabaseResult> loginTask)
        {
            var r = await loginTask;
            return r.IsSuccess
                ? SupabaseSignInResult.Success(SupabaseSDK.CurrentMyProfile)
                : SupabaseSignInResult.Fail(r.ErrorCode, r.BanInfo);
        }

        /// <summary>
        /// 씬의 SupabaseRuntime 초기화를 잠시 대기한 뒤, 필요 시 Resources의 SupabaseSettings로 부트스트랩합니다.
        /// 대부분의 API가 내부에서 호출하므로, 게임 코드에서는 생략해도 됩니다.
        /// </summary>
        /// <param name="timeoutMs">초기화 완료를 기다리는 최대 시간(밀리초). 초과 시 false를 반환합니다.</param>
        internal static Task<bool> EnsureInitializedAsync(int timeoutMs = SupabaseSDK.DefaultEnsureInitTimeoutMs) =>
            SupabaseSDK.EnsureInitializedAsync(timeoutMs);

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

        /// <inheritdoc cref="SupabaseSDK.TryLinkAppleToCurrentAnonymousAsync"/>
        public static Task<SupabaseResult> LinkAppleToCurrentAnonymousAsync() =>
            SupabaseSDK.TryLinkAppleToCurrentAnonymousAsync();

        /// <inheritdoc cref="SupabaseSDK.TryLinkAppleNativeAsync"/>
        public static Task<SupabaseResult> LinkAppleNativeAsync() =>
            SupabaseSDK.TryLinkAppleNativeAsync();

        /// <inheritdoc cref="SupabaseSDK.BuildOAuthAuthorizeUrl"/>
        internal static string BuildOAuthAuthorizeUrl(string provider, string redirectTo) =>
            SupabaseSDK.BuildOAuthAuthorizeUrl(provider, redirectTo);

        /// <inheritdoc cref="SupabaseSDK.TryCompleteOAuthRedirectAsync"/>
        internal static Task<SupabaseResult> TryCompleteOAuthRedirectAsync(string redirectUrl) =>
            SupabaseSDK.TryCompleteOAuthRedirectAsync(redirectUrl);

        /// <inheritdoc cref="SupabaseSDK.TryLinkAppleToCurrentAnonymousWithIdTokenAsync(string, string)"/>
        public static Task<SupabaseResult> LinkAppleToCurrentAnonymousWithIdTokenAsync(
            string idToken, string rawNonce = null) =>
            SupabaseSDK.TryLinkAppleToCurrentAnonymousWithIdTokenAsync(idToken, rawNonce);

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

        /// <inheritdoc cref="SupabaseSDK.TryLinkGoogleToCurrentAnonymousAsync"/>
        public static Task<SupabaseResult> LinkGoogleToCurrentAnonymousAsync() =>
            SupabaseSDK.TryLinkGoogleToCurrentAnonymousAsync();

        /// <inheritdoc cref="SupabaseSDK.TryLinkGoogleToCurrentAnonymousWithIdTokenAsync(string, string)"/>
        public static Task<SupabaseResult> LinkGoogleToCurrentAnonymousWithIdTokenAsync(
            string idToken,
            string googleAccessToken = null) =>
            SupabaseSDK.TryLinkGoogleToCurrentAnonymousWithIdTokenAsync(idToken, googleAccessToken);

        /// <inheritdoc cref="SupabaseSDK.TrySignInAnonymouslyAsync"/>
        public static Task<SupabaseSignInResult> SignInAnonymouslyAsync() =>
            ToSignInResultAsync(SupabaseSDK.TrySignInAnonymouslyAsync());

        /// <inheritdoc cref="SupabaseSDK.TrySignOutFromGoogleAsync"/>
        internal static Task<SupabaseResult> TrySignOutFromGoogleAsync() =>
            SupabaseSDK.TrySignOutFromGoogleAsync();

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

        /// <inheritdoc cref="SupabaseSDK.TryRefreshSessionAsync"/>
        internal static Task<SupabaseResult> TryRefreshSessionAsync(string refreshToken) =>
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
        internal static async Task<SupabaseResult> TryPatchUserDataAsync(
            string tableName,
            System.Collections.Generic.Dictionary<string, object> patch,
            bool ensureRowFirst = true,
            bool setUpdatedAtIsoUtc = true)
        {
            var r = await PatchUserDataAsync(tableName, patch, ensureRowFirst, setUpdatedAtIsoUtc);
            return r != null && r.IsSuccess
                ? SupabaseResult.Ok
                : SupabaseResult.Fail(r?.ErrorCode);
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
        internal static Task<SupabaseResult> TryPatchUserDataDiffAsync<T>(
            T previous,
            T current,
            bool ensureRowFirst = true,
            bool setUpdatedAtIsoUtc = true) =>
            SupabaseSDK.TryPatchUserDataDiffAsync(previous, current, ensureRowFirst, setUpdatedAtIsoUtc);

        /// <inheritdoc cref="SupabaseSDK.TryDeleteUserDataAsync{T}()"/>
        internal static Task<SupabaseResult> DeleteUserDataAsync<T>() =>
            SupabaseSDK.TryDeleteUserDataAsync<T>();

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
            Func<float> getDirtyCooldown = null) =>
            SupabaseSDK.RegisterUserSaveStaticSync(key, hasDirty, flushAsync, resetLocalState, getDirtyCooldown);

        /// <summary>정적 세이브 값이 바뀌었음을 알립니다(쿨타임 스케줄).</summary>
        internal static void MarkUserSaveStaticDirty(string key) =>
            SupabaseSDK.MarkUserSaveStaticDirty(key);

        /// <summary>특정 정적 세이브의 즉시 전송을 요청합니다. 전송 중이면 완료 후 1회 재시도됩니다.</summary>
        internal static bool RequestImmediateUserSaveStaticFlush(string key) =>
            SupabaseSDK.RequestImmediateUserSaveStaticFlush(key);

        /// <summary>특정 정적 세이브를 즉시 전송하고 완료까지 대기합니다.</summary>
        internal static Task<bool> TryFlushUserSaveImmediateAsync(string key, int timeoutMs = 5000) =>
            SupabaseSDK.TryFlushUserSaveImmediateAsync(key, timeoutMs);

        /// <summary>등록된 모든 정적 세이브에 즉시 전송을 요청합니다.</summary>
        internal static void RequestImmediateUserSaveStaticFlushAll() =>
            SupabaseSDK.RequestImmediateUserSaveStaticFlushAll();

        /// <summary>등록된 모든 정적 세이브를 즉시 전송하고 완료까지 대기합니다.</summary>
        public static async Task<SupabaseResult> SaveAllAsync(int timeoutMs = 5000) =>
            await SupabaseSDK.TrySaveAllAsync(timeoutMs)
                ? SupabaseResult.Ok
                : SupabaseResult.Fail(SupabaseFailReason.UserSaveFlushFailed);

        /// <inheritdoc cref="SupabaseSDK.TryGetPublicDisplayNameAsync(string)"/>
        public static Task<SupabaseResult<string>> GetPublicDisplayNameAsync(string userId) =>
            SupabaseSDK.TryGetPublicDisplayNameAsync(userId);

        /// <inheritdoc cref="SupabaseSDK.TrySetMyDisplayNameAsync"/>
        public static Task<SupabaseResult<PublicProfileSnapshot>> SetMyDisplayNameAsync(string displayName) =>
            SupabaseSDK.TrySetMyDisplayNameAsync(displayName);

        /// <inheritdoc cref="SupabaseSDK.TryIsDisplayNameAvailableAsync"/>
        public static Task<SupabaseResult> IsDisplayNameAvailableAsync(string displayName) =>
            SupabaseSDK.TryIsDisplayNameAvailableAsync(displayName);

        /// <inheritdoc cref="SupabaseSDK.TryTransferMyServerAsync"/>
        public static Task<SupabaseResult> TransferMyServerAsync(string targetServerCode, string reason = null) =>
            SupabaseSDK.TryTransferMyServerAsync(targetServerCode, reason);

        /// <summary>로컬에 선택한 서버 코드를 저장합니다.</summary>
        internal static void SetCurrentServerCode(string serverCode) =>
            SupabaseSDK.SetCurrentServerCode(serverCode);

        /// <summary>로컬에 저장된 현재 서버 코드를 반환합니다.</summary>
        internal static string GetCurrentServerCode() =>
            SupabaseSDK.GetCurrentServerCode();

        /// <inheritdoc cref="SupabaseSDK.TryGetMyServerInfoAsync"/>
        public static Task<SupabaseResult<MyServerInfo>> GetMyServerInfoAsync() =>
            SupabaseSDK.TryGetMyServerInfoAsync();

        /// <inheritdoc cref="SupabaseSDK.TryGetPublicProfileAsync"/>
        public static Task<SupabaseResult<PublicProfileSnapshot>> GetPublicProfileAsync(string userId) =>
            SupabaseSDK.TryGetPublicProfileAsync(userId);

        /// <inheritdoc cref="SupabaseSDK.TryMarkMyWithdrawnAsync"/>
        public static Task<SupabaseResult> MarkMyWithdrawnAsync() =>
            SupabaseSDK.TryMarkMyWithdrawnAsync();

        /// <inheritdoc cref="SupabaseSDK.TryRequestMyWithdrawalAsync"/>
        public static Task<SupabaseResult> RequestMyWithdrawalAsync() =>
            SupabaseSDK.TryRequestMyWithdrawalAsync();

        /// <inheritdoc cref="SupabaseSDK.TryClearMyWithdrawalAsync"/>
        public static Task<SupabaseResult> ClearMyWithdrawalAsync() =>
            SupabaseSDK.TryClearMyWithdrawalAsync();

        /// <inheritdoc cref="SupabaseSDK.TryGetMyWithdrawalStatusAsync"/>
        public static Task<SupabaseResult<MyWithdrawalStatus>> GetMyWithdrawalStatusAsync() =>
            SupabaseSDK.TryGetMyWithdrawalStatusAsync();

        /// <inheritdoc cref="SupabaseSDK.TryRequestWithdrawalCancelTokenAsync"/>
        public static Task<SupabaseResult<string>> RequestWithdrawalCancelTokenAsync() =>
            SupabaseSDK.TryRequestWithdrawalCancelTokenAsync();

        /// <inheritdoc cref="SupabaseSDK.TryRedeemWithdrawalCancelAsync(string)"/>
        public static Task<SupabaseResult> RedeemWithdrawalCancelAsync(string cancelToken = null) =>
            SupabaseSDK.TryRedeemWithdrawalCancelAsync(cancelToken);

        /// <summary>로컬에 저장된 탈퇴 게이트 상태를 반환합니다(로그아웃 안내 UI용).</summary>
        internal static MyWithdrawalStatus GetStoredWithdrawalGateStatus() =>
            SupabaseSDK.GetStoredWithdrawalGateStatus();

        /// <inheritdoc cref="SupabaseSDK.TrySetMyWithdrawnAtAsync"/>
        internal static Task<SupabaseResult> TrySetMyWithdrawnAtAsync(string withdrawnAtIsoUtc) =>
            SupabaseSDK.TrySetMyWithdrawnAtAsync(withdrawnAtIsoUtc);

        /// <inheritdoc cref="SupabaseSDK.TryUpdateLastActivityAtAsync"/>
        internal static Task<SupabaseResult> TryUpdateLastActivityAtAsync() =>
            SupabaseSDK.TryUpdateLastActivityAtAsync();

        /// <summary>특정 key가 갱신될 때마다 콜백을 호출합니다. 콜백 인자는 해당 key의 원본 JSON 문자열입니다.</summary>
        /// <param name="key">remote_config 테이블의 key.</param>
        /// <param name="onValueChanged">갱신 시 호출되는 콜백. 해지하려면 같은 델리게이트로 <see cref="UnsubscribeRemoteConfig"/>를 호출해야 합니다.</param>
        /// <param name="invokeIfCached">true면 구독 시점에 캐시된 값이 있을 때 즉시 1회 호출합니다.</param>
        internal static void SubscribeRemoteConfig(string key, Action<string> onValueChanged, bool invokeIfCached = true) =>
            SupabaseSDK.SubscribeRemoteConfig(key, onValueChanged, invokeIfCached);

        /// <summary><see cref="SubscribeRemoteConfig"/>로 등록한 콜백을 해지합니다. 등록 시와 동일한 델리게이트 인스턴스를 넘겨야 합니다.</summary>
        internal static void UnsubscribeRemoteConfig(string key, Action<string> onValueChanged) =>
            SupabaseSDK.UnsubscribeRemoteConfig(key, onValueChanged);

        /// <summary>네트워크 없이 인메모리 캐시에서 동기 조회합니다. 캐시에 없거나 역직렬화에 실패하면 <paramref name="defaultValue"/>를 반환합니다.</summary>
        internal static T GetRemoteConfig<T>(string key, T defaultValue = default) =>
            SupabaseSDK.GetRemoteConfig(key, defaultValue);

        /// <inheritdoc cref="SupabaseSDK.TryGetRemoteConfigAsync{T}(string, int)"/>
        internal static Task<(bool success, T value)> TryGetRemoteConfigAsync<T>(string key, int maxStale = 0) where T : class, new() =>
            SupabaseSDK.TryGetRemoteConfigAsync<T>(key, maxStale);

        /// <summary>키의 값을 비동기로 1회 읽는 함수를 만듭니다.</summary>
        /// <param name="maxStale">유효로 간주할 최대 캐시 경과 시간(초). 0이면 DB의 <c>max_stale_seconds</c> 설정을 따릅니다.</param>
        internal static Func<Task<T>> CreateRemoteConfigReader<T>(string key, int maxStale = 0) where T : class, new() =>
            SupabaseSDK.CreateRemoteConfigReader<T>(key, maxStale);

        /// <summary>폴링으로 값을 자동 갱신하는 바인딩을 만듭니다.</summary>
        /// <param name="pollInterval">갱신 확인 주기(초). 0 이하이면 자동 폴링 없음.</param>
        internal static RemoteConfigBinding<T> CreateRemoteConfigBinding<T>(string key, float pollInterval)
            where T : class, new() =>
            SupabaseSDK.CreateRemoteConfigBinding<T>(key, pollInterval);

        /// <summary>값이 갱신될 때마다 <paramref name="onChange"/>를 호출하는 리스너를 만듭니다.</summary>
        /// <param name="pollInterval">갱신 확인 주기(초). 0 이하이면 자동 폴링 없음.</param>
        /// <param name="invokeIfCached">true면 생성 시점에 캐시된 값이 있을 때 즉시 1회 호출합니다.</param>
        internal static RemoteConfigListener<T> CreateRemoteConfigListener<T>(
            string key, float pollInterval, Action<T> onChange, bool invokeIfCached = true)
            where T : class, new() =>
            SupabaseSDK.CreateRemoteConfigListener<T>(key, pollInterval, onChange, invokeIfCached);

        /// <summary>인메모리 캐시에서 원본 JSON 문자열을 조회합니다. 캐시에 없으면 false를 반환하고 <paramref name="valueJson"/>은 null입니다.</summary>
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

        /// <inheritdoc cref="SupabaseSDK.TrySignOutFullyAsync"/>
        public static Task<SupabaseResult> SignOutFullyAsync() => SupabaseSDK.TrySignOutFullyAsync();

        /// <summary>앱 시작 자동 로그인 정책(로그아웃/이전 계정 정보 여부)을 적용해 자동 로그인을 시도합니다(내부 API).</summary>
        internal static Task<SupabaseResult> TryAutoLoginOnStartAsync() => SupabaseSDK.TryAutoLoginOnStartAsync();

        /// <summary>
        /// 저장된 세션으로 자동 로그인을 시도하고, 성공 시 <c>SupabaseRuntime</c> 후처리 훅을 수행합니다.
        /// <b>UserSave 로드는 포함하지 않으므로</b>, 수동 로그인과 동일하게 성공 후 <c>PlayerSave.LoadAsync()</c>를 직접 호출하세요.
        /// 자동 실행되지 않으므로 원하는 타이밍(인트로 완료 후, 로그인 화면 등)에 직접 호출합니다.
        /// </summary>
        public static Task<SupabaseSignInResult> TriggerAutoLoginAsync() =>
            ToSignInResultAsync(SupabaseSDK.TryTriggerAutoLoginAsync());

        /// <inheritdoc cref="SupabaseSDK.TryRestoreSessionAsync"/>
        public static Task<SupabaseSignInResult> RestoreSessionAsync() =>
            ToSignInResultAsync(SupabaseSDK.TryRestoreSessionAsync());

        /// <inheritdoc cref="SupabaseSDK.TryGetMyMailsAsync"/>
        public static Task<SupabaseResult<IReadOnlyList<Mail>>> GetMyMailsAsync(int limit = 50, int offset = 0, string category = null) =>
            SupabaseSDK.TryGetMyMailsAsync(limit, offset, category);

        /// <inheritdoc cref="SupabaseSDK.TryGetMailDetailAsync"/>
        public static Task<SupabaseResult<Mail>> GetMailDetailAsync(string mailId) =>
            SupabaseSDK.TryGetMailDetailAsync(mailId);

        /// <inheritdoc cref="SupabaseSDK.TryClaimMailItemsAsync"/>
        public static Task<SupabaseResult<IReadOnlyList<ClaimResult>>> ClaimMailItemsAsync(string mailId) =>
            SupabaseSDK.TryClaimMailItemsAsync(mailId);

        /// <inheritdoc cref="SupabaseSDK.TryClaimAllMailItemsAsync"/>
        public static Task<SupabaseResult<IReadOnlyList<ClaimResult>>> ClaimAllMailItemsAsync(string category = null) =>
            SupabaseSDK.TryClaimAllMailItemsAsync(category);

        /// <inheritdoc cref="SupabaseSDK.TryDeleteMailAsync"/>
        public static Task<SupabaseResult> DeleteMailAsync(string mailId) =>
            SupabaseSDK.TryDeleteMailAsync(mailId);

        /// <inheritdoc cref="SupabaseSDK.TryDeleteReadMailsAsync"/>
        public static Task<SupabaseResult<int>> DeleteReadMailsAsync(string category = null) =>
            SupabaseSDK.TryDeleteReadMailsAsync(category);

        /// <inheritdoc cref="SupabaseSDK.TryGetMailInboxCountsAsync"/>
        public static Task<SupabaseResult<MailInboxCounts>> GetMailInboxCountsAsync() =>
            SupabaseSDK.TryGetMailInboxCountsAsync();

        /// <inheritdoc cref="SupabaseSDK.TryGetUnreadMailCountAsync"/>
        public static Task<SupabaseResult<int>> GetUnreadMailCountAsync(string userId = null, string category = null) =>
            SupabaseSDK.TryGetUnreadMailCountAsync(userId, category);

        /// <inheritdoc cref="SupabaseSDK.TryGetUnclaimedItemMailCountAsync"/>
        public static Task<SupabaseResult<int>> GetUnclaimedItemMailCountAsync(string userId = null, string category = null) =>
            SupabaseSDK.TryGetUnclaimedItemMailCountAsync(userId, category);

        /// <summary>
        /// 우편 보상 아이템(<c>items[].key</c>)을 게임에 지급하는 핸들러를 등록합니다. 앱 시작 시 키마다 1회 등록하세요.
        /// 수령 RPC 성공 후 등록된 핸들러가 순서대로 호출됩니다.
        /// </summary>
        public static SupabaseResult RegisterMailItemHandler(IMailItemHandler handler)
        {
            if (handler == null || string.IsNullOrWhiteSpace(handler.ItemKey))
                return SupabaseResult.Fail(SupabaseFailReason.MailItemHandlerInvalid);
            MailItemHandlerRegistry.Register(handler);
            return SupabaseResult.Ok;
        }

        /// <summary>등록된 우편 아이템 핸들러를 해제합니다.</summary>
        public static SupabaseResult UnregisterMailItemHandler(string itemKey)
        {
            if (string.IsNullOrWhiteSpace(itemKey))
                return SupabaseResult.Fail(SupabaseFailReason.MailItemHandlerInvalid);
            MailItemHandlerRegistry.Unregister(itemKey);
            return SupabaseResult.Ok;
        }

        /// <summary>우편함 파사드(<c>SupabaseSDK.Mailbox</c>와 동일 인스턴스).</summary>
        internal static MailboxFacade Mailbox => SupabaseSDK.Mailbox;

        /// <inheritdoc cref="SupabaseSDK.TryGetServerUtcNowAsync"/>
        public static Task<SupabaseResult<DateTime>> GetServerUtcNowAsync() =>
            SupabaseSDK.TryGetServerUtcNowAsync();

        // PlayNANOO 이관 브릿지 전용
        // 게임 코드에서 직접 호출하지 마세요. PlayNanooRuntime이 내부적으로 사용합니다.

        /// <inheritdoc cref="SupabaseSDK.RegisterPlayNanooInterceptors"/>
        public static void RegisterPlayNanooInterceptors(
            Func<Func<Task<SupabaseResult>>, Task<SupabaseResult>>         signInAnonymously,
            Func<string, Func<Task<SupabaseResult>>, Task<SupabaseResult>> signInWithGoogleIdToken,
            Func<string, Func<Task<SupabaseResult>>, Task<SupabaseResult>> signInWithAppleIdToken,
            Func<Func<Task<SupabaseResult>>, Task<SupabaseResult>>         signOutFully,
            Func<Func<Task<SupabaseResult>>, Task<SupabaseResult>>         requestMyWithdrawal,
            Func<string, Func<Task<SupabaseResult>>, Task<SupabaseResult>> linkGoogleToCurrentAnonymousWithIdToken = null,
            Func<string, Func<Task<SupabaseResult>>, Task<SupabaseResult>> linkAppleToCurrentAnonymousWithIdToken  = null,
            Func<string, Func<Task<SupabaseResult>>, Task<SupabaseResult>> setMyDisplayName                       = null,
            Func<string, Func<Task<SupabaseResult>>, Task<SupabaseResult>> linkGoogleWithIdToken                  = null,
            Func<string, Func<Task<SupabaseResult>>, Task<SupabaseResult>> linkAppleWithIdToken                   = null) =>
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
