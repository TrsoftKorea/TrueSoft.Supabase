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

        /// <inheritdoc cref="SupabaseSDK.TryLinkAppleToGuestAsync"/>
        public static Task<SupabaseResult> LinkAppleToGuestAsync() =>
            SupabaseSDK.TryLinkAppleToGuestAsync();

        /// <inheritdoc cref="SupabaseSDK.TryLinkAppleNativeAsync"/>
        public static Task<SupabaseResult> LinkAppleNativeAsync() =>
            SupabaseSDK.TryLinkAppleNativeAsync();

        /// <inheritdoc cref="SupabaseSDK.BuildOAuthAuthorizeUrl"/>
        internal static string BuildOAuthAuthorizeUrl(string provider, string redirectTo) =>
            SupabaseSDK.BuildOAuthAuthorizeUrl(provider, redirectTo);

        /// <inheritdoc cref="SupabaseSDK.TryCompleteOAuthRedirectAsync"/>
        internal static Task<SupabaseResult> TryCompleteOAuthRedirectAsync(string redirectUrl) =>
            SupabaseSDK.TryCompleteOAuthRedirectAsync(redirectUrl);

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

        /// <summary>로그인 직후 <typeparamref name="T"/>의 테이블에 본인 행이 존재하도록 보장합니다. 행이 없으면 DB 기본값으로 생성합니다.</summary>
        internal static Task<SupabaseResult<bool>> EnsureMyRowAsync<T>() =>
            SupabaseSDK.EnsureMyRowAsync<T>();

        /// <inheritdoc cref="SupabaseSDK.TryLoadUserDataAttributedWithRowStateAsync{T}(T, bool)"/>
        internal static Task<(bool success, bool hasRow, T row)> TryLoadUserDataAttributedWithRowStateAsync<T>(
            T defaultWhenFailed = default,
            bool includeUpdatedAt = true) where T : class, new() =>
            SupabaseSDK.TryLoadUserDataAttributedWithRowStateAsync(defaultWhenFailed, includeUpdatedAt);

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
            Func<float> getDirtyCooldown = null,
            Func<bool> hasFreshDirty = null) =>
            SupabaseSDK.RegisterUserSaveStaticSync(key, hasDirty, flushAsync, resetLocalState, getDirtyCooldown, hasFreshDirty);

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

        /// <summary>DB에 본인 세이브 행이 존재하도록 보장합니다. 없으면 DB 기본값으로 생성합니다(로컬 데이터는 변경하지 않음).</summary>
        public static Task<SupabaseResult> EnsureUserSaveRowAsync() =>
            UserSave != null
                ? UserSave.EnsureRowAsync()
                : Task.FromResult(SupabaseResult.Fail(SupabaseErrorCode.UserSaveNotReady));

        /// <summary>
        /// 등록된 모든 정적 세이브를 즉시 전송하고 완료까지 대기합니다.
        /// <para>세이브 타입을 모르는 SDK 내부 코드(로그아웃·앱 종료 훅) 전용입니다.
        /// 게임 코드는 <see cref="SaveNowAsync"/>를 쓰세요.</para>
        /// </summary>
        internal static async Task<SupabaseResult> SaveAllAsync(int timeoutMs = 5000)
        {
            if (!SupabaseSDK.HasPendingUserSaveFlush())
                return SupabaseResult.Fail(SupabaseErrorCode.UserSaveNoChanges);

            return await SupabaseSDK.TrySaveAllAsync(timeoutMs)
                ? SupabaseResult.Ok
                : SupabaseResult.Fail(SupabaseErrorCode.UserSaveTimeout);
        }

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

        /// <summary>특정 key가 갱신될 때마다 콜백을 호출합니다. 콜백 인자는 해당 key의 원본 JSON 문자열입니다.</summary>
        /// <param name="key">remote_config 테이블의 key.</param>
        /// <param name="onValueChanged">갱신 시 호출되는 콜백. 해지하려면 같은 델리게이트로 <see cref="UnsubscribeRemoteConfig"/>를 호출해야 합니다.</param>
        /// <param name="invokeIfCached">true면 구독 시점에 캐시된 값이 있을 때 즉시 1회 호출합니다.</param>
        internal static void SubscribeRemoteConfig(string key, Action<string> onValueChanged, bool invokeIfCached = true) =>
            SupabaseSDK.SubscribeRemoteConfig(key, onValueChanged, invokeIfCached);

        /// <summary><see cref="SubscribeRemoteConfig"/>로 등록한 콜백을 해지합니다. 등록 시와 동일한 델리게이트 인스턴스를 넘겨야 합니다.</summary>
        internal static void UnsubscribeRemoteConfig(string key, Action<string> onValueChanged) =>
            SupabaseSDK.UnsubscribeRemoteConfig(key, onValueChanged);

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
            string code, double score, string extraData = null, IReadOnlyDictionary<string, object> data = null) =>
            SupabaseSDK.TrySubmitLeaderboardScoreAsync(code, score, extraData, data);

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
            string code, string extraData = null, IReadOnlyDictionary<string, object> data = null, int? rotationCount = null) =>
            SupabaseSDK.TrySetLeaderboardPlayerDataAsync(code, extraData, data, rotationCount);

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

        /// <summary>우편함 파사드(<c>SupabaseSDK.Mailbox</c>와 동일 인스턴스).</summary>
        internal static MailboxFacade Mailbox => SupabaseSDK.Mailbox;

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
