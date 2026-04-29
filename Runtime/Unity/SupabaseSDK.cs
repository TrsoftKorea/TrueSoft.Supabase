using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Truesoft.Supabase.Core.Auth;
using Truesoft.Supabase.Core.Common;
using Truesoft.Supabase.Core.Data;
using Truesoft.Supabase.Unity.Auth.Anonymous;
using Truesoft.Supabase.Unity.Auth;
using Truesoft.Supabase.Unity.Auth.Google;
using Truesoft.Supabase.Unity.Config;
using UnityEngine;

namespace Truesoft.Supabase.Unity
{
    /// <summary>
    /// Unity용 Supabase 정적 진입점. 초기화·인증·유저 데이터·공개 닉네임·Remote Config·Edge Functions·채팅 API를 한 곳에 둡니다.
    /// </summary>
    /// <remarks>
    /// <b>구글 로그인 두 가지</b><br/>
    /// • <see cref="SignInWithGoogleAsync()"/> — Android 네이티브 플러그인으로 계정 선택·ID 토큰 획득까지 포함한 끝단 흐름.<br/>
    /// • <see cref="SignInWithGoogleIdTokenAsync"/> — 이미 가진 Google ID 토큰 문자열만 넘겨 Supabase에만 맞출 때(iOS, 커스텀 OAuth, 테스트 등). 입력 형태가 달라 둘 다 유지합니다.
    /// </remarks>
    public static class SupabaseSDK
    {
        private const string RefreshTokenKey = "Truesoft.Supabase.RefreshToken";
        private const string LastSignInMethodKey = "Truesoft.Supabase.LastSignInMethod";
        private const string AutoLoginBlockedKey = "Truesoft.Supabase.AutoLoginBlocked";
        private const string CurrentServerCodeKey = "Truesoft.Supabase.CurrentServerCode";
        private const string WithdrawalCancelTokenKey = "Truesoft.Supabase.WithdrawalCancelToken";
        private const string WithdrawalCancelTokenExpiresAtKey = "Truesoft.Supabase.WithdrawalCancelTokenExpiresAt";
        private const string WithdrawalGateDisplayNameKey = "Truesoft.Supabase.WithdrawalGateDisplayName";
        private const string WithdrawalGateWithdrawnAtKey = "Truesoft.Supabase.WithdrawalGateWithdrawnAt";
        private const string WithdrawalGateServerNowKey = "Truesoft.Supabase.WithdrawalGateServerNow";
        private const string WithdrawalGateSecondsRemainingKey = "Truesoft.Supabase.WithdrawalGateSecondsRemaining";
        private const string WithdrawalCancelIssueFunctionName = "withdrawal-cancel-issue";
        private const string WithdrawalCancelRedeemFunctionName = "withdrawal-cancel-redeem";

        /// <summary>계정별 로컬 세션 토큰 저장 키 접두어. <c>PlayerPrefs</c> 키는 <c>{접두어}{account_id}</c> 입니다.</summary>
        public const string SessionTokenPlayerPrefsKeyPrefix = "Truesoft.Supabase.SessionToken.";

        private static SupabaseUnityBootstrap _bootstrap;
        private static SupabaseSession _currentSession;
        private static UserSavesFacade _userSaves;
        private static MailboxFacade _mailbox;
        private static RemoteConfigFacade _remoteConfig;
        private static ServerFunctionsFacade _functions;
        private static string _initializedProjectUrl;
        private static bool _enableApiResultLogs = true;

        private static float _remoteConfigPollIntervalSeconds = 0f;
        private static Task _remoteConfigKeyPollTickTask;

        private static bool _duplicateSessionMonitorEnabled = true;
        private static float _duplicateSessionPollSeconds = 15f;
        private static float _duplicateSessionActionCheckCooldownSeconds = 5f;
        private static float _withdrawalRequestDelayDays = 7f;
        private static bool _enableWithdrawalGuardOnLogin = true;
        private static string _withdrawalGuardFunctionName = "withdrawal-guard";
        private static bool _isRecreatingAfterWithdrawalDelete;

        private enum SignInMethodKind
        {
            Unknown = 0,
            Anonymous = 1,
            Google = 2
        }

        /// <summary>익명 복구 RPC 경로 결과. GateBlocked/GuardFailed/Banned 시 새 익명 가입을 하면 안 된다.</summary>
        private enum AnonymousRecoveryKind
        {
            None,
            Restored,
            GateBlocked,
            GuardFailed,
            Banned
        }

        private readonly struct AnonymousRecoveryResult
        {
            public AnonymousRecoveryResult(AnonymousRecoveryKind kind, string errorMessage = null)
            {
                Kind = kind;
                ErrorMessage = errorMessage;
            }

            public AnonymousRecoveryKind Kind { get; }
            public string ErrorMessage { get; }
        }

        /// <summary>
        /// 다른 기기에서 같은 계정으로 로그인해 서버 세션 토큰이 바뀐 경우 호출됩니다(이미 <see cref="ClearSession"/> 후).
        /// UI에서 팝업을 띄우세요.
        /// </summary>
        public static event Action OnDuplicateLoginDetected;

        /// <summary><see cref="Config.SupabaseSettings.enableDuplicateSessionMonitor"/>.</summary>
        public static bool DuplicateSessionMonitorEnabled => _duplicateSessionMonitorEnabled;

        /// <summary><see cref="Config.SupabaseSettings.duplicateSessionPollSeconds"/>.</summary>
        public static float DuplicateSessionPollSeconds => _duplicateSessionPollSeconds;

        /// <summary><see cref="Config.SupabaseSettings.duplicateSessionActionCheckCooldownSeconds"/>.</summary>
        public static float DuplicateSessionActionCheckCooldownSeconds => _duplicateSessionActionCheckCooldownSeconds;

        /// <summary><see cref="Config.SupabaseSettings.withdrawalRequestDelayDays"/>.</summary>
        public static float WithdrawalRequestDelayDays => _withdrawalRequestDelayDays;

        /// <summary><see cref="Config.SupabaseSettings.enableWithdrawalGuardOnLogin"/>.</summary>
        public static bool EnableWithdrawalGuardOnLogin => _enableWithdrawalGuardOnLogin;

        /// <summary>중복 로그인 감지용 <c>user_sessions</c> REST 서비스. 미초기화 시 null.</summary>
        public static SupabaseUserSessionService UserSessionService => _bootstrap?.UserSessionService;
        public static SupabaseAnonymousRecoveryService AnonymousRecoveryService => _bootstrap?.AnonymousRecoveryService;

        /// <summary>서버 기준 시각 RPC. 로그인 없이 호출 가능합니다.</summary>
        public static SupabaseServerTimeService ServerTimeService => _bootstrap?.ServerTimeService;

        /// <summary>Try* API 결과 로그 접두어. API마다 고정이며 호출자가 넘기지 않습니다.</summary>
        private static class ApiLogTags
        {
            public const string AuthGoogleSettings = "Supabase.Auth.Google.Settings";
            public const string AuthGoogleIdToken = "Supabase.Auth.Google.IdToken";
            public const string AuthAnonymous = "Supabase.Auth.Anonymous";
            public const string AuthSignOut = "Supabase.Auth.SignOut";
            public const string AuthGoogleSignOut = "Supabase.Auth.Google.SignOut";
            public const string BootStart = "Supabase.Boot.Start";
            public const string AuthRefreshSession = "Supabase.Auth.RefreshSession";
            public const string UserDataSave = "Supabase.UserData.Save";
            public const string UserDataLoad = "Supabase.UserData.Load";
            public const string UserDataLoadAttributed = "Supabase.UserData.LoadAttributed";
            public const string UserDataLoadAttributedRowState = "Supabase.UserData.LoadAttributed.RowState";
            public const string UserDataLoadColumnsRowState = "Supabase.UserData.LoadColumns.RowState";
            public const string UserDataPatchDiff = "Supabase.UserData.PatchDiff";
            public const string EdgeFunctionInvoke = "Supabase.EdgeFunction.Invoke";
            public const string RemoteConfigRefresh = "Supabase.RemoteConfig.Refresh";
            public const string RemoteConfigPoll = "Supabase.RemoteConfig.Poll";
            public const string RemoteConfigGet = "Supabase.RemoteConfig.Get";
            public const string AuthRestoreSession = "Supabase.Auth.RestoreSession";
            public const string ProfilePublicDisplayNameGet = "Supabase.Profile.DisplayName.Get";
            public const string ProfileMyDisplayNameSet = "Supabase.Profile.DisplayName.Set";
            public const string ProfileDisplayNameAvailable = "Supabase.Profile.DisplayName.Available";
            public const string ProfileMyServerGet = "Supabase.Profile.Server.Get";
            public const string ProfileServerTransfer = "Supabase.Profile.Server.Transfer";
            public const string ProfileSnapshotGet = "Supabase.Profile.Snapshot.Get";
            public const string ProfileWithdrawnAt = "Supabase.Profile.WithdrawnAt";
            public const string ProfileWithdrawnRequest = "Supabase.Profile.Withdrawn.Request";
            public const string ProfileWithdrawalStatus = "Supabase.Profile.Withdrawal.Status";
            public const string ProfileWithdrawalCancelIssue = "Supabase.Profile.Withdrawal.Cancel.Issue";
            public const string ProfileWithdrawalCancelRedeem = "Supabase.Profile.Withdrawal.Cancel.Redeem";
            public const string ServerTime = "Supabase.Server.Time";
            public const string MailboxList = "Supabase.Mailbox.List";
            public const string MailboxDetail = "Supabase.Mailbox.Detail";
            public const string MailboxClaimOne = "Supabase.Mailbox.Claim.One";
            public const string MailboxClaimAll = "Supabase.Mailbox.Claim.All";
            public const string MailboxDelete = "Supabase.Mailbox.Delete";
            public const string MailboxDeleteReadBulk = "Supabase.Mailbox.Delete.ReadBulk";
            public const string MailboxUnreadCount = "Supabase.Mailbox.UnreadCount";
            public const string MailboxUnclaimedCount = "Supabase.Mailbox.UnclaimedCount";
        }

        internal static void UpdateRemoteConfigPollIntervalSeconds(float intervalSeconds)
        {
            _remoteConfigPollIntervalSeconds = intervalSeconds <= 0f ? 0f : intervalSeconds;
        }

        private static void RequestRemoteConfigPollingReset()
        {
            if (!IsInitialized)
                return;

            var delay = _remoteConfigPollIntervalSeconds <= 0f ? 60f : _remoteConfigPollIntervalSeconds;
            RemoteConfig.PushBackAllKeyPolls(Time.realtimeSinceStartup, delay);
        }

        /// <summary>
        /// 키별 RemoteConfig 폴링 주기를 인스펙터에서 덮어씁니다. <see cref="SupabaseRuntime"/>에서 한 번 호출하면 됩니다.
        /// 설계: 1키 = 1설정묶음(JSON) = 1폴링주기 (category 없음)
        /// </summary>
        public static void ApplyRemoteConfigKeyPollOverrides(IReadOnlyList<RemoteConfigKeyPollOverrideEntry> entries)
        {
            RemoteConfig.ClearKeyPollIntervalOverrides();
            if (entries == null)
                return;

            foreach (var e in entries)
            {
                if (e == null || string.IsNullOrWhiteSpace(e.key))
                    continue;

                RemoteConfig.SetKeyPollIntervalOverride(e.key.Trim(), e.overrideIntervalSeconds);
            }
        }

        /// <summary>
        /// DB의 <c>poll_interval_seconds</c>에 따라 만기된 키만 폴링합니다. <see cref="SupabaseRuntime.Update"/> 등에서 호출하세요.
        /// </summary>
        public static void TickRemoteConfigKeyPolls(float realtimeSinceStartup)
        {
            if (!IsInitialized)
                return;

            if (_remoteConfigKeyPollTickTask != null && !_remoteConfigKeyPollTickTask.IsCompleted)
                return;

            _remoteConfigKeyPollTickTask = RemoteConfig.TickKeyPollsAsync(realtimeSinceStartup);
        }

        /// <summary><see cref="EnsureInitializedAsync"/> 기본 대기 시간(ms). 씬의 <c>SupabaseRuntime</c> Awake를 기다립니다.</summary>
        public const int DefaultEnsureInitTimeoutMs = 30000;

        /// <summary>SDK가 초기화되었는지 여부.</summary>
        public static bool IsInitialized => _bootstrap != null;

        /// <summary>현재 로그인된 세션. 로그인 후 SetSession으로 설정하세요.</summary>
        public static SupabaseSession Session => _currentSession;

        /// <summary>현재 로그인 여부 (세션이 있고 유효한 토큰이 있는지).</summary>
        public static bool IsLoggedIn =>
            _currentSession != null
            && string.IsNullOrWhiteSpace(_currentSession.AccessToken) == false
            && _currentSession.User != null
            && string.IsNullOrWhiteSpace(_currentSession.User.Id) == false;

        /// <summary>
        /// 씬의 <c>SupabaseRuntime</c> 등으로 초기화될 때까지 대기한 뒤, 실패 시 <c>Resources/SupabaseSettings</c>로 부트스트랩을 시도합니다.
        /// Unity 메인 스레드에서 호출하는 것을 권장합니다.
        /// </summary>
        public static async Task<bool> EnsureInitializedAsync(int timeoutMs = DefaultEnsureInitTimeoutMs)
        {
            if (IsInitialized)
                return true;

            // SupabaseRuntime(Awake) 쪽 초기화를 잠시 기다립니다.
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (!IsInitialized && sw.ElapsedMilliseconds < timeoutMs)
                await Task.Delay(16);

            if (IsInitialized)
                return true;

            return TryBootstrapFromResources();
        }

        /// <summary>
        /// 동기 API에서만 사용. <see cref="EnsureInitializedAsync"/> 없이 호출할 때 Resources에 설정이 있으면 초기화합니다.
        /// </summary>
        private static void EnsureInitializedOrBootstrapSync()
        {
            if (IsInitialized)
                return;
            TryBootstrapFromResources();
        }

        /// <summary>
        /// <c>Resources/SupabaseSettings</c>를 로드해 <see cref="SupabaseUnityBootstrap.Initialize"/>를 호출합니다.
        /// 씬에 <c>SupabaseRuntime</c>이 없을 때의 보조 경로입니다.
        /// </summary>
        private static bool TryBootstrapFromResources()
        {
            if (IsInitialized)
                return true;

            // 씬에 Runtime이 없어도 동작하도록 Resources의 기본 설정으로 직접 부트스트랩합니다.
            var settings = Resources.Load<SupabaseSettings>("SupabaseSettings");
            if (settings == null)
            {
                Debug.LogWarning(
                    "[Supabase] SupabaseSettings를 Resources에서 찾을 수 없습니다. "
                    + "씬에 SupabaseRuntime을 두거나 Resources/SupabaseSettings 에셋을 추가하세요.");
                return false;
            }

            var bootstrap = new SupabaseUnityBootstrap();
            bootstrap.Initialize(settings);
            return IsInitialized;
        }

