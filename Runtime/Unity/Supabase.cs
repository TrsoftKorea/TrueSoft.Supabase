using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Truesoft.Supabase.Core.Auth;
using Truesoft.Supabase.Core.Common;
using Truesoft.Supabase.Core.Data;
using Truesoft.Supabase.Core.Models;
using Truesoft.Supabase.Unity.RemoteConfig;

namespace Truesoft.Supabase.Unity
{
    /// <summary>
    /// 게임 코드에서 쓰기 위한 정적 진입점입니다. 실제 구현은 <see cref="SupabaseSDK"/>에 있습니다.
    /// </summary>
    /// <remarks>
    /// • 구글: <see cref="TrySignInWithGoogleAsync(bool)"/>는 Android 네이티브 전체 플로우(설정의 Web Client ID), <see cref="TrySignInWithGoogleIdTokenAsync"/>는 ID 토큰 문자열만 넘길 때.<br/>
    /// • 공개 프로필: <see cref="TryGetPublicProfileAsync"/>, displayName <see cref="TryIsDisplayNameAvailableAsync"/> → <see cref="TrySetMyDisplayNameAsync"/>, 탈퇴 표시 <see cref="TryMarkMyWithdrawnAsync"/> 등 (DB <c>profiles</c>, README).<br/>
    /// • 우편함: <see cref="TryGetMyMailsAsync"/>, <see cref="TryClaimMailItemsAsync"/>, <see cref="TryClaimAllMailItemsAsync"/>, <see cref="TryDeleteReadMailsAsync"/>, <see cref="Mailbox"/> (SQL <c>Sql/player/11_mails.sql</c>).<br/>
    /// • Try API들은 <c>SupabaseSettings.enableApiResultLogs</c>에 따라 API별 고정 태그로 성공/실패 로그를 자동 출력합니다.
    /// </remarks>
    public static class Supabase
    {
        /// <summary>SDK가 초기화되었는지 여부.</summary>
        public static bool IsInitialized => SupabaseSDK.IsInitialized;

        /// <summary>현재 로그인된 세션.</summary>
        public static SupabaseSession Session => SupabaseSDK.Session;

        /// <summary>현재 로그인 여부.</summary>
        public static bool IsLoggedIn => SupabaseSDK.IsLoggedIn;

        /// <summary>
        /// 씬의 SupabaseRuntime 초기화를 잠시 대기한 뒤, 필요 시 Resources의 SupabaseSettings로 부트스트랩합니다.
        /// 대부분의 API가 내부에서 호출하므로, 게임 코드에서는 생략해도 됩니다.
        /// </summary>
        public static Task<bool> EnsureInitializedAsync(int timeoutMs = SupabaseSDK.DefaultEnsureInitTimeoutMs) =>
            SupabaseSDK.EnsureInitializedAsync(timeoutMs);

        /// <summary>이미 가진 Google ID 토큰으로 Supabase 세션을 맞춥니다(iOS·커스텀 OAuth·테스트 등).</summary>
        internal static Task<SupabaseResult<SupabaseSession>> SignInWithGoogleIdTokenAsync(
            string idToken,
            bool saveSessionToStorage = true) =>
            SupabaseSDK.SignInWithGoogleIdTokenAsync(idToken, saveSessionToStorage);

        /// <summary>
        /// Android 네이티브 Google 로그인 후 Supabase 세션까지 한 번에 처리합니다.
        /// <c>SupabaseSettings.googleWebClientId</c>(Resources)를 사용합니다.
        /// </summary>
        internal static Task<SupabaseResult<SupabaseSession>> SignInWithGoogleAsync(bool saveSessionToStorage = true) =>
            SupabaseSDK.SignInWithGoogleAsync(saveSessionToStorage);

        /// <summary>Android 네이티브 Google 계정 로그아웃 (Supabase 세션은 유지).</summary>
        internal static Task<SupabaseResult<bool>> SignOutFromGoogleAsync() =>
            SupabaseSDK.SignOutFromGoogleAsync();

        /// <summary>현재 익명 세션에 Google identity를 연동합니다(Android 네이티브 Google 로그인 사용).</summary>
        internal static Task<SupabaseResult<SupabaseSession>> LinkGoogleToCurrentAnonymousAsync(bool saveSessionToStorage = true) =>
            SupabaseSDK.LinkGoogleToCurrentAnonymousAsync(saveSessionToStorage);

        /// <summary>현재 익명 세션에 Google identity를 연동합니다(ID 토큰 직접 전달).</summary>
        internal static Task<SupabaseResult<SupabaseSession>> LinkGoogleToCurrentAnonymousWithIdTokenAsync(
            string idToken,
            string googleAccessToken = null,
            bool saveSessionToStorage = true) =>
            SupabaseSDK.LinkGoogleToCurrentAnonymousWithIdTokenAsync(idToken, googleAccessToken, saveSessionToStorage);

        /// <summary>게스트(익명)로 가입하고 SDK 세션을 자동 설정.</summary>
        internal static Task<SupabaseResult<SupabaseSession>> SignInAnonymouslyAsync(
            bool saveSessionToStorage = true) =>
            SupabaseSDK.SignInAnonymouslyAsync(saveSessionToStorage);