        /// <summary>인증 서비스. 가능하면 Resources 부트스트랩 후 반환합니다.</summary>
        public static SupabaseAuthService Auth
        {
            get
            {
                EnsureInitializedOrBootstrapSync();
                return _bootstrap?.AuthService;
            }
        }

        /// <summary>이미 가진 Google ID 토큰 문자열로 Supabase에 로그인하고 SDK 세션을 맞춥니다.</summary>
        /// <remarks>
        /// Android 네이티브 <see cref="SignInWithGoogleAsync(bool)"/>와 달리 «토큰 획득»은 호출자 책임입니다(iOS 플러그인, 웹 OAuth, 수동 입력 등).
        /// 익명(게스트) 세션에서는 자동 연동을 수행하지 않습니다. 연동은 <see cref="LinkGoogleToCurrentAnonymousWithIdTokenAsync"/>를 사용하세요.
        /// </remarks>
        public static async Task<SupabaseResult<SupabaseSession>> SignInWithGoogleIdTokenAsync(string idToken, bool saveSessionToStorage = true)
        {
            if (!await EnsureInitializedAsync())
                return SupabaseResult<SupabaseSession>.Fail("sdk_not_initialized");

            if (Auth == null)
                return SupabaseResult<SupabaseSession>.Fail("sdk_not_initialized");

            if (IsAnonymousSession(_currentSession))
                return SupabaseResult<SupabaseSession>.Fail("anonymous_session_requires_explicit_link");

            var result = await Auth.SignInWithGoogleIdTokenAsync(idToken);
            if (result.IsSuccess && result.Data != null)
            {
                SetSession(result.Data, SupabaseSessionChangeKind.NewSignIn);
                if (saveSessionToStorage)
                    SaveSessionToStorage();

                RememberLastSignInMethod(SignInMethodKind.Google);
                if (!_isRecreatingAfterWithdrawalDelete)
                {
                    var guarded = await HandleWithdrawalGuardAfterSignInAsync(
                        SignInMethodKind.Google,
                        saveSessionToStorage,
                        allowRecreateOnDeletion: true);
                    if (guarded != null)
                        return guarded;

                    var reserved = await HandleWithdrawalReservationGateAfterSignInAsync();
                    if (reserved != null)
                        return reserved;
                }

                await TryApplyAnonymousDisplayNameAfterNewGoogleSignUpAsync(saveSessionToStorage);
                await TryEnsureProfileRowAfterSignInAsync();
            }

            return result;
        }

        /// <summary>
        /// 현재 익명 세션에 Google identity를 연동합니다(ID 토큰 직접 전달).
        /// 성공 시 같은 <c>auth.users.id</c>를 유지한 채 갱신된 세션을 받으며, 익명 플래그가 해제되어야 합니다.
        /// </summary>
        public static async Task<SupabaseResult<SupabaseSession>> LinkGoogleToCurrentAnonymousWithIdTokenAsync(
            string idToken,
            string googleAccessToken = null,
            bool saveSessionToStorage = true)
        {
            if (!await EnsureInitializedAsync())
                return SupabaseResult<SupabaseSession>.Fail("sdk_not_initialized");

            if (Auth == null)
                return SupabaseResult<SupabaseSession>.Fail("sdk_not_initialized");

            if (string.IsNullOrWhiteSpace(idToken))
                return SupabaseResult<SupabaseSession>.Fail("google_id_token_empty");

            var s = _currentSession;
            if (!IsAnonymousSession(s))
                return SupabaseResult<SupabaseSession>.Fail("anonymous_session_required");

            if (string.IsNullOrWhiteSpace(s.AccessToken) || string.IsNullOrWhiteSpace(s.RefreshToken))
                return SupabaseResult<SupabaseSession>.Fail("anonymous_session_token_missing");

            var linked = await Auth.LinkIdentityWithIdTokenAsync(
                s.AccessToken,
                "google",
                idToken.Trim(),
                nonce: null,
                oauthAccessToken: googleAccessToken);
            if (linked == null || !linked.IsSuccess || linked.Data == null)
                return SupabaseResult<SupabaseSession>.Fail(linked?.ErrorMessage ?? "google_link_failed");

            if (linked.Data.User == null || linked.Data.User.IsAnonymous)
                return SupabaseResult<SupabaseSession>.Fail("google_link_anonymous_not_cleared");

            SetSession(linked.Data, SupabaseSessionChangeKind.RestoredOrRefreshed);
            if (saveSessionToStorage)
                SaveSessionToStorage();

            RememberLastSignInMethod(SignInMethodKind.Google);
            await TryDeleteAnonymousRecoveryForCurrentDeviceAsync();
            if (!_isRecreatingAfterWithdrawalDelete)
            {
                var guarded = await HandleWithdrawalGuardAfterSignInAsync(
                    SignInMethodKind.Google,
                    saveSessionToStorage,
                    allowRecreateOnDeletion: true);
                if (guarded != null)
                    return guarded;

                var reserved = await HandleWithdrawalReservationGateAfterSignInAsync();
                if (reserved != null)
                    return reserved;
            }

            await TryEnsureProfileRowAfterSignInAsync();
            return SupabaseResult<SupabaseSession>.Success(_currentSession);
        }

        /// <summary>
        /// <c>Resources/SupabaseSettings</c>에 입력한 <c>googleWebClientId</c>로 Android 네이티브 Google 로그인을 수행합니다.
        /// </summary>
        /// <remarks>
        /// 인스펙터에 Web Client ID를 저장해 두고, 게임 코드에서는 인자 없이 호출할 때 쓰는 오버로드입니다.
        /// </remarks>
        public static async Task<SupabaseResult<SupabaseSession>> SignInWithGoogleAsync(bool saveSessionToStorage = true)
        {
            var webClientId = TryGetGoogleWebClientIdFromSettings();
            if (string.IsNullOrWhiteSpace(webClientId))
                return SupabaseResult<SupabaseSession>.Fail("google_web_client_id_empty");

            if (!await EnsureInitializedAsync())
                return SupabaseResult<SupabaseSession>.Fail("sdk_not_initialized");

            if (Auth == null)
                return SupabaseResult<SupabaseSession>.Fail("sdk_not_initialized");

            if (IsAnonymousSession(_currentSession))
                return SupabaseResult<SupabaseSession>.Fail("anonymous_session_requires_explicit_link");

            var bridge = EnsureGoogleLoginBridge();
            var provider = new AndroidGoogleLoginProvider(bridge, webClientId.Trim());
            var loginResult = await RequestGoogleLoginResultAsync(provider);
            if (loginResult == null || !loginResult.IsSuccess || loginResult.Data == null)
                return SupabaseResult<SupabaseSession>.Fail(loginResult?.ErrorMessage ?? "google_signin_failed");

            if (string.IsNullOrWhiteSpace(loginResult.Data.IdToken))
                return SupabaseResult<SupabaseSession>.Fail("google_id_token_empty");

            var googleResult = await Auth.SignInWithGoogleIdTokenAsync(loginResult.Data.IdToken.Trim());
            if (googleResult.IsSuccess && googleResult.Data != null)
            {
                SetSession(googleResult.Data, SupabaseSessionChangeKind.NewSignIn);
                if (saveSessionToStorage)
                    SaveSessionToStorage();

                RememberLastSignInMethod(SignInMethodKind.Google);

                if (!_isRecreatingAfterWithdrawalDelete)
                {
                    var guarded = await HandleWithdrawalGuardAfterSignInAsync(
                        SignInMethodKind.Google,
                        saveSessionToStorage,
                        allowRecreateOnDeletion: true);
                    if (guarded != null)
                        return guarded;

                    var reserved = await HandleWithdrawalReservationGateAfterSignInAsync();
                    if (reserved != null)
                        return reserved;

                    await TryApplyAnonymousDisplayNameAfterNewGoogleSignUpAsync(saveSessionToStorage);
                    await TryEnsureProfileRowAfterSignInAsync();
                }
            }

            return googleResult;
        }

        /// <summary>
        /// 현재 익명 세션에 Google identity를 연동합니다(Android 네이티브 Google 로그인 사용).
        /// </summary>
        public static async Task<SupabaseResult<SupabaseSession>> LinkGoogleToCurrentAnonymousAsync(bool saveSessionToStorage = true)
        {
            var webClientId = TryGetGoogleWebClientIdFromSettings();
            if (string.IsNullOrWhiteSpace(webClientId))
                return SupabaseResult<SupabaseSession>.Fail("google_web_client_id_empty");

            if (!await EnsureInitializedAsync())
                return SupabaseResult<SupabaseSession>.Fail("sdk_not_initialized");

            if (Auth == null)
                return SupabaseResult<SupabaseSession>.Fail("sdk_not_initialized");

            var bridge = EnsureGoogleLoginBridge();
            var provider = new AndroidGoogleLoginProvider(bridge, webClientId.Trim());
            var loginResult = await RequestGoogleLoginResultAsync(provider);
            if (loginResult == null || !loginResult.IsSuccess || loginResult.Data == null)
                return SupabaseResult<SupabaseSession>.Fail(loginResult?.ErrorMessage ?? "google_signin_failed");

            var google = loginResult.Data;
            return await LinkGoogleToCurrentAnonymousWithIdTokenAsync(
                google.IdToken,
                google.AccessToken,
                saveSessionToStorage);
        }

        /// <summary><see cref="SignInWithGoogleAsync(bool)"/>를 bool 기반으로 호출합니다.</summary>
        public static async Task<bool> TrySignInWithGoogleAsync(bool saveSessionToStorage = true)
        {
            var r = await SignInWithGoogleAsync(saveSessionToStorage);
            return LogAndReturn(ApiLogTags.AuthGoogleSettings, r);
        }

        /// <summary><see cref="SignInWithGoogleIdTokenAsync"/>를 bool 기반으로 호출합니다.</summary>
        public static async Task<bool> TrySignInWithGoogleIdTokenAsync(string idToken, bool saveSessionToStorage = true)
        {
            var r = await SignInWithGoogleIdTokenAsync(idToken, saveSessionToStorage);
            return LogAndReturn(ApiLogTags.AuthGoogleIdToken, r);
        }

        /// <summary><see cref="LinkGoogleToCurrentAnonymousAsync(bool)"/>를 bool 기반으로 호출합니다.</summary>
        public static async Task<bool> TryLinkGoogleToCurrentAnonymousAsync(bool saveSessionToStorage = true)
        {
            var r = await LinkGoogleToCurrentAnonymousAsync(saveSessionToStorage);
            return LogAndReturn(ApiLogTags.AuthGoogleSettings, r);
        }

        /// <summary><see cref="LinkGoogleToCurrentAnonymousWithIdTokenAsync"/>를 bool 기반으로 호출합니다.</summary>
        public static async Task<bool> TryLinkGoogleToCurrentAnonymousWithIdTokenAsync(
            string idToken,
            string googleAccessToken = null,
            bool saveSessionToStorage = true)
        {
            var r = await LinkGoogleToCurrentAnonymousWithIdTokenAsync(idToken, googleAccessToken, saveSessionToStorage);
            return LogAndReturn(ApiLogTags.AuthGoogleIdToken, r);
        }

        /// <summary><see cref="SignInAnonymouslyAsync"/>를 bool 기반으로 호출합니다.</summary>
        public static async Task<bool> TrySignInAnonymouslyAsync(bool saveSessionToStorage = true)
        {
            var r = await SignInAnonymouslyAsync(saveSessionToStorage);
            return LogAndReturn(ApiLogTags.AuthAnonymous, r);
        }

        /// <summary><see cref="SignOutFromGoogleAsync"/>를 bool 기반으로 호출합니다.</summary>
        public static async Task<bool> TrySignOutFromGoogleAsync()
        {
            var r = await SignOutFromGoogleAsync();
            return LogAndReturn(ApiLogTags.AuthGoogleSignOut, r);
        }

        /// <summary><see cref="StartAsync"/>를 bool 기반으로 호출합니다.</summary>
        public static async Task<bool> TryStartAsync(
            bool restoreSessionFirst = true,
            bool refreshRemoteConfigOnStart = false)
        {
            var ok = await StartAsync(restoreSessionFirst, refreshRemoteConfigOnStart);
            LogApiResult(ApiLogTags.BootStart, ok, ok ? null : "start_failed");
            return ok;
        }

        /// <summary><see cref="RefreshSessionAsync"/>를 bool 기반으로 호출합니다.</summary>
        public static async Task<bool> TryRefreshSessionAsync(string refreshToken, bool saveSessionToStorage = true)
        {
            var r = await RefreshSessionAsync(refreshToken, saveSessionToStorage);
            return LogAndReturn(ApiLogTags.AuthRefreshSession, r);
        }

        /// <inheritdoc cref="LoadUserDataAttributedAsync{T}(bool)"/>
        public static async Task<T> TryLoadUserDataAttributedAsync<T>(T defaultValue = default, bool includeUpdatedAt = true) where T : class, new()
        {
            var r = await LoadUserDataAttributedAsync<T>(includeUpdatedAt);
            return LogAndReturnData(ApiLogTags.UserDataLoadAttributed, r, defaultValue);
        }

        /// <summary>
        /// <see cref="DataColumnAttribute"/> 기반 로드 결과에 <b>본인 행 존재 여부</b>(HTTP 200·빈 배열이면 <c>HasRow == false</c>)를 포함합니다.
        /// 인증/HTTP/파싱 실패는 <see cref="SupabaseResult{T}.IsSuccess"/>가 <c>false</c>입니다.
        /// </summary>
        public static async Task<SupabaseResult<DataColumnsLoadResult<T>>> LoadUserDataAttributedWithRowStateAsync<T>(
            bool includeUpdatedAt = true) where T : class, new()
        {
            var ready = await EnsureReadySessionAsync();
            if (!ready.IsSuccess)
                return SupabaseResult<DataColumnsLoadResult<T>>.Fail(ready.ErrorMessage ?? "auth_not_signed_in");

            return await UserSaves.LoadAttributedWithRowStateAsync<T>(includeUpdatedAt);
        }

        /// <summary>
        /// <see cref="LoadUserDataAttributedWithRowStateAsync{T}(bool)"/>의 Try 형태. 실패 시 <c>success == false</c>, <c>hasRow</c>는 의미 없음.
        /// </summary>
        public static async Task<(bool success, bool hasRow, T row)> TryLoadUserDataAttributedWithRowStateAsync<T>(
            T defaultWhenFailed = default,
            bool includeUpdatedAt = true) where T : class, new()
        {
            var r = await LoadUserDataAttributedWithRowStateAsync<T>(includeUpdatedAt);
            var ok = r != null && r.IsSuccess;
            LogApiResult(ApiLogTags.UserDataLoadAttributedRowState, ok, ok ? null : r?.ErrorMessage);
            if (!ok)
            {
                var d = defaultWhenFailed ?? new T();
                return (false, false, d);
            }

            return (true, r.Data.HasRow, r.Data.Row);
        }

        /// <summary>
        /// 명시 <c>select</c> 컬럼 로드에 행 존재 여부를 포함합니다.
        /// </summary>
        public static async Task<SupabaseResult<DataColumnsLoadResult<T>>> LoadUserDataColumnsWithRowStateAsync<T>(
            string tableName,
            string selectColumnsCsv) where T : class, new()
        {
            var ready = await EnsureReadySessionAsync();
            if (!ready.IsSuccess)
                return SupabaseResult<DataColumnsLoadResult<T>>.Fail(ready.ErrorMessage ?? "auth_not_signed_in");

            if (string.IsNullOrWhiteSpace(selectColumnsCsv))
                return SupabaseResult<DataColumnsLoadResult<T>>.Fail("select_columns_empty");

            return await UserSaves.LoadColumnsWithRowStateAsync<T>(tableName, selectColumnsCsv);
        }

        /// <inheritdoc cref="LoadUserDataColumnsWithRowStateAsync{T}(string, string)"/>
        public static async Task<(bool success, bool hasRow, T row)> TryLoadUserDataColumnsWithRowStateAsync<T>(
            string tableName,
            string selectColumnsCsv,
            T defaultWhenFailed = default) where T : class, new()
        {
            var r = await LoadUserDataColumnsWithRowStateAsync<T>(tableName, selectColumnsCsv);
            var ok = r != null && r.IsSuccess;
            LogApiResult(ApiLogTags.UserDataLoadColumnsRowState, ok, ok ? null : r?.ErrorMessage);
            if (!ok)
            {
                var d = defaultWhenFailed ?? new T();
                return (false, false, d);
            }

            return (true, r.Data.HasRow, r.Data.Row);
        }

        /// <inheritdoc cref="PatchUserDataDiffAsync{T}(T, T, bool, bool)"/>
        public static async Task<bool> TryPatchUserDataDiffAsync<T>(
            T previous,
            T current,
            bool ensureRowFirst = true,
            bool setUpdatedAtIsoUtc = true)
        {
            var r = await PatchUserDataDiffAsync(previous, current, ensureRowFirst, setUpdatedAtIsoUtc);
            return LogAndReturn(ApiLogTags.UserDataPatchDiff, r);
        }

        /// <summary>정적 세이브 자동 동기화 쿨타임(초)을 설정합니다.</summary>
        public static void ConfigureUserSaveAutoSyncCooldown(float seconds)
        {
            UserSaveStaticSyncRegistry.ConfigureCooldown(seconds);
        }

        /// <summary>생성된 정적 세이브 타입을 자동 동기화 레지스트리에 등록합니다.</summary>
        public static void RegisterUserSaveStaticSync(
            string key,
            Func<bool> hasDirty,
            Func<Task<bool>> flushAsync,
            Action resetLocalState = null)
        {
            UserSaveStaticSyncRegistry.Register(key, hasDirty, flushAsync, resetLocalState);
        }

        /// <summary>정적 세이브에 변경이 생겼음을 알립니다(쿨타임 스케줄).</summary>
        public static void MarkUserSaveStaticDirty(string key)
        {
            UserSaveStaticSyncRegistry.MarkDirty(key);
        }

        /// <summary>특정 정적 세이브를 즉시 전송 시도합니다. 전송 중이면 완료 후 1회 재시도됩니다.</summary>
        public static bool RequestImmediateUserSaveStaticFlush(string key)
        {
            return UserSaveStaticSyncRegistry.RequestImmediateFlush(key);
        }

        /// <summary>특정 정적 세이브를 즉시 전송하고 완료까지 대기합니다. 전송 중이면 완료 후 1회까지 반영 대기합니다.</summary>
        public static Task<bool> TryFlushUserSaveImmediateAsync(string key, int timeoutMs = 5000)
        {
            return UserSaveStaticSyncRegistry.RequestImmediateFlushAsync(key, timeoutMs);
        }

        /// <summary>등록된 모든 정적 세이브에 즉시 전송을 요청합니다.</summary>
        public static void RequestImmediateUserSaveStaticFlushAll()
        {
            UserSaveStaticSyncRegistry.RequestImmediateFlushAll();
        }

        /// <summary>등록된 모든 정적 세이브를 즉시 전송하고 완료까지 대기합니다.</summary>
        public static Task<bool> TryFlushAllUserSaveImmediateAsync(int timeoutMs = 5000)
        {
            return UserSaveStaticSyncRegistry.RequestImmediateFlushAllAsync(timeoutMs);
        }

        /// <summary>정적 세이브 자동 동기화 타이머를 진행합니다. (보통 SupabaseRuntime.Update에서 호출)</summary>
        internal static void TickUserSaveAutoSync(float realtimeNow)
        {
            UserSaveStaticSyncRegistry.Tick(realtimeNow);
        }

        /// <summary><see cref="InvokeFunctionAsync{TResponse}(string, object)"/>를 호출하고 성공 시 데이터를 반환, 실패 시 default를 반환합니다.</summary>
        public static async Task<TResponse> TryInvokeFunctionAsync<TResponse>(
            string functionName,
            object requestBody = null,
            TResponse defaultValue = default)
        {
            var r = await InvokeFunctionAsync<TResponse>(functionName, requestBody);
            return LogAndReturnData(ApiLogTags.EdgeFunctionInvoke, r, defaultValue);
        }

        /// <summary><see cref="RefreshRemoteConfigAsync"/>를 bool 기반으로 호출합니다.</summary>
        public static async Task<bool> TryRefreshRemoteConfigAsync()
        {
            var ok = await RefreshRemoteConfigAsync();
            if (ok == false)
            {
                LogApiResult(ApiLogTags.RemoteConfigRefresh, ok, "remote_config_refresh_failed");
                return false;
            }

            // 변경이 없으면 로그를 찍지 않습니다(폴링 성공 로그 스팸 방지).
            if (_remoteConfig != null && _remoteConfig.LastApplyHadChanges)
                LogApiResult(ApiLogTags.RemoteConfigRefresh, true, null);

            return ok;
        }

        /// <summary><see cref="PollRemoteConfigAsync"/>를 bool 기반으로 호출합니다.</summary>
        public static async Task<bool> TryPollRemoteConfigAsync()
        {
            var ok = await PollRemoteConfigAsync();
            if (ok == false)
            {
                LogApiResult(ApiLogTags.RemoteConfigPoll, ok, "remote_config_poll_failed");
                return false;
            }

            // 실제 변경이 있을 때만 success 로그를 찍습니다.
            if (_remoteConfig != null && _remoteConfig.LastApplyHadChanges)
                LogApiResult(ApiLogTags.RemoteConfigPoll, true, null);

            return ok;
        }

        /// <summary>
        /// 온디맨드 RemoteConfig 동기화: 서버에서 즉시 최신 값을 받아 캐시를 갱신합니다.
        /// 호출 직후에는 주기 폴링 타이머를 초기화(의도치 않게 더 자주 호출되지 않도록 다음 폴링을 뒤로 미룸)합니다.
        /// </summary>
        public static async Task<bool> RefreshRemoteConfigOnDemandAsync()
        {
            var ok = await TryRefreshRemoteConfigAsync();
            if (ok)
                RequestRemoteConfigPollingReset();
            return ok;
        }

        /// <summary>
        /// <see cref="GetRemoteConfigAsync{T}(string)"/>를 호출하고 성공 여부를 로그로 남깁니다.
        /// 실패 시 <c>value</c>는 null입니다.
        /// </summary>
        public static async Task<(bool success, T value)> TryGetRemoteConfigAsync<T>(string key) where T : class, new()
        {
            var r = await GetRemoteConfigAsync<T>(key);
            var ok = r.IsSuccess;
            LogApiResult(ApiLogTags.RemoteConfigGet, ok, ok ? null : r.ErrorMessage ?? "remote_config_get_failed");
            return (ok, ok ? r.Data : null);
        }


        /// <summary><see cref="RestoreSessionAsync"/>를 bool 기반으로 호출합니다.</summary>
        public static async Task<bool> TryRestoreSessionAsync()
        {
            var ok = await RestoreSessionAsync();
            LogApiResult(ApiLogTags.AuthRestoreSession, ok, ok ? null : "restore_session_failed");
            return ok;
        }

        /// <summary>
        /// 앱 시작 자동 로그인 정책을 적용해 세션 복원을 시도합니다.
        /// 로그아웃으로 자동 로그인이 차단되었거나, 저장된 이전 계정 정보(refresh token)가 없으면 아무 동작도 하지 않습니다.
        /// </summary>
        public static async Task<bool> TryAutoLoginOnStartAsync()
        {
            if (IsAutoLoginBlocked() || HasStoredRefreshToken() == false)
                return false;

            var ok = await RestoreSessionAsync();
            LogApiResult(ApiLogTags.AuthRestoreSession, ok, ok ? null : "auto_login_on_start_failed");
            return ok;
        }

        /// <summary>
        /// Postgres RPC <c>ts_server_now</c>로 서버 시각(UTC)을 가져옵니다. 로그인 세션 없이 Publishable 키로 호출합니다.
        /// </summary>
        public static async Task<SupabaseResult<DateTime>> GetServerUtcNowAsync()
        {
            if (!await EnsureInitializedAsync())
                return SupabaseResult<DateTime>.Fail("sdk_not_initialized");

            var svc = ServerTimeService;
            if (svc == null)
                return SupabaseResult<DateTime>.Fail("sdk_not_initialized");

            return await svc.GetServerUtcNowAsync();
        }

        /// <summary><see cref="GetServerUtcNowAsync"/>를 값 기반으로 호출합니다.</summary>
        public static async Task<DateTime> TryGetServerUtcNowAsync(DateTime defaultValue = default)
        {
            var r = await GetServerUtcNowAsync();
            return LogAndReturnData(ApiLogTags.ServerTime, r, defaultValue);
        }

        private static bool LogAndReturn<T>(string logTag, SupabaseResult<T> result)
        {
            var ok = result != null && result.IsSuccess;
            LogApiResult(logTag, ok, ok ? null : result?.ErrorMessage);
            return ok;
        }

        private static T LogAndReturnData<T>(string logTag, SupabaseResult<T> result, T defaultValue)
        {
            var ok = result != null && result.IsSuccess;
            LogApiResult(logTag, ok, ok ? null : result?.ErrorMessage);
            return ok ? result.Data : defaultValue;
        }

        private static void LogApiResult(string logTag, bool isSuccess, string message = null)
        {
            if (!_enableApiResultLogs)
                return;

            var prefix = $"[{logTag}]";
            if (isSuccess)
            {
                Debug.Log($"{prefix} success");
                return;
            }

            var detail = string.IsNullOrWhiteSpace(message) ? "unknown_error" : message.Trim();
            Debug.LogError($"{prefix} failed: {detail}");
        }

        /// <summary>
        /// Android 네이티브 Google 계정에서 로그아웃합니다. (Supabase 세션은 그대로이므로 필요하면 <see cref="ClearSession"/> 호출)
        /// </summary>
        public static async Task<SupabaseResult<bool>> SignOutFromGoogleAsync()
        {
            if (!await EnsureInitializedAsync())
                return SupabaseResult<bool>.Fail("sdk_not_initialized");

            var bridge = EnsureGoogleLoginBridge();
            var tcs = new TaskCompletionSource<SupabaseResult<bool>>(TaskCreationOptions.RunContinuationsAsynchronously);

            bridge.SignOut(
                () => tcs.TrySetResult(SupabaseResult<bool>.Success(true)),
                err => tcs.TrySetResult(
                    SupabaseResult<bool>.Fail(string.IsNullOrWhiteSpace(err) ? "google_signout_failed" : err)));

            return await tcs.Task;
        }

        /// <summary>게스트(익명)로 로그인하고 SDK 세션을 자동 설정합니다.</summary>
        /// <remarks>
        /// 저장된 refresh_token이 있으면 먼저 <see cref="RestoreSessionAsync"/>를 시도해 동일 계정을 이어갑니다(수동 로그인 버튼 흐름).<br/>
        /// 이미 Google 등 <b>비익명</b>으로 로그인 중이면 실패(<c>signed_in_non_anonymous_sign_out_first</c>) — 먼저 로그아웃한 뒤 호출하세요.
        /// </remarks>
        public static async Task<SupabaseResult<SupabaseSession>> SignInAnonymouslyAsync(bool saveSessionToStorage = true)
        {
            if (!await EnsureInitializedAsync())
                return SupabaseResult<SupabaseSession>.Fail("sdk_not_initialized");

            if (IsLoggedIn && !IsAnonymousSession(_currentSession))
                return SupabaseResult<SupabaseSession>.Fail("signed_in_non_anonymous_sign_out_first");

            var forceFreshAnonymous = false;
            if (!IsLoggedIn && ReadLastSignInMethod() == SignInMethodKind.Google)
            {
                // 직전 연동 계정을 복원하지 않도록, 익명 시작을 명시적으로 새 가입으로 강제합니다.
                forceFreshAnonymous = true;
                PlayerPrefs.DeleteKey(RefreshTokenKey);
                SetAutoLoginBlocked(true);
                PlayerPrefs.Save();
            }

            // RestoreSessionOnStart가 꺼져 있어도, 저장된 refresh_token이 있으면 동일 계정을 이어갑니다.
            if (!forceFreshAnonymous && !_isRecreatingAfterWithdrawalDelete && !IsLoggedIn)
                await RestoreSessionAsyncCore(allowRecreateOnDeletion: true);

            // 로컬 토큰이 사라진(재설치/로그아웃/탈퇴 예약 후 ClearSession) 경우를 위해 서버의 best-effort 복구 토큰을 1회 시도합니다.
            if (!forceFreshAnonymous && !_isRecreatingAfterWithdrawalDelete && !IsLoggedIn)
            {
                Debug.Log("[Supabase.SignIn] Attempting anonymous recovery...");
                var recovery = await TryRestoreSessionFromAnonymousRecoveryAsync();
                Debug.Log($"[Supabase.SignIn] Recovery result: Kind={recovery.Kind}, IsLoggedIn={IsLoggedIn}");
                if (recovery.Kind == AnonymousRecoveryKind.GateBlocked)
                    return SupabaseResult<SupabaseSession>.Fail("withdrawal_scheduled_gate_blocked");
                if (recovery.Kind == AnonymousRecoveryKind.GuardFailed)
                    return SupabaseResult<SupabaseSession>.Fail(
                        string.IsNullOrWhiteSpace(recovery.ErrorMessage) ? "withdrawal_guard_failed" : recovery.ErrorMessage);
                if (recovery.Kind == AnonymousRecoveryKind.Banned)
                    return SupabaseResult<SupabaseSession>.Fail("user_banned");
                if (recovery.Kind == AnonymousRecoveryKind.Restored && IsLoggedIn)
                {
                    Debug.Log($"[Supabase.SignIn] Recovery successful. IsAnonymous={IsAnonymousSession(_currentSession)}");
                    if (!IsAnonymousSession(_currentSession))
                    {
                        // 연동 등으로 더 이상 익명이 아닌 계정의 refresh가 복구 테이블에 남은 경우 — 게스트 버튼은 새 익명을 의미함.
                        Debug.Log("[Supabase.SignIn] Recovered account is not anonymous, clearing and creating new one");
                        await TryDeleteAnonymousRecoveryForCurrentDeviceAsync();
                        await SignOutAsync(clearStorage: true, deleteUserSessionRow: true);
                        RememberLastSignInMethod(SignInMethodKind.Google);
                        PlayerPrefs.Save();
                    }
                    else
                    {
                        Debug.Log("[Supabase.SignIn] Using recovered anonymous session");
                        RememberLastSignInMethod(SignInMethodKind.Anonymous);
                        if (saveSessionToStorage)
                            SaveSessionToStorage();
                        return SupabaseResult<SupabaseSession>.Success(_currentSession);
                    }
                }
                else
                {
                    Debug.Log($"[Supabase.SignIn] Recovery not applied: Kind={recovery.Kind}, IsLoggedIn={IsLoggedIn}");
                }
            }

            if (IsLoggedIn)
            {
                if (IsAnonymousSession(_currentSession))
                {
                    RememberLastSignInMethod(SignInMethodKind.Anonymous);

                    var reserved = await HandleWithdrawalReservationGateAfterSignInAsync();
                    if (reserved != null)
                        return reserved;

                    await TryEnsureProfileRowAfterSignInAsync();

                    if (saveSessionToStorage)
                        SaveSessionToStorage();
                    return SupabaseResult<SupabaseSession>.Success(_currentSession);
                }

                // 저장된 refresh 복원 등으로 비익명 세션이 잡힌 경우 — 익명 로그인 의도에 맞게 끊고 신규 익명 가입으로 진행
                await SignOutAsync(clearStorage: true, deleteUserSessionRow: true);
                RememberLastSignInMethod(SignInMethodKind.Google);
                PlayerPrefs.Save();
            }

            if (Auth == null)
                return SupabaseResult<SupabaseSession>.Fail("sdk_not_initialized");

            var result = await Auth.SignInAnonymouslyAsync();

            if (result.IsSuccess && result.Data != null)
            {
                SetSession(result.Data, SupabaseSessionChangeKind.NewSignIn);
                PlayerPrefs.DeleteKey(RefreshTokenKey);
                if (saveSessionToStorage)
                    SaveSessionToStorage();

                await TryUpsertAnonymousRecoveryTokenAsync(result.Data);
                RememberLastSignInMethod(SignInMethodKind.Anonymous);
                if (!_isRecreatingAfterWithdrawalDelete)
                {
                    var guarded = await HandleWithdrawalGuardAfterSignInAsync(
                        SignInMethodKind.Anonymous,
                        saveSessionToStorage,
                        allowRecreateOnDeletion: true);
                    if (guarded != null)
                        return guarded;

                    var reserved = await HandleWithdrawalReservationGateAfterSignInAsync();
                    if (reserved != null)
                        return reserved;
                }

                await TryEnsureProfileRowAfterSignInAsync();
            }

            return result;
        }