        /// <inheritdoc cref="SupabaseSDK.TrySignInWithGoogleAsync(bool)"/>
        public static Task<bool> TrySignInWithGoogleAsync(bool saveSessionToStorage = true) =>
            SupabaseSDK.TrySignInWithGoogleAsync(saveSessionToStorage);

        /// <inheritdoc cref="SupabaseSDK.TrySignInWithGoogleIdTokenAsync(string, bool)"/>
        public static Task<bool> TrySignInWithGoogleIdTokenAsync(string idToken, bool saveSessionToStorage = true) =>
            SupabaseSDK.TrySignInWithGoogleIdTokenAsync(idToken, saveSessionToStorage);

        /// <inheritdoc cref="SupabaseSDK.TryLinkGoogleToCurrentAnonymousAsync(bool)"/>
        public static Task<bool> TryLinkGoogleToCurrentAnonymousAsync(bool saveSessionToStorage = true) =>
            SupabaseSDK.TryLinkGoogleToCurrentAnonymousAsync(saveSessionToStorage);

        /// <inheritdoc cref="SupabaseSDK.TryLinkGoogleToCurrentAnonymousWithIdTokenAsync(string, string, bool)"/>
        public static Task<bool> TryLinkGoogleToCurrentAnonymousWithIdTokenAsync(
            string idToken,
            string googleAccessToken = null,
            bool saveSessionToStorage = true) =>
            SupabaseSDK.TryLinkGoogleToCurrentAnonymousWithIdTokenAsync(idToken, googleAccessToken, saveSessionToStorage);

        /// <inheritdoc cref="SupabaseSDK.TrySignInAnonymouslyAsync(bool)"/>
        public static Task<bool> TrySignInAnonymouslyAsync(bool saveSessionToStorage = true) =>
            SupabaseSDK.TrySignInAnonymouslyAsync(saveSessionToStorage);

        /// <inheritdoc cref="SupabaseSDK.TrySignOutFromGoogleAsync"/>
        public static Task<bool> TrySignOutFromGoogleAsync() =>
            SupabaseSDK.TrySignOutFromGoogleAsync();

        /// <inheritdoc cref="SupabaseSDK.TryRefreshSessionAsync"/>
        public static Task<bool> TryRefreshSessionAsync(string refreshToken, bool saveSessionToStorage = true) =>
            SupabaseSDK.TryRefreshSessionAsync(refreshToken, saveSessionToStorage);

        /// <summary>초기화 + 로그인 세션을 확인합니다. 미로그인이면 실패를 반환합니다.</summary>
        internal static Task<SupabaseResult<SupabaseSession>> EnsureReadySessionAsync() =>
            SupabaseSDK.EnsureReadySessionAsync();

        /// <summary>
        /// 앱 시작 시 자주 필요한 준비를 한 번에 수행합니다.
        /// 초기화 → (선택) 저장 세션 복원.
        /// </summary>
        public static Task<bool> StartAsync(bool restoreSessionFirst = true) =>
            SupabaseSDK.StartAsync(restoreSessionFirst);

        /// <summary>refresh_token으로 세션 갱신 후 SDK 세션 자동 설정.</summary>
        internal static Task<SupabaseResult<SupabaseSession>> RefreshSessionAsync(
            string refreshToken,
            bool saveSessionToStorage = true) =>
            SupabaseSDK.RefreshSessionAsync(refreshToken, saveSessionToStorage);

        /// <summary>로그인 직후 <typeparamref name="T"/>의 테이블에 본인 행이 존재하도록 보장합니다. 행이 없으면 DB 기본값으로 생성합니다.</summary>
        public static Task<SupabaseResult<bool>> EnsureMyRowAsync<T>() =>
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

        /// <summary><see cref="SupabaseSDK.LoadUserDataAttributedAsync{T}(bool)"/> (내부 Result API).</summary>
        internal static Task<SupabaseResult<T>> LoadUserDataAttributedAsync<T>(bool includeUpdatedAt = true) where T : class, new() =>
            SupabaseSDK.LoadUserDataAttributedAsync<T>(includeUpdatedAt);

        /// <summary><see cref="SupabaseSDK.PatchUserDataDiffAsync{T}(T, T, bool, bool)"/> (내부 Result API).</summary>
        internal static Task<SupabaseResult<bool>> PatchUserDataDiffAsync<T>(
            T previous,
            T current,
            bool ensureRowFirst = true,
            bool setUpdatedAtIsoUtc = true) =>
            SupabaseSDK.PatchUserDataDiffAsync(previous, current, ensureRowFirst, setUpdatedAtIsoUtc);

        /// <inheritdoc cref="SupabaseSDK.PatchUserDataAsync"/>
        public static async Task<bool> TryPatchUserDataAsync(
            string tableName,
            System.Collections.Generic.Dictionary<string, object> patch,
            bool ensureRowFirst = true,
            bool setUpdatedAtIsoUtc = true)
        {
            var r = await PatchUserDataAsync(tableName, patch, ensureRowFirst, setUpdatedAtIsoUtc);
            return r != null && r.IsSuccess;
        }

        /// <inheritdoc cref="SupabaseSDK.LoadUserDataColumnsAsync{T}(string, string)"/>
        public static async Task<T> TryLoadUserDataColumnsAsync<T>(
            string tableName,
            string selectColumnsCsv,
            T defaultValue = default) where T : class, new()
        {
            var r = await LoadUserDataColumnsAsync<T>(tableName, selectColumnsCsv);
            return r != null && r.IsSuccess ? r.Data : defaultValue;
        }

        /// <inheritdoc cref="SupabaseSDK.TryLoadUserDataAttributedAsync{T}(T, bool)"/>
        public static Task<T> TryLoadUserDataAttributedAsync<T>(T defaultValue = default, bool includeUpdatedAt = true) where T : class, new() =>
            SupabaseSDK.TryLoadUserDataAttributedAsync(defaultValue, includeUpdatedAt);

        /// <inheritdoc cref="SupabaseSDK.LoadUserDataAttributedWithRowStateAsync{T}(bool)"/>
        public static Task<SupabaseResult<DataColumnsLoadResult<T>>> LoadUserDataAttributedWithRowStateAsync<T>(
            bool includeUpdatedAt = true) where T : class, new() =>
            SupabaseSDK.LoadUserDataAttributedWithRowStateAsync<T>(includeUpdatedAt);

        /// <inheritdoc cref="SupabaseSDK.TryLoadUserDataAttributedWithRowStateAsync{T}(T, bool)"/>
        public static Task<(bool success, bool hasRow, T row)> TryLoadUserDataAttributedWithRowStateAsync<T>(
            T defaultWhenFailed = default,
            bool includeUpdatedAt = true) where T : class, new() =>
            SupabaseSDK.TryLoadUserDataAttributedWithRowStateAsync(defaultWhenFailed, includeUpdatedAt);

        /// <inheritdoc cref="SupabaseSDK.LoadUserDataColumnsWithRowStateAsync{T}(string, string)"/>
        public static Task<SupabaseResult<DataColumnsLoadResult<T>>> LoadUserDataColumnsWithRowStateAsync<T>(
            string tableName,
            string selectColumnsCsv) where T : class, new() =>
            SupabaseSDK.LoadUserDataColumnsWithRowStateAsync<T>(tableName, selectColumnsCsv);

        /// <inheritdoc cref="SupabaseSDK.TryLoadUserDataColumnsWithRowStateAsync{T}(string, string, T)"/>
        public static Task<(bool success, bool hasRow, T row)> TryLoadUserDataColumnsWithRowStateAsync<T>(
            string tableName,
            string selectColumnsCsv,
            T defaultWhenFailed = default) where T : class, new() =>
            SupabaseSDK.TryLoadUserDataColumnsWithRowStateAsync(tableName, selectColumnsCsv, defaultWhenFailed);

        /// <inheritdoc cref="SupabaseSDK.TryPatchUserDataDiffAsync{T}(T, T, bool, bool)"/>
        public static Task<bool> TryPatchUserDataDiffAsync<T>(
            T previous,
            T current,
            bool ensureRowFirst = true,
            bool setUpdatedAtIsoUtc = true) =>
            SupabaseSDK.TryPatchUserDataDiffAsync(previous, current, ensureRowFirst, setUpdatedAtIsoUtc);

        /// <summary>정적 세이브 자동 동기화 쿨타임(초)을 설정합니다.</summary>
        public static void ConfigureUserSaveAutoSyncCooldown(float seconds) =>
            SupabaseSDK.ConfigureUserSaveAutoSyncCooldown(seconds);

        /// <summary>생성된 정적 세이브 타입을 자동 동기화 레지스트리에 등록합니다.</summary>
        public static void RegisterUserSaveStaticSync(
            string key,
            Func<bool> hasDirty,
            Func<Task<bool>> flushAsync,
            Action resetLocalState = null) =>
            SupabaseSDK.RegisterUserSaveStaticSync(key, hasDirty, flushAsync, resetLocalState);

        /// <summary>정적 세이브 값이 바뀌었음을 알립니다(쿨타임 스케줄).</summary>
        public static void MarkUserSaveStaticDirty(string key) =>
            SupabaseSDK.MarkUserSaveStaticDirty(key);

        /// <summary>특정 정적 세이브의 즉시 전송을 요청합니다. 전송 중이면 완료 후 1회 재시도됩니다.</summary>
        public static bool RequestImmediateUserSaveStaticFlush(string key) =>
            SupabaseSDK.RequestImmediateUserSaveStaticFlush(key);

        /// <summary>특정 정적 세이브를 즉시 전송하고 완료까지 대기합니다.</summary>
        public static Task<bool> TryFlushUserSaveImmediateAsync(string key, int timeoutMs = 5000) =>
            SupabaseSDK.TryFlushUserSaveImmediateAsync(key, timeoutMs);

        /// <summary>등록된 모든 정적 세이브에 즉시 전송을 요청합니다.</summary>
        public static void RequestImmediateUserSaveStaticFlushAll() =>
            SupabaseSDK.RequestImmediateUserSaveStaticFlushAll();