        private static async Task<SupabaseResult<GoogleLoginResult>> RequestGoogleLoginResultAsync(AndroidGoogleLoginProvider provider)
        {
            if (provider == null)
                return SupabaseResult<GoogleLoginResult>.Fail("google_provider_null");

            var tcs = new TaskCompletionSource<SupabaseResult<GoogleLoginResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
            provider.SignIn(
                result =>
                {
                    if (result == null)
                    {
                        tcs.TrySetResult(SupabaseResult<GoogleLoginResult>.Fail("google_result_null"));
                        return;
                    }

                    tcs.TrySetResult(SupabaseResult<GoogleLoginResult>.Success(result));
                },
                err =>
                {
                    tcs.TrySetResult(SupabaseResult<GoogleLoginResult>.Fail(
                        string.IsNullOrWhiteSpace(err) ? "google_signin_failed" : err));
                });
            return await tcs.Task;
        }

        /// <summary>
        /// 초기화 후 로그인 세션이 준비되었는지 확인합니다.
        /// 미로그인 상태면 실패를 반환하며, 자동 익명 로그인은 수행하지 않습니다.
        /// </summary>
        public static async Task<SupabaseResult<SupabaseSession>> EnsureReadySessionAsync()
        {
            if (!await EnsureInitializedAsync())
                return SupabaseResult<SupabaseSession>.Fail("sdk_not_initialized");

            // 이미 로그인되어 있으면 재로그인 없이 현재 세션을 그대로 사용합니다.
            if (IsLoggedIn)
            {
                var singleSessionOk = await SupabaseDuplicateSessionCoordinator.VerifyCurrentSessionForActionAsync();
                if (singleSessionOk == false)
                    return SupabaseResult<SupabaseSession>.Fail("duplicate_login_detected");

                return SupabaseResult<SupabaseSession>.Success(_currentSession);
            }

            return SupabaseResult<SupabaseSession>.Fail("auth_not_signed_in");
        }

        /// <summary>
        /// 앱 시작 시 자주 필요한 준비를 한 번에 수행합니다.
        /// 초기화 → (선택) 저장 세션 복원 → (선택) RemoteConfig 새로고침.
        /// </remarks>
        public static async Task<bool> StartAsync(
            bool restoreSessionFirst = true,
            bool refreshRemoteConfigOnStart = false)
        {
            if (!await EnsureInitializedAsync())
                return false;

            if (restoreSessionFirst && !IsLoggedIn)
                _ = await TryAutoLoginOnStartAsync();

            if (refreshRemoteConfigOnStart)
                _ = await RefreshRemoteConfigAsync();

            return true;
        }

        /// <summary>refresh_token 문자열로 세션을 갱신하고 SDK에 반영합니다(직접 보유한 토큰용).</summary>
        /// <remarks>
        /// 앱 재실행 시 저장된 토큰 복원은 <see cref="RestoreSessionAsync"/>를 쓰는 편이 일반적입니다.
        /// </remarks>
        public static async Task<SupabaseResult<SupabaseSession>> RefreshSessionAsync(string refreshToken, bool saveSessionToStorage = true)
        {
            if (!await EnsureInitializedAsync())
                return SupabaseResult<SupabaseSession>.Fail("sdk_not_initialized");

            if (Auth == null)
                return SupabaseResult<SupabaseSession>.Fail("sdk_not_initialized");

            var result = await Auth.RefreshSessionAsync(refreshToken);
            if (result.IsSuccess && result.Data != null)
            {
                SetSession(result.Data, SupabaseSessionChangeKind.RestoredOrRefreshed);
                if (saveSessionToStorage)
                    SaveSessionToStorage();
            }

            return result;
        }

        /// <summary>유저 세이브/로드 퍼사드. 초기화 후에만 사용하세요.</summary>
        public static UserSavesFacade UserSaves
        {
            get
            {
                EnsureInitializedOrBootstrapSync();
                if (_bootstrap == null)
                    throw new InvalidOperationException("SupabaseSDK is not initialized. Call SupabaseUnityBootstrap.Initialize first.");

                return _userSaves ??= new UserSavesFacade(_bootstrap.UserDataService, () => _currentSession);
            }
        }

        /// <summary>우편함(<c>mails</c> + 수령·삭제 RPC).</summary>
        public static MailboxFacade Mailbox
        {
            get
            {
                EnsureInitializedOrBootstrapSync();
                if (_bootstrap == null)
                    throw new InvalidOperationException("SupabaseSDK is not initialized. Call SupabaseUnityBootstrap.Initialize first.");

                return _mailbox ??= new MailboxFacade(_bootstrap.MailboxService, () => _currentSession);
            }
        }

        /// <summary>우편함 목록(RLS: 숨김·만료 제외, 현재 프로필 서버).</summary>
        public static async Task<SupabaseResult<IReadOnlyList<Mail>>> GetMyMailsAsync(int limit = 50, int offset = 0)
        {
            var ready = await EnsureReadySessionAsync();
            if (!ready.IsSuccess)
                return SupabaseResult<IReadOnlyList<Mail>>.Fail(ready.ErrorMessage ?? "auth_not_signed_in");

            return await Mailbox.GetMyMailsAsync(limit, offset);
        }

        /// <summary>우편함 메일 한 건(<c>ts_view_mail_for_user</c>: 보상 items 없으면 조회 시 읽음).</summary>
        public static async Task<SupabaseResult<Mail>> GetMailDetailAsync(string mailId)
        {
            var ready = await EnsureReadySessionAsync();
            if (!ready.IsSuccess)
                return SupabaseResult<Mail>.Fail(ready.ErrorMessage ?? "auth_not_signed_in");

            return await Mailbox.GetMailDetailAsync(mailId);
        }

        /// <summary><c>ts_claim_mail_items</c>(DB에서 수령과 동시에 읽음 처리) 후 등록된 <see cref="IMailItemHandler"/> 순서 실행.</summary>
        public static async Task<SupabaseResult<IReadOnlyList<ClaimResult>>> ClaimMailItemsAsync(string mailId)
        {
            var ready = await EnsureReadySessionAsync();
            if (!ready.IsSuccess)
                return SupabaseResult<IReadOnlyList<ClaimResult>>.Fail(ready.ErrorMessage ?? "auth_not_signed_in");

            return await Mailbox.ClaimMailItemsAsync(mailId);
        }

        /// <summary><c>ts_claim_all_mail_items</c>(DB에서 수령과 동시에 각 메일 읽음 처리) 후 메일·원소 순서대로 핸들러 실행.</summary>
        public static async Task<SupabaseResult<IReadOnlyList<ClaimResult>>> ClaimAllMailItemsAsync()
        {
            var ready = await EnsureReadySessionAsync();
            if (!ready.IsSuccess)
                return SupabaseResult<IReadOnlyList<ClaimResult>>.Fail(ready.ErrorMessage ?? "auth_not_signed_in");

            return await Mailbox.ClaimAllMailItemsAsync();
        }

        /// <summary><c>ts_delete_mail_for_user</c> 소프트 삭제(미수령 보상 있으면 서버 거부).</summary>
        public static async Task<SupabaseResult<bool>> DeleteMailAsync(string mailId)
        {
            var ready = await EnsureReadySessionAsync();
            if (!ready.IsSuccess)
                return SupabaseResult<bool>.Fail(ready.ErrorMessage ?? "auth_not_signed_in");

            return await Mailbox.DeleteMailAsync(mailId);
        }

        /// <summary><c>ts_delete_read_mails_for_user</c> — 읽음·삭제 가능한 메일만 일괄 숨김. 성공 시 데이터는 삭제한 행 수.</summary>
        public static async Task<SupabaseResult<int>> DeleteReadMailsAsync()
        {
            var ready = await EnsureReadySessionAsync();
            if (!ready.IsSuccess)
                return SupabaseResult<int>.Fail(ready.ErrorMessage ?? "auth_not_signed_in");

            return await Mailbox.DeleteReadMailsAsync();
        }

        /// <summary><c>ts_mail_inbox_counts</c>의 미읽음 수. <paramref name="userId"/>는 호환용(무시).</summary>
        public static async Task<SupabaseResult<int>> GetUnreadMailCountAsync(string userId = null)
        {
            var ready = await EnsureReadySessionAsync();
            if (!ready.IsSuccess)
                return SupabaseResult<int>.Fail(ready.ErrorMessage ?? "auth_not_signed_in");

            return await Mailbox.GetUnreadCountAsync(userId);
        }

        /// <summary><c>ts_mail_inbox_counts</c>의 미수령 보상 메일 수. <paramref name="userId"/>는 호환용(무시).</summary>
        public static async Task<SupabaseResult<int>> GetUnclaimedItemMailCountAsync(string userId = null)
        {
            var ready = await EnsureReadySessionAsync();
            if (!ready.IsSuccess)
                return SupabaseResult<int>.Fail(ready.ErrorMessage ?? "auth_not_signed_in");

            return await Mailbox.GetUnclaimedItemMailCountAsync(userId);
        }

        /// <inheritdoc cref="GetMyMailsAsync"/>
        public static async Task<IReadOnlyList<Mail>> TryGetMyMailsAsync(int limit = 50, int offset = 0)
        {
            var r = await GetMyMailsAsync(limit, offset);
            LogApiResult(ApiLogTags.MailboxList, r.IsSuccess, r.ErrorMessage);
            return r.IsSuccess ? r.Data : null;
        }

        /// <inheritdoc cref="GetMailDetailAsync"/>
        public static async Task<Mail> TryGetMailDetailAsync(string mailId)
        {
            var r = await GetMailDetailAsync(mailId);
            LogApiResult(ApiLogTags.MailboxDetail, r.IsSuccess, r.ErrorMessage);
            return r.IsSuccess ? r.Data : null;
        }

        /// <inheritdoc cref="ClaimMailItemsAsync"/>
        public static async Task<IReadOnlyList<ClaimResult>> TryClaimMailItemsAsync(string mailId)
        {
            var r = await ClaimMailItemsAsync(mailId);
            LogApiResult(ApiLogTags.MailboxClaimOne, r.IsSuccess, r.ErrorMessage);
            return r.IsSuccess ? r.Data : null;
        }

        /// <inheritdoc cref="ClaimAllMailItemsAsync"/>
        public static async Task<IReadOnlyList<ClaimResult>> TryClaimAllMailItemsAsync()
        {
            var r = await ClaimAllMailItemsAsync();
            LogApiResult(ApiLogTags.MailboxClaimAll, r.IsSuccess, r.ErrorMessage);
            return r.IsSuccess ? r.Data : null;
        }

        /// <inheritdoc cref="DeleteMailAsync"/>
        public static async Task<bool> TryDeleteMailAsync(string mailId)
        {
            var r = await DeleteMailAsync(mailId);
            LogApiResult(ApiLogTags.MailboxDelete, r.IsSuccess, r.ErrorMessage);
            return r.IsSuccess;
        }

        /// <inheritdoc cref="DeleteReadMailsAsync"/>
        public static async Task<int?> TryDeleteReadMailsAsync()
        {
            var r = await DeleteReadMailsAsync();
            LogApiResult(ApiLogTags.MailboxDeleteReadBulk, r.IsSuccess, r.ErrorMessage);
            return r.IsSuccess ? r.Data : (int?)null;
        }

        /// <inheritdoc cref="GetUnreadMailCountAsync"/>
        public static async Task<int?> TryGetUnreadMailCountAsync(string userId = null)
        {
            var r = await GetUnreadMailCountAsync(userId);
            LogApiResult(ApiLogTags.MailboxUnreadCount, r.IsSuccess, r.ErrorMessage);
            return r.IsSuccess ? r.Data : (int?)null;
        }

        /// <inheritdoc cref="GetUnclaimedItemMailCountAsync"/>
        public static async Task<int?> TryGetUnclaimedItemMailCountAsync(string userId = null)
        {
            var r = await GetUnclaimedItemMailCountAsync(userId);
            LogApiResult(ApiLogTags.MailboxUnclaimedCount, r.IsSuccess, r.ErrorMessage);
            return r.IsSuccess ? r.Data : (int?)null;
        }