        /// <summary>등록된 모든 정적 세이브를 즉시 전송하고 완료까지 대기합니다.</summary>
        public static Task<bool> TryFlushAllUserSaveImmediateAsync(int timeoutMs = 5000) =>
            SupabaseSDK.TryFlushAllUserSaveImmediateAsync(timeoutMs);

        /// <summary>다른 사용자 공개 displayName 조회 (내부 Result API).</summary>
        internal static Task<SupabaseResult<string>> GetPublicDisplayNameAsync(string userId) =>
            SupabaseSDK.GetPublicDisplayNameAsync(userId);

        /// <summary>현재 사용자 displayName 저장 (내부 Result API).</summary>
        internal static Task<SupabaseResult<bool>> SetMyDisplayNameAsync(string displayName) =>
            SupabaseSDK.SetMyDisplayNameAsync(displayName);

        /// <inheritdoc cref="SupabaseSDK.TryGetPublicDisplayNameAsync(string, string)"/>
        public static Task<string> TryGetPublicDisplayNameAsync(string userId, string defaultValue = "") =>
            SupabaseSDK.TryGetPublicDisplayNameAsync(userId, defaultValue);

        /// <inheritdoc cref="SupabaseSDK.TrySetMyDisplayNameAsync"/>
        public static Task<bool> TrySetMyDisplayNameAsync(string displayName) =>
            SupabaseSDK.TrySetMyDisplayNameAsync(displayName);

        /// <inheritdoc cref="SupabaseSDK.TryIsDisplayNameAvailableAsync"/>
        public static Task<bool> TryIsDisplayNameAvailableAsync(string displayName) =>
            SupabaseSDK.TryIsDisplayNameAvailableAsync(displayName);

        /// <summary>displayName 사용 가능 여부 (내부 Result API).</summary>
        internal static Task<SupabaseResult<bool>> IsDisplayNameAvailableAsync(string displayName) =>
            SupabaseSDK.IsDisplayNameAvailableAsync(displayName);

        /// <summary>현재 로그인 계정을 지정 서버 코드로 이주시킵니다.</summary>
        public static Task<SupabaseResult<bool>> TransferMyServerAsync(string targetServerCode, string reason = null) =>
            SupabaseSDK.TransferMyServerAsync(targetServerCode, reason);

        /// <inheritdoc cref="SupabaseSDK.TryTransferMyServerAsync"/>
        public static Task<bool> TryTransferMyServerAsync(string targetServerCode, string reason = null) =>
            SupabaseSDK.TryTransferMyServerAsync(targetServerCode, reason);

        /// <summary>로컬에 선택한 서버 코드를 저장합니다.</summary>
        public static void SetCurrentServerCode(string serverCode) =>
            SupabaseSDK.SetCurrentServerCode(serverCode);

        /// <summary>로컬에 저장된 현재 서버 코드를 반환합니다.</summary>
        public static string GetCurrentServerCode() =>
            SupabaseSDK.GetCurrentServerCode();

        /// <summary>DB에 기록된 내 서버(<c>ts_my_server_id</c>)를 조회합니다.</summary>
        public static Task<SupabaseResult<MyServerInfo>> GetMyServerInfoAsync() =>
            SupabaseSDK.GetMyServerInfoAsync();

        /// <inheritdoc cref="SupabaseSDK.TryGetMyServerInfoAsync"/>
        public static Task<MyServerInfo> TryGetMyServerInfoAsync(MyServerInfo defaultValue = default) =>
            SupabaseSDK.TryGetMyServerInfoAsync(defaultValue);

        /// <inheritdoc cref="SupabaseSDK.TryGetPublicProfileAsync"/>
        public static Task<PublicProfileSnapshot> TryGetPublicProfileAsync(string userId) =>
            SupabaseSDK.TryGetPublicProfileAsync(userId);

        /// <summary>공개 프로필 조회 (내부 Result API).</summary>
        internal static Task<SupabaseResult<PublicProfileSnapshot>> GetPublicProfileAsync(string userId) =>
            SupabaseSDK.GetPublicProfileAsync(userId);

        /// <inheritdoc cref="SupabaseSDK.TryMarkMyWithdrawnAsync"/>
        public static Task<bool> TryMarkMyWithdrawnAsync() =>
            SupabaseSDK.TryMarkMyWithdrawnAsync();

        /// <summary>
        /// 설정(<c>SupabaseSettings.withdrawalRequestDelayDays</c>)에 정의된 유예 기간 뒤 시각으로 탈퇴를 요청합니다.
        /// 내부적으로 RPC(<c>ts_request_withdrawal</c>)를 호출해 서버 시각 기준으로 <c>profiles.withdrawn_at</c>을 예약합니다.
        /// </summary>
        public static Task<SupabaseResult<bool>> RequestMyWithdrawalAsync() =>
            SupabaseSDK.RequestMyWithdrawalAsync();

        /// <inheritdoc cref="SupabaseSDK.TryRequestMyWithdrawalAsync"/>
        public static Task<bool> TryRequestMyWithdrawalAsync() =>
            SupabaseSDK.TryRequestMyWithdrawalAsync();

        /// <inheritdoc cref="SupabaseSDK.TryClearMyWithdrawalAsync"/>
        public static Task<bool> TryClearMyWithdrawalAsync() =>
            SupabaseSDK.TryClearMyWithdrawalAsync();

        /// <summary>로그인한 본인의 탈퇴 예약 게이트 상태(닉네임/예약 시각/남은 시간)를 조회합니다.</summary>
        public static Task<SupabaseResult<MyWithdrawalStatus>> GetMyWithdrawalStatusAsync() =>
            SupabaseSDK.GetMyWithdrawalStatusAsync();

        /// <inheritdoc cref="SupabaseSDK.TryGetMyWithdrawalStatusAsync"/>
        public static Task<MyWithdrawalStatus> TryGetMyWithdrawalStatusAsync() =>
            SupabaseSDK.TryGetMyWithdrawalStatusAsync();

        /// <summary>탈퇴 예약 철회 전용 토큰 발급을 요청합니다(로그인 세션 필요).</summary>
        public static Task<SupabaseResult<string>> RequestWithdrawalCancelTokenAsync() =>
            SupabaseSDK.RequestWithdrawalCancelTokenAsync();

        /// <inheritdoc cref="SupabaseSDK.TryRequestWithdrawalCancelTokenAsync(string)"/>
        public static Task<string> TryRequestWithdrawalCancelTokenAsync(string defaultValue = null) =>
            SupabaseSDK.TryRequestWithdrawalCancelTokenAsync(defaultValue);

        /// <summary>저장된(또는 전달한) 철회 토큰으로 탈퇴 예약을 해제합니다.</summary>
        public static Task<SupabaseResult<bool>> RedeemWithdrawalCancelAsync(string cancelToken = null) =>
            SupabaseSDK.RedeemWithdrawalCancelAsync(cancelToken);

        /// <inheritdoc cref="SupabaseSDK.TryRedeemWithdrawalCancelAsync(string)"/>
        public static Task<bool> TryRedeemWithdrawalCancelAsync(string cancelToken = null) =>
            SupabaseSDK.TryRedeemWithdrawalCancelAsync(cancelToken);

        /// <summary>로컬에 저장된 탈퇴 게이트 상태를 반환합니다(로그아웃 안내 UI용).</summary>
        public static MyWithdrawalStatus GetStoredWithdrawalGateStatus() =>
            SupabaseSDK.GetStoredWithdrawalGateStatus();

        /// <inheritdoc cref="SupabaseSDK.TrySetMyWithdrawnAtAsync"/>
        public static Task<bool> TrySetMyWithdrawnAtAsync(string withdrawnAtIsoUtc) =>
            SupabaseSDK.TrySetMyWithdrawnAtAsync(withdrawnAtIsoUtc);

        /// <inheritdoc cref="SupabaseSDK.TryUpdateLastActivityAtAsync"/>
        public static Task<bool> TryUpdateLastActivityAtAsync() =>
            SupabaseSDK.TryUpdateLastActivityAtAsync();

        /// <summary>특정 key가 갱신될 때마다 콜백 (코드 연결, 실제 JSON 문자열 전달).</summary>
        public static void SubscribeRemoteConfig(string key, Action<string> onValueChanged, bool invokeIfCached = true) =>
            SupabaseSDK.SubscribeRemoteConfig(key, onValueChanged, invokeIfCached);

        public static void UnsubscribeRemoteConfig(string key, Action<string> onValueChanged) =>
            SupabaseSDK.UnsubscribeRemoteConfig(key, onValueChanged);

        public static T GetRemoteConfig<T>(string key, T defaultValue = default) =>
            SupabaseSDK.GetRemoteConfig(key, defaultValue);

        /// <inheritdoc cref="SupabaseSDK.GetRemoteConfigAsync{T}(string, int)"/>
        public static Task<SupabaseResult<T>> GetRemoteConfigAsync<T>(string key, int maxStaleSeconds = 0) where T : class, new() =>
            SupabaseSDK.GetRemoteConfigAsync<T>(key, maxStaleSeconds);

        /// <inheritdoc cref="SupabaseSDK.TryGetRemoteConfigAsync{T}(string, int)"/>
        public static Task<(bool success, T value)> TryGetRemoteConfigAsync<T>(string key, int maxStaleSeconds = 0) where T : class, new() =>
            SupabaseSDK.TryGetRemoteConfigAsync<T>(key, maxStaleSeconds);

        /// <inheritdoc cref="SupabaseSDK.SetRemoteConfigKeyPolling"/>
        public static void SetRemoteConfigKeyPolling(string key, float intervalSeconds) =>
            SupabaseSDK.SetRemoteConfigKeyPolling(key, intervalSeconds);

        /// <inheritdoc cref="SupabaseSDK.CreateRemoteConfigReader{T}(string, int)"/>
        public static Func<Task<T>> CreateRemoteConfigReader<T>(string key, int maxStaleSeconds = 0) where T : class, new() =>
            SupabaseSDK.CreateRemoteConfigReader<T>(key, maxStaleSeconds);