        /// <summary>
        /// 로그인 직후 <typeparamref name="T"/>의 <see cref="DataTableAttribute"/> 테이블에 본인 행이 존재하도록 보장합니다.
        /// DB RPC: <c>ts_ensure_my_row(table, user_id)</c>.
        /// </summary>
        public static async Task<SupabaseResult<bool>> EnsureMyRowAsync<T>()
        {
            var ready = await EnsureReadySessionAsync();
            if (!ready.IsSuccess)
                return SupabaseResult<bool>.Fail(ready.ErrorMessage ?? "auth_not_signed_in");

            return await UserSaves.EnsureMyRowAsync<T>();
        }

        /// <summary>
        /// 변경된 컬럼만 부분 저장(PATCH)합니다. <paramref name="tableName"/>은 대상 테이블을 명시합니다.
        /// </summary>
        public static async Task<SupabaseResult<bool>> PatchUserDataAsync(
            string tableName,
            Dictionary<string, object> patch,
            bool ensureRowFirst = true,
            bool setUpdatedAtIsoUtc = true)
        {
            var ready = await EnsureReadySessionAsync();
            if (!ready.IsSuccess)
                return SupabaseResult<bool>.Fail(ready.ErrorMessage ?? "auth_not_signed_in");

            return await UserSaves.PatchAsync(tableName, patch, ensureRowFirst, setUpdatedAtIsoUtc);
        }

        /// <summary>
        /// 프로젝트별 명시 컬럼을 select로 지정해 유저 데이터를 로드합니다.
        /// </summary>
        public static async Task<SupabaseResult<T>> LoadUserDataColumnsAsync<T>(
            string tableName,
            string selectColumnsCsv) where T : class, new()
        {
            var ready = await EnsureReadySessionAsync();
            if (!ready.IsSuccess)
                return SupabaseResult<T>.Fail(ready.ErrorMessage ?? "auth_not_signed_in");

            if (string.IsNullOrWhiteSpace(selectColumnsCsv))
                return SupabaseResult<T>.Fail("select_columns_empty");

            return await UserSaves.LoadColumnsAsync<T>(tableName, selectColumnsCsv);
        }

        /// <summary>
        /// <see cref="DataColumnAttribute"/>가 붙은 멤버로 <c>select</c> 목록을 만들고 로드합니다.
        /// </summary>
        public static async Task<SupabaseResult<T>> LoadUserDataAttributedAsync<T>(bool includeUpdatedAt = true) where T : class, new()
        {
            var ready = await EnsureReadySessionAsync();
            if (!ready.IsSuccess)
                return SupabaseResult<T>.Fail(ready.ErrorMessage ?? "auth_not_signed_in");

            return await UserSaves.LoadAttributedAsync<T>(includeUpdatedAt);
        }

        /// <summary>
        /// 이전 스냅샷과 비교해 변경된 컬럼만 PATCH합니다.
        /// </summary>
        public static async Task<SupabaseResult<bool>> PatchUserDataDiffAsync<T>(
            T previous,
            T current,
            bool ensureRowFirst = true,
            bool setUpdatedAtIsoUtc = true)
        {
            var ready = await EnsureReadySessionAsync();
            if (!ready.IsSuccess)
                return SupabaseResult<bool>.Fail(ready.ErrorMessage ?? "auth_not_signed_in");

            return await UserSaves.PatchDiffAsync(previous, current, ensureRowFirst, setUpdatedAtIsoUtc);
        }

        /// <summary>
        /// 다른 사용자의 공개 displayName을 조회합니다. <paramref name="userId"/>는 DB <c>profiles.user_id</c>(OAuth <c>sub</c> 등 안정 id)입니다.
        /// 서버 단절 정책상 로그인 세션이 필요합니다.
        /// </summary>
        public static async Task<SupabaseResult<string>> GetPublicDisplayNameAsync(string userId)
        {
            var ready = await EnsureReadySessionAsync();
            if (!ready.IsSuccess)
                return SupabaseResult<string>.Fail(ready.ErrorMessage ?? "auth_not_signed_in");

            return await _bootstrap.PublicProfileService.GetDisplayNameAsync(_currentSession.AccessToken, userId);
        }

        /// <summary>현재 로그인 사용자의 displayName을 설정합니다(유니크 강제 + auth metadata 동기화).</summary>
        public static async Task<SupabaseResult<bool>> SetMyDisplayNameAsync(string displayName)
        {
            var ready = await EnsureReadySessionAsync();
            if (!ready.IsSuccess)
                return SupabaseResult<bool>.Fail(ready.ErrorMessage ?? "auth_not_signed_in");

            if (_bootstrap?.EdgeFunctionsService == null)
                return SupabaseResult<bool>.Fail("sdk_not_initialized");

            var userId = _currentSession?.User?.PlayerUserId;
            var request = new DisplayNameSetRequest
            {
                display_name = displayName,
                user_id = string.IsNullOrWhiteSpace(userId) ? null : userId.Trim()
            };

            var result = await _bootstrap.EdgeFunctionsService.InvokeAsync<DisplayNameSetResponse>(
                "displayname-set",
                _currentSession.AccessToken,
                request);

            if (result == null || !result.IsSuccess || result.Data == null)
                return SupabaseResult<bool>.Fail(result?.ErrorMessage ?? "display_name_set_failed");

            if (!result.Data.ok)
                return SupabaseResult<bool>.Fail(string.IsNullOrWhiteSpace(result.Data.reason) ? "display_name_set_failed" : result.Data.reason);

            return SupabaseResult<bool>.Success(true);
        }

        /// <inheritdoc cref="GetPublicDisplayNameAsync"/>
        public static async Task<string> TryGetPublicDisplayNameAsync(string userId, string defaultValue = "")
        {
            var r = await GetPublicDisplayNameAsync(userId);
            return LogAndReturnData(ApiLogTags.ProfilePublicDisplayNameGet, r, defaultValue);
        }

        /// <inheritdoc cref="SetMyDisplayNameAsync"/>
        public static async Task<bool> TrySetMyDisplayNameAsync(string displayName)
        {
            var r = await SetMyDisplayNameAsync(displayName);
            return LogAndReturn(ApiLogTags.ProfileMyDisplayNameSet, r);
        }

        /// <summary>displayName이 사용 가능한지 조회합니다(유니크 체크). 로그인 중이면 현재 계정이 이미 쓰는 이름은 사용 가능으로 처리합니다.</summary>
        public static async Task<SupabaseResult<bool>> IsDisplayNameAvailableAsync(string displayName)
        {
            var ready = await EnsureReadySessionAsync();
            if (!ready.IsSuccess)
                return SupabaseResult<bool>.Fail(ready.ErrorMessage ?? "auth_not_signed_in");

            var selfAccountId = _currentSession?.User?.Id;
            return await _bootstrap.PublicProfileService.IsDisplayNameAvailableAsync(_currentSession.AccessToken, displayName, selfAccountId);
        }

        /// <inheritdoc cref="IsDisplayNameAvailableAsync"/>
        public static async Task<bool> TryIsDisplayNameAvailableAsync(string displayName)
        {
            var r = await IsDisplayNameAvailableAsync(displayName);
            if (r == null || !r.IsSuccess)
            {
                LogApiResult(ApiLogTags.ProfileDisplayNameAvailable, false, r?.ErrorMessage ?? "unknown");
                return false;
            }

            if (!r.Data)
                LogApiResult(ApiLogTags.ProfileDisplayNameAvailable, false, "display_name_taken");
            else
                LogApiResult(ApiLogTags.ProfileDisplayNameAvailable, true, null);

            return r.Data;
        }

        /// <summary>로컬에 선택된 서버 코드를 저장합니다(예: GLOBAL, KR1). 다음 로그인/복구 흐름에서 사용됩니다.</summary>
        public static void SetCurrentServerCode(string serverCode)
        {
            if (string.IsNullOrWhiteSpace(serverCode))
                return;

            PlayerPrefs.SetString(CurrentServerCodeKey, serverCode.Trim());
            PlayerPrefs.Save();
        }

        /// <summary>현재 로컬에 선택된 서버 코드. 없으면 설정 기본값(<c>SupabaseSettings.defaultServerCode</c>)을 반환합니다.</summary>
        public static string GetCurrentServerCode()
        {
            var local = PlayerPrefs.GetString(CurrentServerCodeKey, null);
            if (string.IsNullOrWhiteSpace(local) == false)
                return local.Trim();

            var fromSettings = _bootstrap?.DefaultServerCode;
            return string.IsNullOrWhiteSpace(fromSettings) ? "GLOBAL" : fromSettings.Trim();
        }

        /// <summary>DB에 기록된 내 <c>profiles.server_id</c>에 대응하는 서버 코드를 조회합니다(RPC <c>ts_my_server_id</c>).</summary>
        public static async Task<SupabaseResult<MyServerInfo>> GetMyServerInfoAsync()
        {
            var ready = await EnsureReadySessionAsync();
            if (!ready.IsSuccess)
                return SupabaseResult<MyServerInfo>.Fail(ready.ErrorMessage ?? "auth_not_signed_in");

            var svc = _bootstrap?.PublicProfileService;
            if (svc == null)
                return SupabaseResult<MyServerInfo>.Fail("sdk_not_initialized");

            return await svc.GetMyServerIdAsync(_currentSession.AccessToken);
        }

        /// <inheritdoc cref="GetMyServerInfoAsync"/>
        public static async Task<MyServerInfo> TryGetMyServerInfoAsync(MyServerInfo defaultValue = default)
        {
            var r = await GetMyServerInfoAsync();
            if (r == null || !r.IsSuccess)
            {
                LogApiResult(ApiLogTags.ProfileMyServerGet, false, r?.ErrorMessage ?? "unknown");
                return defaultValue;
            }

            LogApiResult(ApiLogTags.ProfileMyServerGet, true, null);
            return r.Data;
        }

        /// <summary>현재 로그인 계정을 지정 서버 코드로 이주시킵니다.</summary>
        public static async Task<SupabaseResult<bool>> TransferMyServerAsync(string targetServerCode, string reason = null)
        {
            var ready = await EnsureReadySessionAsync();
            if (!ready.IsSuccess)
                return SupabaseResult<bool>.Fail(ready.ErrorMessage ?? "auth_not_signed_in");

            var svc = _bootstrap?.PublicProfileService;
            if (svc == null)
                return SupabaseResult<bool>.Fail("sdk_not_initialized");

            var r = await svc.TransferMyServerAsync(_currentSession.AccessToken, targetServerCode, reason);
            if (r != null && r.IsSuccess && string.IsNullOrWhiteSpace(targetServerCode) == false)
                SetCurrentServerCode(targetServerCode);
            return r;
        }

        /// <inheritdoc cref="TransferMyServerAsync"/>
        public static async Task<bool> TryTransferMyServerAsync(string targetServerCode, string reason = null)
        {
            var r = await TransferMyServerAsync(targetServerCode, reason);
            return LogAndReturn(ApiLogTags.ProfileServerTransfer, r);
        }

        /// <summary>공개 프로필(닉네임·탈퇴 시각)을 한 번에 조회합니다. <paramref name="userId"/>는 <c>profiles.user_id</c>(안정 플레이어 id)입니다.</summary>
        public static async Task<SupabaseResult<PublicProfileSnapshot>> GetPublicProfileAsync(string userId)
        {
            var ready = await EnsureReadySessionAsync();
            if (!ready.IsSuccess)
                return SupabaseResult<PublicProfileSnapshot>.Fail(ready.ErrorMessage ?? "auth_not_signed_in");

            return await _bootstrap.PublicProfileService.GetProfileAsync(_currentSession.AccessToken, userId);
        }

        /// <inheritdoc cref="GetPublicProfileAsync"/>
        public static async Task<PublicProfileSnapshot> TryGetPublicProfileAsync(string userId)
        {
            var r = await GetPublicProfileAsync(userId);
            if (r == null || !r.IsSuccess)
            {
                LogApiResult(ApiLogTags.ProfileSnapshotGet, false, r?.ErrorMessage ?? "unknown");
                return null;
            }

            LogApiResult(ApiLogTags.ProfileSnapshotGet, true, null);
            return r.Data;
        }

        /// <summary>본인 <c>withdrawn_at</c>을 ISO 8601로 설정합니다(soft 탈퇴 표시).</summary>
        public static async Task<SupabaseResult<bool>> SetMyWithdrawnAtAsync(string withdrawnAtIsoUtc)
        {
            var ready = await EnsureReadySessionAsync();
            if (!ready.IsSuccess)
                return SupabaseResult<bool>.Fail(ready.ErrorMessage ?? "auth_not_signed_in");

            if (_bootstrap?.PublicProfileService == null)
                return SupabaseResult<bool>.Fail("sdk_not_initialized");

            if (string.IsNullOrWhiteSpace(withdrawnAtIsoUtc))
                return SupabaseResult<bool>.Fail("withdrawn_at_empty");

            return await _bootstrap.PublicProfileService.PatchMyWithdrawnAtAsync(
                _currentSession.AccessToken,
                _currentSession.User.Id,
                withdrawnAtIsoUtc);
        }

        /// <summary>본인 <c>withdrawn_at</c>을 비웁니다(SQL NULL).</summary>
        public static async Task<SupabaseResult<bool>> ClearMyWithdrawalAsync()
        {
            var ready = await EnsureReadySessionAsync();
            if (!ready.IsSuccess)
                return SupabaseResult<bool>.Fail(ready.ErrorMessage ?? "auth_not_signed_in");

            if (_bootstrap?.PublicProfileService == null)
                return SupabaseResult<bool>.Fail("sdk_not_initialized");

            return await _bootstrap.PublicProfileService.PatchMyWithdrawnAtAsync(
                _currentSession.AccessToken,
                _currentSession.User.Id,
                withdrawnAtIso: null);
        }

        /// <summary>현재 시각(UTC)으로 soft 탈퇴 시각을 기록합니다.</summary>
        public static Task<SupabaseResult<bool>> MarkMyWithdrawnAsync() =>
            SetMyWithdrawnAtAsync(DateTime.UtcNow.ToString("o"));