        /// <inheritdoc cref="SupabaseSDK.CreateRemoteConfigBinding{T}(string, float)"/>
        public static RemoteConfigBinding<T> CreateRemoteConfigBinding<T>(string key, float pollIntervalSeconds) where T : class, new() =>
            SupabaseSDK.CreateRemoteConfigBinding<T>(key, pollIntervalSeconds);

        /// <inheritdoc cref="SupabaseSDK.CreateRemoteConfigListener{T}(string, float, Action{T}, bool)"/>
        public static RemoteConfigListener<T> CreateRemoteConfigListener<T>(
            string key, float pollIntervalSeconds, Action<T> onChange, bool invokeIfCached = true)
            where T : class, new() =>
            SupabaseSDK.CreateRemoteConfigListener<T>(key, pollIntervalSeconds, onChange, invokeIfCached);

        public static bool TryGetRemoteConfigRaw(string key, out string valueJson) =>
            SupabaseSDK.TryGetRemoteConfigRaw(key, out valueJson);

        /// <summary>서버 함수 호출 (내부 Result API, 로그인 세션 필요).</summary>
        internal static Task<SupabaseResult<TResponse>> InvokeFunctionAsync<TResponse>(
            string functionName,
            object requestBody = null) =>
            SupabaseSDK.InvokeFunctionAsync<TResponse>(functionName, requestBody);

        /// <inheritdoc cref="SupabaseSDK.TryInvokeFunctionAsync{TResponse}(string, object, TResponse)"/>
        public static Task<TResponse> TryInvokeFunctionAsync<TResponse>(
            string functionName,
            object requestBody = null,
            TResponse defaultValue = default) =>
            SupabaseSDK.TryInvokeFunctionAsync(functionName, requestBody, defaultValue);

        /// <inheritdoc cref="SupabaseSDK.VerifyGooglePlayPurchaseAsync"/>
        internal static Task<SupabaseResult<GooglePlayPurchaseResponse>> VerifyGooglePlayPurchaseAsync(
            string purchaseToken,
            string productId,
            string packageName = null) =>
            SupabaseSDK.VerifyGooglePlayPurchaseAsync(purchaseToken, productId, packageName);

        /// <inheritdoc cref="SupabaseSDK.TryVerifyGooglePlayPurchaseAsync"/>
        public static Task<(bool success, GooglePlayPurchaseResponse value)> TryVerifyGooglePlayPurchaseAsync(
            string purchaseToken,
            string productId,
            string packageName = null) =>
            SupabaseSDK.TryVerifyGooglePlayPurchaseAsync(purchaseToken, productId, packageName);

        /// <inheritdoc cref="SupabaseSDK.VerifyApplePurchaseAsync"/>
        internal static Task<SupabaseResult<AppleIAPPurchaseResponse>> VerifyApplePurchaseAsync(
            string receiptData,
            string productId,
            string bundleId = null) =>
            SupabaseSDK.VerifyApplePurchaseAsync(receiptData, productId, bundleId);

        /// <inheritdoc cref="SupabaseSDK.TryVerifyApplePurchaseAsync"/>
        public static Task<(bool success, AppleIAPPurchaseResponse value)> TryVerifyApplePurchaseAsync(
            string receiptData,
            string productId,
            string bundleId = null) =>
            SupabaseSDK.TryVerifyApplePurchaseAsync(receiptData, productId, bundleId);

#if TRUESOFT_APPLE_AUTH_AVAILABLE
        /// <inheritdoc cref="SupabaseSDK.TrySignInWithAppleAsync"/>
        public static Task<bool> TrySignInWithAppleAsync(bool saveSessionToStorage = true) =>
            SupabaseSDK.TrySignInWithAppleAsync(saveSessionToStorage);

        /// <inheritdoc cref="SupabaseSDK.TrySignInWithAppleIdTokenAsync"/>
        public static Task<bool> TrySignInWithAppleIdTokenAsync(
            string idToken, string rawNonce = null, bool saveSessionToStorage = true) =>
            SupabaseSDK.TrySignInWithAppleIdTokenAsync(idToken, rawNonce, saveSessionToStorage);

        /// <inheritdoc cref="SupabaseSDK.TryLinkAppleToCurrentAnonymousAsync"/>
        public static Task<bool> TryLinkAppleToCurrentAnonymousAsync(bool saveSessionToStorage = true) =>
            SupabaseSDK.TryLinkAppleToCurrentAnonymousAsync(saveSessionToStorage);

        /// <inheritdoc cref="SupabaseSDK.TryLinkAppleToCurrentAnonymousWithIdTokenAsync"/>
        public static Task<bool> TryLinkAppleToCurrentAnonymousWithIdTokenAsync(
            string idToken, string rawNonce = null, bool saveSessionToStorage = true) =>
            SupabaseSDK.TryLinkAppleToCurrentAnonymousWithIdTokenAsync(idToken, rawNonce, saveSessionToStorage);
#endif

#if TRUESOFT_IAP_AVAILABLE
        /// <summary>
        /// Apple App Store IAP 파사드를 생성합니다. 씬별로 독립 인스턴스를 사용하세요.
        /// </summary>
        public static AppleIAPFacade CreateAppleIAP() =>
            SupabaseSDK.CreateAppleIAP();