        /// <summary>
        /// 설정(<see cref="WithdrawalRequestDelayDays"/>)에 정의된 유예 기간(일)으로 <c>withdrawn_at</c>을 서버에서 예약합니다.
        /// 요청이 성공하면 앱에서도 즉시 로그아웃 상태로 전환합니다(자동 로그인 방지).
        /// 철회용 <c>cancel_token</c>은 여기서 발급하지 않습니다. <b>탈퇴 예약(유예) 중인 계정으로 로그인</b>하면 SDK가 Edge Function으로 토큰을 발급·로컬에 저장한 뒤 세션을 정리합니다(게이트).
        /// 그 다음 <see cref="RedeemWithdrawalCancelAsync"/>로 철회할 수 있습니다. 다른 기기에서 로그인해도 그 기기에 토큰이 저장됩니다.
        /// 즉시 탈퇴처럼 예약이 아닌 경우(<c>IsScheduled == false</c>)에는 철회 토큰이 발급되지 않습니다.
        /// </summary>
        public static async Task<SupabaseResult<bool>> RequestMyWithdrawalAsync()
        {
            var ready = await EnsureReadySessionAsync();
            if (!ready.IsSuccess)
                return SupabaseResult<bool>.Fail(ready.ErrorMessage ?? "auth_not_signed_in");

            if (_bootstrap?.PublicProfileService == null)
                return SupabaseResult<bool>.Fail("sdk_not_initialized");

            var delayDays = _withdrawalRequestDelayDays < 0f ? 0f : _withdrawalRequestDelayDays;
            var delayInt = Mathf.RoundToInt(delayDays);
            var request = await _bootstrap.PublicProfileService.RequestMyWithdrawalByDelayDaysAsync(
                _currentSession.AccessToken,
                delayInt);

            if (request == null || !request.IsSuccess)
                return SupabaseResult<bool>.Fail(request?.ErrorMessage ?? "withdrawal_request_failed");

            // 로컬 refresh_token을 지우기 전에 서버에 복구용 refresh를 남겨, 다음 익명 로그인 시 동일 auth 계정으로 복구되게 합니다.
            await TryUpsertAnonymousRecoveryTokenAsync(_currentSession);

            // 유예 여부와 무관하게, "탈퇴 예약을 건 계정"은 항상 수동 로그인 UX를 타도록 즉시 로그아웃 처리합니다.
            ClearSession(clearStorage: true, deleteUserSessionRow: true);

            return SupabaseResult<bool>.Success(true);
        }

        /// <inheritdoc cref="MarkMyWithdrawnAsync"/>
        public static async Task<bool> TryMarkMyWithdrawnAsync()
        {
            var r = await MarkMyWithdrawnAsync();
            return LogAndReturn(ApiLogTags.ProfileWithdrawnAt, r);
        }

        /// <inheritdoc cref="RequestMyWithdrawalAsync"/>
        public static async Task<bool> TryRequestMyWithdrawalAsync()
        {
            var r = await RequestMyWithdrawalAsync();
            return LogAndReturn(ApiLogTags.ProfileWithdrawnRequest, r);
        }

        /// <inheritdoc cref="ClearMyWithdrawalAsync"/>
        public static async Task<bool> TryClearMyWithdrawalAsync()
        {
            var r = await ClearMyWithdrawalAsync();
            return LogAndReturn(ApiLogTags.ProfileWithdrawnAt, r);
        }

        /// <inheritdoc cref="SetMyWithdrawnAtAsync"/>
        public static async Task<bool> TrySetMyWithdrawnAtAsync(string withdrawnAtIsoUtc)
        {
            var r = await SetMyWithdrawnAtAsync(withdrawnAtIsoUtc);
            return LogAndReturn(ApiLogTags.ProfileWithdrawnAt, r);
        }

        /// <summary>
        /// 본인의 <c>profiles.last_activity_at</c>을 현재 시각으로 갱신합니다. Retool 운영 대시보드 모니터링용.
        /// </summary>
        public static async Task<bool> TryUpdateLastActivityAtAsync()
        {
            var ready = await EnsureReadySessionAsync();
            if (!ready.IsSuccess)
                return LogAndReturn("Supabase.Profile.LastActivityAt", ready);

            if (_bootstrap?.PublicProfileService == null)
                return LogAndReturn("Supabase.Profile.LastActivityAt", SupabaseResult<bool>.Fail("sdk_not_initialized"));

            var r = await _bootstrap.PublicProfileService.PatchMyLastActivityAtAsync(_currentSession.AccessToken, _currentSession.User.Id);
            return LogAndReturn("Supabase.Profile.LastActivityAt", r);
        }

        /// <summary>
        /// 로그인한 본인의 탈퇴 예약 게이트 상태(닉네임/예약 시각/남은 시간)를 조회합니다.
        /// </summary>
        public static async Task<SupabaseResult<MyWithdrawalStatus>> GetMyWithdrawalStatusAsync()
        {
            var ready = await EnsureReadySessionAsync();
            if (!ready.IsSuccess)
                return SupabaseResult<MyWithdrawalStatus>.Fail(ready.ErrorMessage ?? "auth_not_signed_in");

            if (_bootstrap?.PublicProfileService == null)
                return SupabaseResult<MyWithdrawalStatus>.Fail("sdk_not_initialized");

            return await _bootstrap.PublicProfileService.GetMyWithdrawalStatusAsync(_currentSession.AccessToken);
        }

        /// <inheritdoc cref="GetMyWithdrawalStatusAsync"/>
        public static async Task<MyWithdrawalStatus> TryGetMyWithdrawalStatusAsync()
        {
            var r = await GetMyWithdrawalStatusAsync();
            return LogAndReturnData(ApiLogTags.ProfileWithdrawalStatus, r, default(MyWithdrawalStatus));
        }

        /// <summary>
        /// 철회 전용 토큰 발급을 요청합니다. 로그인 세션(access token)이 필요합니다.
        /// 발급 성공 시 토큰과 만료 시각을 로컬에 저장합니다.
        /// </summary>
        public static async Task<SupabaseResult<string>> RequestWithdrawalCancelTokenAsync()
        {
            var ready = await EnsureReadySessionAsync();
            if (!ready.IsSuccess)
                return SupabaseResult<string>.Fail(ready.ErrorMessage ?? "auth_not_signed_in");

            if (_bootstrap?.EdgeFunctionsService == null)
                return SupabaseResult<string>.Fail("sdk_not_initialized");

            var issue = await RequestWithdrawalCancelTokenCoreAsync(_currentSession.AccessToken);
            if (issue == null || !issue.IsSuccess || issue.Data == null)
                return SupabaseResult<string>.Fail(issue?.ErrorMessage ?? "withdrawal_cancel_issue_failed");

            return SupabaseResult<string>.Success(issue.Data.CancelToken);
        }

        /// <inheritdoc cref="RequestWithdrawalCancelTokenAsync"/>
        public static async Task<string> TryRequestWithdrawalCancelTokenAsync(string defaultValue = null)
        {
            var r = await RequestWithdrawalCancelTokenAsync();
            return LogAndReturnData(ApiLogTags.ProfileWithdrawalCancelIssue, r, defaultValue);
        }

        /// <summary>
        /// 철회 전용 토큰으로 탈퇴 예약을 해제합니다.
        /// 토큰을 넘기지 않으면 로컬에 저장된 토큰을 사용합니다.
        /// </summary>
        public static async Task<SupabaseResult<bool>> RedeemWithdrawalCancelAsync(string cancelToken = null)
        {
            if (!await EnsureInitializedAsync())
                return SupabaseResult<bool>.Fail("sdk_not_initialized");

            if (_bootstrap?.EdgeFunctionsService == null)
                return SupabaseResult<bool>.Fail("sdk_not_initialized");

            var token = string.IsNullOrWhiteSpace(cancelToken)
                ? ReadStoredWithdrawalCancelToken()
                : cancelToken.Trim();
            if (string.IsNullOrWhiteSpace(token))
                return SupabaseResult<bool>.Fail("withdrawal_cancel_token_empty");

            var result = await _bootstrap.EdgeFunctionsService.InvokeAsync<WithdrawalCancelRedeemResponse>(
                WithdrawalCancelRedeemFunctionName,
                accessToken: null,
                requestBody: new WithdrawalCancelRedeemRequest { cancel_token = token });

            if (result == null || !result.IsSuccess || result.Data == null)
            {
                var err = result?.ErrorMessage ?? "withdrawal_cancel_redeem_failed";
                if (err.IndexOf("Missing authorization header", StringComparison.OrdinalIgnoreCase) >= 0)
                    return SupabaseResult<bool>.Fail("withdrawal_cancel_redeem_verify_jwt_must_be_off");

                return SupabaseResult<bool>.Fail(result?.ErrorMessage ?? "withdrawal_cancel_redeem_failed");
            }

            if (!result.Data.ok)
                return SupabaseResult<bool>.Fail(string.IsNullOrWhiteSpace(result.Data.reason) ? "withdrawal_cancel_redeem_failed" : result.Data.reason);

            ClearStoredWithdrawalCancelToken();
            ClearStoredWithdrawalGateStatus();
            return SupabaseResult<bool>.Success(true);
        }

        /// <inheritdoc cref="RedeemWithdrawalCancelAsync"/>
        public static async Task<bool> TryRedeemWithdrawalCancelAsync(string cancelToken = null)
        {
            var r = await RedeemWithdrawalCancelAsync(cancelToken);
            return LogAndReturn(ApiLogTags.ProfileWithdrawalCancelRedeem, r);
        }

        /// <summary>
        /// 로컬에 저장된 탈퇴 게이트 상태를 반환합니다(로그아웃 이후 안내 UI용).
        /// </summary>
        public static MyWithdrawalStatus GetStoredWithdrawalGateStatus()
        {
            var displayName = PlayerPrefs.GetString(WithdrawalGateDisplayNameKey, string.Empty);
            var withdrawnAt = PlayerPrefs.GetString(WithdrawalGateWithdrawnAtKey, null);
            var serverNow = PlayerPrefs.GetString(WithdrawalGateServerNowKey, null);
            var seconds = PlayerPrefs.GetString(WithdrawalGateSecondsRemainingKey, "0");
            if (!long.TryParse(seconds, out var remaining))
                remaining = 0;

            var scheduled = string.IsNullOrWhiteSpace(withdrawnAt) == false && remaining > 0;
            return new MyWithdrawalStatus(displayName, withdrawnAt, serverNow, scheduled, remaining);
        }

        /// <summary>Remote Config 캐시·구독·서버 동기화 퍼사드.</summary>
        /// <remarks>
        /// 서버 요청 시 액세스 토큰이 있으면 전달됩니다(정책에 따라 익명/로그인 모두 가능한 경우가 많음).
        /// </remarks>
        public static RemoteConfigFacade RemoteConfig
        {
            get
            {
                EnsureInitializedOrBootstrapSync();
                if (_bootstrap == null)
                    throw new InvalidOperationException("SupabaseSDK is not initialized. Call SupabaseUnityBootstrap.Initialize first.");

                return _remoteConfig ??= new RemoteConfigFacade(
                    _bootstrap.RemoteConfigService,
                    () => _currentSession?.AccessToken);
            }
        }

        /// <summary>Remote Config 전체를 서버에서 다시 받아 캐시를 갱신합니다.</summary>
        public static async Task<bool> RefreshRemoteConfigAsync()
        {
            if (!await EnsureInitializedAsync())
                return false;

            return await RemoteConfig.RefreshAllAsync();
        }

        /// <summary>
        /// 마지막 동기 시각 이후 변경분만 가져와 캐시에 머지합니다.
        /// </summary>
        public static async Task<bool> PollRemoteConfigAsync()
        {
            if (!await EnsureInitializedAsync())
                return false;

            return await RemoteConfig.PollAsync();
        }

        /// <summary>로컬 캐시에서 key에 해당하는 값을 읽습니다(네트워크 호출 없음).</summary>
        /// <remarks>
        /// 최신 값이 필요하면 먼저 <see cref="RefreshRemoteConfigAsync"/> 또는 <see cref="GetRemoteConfigAsync{T}(string)"/>를 호출합니다.
        /// </remarks>
        public static T GetRemoteConfig<T>(string key, T defaultValue = default)
        {
            return RemoteConfig.Get(key, defaultValue);
        }

        /// <summary>
        /// Remote Config를 서버와 맞춘 뒤 역직렬화합니다. 캐시 유효 시간은 DB <c>max_stale_seconds</c>를 사용합니다.
        /// </summary>
        public static async Task<SupabaseResult<T>> GetRemoteConfigAsync<T>(string key) where T : class, new()
        {
            if (!await EnsureInitializedAsync())
                return SupabaseResult<T>.Fail("supabase_not_initialized");

            return await RemoteConfig.GetTypedAsync<T>(key);
        }

        public static bool TryGetRemoteConfigRaw(string key, out string valueJson)
        {
            return RemoteConfig.TryGetRaw(key, out valueJson);
        }

        /// <summary>특정 key 값이 바뀔 때마다 콜백을 호출합니다(캐시에 객체 루트 JSON이 있을 때).</summary>
        public static void SubscribeRemoteConfig(string key, Action<string> onValueChanged, bool invokeIfCached = true)
        {
            RemoteConfig.Subscribe(key, onValueChanged, invokeIfCached);
        }

        /// <summary><see cref="SubscribeRemoteConfig"/>에서 등록한 콜백을 제거합니다.</summary>
        public static void UnsubscribeRemoteConfig(string key, Action<string> onValueChanged)
        {
            RemoteConfig.Unsubscribe(key, onValueChanged);
        }

        /// <summary>Supabase Edge Functions 호출 퍼사드(로그인 세션의 액세스 토큰 사용).</summary>
        public static ServerFunctionsFacade Functions
        {
            get
            {
                EnsureInitializedOrBootstrapSync();
                if (_bootstrap == null)
                    throw new InvalidOperationException("SupabaseSDK is not initialized. Call SupabaseUnityBootstrap.Initialize first.");

                return _functions ??= new ServerFunctionsFacade(
                    _bootstrap.EdgeFunctionsService,
                    () => _currentSession);
            }
        }

        /// <summary>서버 함수 호출(로그인 세션 필요).</summary>
        public static async Task<SupabaseResult<TResponse>> InvokeFunctionAsync<TResponse>(string functionName, object requestBody = null)
        {
            return await Functions.InvokeAsync<TResponse>(functionName, requestBody, requireAuth: true);
        }

        /// <summary>로그인 성공 시 세션을 SDK에 설정하세요. 이후 PatchAsync/LoadColumnsAsync/Events는 세션 없이 호출 가능.</summary>
        public static void SetSession(SupabaseSession session)
        {
            SetSession(session, SupabaseSessionChangeKind.RestoredOrRefreshed);
        }

        /// <summary>
        /// 세션을 설정합니다. <paramref name="kind"/>가 <see cref="SupabaseSessionChangeKind.NewSignIn"/>이면 서버에 새 세션 토큰을 등록합니다(중복 로그인 감지).
        /// </summary>
        public static void SetSession(SupabaseSession session, SupabaseSessionChangeKind kind)
        {
            _currentSession = session;
            if (session == null || session.User == null || string.IsNullOrWhiteSpace(session.User.Id))
                return;

            SupabaseDuplicateSessionCoordinator.ScheduleSyncAfterSessionChange(kind);
        }

        /// <summary>
        /// 로그아웃. 익명 세션이고 <paramref name="clearStorage"/>가 true이면, 로컬 refresh를 지우기 전에 지문 기반 복구용 refresh를 서버에 남깁니다.
        /// 동일 기기에서 다시 익명 로그인할 때 같은 <c>auth.users</c> 계정으로 이어지게 하려면 이 메서드(또는 탈퇴 예약 등 SDK가 호출하는 경로)를 쓰세요.
        /// </summary>
        public static async Task SignOutAsync(bool clearStorage = true, bool deleteUserSessionRow = true)
        {
            if (clearStorage && IsAnonymousSession(_currentSession))
                await TryUpsertAnonymousRecoveryTokenAsync(_currentSession);

            ClearSession(clearStorage, deleteUserSessionRow);
        }