        /// <summary>
        /// 통합 IAP 파사드를 생성합니다. Android(Google Play)와 iOS(Apple App Store)를 플랫폼 자동 감지로 처리합니다.
        /// 게임 코드에서 #if UNITY_ANDROID / #elif UNITY_IOS 분기 없이 사용할 수 있습니다.
        /// </summary>
        public static IAPFacade CreateIAP() =>
            SupabaseSDK.CreateIAP();

        /// <summary>
        /// Google Play IAP 파사드를 생성합니다. 씬별로 독립 인스턴스를 사용하세요.
        /// </summary>
        /// <example>
        /// <code>
        /// var iap = Supabase.CreateGooglePlayIAP();
        /// iap.OnGrantItemAsync = async (order, resp, isResuming) =>
        /// {
        ///     var productId = order.CartOrdered.Items()[0].Product.definition.id;
        ///     await MyInventory.GiveItemAsync(productId);
        ///     return true; // true → SDK가 ConfirmPurchase 호출 (소모품 소비)
        /// };
        /// await iap.InitializeAsync(new[] { "com.mygame.item" });
        /// </code>
        /// </example>
        public static GooglePlayIAPFacade CreateGooglePlayIAP() =>
            SupabaseSDK.CreateGooglePlayIAP();
#endif

        /// <summary>로그인 성공 시 세션을 SDK에 설정. 이후 Patch/LoadColumns API는 세션 인자 없이 사용 가능.</summary>
        public static void SetSession(SupabaseSession session) => SupabaseSDK.SetSession(session);

        /// <inheritdoc cref="SupabaseSDK.SetSession(SupabaseSession, SupabaseSessionChangeKind)"/>
        public static void SetSession(SupabaseSession session, SupabaseSessionChangeKind kind) =>
            SupabaseSDK.SetSession(session, kind);

        /// <summary>다른 기기에서 같은 계정으로 로그인해 이 기기 세션이 무효화된 경우(이미 로그아웃 처리 후). UI 팝업에 구독하세요.</summary>
        public static event Action OnDuplicateLoginDetected
        {
            add => SupabaseSDK.OnDuplicateLoginDetected += value;
            remove => SupabaseSDK.OnDuplicateLoginDetected -= value;
        }

        /// <summary>로그아웃 시 호출. clearStorage가 true면 저장된 refresh_token도 삭제.</summary>
        public static void ClearSession(bool clearStorage = true) => SupabaseSDK.ClearSession(clearStorage);

        /// <inheritdoc cref="SupabaseSDK.ClearSession(bool, bool)"/>
        public static void ClearSession(bool clearStorage, bool deleteUserSessionRow) =>
            SupabaseSDK.ClearSession(clearStorage, deleteUserSessionRow);

        /// <inheritdoc cref="SupabaseSDK.SignOutAsync"/>
        public static Task SignOutAsync(bool clearStorage = true, bool deleteUserSessionRow = true) =>
            SupabaseSDK.SignOutAsync(clearStorage, deleteUserSessionRow);

        /// <inheritdoc cref="SupabaseSDK.TrySignOutAsync"/>
        public static Task<bool> TrySignOutAsync(bool clearStorage = true, bool deleteUserSessionRow = true) =>
            SupabaseSDK.TrySignOutAsync(clearStorage, deleteUserSessionRow);

        /// <inheritdoc cref="SupabaseSDK.SignOutFullyAsync"/>
        public static Task SignOutFullyAsync(bool clearStorage = true, bool deleteUserSessionRow = true) =>
            SupabaseSDK.SignOutFullyAsync(clearStorage, deleteUserSessionRow);

        /// <inheritdoc cref="SupabaseSDK.TrySignOutFullyAsync"/>
        public static Task<bool> TrySignOutFullyAsync(bool clearStorage = true, bool deleteUserSessionRow = true) =>
            SupabaseSDK.TrySignOutFullyAsync(clearStorage, deleteUserSessionRow);

        /// <summary>현재 세션을 기기에 저장. 앱 재시작 후 RestoreSessionAsync로 복원 가능.</summary>
        public static void SaveSessionToStorage() => SupabaseSDK.SaveSessionToStorage();

        /// <summary>저장된 refresh_token으로 세션 복원 (내부 API).</summary>
        internal static Task<bool> RestoreSessionAsync() => SupabaseSDK.RestoreSessionAsync();

        /// <summary>앱 시작 자동 로그인 정책(로그아웃/이전 계정 정보 여부)을 적용해 세션 복원을 시도합니다(내부 API).</summary>
        internal static Task<bool> TryAutoLoginOnStartAsync() => SupabaseSDK.TryAutoLoginOnStartAsync();

        /// <inheritdoc cref="SupabaseSDK.TryRestoreSessionAsync"/>
        public static Task<bool> TryRestoreSessionAsync() => SupabaseSDK.TryRestoreSessionAsync();