        /// <summary><see cref="SignOutAsync"/>를 bool 기반으로 호출합니다.</summary>
        public static async Task<bool> TrySignOutAsync(bool clearStorage = true, bool deleteUserSessionRow = true)
        {
            await SignOutAsync(clearStorage, deleteUserSessionRow);
            LogApiResult(ApiLogTags.AuthSignOut, true, null);
            return true;
        }

        /// <summary>
        /// Android에서 네이티브 Google 계정 로그아웃을 시도한 뒤 <see cref="SignOutAsync"/>로 Supabase 세션을 정리합니다.
        /// 에디터·비 Android에서는 Google 단계가 실패해도 Supabase 로그아웃은 진행합니다.
        /// </summary>
        public static async Task SignOutFullyAsync(bool clearStorage = true, bool deleteUserSessionRow = true)
        {
            if (await EnsureInitializedAsync())
                _ = await SignOutFromGoogleAsync();

            await SignOutAsync(clearStorage, deleteUserSessionRow);
        }

        /// <summary><see cref="SignOutFullyAsync"/>를 bool 기반으로 호출합니다.</summary>
        public static async Task<bool> TrySignOutFullyAsync(bool clearStorage = true, bool deleteUserSessionRow = true)
        {
            await SignOutFullyAsync(clearStorage, deleteUserSessionRow);
            LogApiResult(ApiLogTags.AuthSignOut, true, null);
            return true;
        }

        /// <summary>로그아웃 시 호출. clearStorage가 true면 PlayerPrefs에 저장된 refresh_token도 삭제합니다.</summary>
        /// <remarks>
        /// 익명 계정을 같은 기기에서 다시 이어가려면 <see cref="SignOutAsync"/>를 사용하세요(로컬 삭제 전 서버 복구 토큰 upsert).
        /// </remarks>
        /// <param name="clearStorage">저장된 refresh_token 삭제 여부.</param>
        /// <param name="deleteUserSessionRow">true면 <c>user_sessions</c>에서 본인 행을 삭제합니다. 다른 기기에 의해 세션이 무효화된 경우 false로 두세요.</param>
        public static void ClearSession(bool clearStorage = true, bool deleteUserSessionRow = true)
        {
            var accessToken = _currentSession?.AccessToken;
            var accountId = _currentSession?.User?.Id;

            SupabaseDuplicateSessionCoordinator.StopPolling();

            UserSaveStaticSyncRegistry.ResetAll();

            _currentSession = null;
            SetAutoLoginBlocked(true);
            if (clearStorage)
                PlayerPrefs.DeleteKey(RefreshTokenKey);

            if (string.IsNullOrWhiteSpace(accountId) == false)
                PlayerPrefs.DeleteKey(SessionTokenPlayerPrefsKeyPrefix + accountId);

            PlayerPrefs.Save();

            if (deleteUserSessionRow
                && string.IsNullOrWhiteSpace(accessToken) == false
                && string.IsNullOrWhiteSpace(accountId) == false)
            {
                var svc = _bootstrap?.UserSessionService;
                if (svc != null)
                    _ = svc.DeleteMySessionRowAsync(accessToken, accountId);
            }
        }

        /// <summary>내부: 다른 기기 로그인으로 서버 토큰이 바뀐 경우 세션을 끊고 이벤트를 올립니다.</summary>
        internal static void RaiseDuplicateLoginDetected()
        {
            ClearSession(clearStorage: true, deleteUserSessionRow: false);
            OnDuplicateLoginDetected?.Invoke();
        }

        /// <summary>현재 세션의 refresh_token을 PlayerPrefs에 저장. 앱 재시작 후 RestoreSessionAsync로 복원할 수 있습니다.</summary>
        public static void SaveSessionToStorage()
        {
            if (_currentSession == null || string.IsNullOrWhiteSpace(_currentSession.RefreshToken))
                return;
            PlayerPrefs.SetString(RefreshTokenKey, _currentSession.RefreshToken);
            SetAutoLoginBlocked(false);
            PlayerPrefs.Save();
        }

        /// <summary>PlayerPrefs에 저장된 refresh_token으로 세션을 복원합니다.</summary>
        /// <remarks>
        /// 자동 로그인 정책(로그아웃 상태 여부)과 무관하게 "명시적으로" 복원을 시도할 때 사용합니다.
        /// 앱 시작 자동 복원은 <see cref="TryAutoLoginOnStartAsync"/>를 사용하세요.
        /// </remarks>
        public static Task<bool> RestoreSessionAsync() =>
            RestoreSessionAsyncCore(allowRecreateOnDeletion: false);

        private static async Task<bool> RestoreSessionAsyncCore(bool allowRecreateOnDeletion)
        {
            if (!await EnsureInitializedAsync())
                return false;

            if (_bootstrap?.AuthService == null)
                return false;

            var refreshToken = PlayerPrefs.GetString(RefreshTokenKey, null);

            if (string.IsNullOrWhiteSpace(refreshToken))
                return false;

            var result = await _bootstrap.AuthService.RefreshSessionAsync(refreshToken);
            if (result.IsSuccess && result.Data != null)
            {
                SetSession(result.Data, SupabaseSessionChangeKind.RestoredOrRefreshed);
                SetAutoLoginBlocked(false);

                if (!_isRecreatingAfterWithdrawalDelete)
                {
                    var method = ReadLastSignInMethod();
                    var guarded = await HandleWithdrawalGuardAfterSignInAsync(
                        method == SignInMethodKind.Unknown ? SignInMethodKind.Anonymous : method,
                        saveSessionToStorage: true,
                        allowRecreateOnDeletion: allowRecreateOnDeletion);
                    if (guarded != null)
                        return guarded.IsSuccess;

                    var reserved = await HandleWithdrawalReservationGateAfterSignInAsync();
                    if (reserved != null)
                        return reserved.IsSuccess;
                }

                // TryStartAsync / TryRestoreSessionAsync 경로는 SignIn* 를 거치지 않아 프로필 보장이 빠질 수 있음 → ts_my_server_id 빈 결과 방지
                await TryEnsureProfileRowAfterSignInAsync();

                return true;
            }

            PlayerPrefs.DeleteKey(RefreshTokenKey);
            return false;
        }

        /// <summary><see cref="SupabaseUnityBootstrap.Initialize"/>에서 호출합니다. 서비스 인스턴스를 묶고 캐시를 초기화합니다.</summary>
        /// <remarks>
        /// 동일 프로젝트 URL로 재초기화되고 이미 로그인 중이면 세션을 유지합니다(Resources 부트스트랩 후 Runtime Awake 등).
        /// </remarks>
        public static void Initialize(SupabaseUnityBootstrap bootstrap)
        {
            _ = bootstrap ?? throw new ArgumentNullException(nameof(bootstrap));

            var newUrl = bootstrap.ProjectUrl ?? string.Empty;

            var sameProject = _bootstrap != null
                && _initializedProjectUrl != null
                && string.Equals(_initializedProjectUrl, newUrl, StringComparison.OrdinalIgnoreCase);

            // 동일 프로젝트 재초기화(예: Resources 부트스트랩 후 Runtime Awake)면 로그인 세션을 유지합니다.
            var preserveSession = sameProject && IsLoggedIn;

            _bootstrap = bootstrap;
            _initializedProjectUrl = newUrl;
            _enableApiResultLogs = bootstrap.EnableApiResultLogs;
            _duplicateSessionMonitorEnabled = bootstrap.EnableDuplicateSessionMonitor;
            _duplicateSessionPollSeconds = bootstrap.DuplicateSessionPollSeconds;
            _duplicateSessionActionCheckCooldownSeconds = bootstrap.DuplicateSessionActionCheckCooldownSeconds;
            _withdrawalRequestDelayDays = bootstrap.WithdrawalRequestDelayDays;
            _enableWithdrawalGuardOnLogin = bootstrap.EnableWithdrawalGuardOnLogin;
            _withdrawalGuardFunctionName = string.IsNullOrWhiteSpace(bootstrap.WithdrawalGuardFunctionName)
                ? "withdrawal-guard"
                : bootstrap.WithdrawalGuardFunctionName.Trim();

            if (!preserveSession)
                _currentSession = null;

            _userSaves = null;
            _mailbox = null;
            _remoteConfig = null;
            _functions = null;

            if (!preserveSession)
                UserSaveStaticSyncRegistry.ResetAll();

            if (preserveSession && IsLoggedIn)
                SupabaseDuplicateSessionCoordinator.ScheduleSyncAfterSessionChange(SupabaseSessionChangeKind.RestoredOrRefreshed);
        }

        /// <summary>
        /// GoogleLoginBridge가 씬에 없으면 생성합니다. (<see cref="Config.SupabaseRuntime"/>와 동일한 이름의 오브젝트)
        /// </summary>
        private static GoogleLoginBridge EnsureGoogleLoginBridge()
        {
            var existing = UnityEngine.Object.FindFirstObjectByType<GoogleLoginBridge>();
            if (existing != null)
                return existing;

            var go = new GameObject("TruesoftGoogleLoginBridge");
            UnityEngine.Object.DontDestroyOnLoad(go);
            return go.AddComponent<GoogleLoginBridge>();
        }

        /// <summary><c>Resources/SupabaseSettings</c>에서 Google Web Client ID를 읽습니다.</summary>
        private static string TryGetGoogleWebClientIdFromSettings()
        {
            var settings = Resources.Load<SupabaseSettings>("SupabaseSettings");
            if (settings == null)
                return null;

            return string.IsNullOrWhiteSpace(settings.googleWebClientId) ? null : settings.googleWebClientId.Trim();
        }

        private static bool IsAnonymousSession(SupabaseSession session)
        {
            if (session == null || session.User == null)
                return false;

            if (string.IsNullOrWhiteSpace(session.AccessToken))
                return false;

            return session.User.IsAnonymous;
        }

        private static void RememberLastSignInMethod(SignInMethodKind kind)
        {
            if (kind == SignInMethodKind.Unknown)
                return;

            PlayerPrefs.SetInt(LastSignInMethodKey, (int)kind);
            PlayerPrefs.Save();
        }

        private static SignInMethodKind ReadLastSignInMethod()
        {
            var raw = PlayerPrefs.GetInt(LastSignInMethodKey, (int)SignInMethodKind.Unknown);
            if (raw == (int)SignInMethodKind.Google)
                return SignInMethodKind.Google;
            if (raw == (int)SignInMethodKind.Anonymous)
                return SignInMethodKind.Anonymous;
            return SignInMethodKind.Unknown;
        }

        private static bool HasStoredRefreshToken()
        {
            var token = PlayerPrefs.GetString(RefreshTokenKey, null);
            return string.IsNullOrWhiteSpace(token) == false;
        }

        private static bool IsAutoLoginBlocked()
        {
            return PlayerPrefs.GetInt(AutoLoginBlockedKey, 0) == 1;
        }

        private static void SetAutoLoginBlocked(bool blocked)
        {
            PlayerPrefs.SetInt(AutoLoginBlockedKey, blocked ? 1 : 0);
            PlayerPrefs.Save();
        }

        private static async Task<SupabaseResult<WithdrawalCancelIssueInfo>> RequestWithdrawalCancelTokenCoreAsync(
            string accessToken,
            string issueTrigger = "withdrawal_gate")
        {
            if (_bootstrap?.EdgeFunctionsService == null)
                return SupabaseResult<WithdrawalCancelIssueInfo>.Fail("sdk_not_initialized");

            if (string.IsNullOrWhiteSpace(accessToken))
                return SupabaseResult<WithdrawalCancelIssueInfo>.Fail("access_token_empty");

            var trigger = string.IsNullOrWhiteSpace(issueTrigger) ? "withdrawal_gate" : issueTrigger.Trim();
            var result = await _bootstrap.EdgeFunctionsService.InvokeAsync<WithdrawalCancelIssueResponse>(
                WithdrawalCancelIssueFunctionName,
                accessToken,
                new WithdrawalCancelIssueRequest { trigger = trigger });

            if (result == null || !result.IsSuccess || result.Data == null)
                return SupabaseResult<WithdrawalCancelIssueInfo>.Fail(result?.ErrorMessage ?? "withdrawal_cancel_issue_failed");

            if (!result.Data.ok)
                return SupabaseResult<WithdrawalCancelIssueInfo>.Fail(
                    string.IsNullOrWhiteSpace(result.Data.reason) ? "withdrawal_cancel_issue_failed" : result.Data.reason);

            if (string.IsNullOrWhiteSpace(result.Data.cancel_token))
                return SupabaseResult<WithdrawalCancelIssueInfo>.Fail("withdrawal_cancel_token_empty");

            var info = new WithdrawalCancelIssueInfo(
                result.Data.cancel_token.Trim(),
                string.IsNullOrWhiteSpace(result.Data.expires_at) ? null : result.Data.expires_at.Trim());

            SaveStoredWithdrawalCancelToken(info.CancelToken, info.ExpiresAtIso);
            return SupabaseResult<WithdrawalCancelIssueInfo>.Success(info);
        }

        private static async Task<SupabaseResult<SupabaseSession>> HandleWithdrawalReservationGateAfterSignInAsync()
        {
            if (_bootstrap?.PublicProfileService == null)
                return null;

            if (_currentSession == null || string.IsNullOrWhiteSpace(_currentSession.AccessToken))
                return null;

            var statusResult = await _bootstrap.PublicProfileService.GetMyWithdrawalStatusAsync(_currentSession.AccessToken);
            if (statusResult == null || !statusResult.IsSuccess || statusResult.Data == null)
                return null;

            var status = statusResult.Data;
            if (!status.IsScheduled)
            {
                ClearStoredWithdrawalGateStatus();
                ClearStoredWithdrawalCancelToken();
                return null;
            }

            SaveStoredWithdrawalGateStatus(status);
            var issue = await RequestWithdrawalCancelTokenCoreAsync(_currentSession.AccessToken);

            // 로컬 refresh 삭제 전에 지문 복구용으로 서버에 남겨, 다음 익명 로그인 시 동일 auth 계정으로 복구되게 합니다.
            await TryUpsertAnonymousRecoveryTokenAsync(_currentSession);

            // 예약 중 계정은 본편 진입을 막기 위해 세션을 즉시 정리합니다.
            ClearSession(clearStorage: true, deleteUserSessionRow: true);

            if (issue == null || !issue.IsSuccess || issue.Data == null)
                return SupabaseResult<SupabaseSession>.Fail("withdrawal_scheduled_cancel_token_issue_failed");

            return SupabaseResult<SupabaseSession>.Fail("withdrawal_scheduled_gate_blocked");
        }

        private static void SaveStoredWithdrawalGateStatus(MyWithdrawalStatus status)
        {
            if (status == null)
                return;

            PlayerPrefs.SetString(WithdrawalGateDisplayNameKey, status.DisplayName ?? string.Empty);
            if (string.IsNullOrWhiteSpace(status.WithdrawnAtIso))
                PlayerPrefs.DeleteKey(WithdrawalGateWithdrawnAtKey);
            else
                PlayerPrefs.SetString(WithdrawalGateWithdrawnAtKey, status.WithdrawnAtIso);

            if (string.IsNullOrWhiteSpace(status.ServerNowIso))
                PlayerPrefs.DeleteKey(WithdrawalGateServerNowKey);
            else
                PlayerPrefs.SetString(WithdrawalGateServerNowKey, status.ServerNowIso);

            PlayerPrefs.SetString(WithdrawalGateSecondsRemainingKey, status.SecondsRemaining.ToString());
            PlayerPrefs.Save();
        }

        private static void ClearStoredWithdrawalGateStatus()
        {
            PlayerPrefs.DeleteKey(WithdrawalGateDisplayNameKey);
            PlayerPrefs.DeleteKey(WithdrawalGateWithdrawnAtKey);
            PlayerPrefs.DeleteKey(WithdrawalGateServerNowKey);
            PlayerPrefs.DeleteKey(WithdrawalGateSecondsRemainingKey);
            PlayerPrefs.Save();
        }

        private static void SaveStoredWithdrawalCancelToken(string token, string expiresAtIso)
        {
            if (string.IsNullOrWhiteSpace(token))
                return;

            PlayerPrefs.SetString(WithdrawalCancelTokenKey, token.Trim());
            if (string.IsNullOrWhiteSpace(expiresAtIso))
                PlayerPrefs.DeleteKey(WithdrawalCancelTokenExpiresAtKey);
            else
                PlayerPrefs.SetString(WithdrawalCancelTokenExpiresAtKey, expiresAtIso.Trim());
            PlayerPrefs.Save();
        }

        private static string ReadStoredWithdrawalCancelToken()
        {
            var token = PlayerPrefs.GetString(WithdrawalCancelTokenKey, null);
            return string.IsNullOrWhiteSpace(token) ? null : token.Trim();
        }

        private static void ClearStoredWithdrawalCancelToken()
        {
            PlayerPrefs.DeleteKey(WithdrawalCancelTokenKey);
            PlayerPrefs.DeleteKey(WithdrawalCancelTokenExpiresAtKey);
            PlayerPrefs.Save();
        }

        private static async Task<SupabaseResult<SupabaseSession>> HandleWithdrawalGuardAfterSignInAsync(
            SignInMethodKind method,
            bool saveSessionToStorage,
            bool allowRecreateOnDeletion)
        {
            if (!_enableWithdrawalGuardOnLogin || _isRecreatingAfterWithdrawalDelete)
                return null;

            var shouldDelete = await ShouldDeleteCurrentAccountByWithdrawalGuardAsync();
            if (!shouldDelete)
                return null;

            ClearSession(clearStorage: true, deleteUserSessionRow: true);

            if (!allowRecreateOnDeletion)
            {
                // 앱 시작(버튼 없이) 자동 복원 흐름에서는 자동 재로그인을 막습니다.
                return SupabaseResult<SupabaseSession>.Fail("withdrawal_deleted_manual_login_required");
            }

            // 수동 로그인 흐름에서는 삭제 감지 시 자동으로 새 계정을 만들어 로그인시킵니다.
            _isRecreatingAfterWithdrawalDelete = true;
            try
            {
                var recreated = await RecreateSessionByMethodAsync(method, saveSessionToStorage);
                if (recreated == null || !recreated.IsSuccess || recreated.Data == null)
                    return SupabaseResult<SupabaseSession>.Fail("withdrawal_deleted_recreate_failed");

                return recreated;
            }
            finally
            {
                _isRecreatingAfterWithdrawalDelete = false;
            }
        }

        private static async Task<SupabaseResult<SupabaseSession>> RecreateSessionByMethodAsync(
            SignInMethodKind method,
            bool saveSessionToStorage)
        {
            return method switch
            {
                SignInMethodKind.Google => await SignInWithGoogleAsync(saveSessionToStorage),
                SignInMethodKind.Anonymous => await SignInAnonymouslyAsync(saveSessionToStorage),
                _ => SupabaseResult<SupabaseSession>.Fail("invalid_signin_method")
            };
        }

        private static async Task<bool> ShouldDeleteCurrentAccountByWithdrawalGuardAsync()
        {
            if (_bootstrap?.EdgeFunctionsService == null)
                return false;

            if (_currentSession == null || string.IsNullOrWhiteSpace(_currentSession.AccessToken))
                return false;

            if (string.IsNullOrWhiteSpace(_withdrawalGuardFunctionName))
                return false;

            var result = await _bootstrap.EdgeFunctionsService.InvokeAsync<WithdrawalGuardResponse>(
                _withdrawalGuardFunctionName,
                _currentSession.AccessToken,
                new WithdrawalGuardRequest { trigger = "post_login" });

            if (result == null || !result.IsSuccess)
            {
                Debug.LogWarning("[Supabase] withdrawal guard invoke failed: " + (result?.ErrorMessage ?? "unknown"));
                return false;
            }

            var data = result.Data;
            if (data == null)
                return false;

            return data.deleted
                   || data.should_delete
                   || string.Equals(data.action, "deleted", StringComparison.OrdinalIgnoreCase);
        }

        private static async Task<AnonymousRecoveryResult> TryRestoreSessionFromAnonymousRecoveryAsync()
        {
            var svc = AnonymousRecoveryService;
            if (svc == null)
                return new AnonymousRecoveryResult(AnonymousRecoveryKind.None);

            var fingerprintHash = DeviceFingerprintProvider.TryCreateHashedFingerprint(_initializedProjectUrl);
            if (string.IsNullOrWhiteSpace(fingerprintHash))
            {
                Debug.Log("[Supabase.AnonymousRecovery] No fingerprint hash");
                return new AnonymousRecoveryResult(AnonymousRecoveryKind.None);
            }

            var serverCode = GetCurrentServerCode();
            Debug.Log($"[Supabase.AnonymousRecovery] Attempting recovery with fingerprint={fingerprintHash}, serverCode={serverCode}");
            var tokenResult = await svc.TryGetRefreshTokenByFingerprintAsync(fingerprintHash, serverCode);
            Debug.Log($"[Supabase.AnonymousRecovery] Token query result: Success={tokenResult?.IsSuccess}, HasData={!string.IsNullOrWhiteSpace(tokenResult?.Data)}, Error={tokenResult?.ErrorMessage}");
            if (tokenResult == null || tokenResult.IsSuccess == false || string.IsNullOrWhiteSpace(tokenResult.Data))
            {
                Debug.LogWarning($"[Supabase.AnonymousRecovery] Token not found. Searched with fingerprint={fingerprintHash}, serverCode={serverCode}");
                return new AnonymousRecoveryResult(AnonymousRecoveryKind.None);
            }
            Debug.Log("[Supabase.AnonymousRecovery] Token found, refreshing session");

            var refreshResult = await RefreshSessionAsync(tokenResult.Data, saveSessionToStorage: true);
            if (refreshResult == null || refreshResult.IsSuccess == false || refreshResult.Data == null)
            {
                if (refreshResult?.ErrorMessage?.IndexOf("banned", StringComparison.OrdinalIgnoreCase) >= 0)
                    return new AnonymousRecoveryResult(AnonymousRecoveryKind.Banned);
                return new AnonymousRecoveryResult(AnonymousRecoveryKind.None);
            }

            // 사용자가 "익명 로그인 버튼"을 눌렀다고 가정하고, 만료(삭제 필요) 계정이면
            // allowRecreate=true 로 처리합니다(자동 복원 경로와 분리 목적).
            var guarded = await HandleWithdrawalGuardAfterSignInAsync(
                SignInMethodKind.Anonymous,
                saveSessionToStorage: true,
                allowRecreateOnDeletion: true);

            if (guarded != null)
            {
                if (!guarded.IsSuccess)
                    return new AnonymousRecoveryResult(AnonymousRecoveryKind.GuardFailed, guarded.ErrorMessage);
                // 재생성 등으로 세션이 바뀐 경우에도 복구 경로는 완료된 것으로 본다.
            }

            var reserved = await HandleWithdrawalReservationGateAfterSignInAsync();
            if (reserved != null)
                return new AnonymousRecoveryResult(AnonymousRecoveryKind.GateBlocked);

            Debug.Log($"[Supabase.AnonymousRecovery] Recovery complete. IsLoggedIn={IsLoggedIn}, SessionId={_currentSession?.User?.Id}");
            return new AnonymousRecoveryResult(AnonymousRecoveryKind.Restored);
        }

        private static async Task TryUpsertAnonymousRecoveryTokenAsync(SupabaseSession session)
        {
            var svc = AnonymousRecoveryService;
            if (svc == null)
                return;

            if (session == null || session.User == null || string.IsNullOrWhiteSpace(session.RefreshToken))
                return;

            var fingerprintHash = DeviceFingerprintProvider.TryCreateHashedFingerprint(_initializedProjectUrl);
            if (string.IsNullOrWhiteSpace(fingerprintHash))
                return;

            try
            {
                var serverCode = GetCurrentServerCode();
                var accountId = session.User?.Id;
                Debug.Log($"[Supabase.AnonymousRecovery] Upserting token for fingerprint={fingerprintHash}, serverCode={serverCode}, accountId={accountId}");
                _ = await svc.UpsertRefreshTokenByFingerprintAsync(
                    fingerprintHash,
                    session.RefreshToken,
                    accountId,
                    serverCode);
                Debug.Log("[Supabase.AnonymousRecovery] Upsert succeeded");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Supabase.AnonymousRecovery] Upsert failed: {e.Message}");
            }
        }

        private static async Task TryDeleteAnonymousRecoveryForCurrentDeviceAsync()
        {
            var svc = AnonymousRecoveryService;
            if (svc == null)
                return;

            var fingerprintHash = DeviceFingerprintProvider.TryCreateHashedFingerprint(_initializedProjectUrl);
            if (string.IsNullOrWhiteSpace(fingerprintHash))
                return;

            try
            {
                _ = await svc.DeleteByFingerprintAsync(fingerprintHash, GetCurrentServerCode());
            }
            catch
            {
                // best-effort
            }
        }

        private static async Task TryEnsureProfileRowAfterSignInAsync()
        {
            var svc = _bootstrap?.PublicProfileService;
            if (svc == null)
                return;

            var s = _currentSession;
            if (s == null || s.User == null)
                return;

            if (string.IsNullOrWhiteSpace(s.AccessToken) || string.IsNullOrWhiteSpace(s.User.Id))
                return;

            try
            {
                var r = await svc.EnsureMyProfileRowAsync(s.AccessToken, s.User.Id, s.User.PlayerUserId);
                if (r == null || !r.IsSuccess)
                {
                    Debug.LogWarning("[Supabase] ensure profile row failed: " + (r?.ErrorMessage ?? "unknown"));
                    return;
                }

                var selectedCode = GetCurrentServerCode();
                if (string.IsNullOrWhiteSpace(selectedCode))
                    return;

                var mine = await svc.GetMyServerIdAsync(s.AccessToken);
                if (mine == null || !mine.IsSuccess || mine.Data.ServerCode.Length == 0)
                    return;

                if (string.Equals(mine.Data.ServerCode, selectedCode, StringComparison.OrdinalIgnoreCase))
                    return;

                var moved = await svc.TransferMyServerAsync(s.AccessToken, selectedCode, "sdk_signin_server_sync");
                if (moved == null || !moved.IsSuccess)
                    Debug.LogWarning("[Supabase] server transfer after sign-in failed: " + (moved?.ErrorMessage ?? "unknown"));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Supabase] ensure profile row exception: " + e.Message);
            }
        }

        /// <summary>
        /// Google 신규 가입으로 판단되면 구글이 넣은 <c>user_metadata.displayName</c>을 <c>Player_xxxxxxxx</c>로 덮어쓰고 세션을 갱신합니다.
        /// </summary>
        private static async Task TryApplyAnonymousDisplayNameAfterNewGoogleSignUpAsync(bool saveSessionToStorage)
        {
            if (_bootstrap != null && _bootstrap.ApplyAnonymousDisplayNameOnNewGoogleSignUp == false)
                return;

            var s = _currentSession;
            if (s == null || s.User == null || s.likely_brand_new_google_signup == false)
                return;

            if (Auth == null)
                return;

            if (string.IsNullOrWhiteSpace(s.AccessToken) || string.IsNullOrWhiteSpace(s.RefreshToken))
                return;

            var def = SupabaseAuthService.BuildAnonymousDefaultDisplayNameFromAuthUserId(s.User.Id);
            var upd = await Auth.UpdateUserMetadataDisplayNameAsync(s.AccessToken, def);
            if (upd == null || !upd.IsSuccess)
            {
                Debug.LogWarning("[Supabase] new Google user: displayName metadata reset failed: " + (upd?.ErrorMessage ?? "unknown"));
                return;
            }

            var refr = await Auth.RefreshSessionAsync(s.RefreshToken);
            if (refr == null || !refr.IsSuccess || refr.Data == null)
            {
                Debug.LogWarning("[Supabase] new Google user: displayName updated but session refresh failed: " + (refr?.ErrorMessage ?? "unknown"));
                return;
            }

            refr.Data.likely_brand_new_google_signup = false;
            SetSession(refr.Data, SupabaseSessionChangeKind.RestoredOrRefreshed);
            if (saveSessionToStorage)
                SaveSessionToStorage();
        }

        [Serializable]
        private sealed class WithdrawalGuardRequest
        {
            public string trigger;
        }

        [Serializable]
        private sealed class WithdrawalGuardResponse
        {
            public bool deleted;
            public bool should_delete;
            public string action;
            public string reason;
        }

        [Serializable]
        private sealed class WithdrawalCancelIssueRequest
        {
            public string trigger;
        }

        [Serializable]
        private sealed class WithdrawalCancelIssueResponse
        {
            public bool ok;
            public string cancel_token;
            public string expires_at;
            public string reason;
        }

        [Serializable]
        private sealed class WithdrawalCancelRedeemRequest
        {
            public string cancel_token;
        }

        [Serializable]
        private sealed class WithdrawalCancelRedeemResponse
        {
            public bool ok;
            public string reason;
        }

        [Serializable]
        private sealed class DisplayNameSetRequest
        {
            public string display_name;
            public string user_id;
        }

        [Serializable]
        private sealed class DisplayNameSetResponse
        {
            public bool ok;
            public string reason;
        }

        private sealed class WithdrawalCancelIssueInfo
        {
            public WithdrawalCancelIssueInfo(string cancelToken, string expiresAtIso)
            {
                CancelToken = cancelToken;
                ExpiresAtIso = expiresAtIso;
            }

            public string CancelToken { get; }
            public string ExpiresAtIso { get; }
        }
    }
}