        /// <inheritdoc cref="SupabaseSDK.GetMyMailsAsync"/>
        public static Task<SupabaseResult<IReadOnlyList<Mail>>> GetMyMailsAsync(int limit = 50, int offset = 0) =>
            SupabaseSDK.GetMyMailsAsync(limit, offset);

        /// <inheritdoc cref="SupabaseSDK.GetMailDetailAsync"/>
        public static Task<SupabaseResult<Mail>> GetMailDetailAsync(string mailId) =>
            SupabaseSDK.GetMailDetailAsync(mailId);

        /// <inheritdoc cref="SupabaseSDK.ClaimMailItemsAsync"/>
        public static Task<SupabaseResult<IReadOnlyList<ClaimResult>>> ClaimMailItemsAsync(string mailId) =>
            SupabaseSDK.ClaimMailItemsAsync(mailId);

        /// <inheritdoc cref="SupabaseSDK.ClaimAllMailItemsAsync"/>
        public static Task<SupabaseResult<IReadOnlyList<ClaimResult>>> ClaimAllMailItemsAsync() =>
            SupabaseSDK.ClaimAllMailItemsAsync();

        /// <inheritdoc cref="SupabaseSDK.DeleteMailAsync"/>
        public static Task<SupabaseResult<bool>> DeleteMailAsync(string mailId) =>
            SupabaseSDK.DeleteMailAsync(mailId);

        /// <inheritdoc cref="SupabaseSDK.DeleteReadMailsAsync"/>
        public static Task<SupabaseResult<int>> DeleteReadMailsAsync() =>
            SupabaseSDK.DeleteReadMailsAsync();

        /// <inheritdoc cref="SupabaseSDK.GetUnreadMailCountAsync"/>
        public static Task<SupabaseResult<int>> GetUnreadMailCountAsync(string userId = null) =>
            SupabaseSDK.GetUnreadMailCountAsync(userId);

        /// <inheritdoc cref="SupabaseSDK.GetUnclaimedItemMailCountAsync"/>
        public static Task<SupabaseResult<int>> GetUnclaimedItemMailCountAsync(string userId = null) =>
            SupabaseSDK.GetUnclaimedItemMailCountAsync(userId);

        /// <inheritdoc cref="SupabaseSDK.TryGetMyMailsAsync"/>
        public static Task<IReadOnlyList<Mail>> TryGetMyMailsAsync(int limit = 50, int offset = 0) =>
            SupabaseSDK.TryGetMyMailsAsync(limit, offset);

        /// <inheritdoc cref="SupabaseSDK.TryGetMailDetailAsync"/>
        public static Task<Mail> TryGetMailDetailAsync(string mailId) =>
            SupabaseSDK.TryGetMailDetailAsync(mailId);

        /// <inheritdoc cref="SupabaseSDK.TryClaimMailItemsAsync"/>
        public static Task<IReadOnlyList<ClaimResult>> TryClaimMailItemsAsync(string mailId) =>
            SupabaseSDK.TryClaimMailItemsAsync(mailId);

        /// <inheritdoc cref="SupabaseSDK.TryClaimAllMailItemsAsync"/>
        public static Task<IReadOnlyList<ClaimResult>> TryClaimAllMailItemsAsync() =>
            SupabaseSDK.TryClaimAllMailItemsAsync();

        /// <inheritdoc cref="SupabaseSDK.TryDeleteMailAsync"/>
        public static Task<bool> TryDeleteMailAsync(string mailId) =>
            SupabaseSDK.TryDeleteMailAsync(mailId);

        /// <inheritdoc cref="SupabaseSDK.TryDeleteReadMailsAsync"/>
        public static Task<int?> TryDeleteReadMailsAsync() =>
            SupabaseSDK.TryDeleteReadMailsAsync();

        /// <inheritdoc cref="SupabaseSDK.TryGetUnreadMailCountAsync"/>
        public static Task<int?> TryGetUnreadMailCountAsync(string userId = null) =>
            SupabaseSDK.TryGetUnreadMailCountAsync(userId);

        /// <inheritdoc cref="SupabaseSDK.TryGetUnclaimedItemMailCountAsync"/>
        public static Task<int?> TryGetUnclaimedItemMailCountAsync(string userId = null) =>
            SupabaseSDK.TryGetUnclaimedItemMailCountAsync(userId);

        /// <summary>우편함 파사드(<c>SupabaseSDK.Mailbox</c>와 동일 인스턴스).</summary>
        public static MailboxFacade Mailbox => SupabaseSDK.Mailbox;

        /// <summary>서버 기준 현재 시각(UTC). 로그인 없이 호출 가능합니다. SQL: <c>Sql/supabase_server_time.sql</c>.</summary>
        public static Task<SupabaseResult<DateTime>> GetServerUtcNowAsync() => SupabaseSDK.GetServerUtcNowAsync();

        /// <inheritdoc cref="SupabaseSDK.TryGetServerUtcNowAsync"/>
        public static Task<DateTime> TryGetServerUtcNowAsync(DateTime defaultValue = default) =>
            SupabaseSDK.TryGetServerUtcNowAsync(defaultValue);
    }
}
