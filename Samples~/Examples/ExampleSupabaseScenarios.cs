using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Purchasing;
using Truesoft.Supabase.Core.Data;
using Truesoft.Supabase.Unity;
using Truesoft.Supabase.Unity.RemoteConfig;

using SupabaseClient = global::Truesoft.Supabase.Unity.Supabase;

// ──────────────────────────────────────────────────────────────────────────────
// Remote Config 키 상수 샘플 — 프로젝트에 맞게 수정하여 사용하세요.
// ──────────────────────────────────────────────────────────────────────────────
namespace Truesoft.Supabase.Unity.RemoteConfig
{
    public static class RemoteConfigKeys
    {
        // 게임 설정 예시
        public const string MaxPlayers           = "game.max_players";
        public const string MatchTimeoutSeconds  = "game.match_timeout_seconds";
        public const string MaintenanceMode      = "game.maintenance_mode";

        // 밸런스 조정 예시
        public const string PlayerSpeedMultiplier = "balance.player_speed_multiplier";
        public const string EnemyHealthMultiplier  = "balance.enemy_health_multiplier";

        // A/B 테스트 예시
        public const string ExperimentVariant = "experiment.new_ui_variant";
    }

    // ──────────────────────────────────────────────────────────────────────────────
    // RemoteConfig 데이터 모델 샘플 — DB value_json 구조에 맞게 수정하세요.
    // ──────────────────────────────────────────────────────────────────────────────
    [Serializable]
    public class SampleRemoteConfigData
    {
        public float value;
        public string message;
    }
}

// ──────────────────────────────────────────────────────────────────────────────
// 샘플 코드
// ──────────────────────────────────────────────────────────────────────────────
namespace Truesoft.SupabaseUnity.Samples
{
    // ──────────────────────────────────────────────────────────────────────────
    // SampleUserSave
    //
    // StaticUserSave<TRow> 베이스 클래스를 활용한 유저 세이브 예시입니다.
    // 실제 프로젝트에서는:
    //   1. SampleUserSaveRow 의 [DataTable("basic")] 를 실제 테이블명으로 바꿉니다.
    //   2. SampleUserSaveRow 필드를 프로젝트 컬럼에 맞게 수정하고, [DataColumn] 을 붙입니다.
    //   3. SampleUserSave 에 프로퍼티를 추가합니다 (Level/Coins 패턴 참고).
    // ──────────────────────────────────────────────────────────────────────────

    public sealed class SampleUserSave : StaticUserSave<SampleUserSave.Row>
    {
        public static readonly SampleUserSave Instance = new("Truesoft.SupabaseUnity.Samples.SampleUserSave");
        private SampleUserSave(string syncKey) : base(syncKey) { }

        // [DataTable("테이블명")] 에 실제 테이블명을 입력하세요. "data_" 접두사는 자동으로 붙습니다.
        // 예: [DataTable("basic")] → DB 테이블 "data_basic"
        [DataTable("basic")]
        [Serializable]
        public sealed class Row
        {
            [DataColumn("level")] public int level;
            [DataColumn("coins")] public int coins;
        }

        public static int Level
        {
            get => Instance.Current.level;
            set { if (Instance.Current.level == value) return; Instance.Current.level = value; Instance.MarkDirty(); }
        }

        public static int Coins
        {
            get => Instance.Current.coins;
            set { if (Instance.Current.coins == value) return; Instance.Current.coins = value; Instance.MarkDirty(); }
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // ExampleSupabaseScenarios
    //
    // 샘플: 로그인·데이터·RemoteConfig·Edge Function·서버 샤드 등 전 기능 예시.
    // Run All On Start 또는 인스펙터 키보드 단축키로 실행합니다.
    // ──────────────────────────────────────────────────────────────────────────
    public sealed class ExampleSupabaseScenarios : MonoBehaviour
    {
        [Header("실행")]
        [SerializeField] private bool runAllOnStart = false;

        [Header("세이브 데모")]
        [SerializeField] private int level = 1;
        [SerializeField] private int coins = 100;

        [Header("원격 설정")]
        [Tooltip("T/U 예제용 key — DB remote_config.key와 같아야 합니다.")]
        [SerializeField] private string remoteConfigKey = "game_balance";

        [Header("엣지 함수")]
        [SerializeField] private string functionName = "gacha";

        [Header("표시 이름")]
        [SerializeField] private string demoDisplayName = "SamplePlayer";

        [Header("서버 샤드")]
        [Tooltip("이주 목표 서버 코드")]
        [SerializeField] private string serverShardTransferTargetCode = "GLOBAL";

        [Tooltip("시작 시 이주까지 시도")]
        [SerializeField] private bool serverShardAttemptTransfer = false;

        [Tooltip("시작 시 로컬 서버 코드 덮어쓰기. 비우면 유지")]
        [SerializeField] private string serverShardOptionalSetLocalCode = "";

        [Header("중복 로그인")]
        [Tooltip("중복 로그인 감지 이벤트 구독")]
        [SerializeField] private bool subscribeDuplicateLoginOnEnable = true;

        [Header("키보드 테스트")]
        [Tooltip("키 입력으로 샘플 API 호출")]
        [SerializeField] private bool enableKeyboardTest = true;

        [Tooltip("익명 로그인")]
        [SerializeField] private KeyCode keyLoginAnonymous = KeyCode.Q;

        [Tooltip("구글 로그인")]
        [SerializeField] private KeyCode keyLoginGoogle = KeyCode.I;

        [Tooltip("구글 연동")]
        [SerializeField] private KeyCode keyLinkGoogle = KeyCode.P;

        [Header("Apple 로그인 (iOS)")]
        [Tooltip("Apple 로그인")]
        [SerializeField] private KeyCode keyLoginApple = KeyCode.Alpha8;

        [Tooltip("Apple 익명 연동")]
        [SerializeField] private KeyCode keyLinkApple = KeyCode.Alpha9;

        [Tooltip("통합 로그아웃")]
        [SerializeField] private KeyCode keyLogout = KeyCode.W;

        [Tooltip("공개 닉네임 설정")]
        [SerializeField] private KeyCode keySetDisplayName = KeyCode.E;

        [Tooltip("유저 세이브 로드 (행 없으면 인스펙터 레벨/코인을 초기값으로)")]
        [SerializeField] private KeyCode keyLoadUserSave = KeyCode.R;

        [Tooltip("유저 세이브 저장 (서버와 비교해 변경분만 전송, 같으면 생략)")]
        [SerializeField] private KeyCode keySaveUserSave = KeyCode.V;

        [Tooltip("원격 설정 새로고침 및 조회")]
        [SerializeField] private KeyCode keyRemoteConfig = KeyCode.T;

        [Tooltip("RemoteConfigBinding 현재 값 로그")]
        [SerializeField] private KeyCode keyRemoteConfigOnDemand = KeyCode.U;

        [Tooltip("엣지 함수 호출")]
        [SerializeField] private KeyCode keyInvokeFunction = KeyCode.Y;

        [Tooltip("중복 로그인 테스트 안내")]
        [SerializeField] private KeyCode keyDuplicateLoginInfo = KeyCode.L;

        [Tooltip("서버 시각 조회")]
        [SerializeField] private KeyCode keyServerTime = KeyCode.H;

        [Tooltip("탈퇴 요청")]
        [SerializeField] private KeyCode keyRequestWithdrawal = KeyCode.J;

        [Tooltip("탈퇴 상태 조회")]
        [SerializeField] private KeyCode keyWithdrawalStatus = KeyCode.K;

        [Tooltip("탈퇴 예약 취소")]
        [SerializeField] private KeyCode keyWithdrawalCancel = KeyCode.C;

        [Tooltip("서버 샤드 조회 및 이주")]
        [SerializeField] private KeyCode keyServerShard = KeyCode.N;

        [Header("인앱 결제")]
        [Tooltip("구매할 상품 ID (Google Play Console / App Store Connect에 등록된 Product ID)")]
        [SerializeField] private string demoPurchaseProductId = "com.yourcompany.yourgame.item_id";

        [Tooltip("IAP 초기화 (또는 재초기화 — 미처리 구매 자동 재처리)")]
        [SerializeField] private KeyCode keyInitializeIAP = KeyCode.M;

        [Tooltip("실제 결제 프로세스 시작 및 자동 검증 (M으로 IAP 초기화 후 사용)")]
        [SerializeField] private KeyCode keyVerifyPurchase = KeyCode.B;

        [Header("인앱 결제 - 미처리 테스트")]
        [Tooltip("켜면 구매 후 Confirm(소비)을 건너뜀 → 미처리 상태로 남김. M 키로 재초기화하면 자동 재처리됨.")]
        [SerializeField] private bool skipConfirmForTest = false;

        private IAPFacade _iapFacade;
        private bool _keyboardBusy;

        // RemoteConfig 폴링 패턴 샘플
        private RemoteConfigBinding<SampleRemoteConfigData> _remoteConfigBinding;
        private IDisposable _remoteConfigListener;

        private void OnEnable()
        {
            if (subscribeDuplicateLoginOnEnable)
                SupabaseClient.OnDuplicateLoginDetected += HandleDuplicateLoginDetected;

            // RemoteConfigBinding — 30초마다 폴링, Value로 읽기.
            _remoteConfigBinding = SupabaseClient.CreateRemoteConfigBinding<SampleRemoteConfigData>(
                remoteConfigKey, pollIntervalSeconds: 30f);

            // RemoteConfigListener — 값 변경 시 콜백 자동 호출.
            _remoteConfigListener = SupabaseClient.CreateRemoteConfigListener<SampleRemoteConfigData>(
                remoteConfigKey, pollIntervalSeconds: 30f,
                cfg => Debug.Log("[Sample] RemoteConfigListener: value=" + cfg?.value + " message=" + cfg?.message));
        }

        private void OnDisable()
        {
            SupabaseClient.OnDuplicateLoginDetected -= HandleDuplicateLoginDetected;
            _remoteConfigBinding?.Dispose();
            _remoteConfigListener?.Dispose();
            _iapFacade?.Dispose();
        }

        private void HandleDuplicateLoginDetected()
        {
            Debug.Log(
                "[Sample] OnDuplicateLoginDetected: 다른 기기에서 같은 계정으로 로그인했습니다. "
                + "이미 Supabase 세션은 정리되었으므로 로그인 화면으로 보내거나 팝업만 띄우면 됩니다.");
        }

        private void Start()
        {
            if (runAllOnStart)
                _ = RunAllExamplesAsync();
        }

        private void Update()
        {
            if (!enableKeyboardTest || _keyboardBusy)
                return;

            if      (Input.GetKeyDown(keyLoginAnonymous))       _ = RunAsyncGuarded(RunLoginExampleAsync);
            else if (Input.GetKeyDown(keyLoginGoogle))          _ = RunAsyncGuarded(RunGoogleLoginExampleAsync);
            else if (Input.GetKeyDown(keyLinkGoogle))           _ = RunAsyncGuarded(RunGoogleLinkExampleAsync);
            else if (Input.GetKeyDown(keyLogout))               _ = RunAsyncGuarded(RunLogoutExampleAsync);
            else if (Input.GetKeyDown(keySetDisplayName))       _ = RunAsyncGuarded(RunPublicNicknameExampleAsync);
            else if (Input.GetKeyDown(keyLoadUserSave))         _ = RunAsyncGuarded(RunLoadUserSaveExampleAsync);
            else if (Input.GetKeyDown(keySaveUserSave))         _ = RunAsyncGuarded(RunSaveUserSaveExampleAsync);
            else if (Input.GetKeyDown(keyRemoteConfig))         _ = RunAsyncGuarded(RunRemoteConfigExampleAsync);
            else if (Input.GetKeyDown(keyRemoteConfigOnDemand)) _ = RunAsyncGuarded(RunRemoteConfigBindingExampleAsync);
            else if (Input.GetKeyDown(keyInvokeFunction))       _ = RunAsyncGuarded(RunFunctionExampleAsync);
            else if (Input.GetKeyDown(keyDuplicateLoginInfo))   LogDuplicateLoginHowToTest();
            else if (Input.GetKeyDown(keyServerTime))           _ = RunAsyncGuarded(RunServerTimeExampleAsync);
            else if (Input.GetKeyDown(keyRequestWithdrawal))    _ = RunAsyncGuarded(RunWithdrawalRequestExampleAsync);
            else if (Input.GetKeyDown(keyWithdrawalStatus))     _ = RunAsyncGuarded(RunWithdrawalStatusExampleAsync);
            else if (Input.GetKeyDown(keyWithdrawalCancel))     _ = RunAsyncGuarded(RunWithdrawalCancelRedeemExampleAsync);
            else if (Input.GetKeyDown(keyServerShard))          _ = RunAsyncGuarded(RunServerShardExampleAsync);
            else if (Input.GetKeyDown(keyInitializeIAP))        _ = RunAsyncGuarded(RunInitializeIAPExampleAsync);
            else if (Input.GetKeyDown(keyVerifyPurchase))       _ = RunAsyncGuarded(RunVerifyPurchaseExampleAsync);
#if TRUESOFT_APPLE_AUTH_AVAILABLE
            else if (Input.GetKeyDown(keyLoginApple))           _ = RunAsyncGuarded(RunAppleLoginExampleAsync);
            else if (Input.GetKeyDown(keyLinkApple))            _ = RunAsyncGuarded(RunAppleLinkExampleAsync);
#endif
        }

        private async Task RunAsyncGuarded(Func<Task<bool>> body)
        {
            try
            {
                _keyboardBusy = true;
                var ok = await body();
                if (!ok)
                    Debug.LogWarning("[Sample] Keyboard test failed (see previous logs).");
            }
            catch (Exception e)
            {
                Debug.LogError("[Sample] Keyboard test exception: " + e.Message);
            }
            finally
            {
                _keyboardBusy = false;
            }
        }

        private async Task<bool> RunLoginExampleAsync()
        {
            var ok = await SupabaseClient.TrySignInAnonymouslyAsync();
            if (!ok) { Debug.Log("[Sample] login example failed."); return false; }

            Debug.Log("[Sample] login example success.");
            return true;
        }

        private async Task<bool> RunGoogleLoginExampleAsync()
        {
            var ok = await SupabaseClient.TrySignInWithGoogleAsync();
            if (!ok) { Debug.Log("[Sample] google login example failed."); return false; }

            Debug.Log("[Sample] google login example success.");
            return true;
        }

        private async Task<bool> RunGoogleLinkExampleAsync()
        {
            if (!SupabaseClient.IsLoggedIn || SupabaseClient.Session?.User == null || !SupabaseClient.Session.User.IsAnonymous)
            {
                Debug.LogWarning("[Sample] google link example skipped: anonymous session required.");
                return false;
            }

            var beforeId = SupabaseClient.Session.User.Id;
            var ok = await SupabaseClient.TryLinkGoogleToCurrentAnonymousAsync();
            if (!ok || !SupabaseClient.IsLoggedIn || SupabaseClient.Session?.User == null)
            {
                Debug.LogWarning("[Sample] google link example failed (이미 사용 중인 Google이면 연동 불가).");
                return false;
            }

            var after     = SupabaseClient.Session.User;
            var sameId    = string.Equals(beforeId, after.Id, StringComparison.OrdinalIgnoreCase);
            var converted = !after.IsAnonymous;
            Debug.Log("[Sample] google link example result. "
                + $"same_auth_user_id={sameId}, is_anonymous_after={after.IsAnonymous}");
            return sameId && converted;
        }

        private async Task<bool> RunLoadUserSaveExampleAsync()
        {
            if (!SupabaseClient.IsLoggedIn)
            {
                Debug.LogWarning("[Sample] load user save skipped: sign in first.");
                return false;
            }

            if (!await SampleUserSave.Instance.TryLoadAsync())
            {
                Debug.LogWarning("[Sample] load user save failed (네트워크·인증 등).");
                return false;
            }

            Debug.Log($"[Sample] load user save ok. level={SampleUserSave.Level}, coins={SampleUserSave.Coins}");
            return true;
        }

        private async Task<bool> RunSaveUserSaveExampleAsync()
        {
            if (!SupabaseClient.IsLoggedIn)
            {
                Debug.LogWarning("[Sample] save user save skipped: sign in first.");
                return false;
            }

            // 서버 스냅샷을 먼저 맞춘 뒤, 변경할 값을 프로퍼티에 설정합니다.
            if (!await SampleUserSave.Instance.TryLoadAsync())
            {
                Debug.LogWarning("[Sample] save user save: load failed (스냅샷 맞추기 전 단계).");
                return false;
            }

            SampleUserSave.Level = level;
            SampleUserSave.Coins = coins;

            if (!await SampleUserSave.Instance.TrySaveIfChangedAsync())
            {
                Debug.LogWarning("[Sample] save user save: TrySaveIfChangedAsync failed (상세는 [SampleStaticUserSave] 로그).");
                return false;
            }

            Debug.Log("[Sample] save user save finished. 변경·전송 여부는 위쪽 [SampleStaticUserSave] 로그를 보세요.");
            return true;
        }

        private async Task<bool> RunPublicNicknameExampleAsync()
        {
            if (!SupabaseClient.IsLoggedIn || SupabaseClient.Session?.User == null)
            {
                Debug.LogWarning("[Sample] displayName example skipped: sign in first.");
                return false;
            }

            var playerUserId = SupabaseClient.Session.User.PlayerUserId;

            if (!await SupabaseClient.TryIsDisplayNameAvailableAsync(demoDisplayName))
            {
                Debug.LogWarning($"[Sample] displayName \"{demoDisplayName}\": 이미 사용 중인 닉네임입니다.");
                return false;
            }

            if (!await SupabaseClient.TrySetMyDisplayNameAsync(demoDisplayName))
            {
                Debug.LogWarning($"[Sample] displayName \"{demoDisplayName}\": 설정 실패.");
                return false;
            }

            var readBack = await SupabaseClient.TryGetPublicDisplayNameAsync(playerUserId, defaultValue: "");
            Debug.Log(readBack == demoDisplayName
                ? $"[Sample] displayName example success: '{readBack}'"
                : $"[Sample] displayName example: set ok but read '{readBack}' (expected '{demoDisplayName}').");
            return readBack == demoDisplayName;
        }

        /// <summary>
        /// 통합 로그아웃: Android에서는 TrySignOutFullyAsync 가 Google 네이티브 로그아웃을 시도한 뒤
        /// Supabase SignOutAsync 와 동일하게 처리합니다. 익명이면 복구용 upsert 가 수행됩니다.
        /// </summary>
        private async Task<bool> RunLogoutExampleAsync()
        {
            if (!SupabaseClient.IsLoggedIn)
            {
                Debug.LogWarning("[Sample] logout example skipped: not signed in.");
                return false;
            }

            await SupabaseClient.TrySignOutFullyAsync();
            Debug.Log("[Sample] logout example: TrySignOutFullyAsync 완료.");
            return true;
        }

        private static void LogDuplicateLoginHowToTest()
        {
            Debug.Log(
                "[Sample] 중복 로그인 테스트: Sql/player/05_user_sessions.sql 적용 후, "
                + "SupabaseSettings에서 enableDuplicateSessionMonitor를 켠 뒤 "
                + "기기 A·B(또는 에뮬+실기)에서 같은 계정으로 순서대로 로그인하면, "
                + "먼저 켜 둔 쪽에서 OnDuplicateLoginDetected가 호출됩니다.");
        }

        /// <summary>
        /// 로컬 GetCurrentServerCode 와 RPC ts_my_server_id 결과를 비교하고,
        /// 인스펙터에서 허용한 경우 TryTransferMyServerAsync 를 호출합니다.
        /// </summary>
        private async Task<bool> RunServerShardExampleAsync()
        {
            if (!await SupabaseClient.EnsureInitializedAsync())
            {
                Debug.LogWarning("[Sample] server shard skipped: SDK not initialized.");
                return false;
            }

            if (!SupabaseClient.IsLoggedIn)
            {
                Debug.LogWarning("[Sample] server shard skipped: sign in first (anonymous or Google).");
                return false;
            }

            if (!string.IsNullOrWhiteSpace(serverShardOptionalSetLocalCode))
            {
                SupabaseClient.SetCurrentServerCode(serverShardOptionalSetLocalCode.Trim());
                Debug.Log("[Sample] server shard: applied local server code from inspector: " + serverShardOptionalSetLocalCode.Trim());
            }

            var localCode = SupabaseClient.GetCurrentServerCode();
            var db        = await SupabaseClient.GetMyServerInfoAsync();
            if (db == null || !db.IsSuccess)
            {
                var hint = string.Equals(db?.ErrorMessage, "my_server_not_found", StringComparison.Ordinal)
                    ? "profiles에 account_id=본인 행이 없을 때 흔함. Q로 익명 로그인해도 됩니다."
                    : "Sql/player/08_transfer_server.sql 적용·ts_my_server_id·로그인·프로필 행 확인.";
                Debug.LogWarning("[Sample] server shard: ts_my_server_id failed — " + (db?.ErrorMessage ?? "null") + ". " + hint);
                return false;
            }

            Debug.Log(
                "[Sample] server shard: local_selected_code=" + localCode
                + ", db_server_code=" + db.Data.ServerCode
                + ", db_server_id="   + db.Data.ServerId);

            if (!serverShardAttemptTransfer)
            {
                Debug.Log("[Sample] server shard: transfer skipped (enable Server Shard Attempt Transfer in inspector to call TryTransferMyServerAsync).");
                return true;
            }

            var target = serverShardTransferTargetCode?.Trim();
            if (string.IsNullOrEmpty(target))
            {
                Debug.LogWarning("[Sample] server shard: transfer skipped — serverShardTransferTargetCode is empty.");
                return false;
            }

            var moved = await SupabaseClient.TryTransferMyServerAsync(target, "sample_ExampleSupabaseScenarios");
            Debug.Log(moved
                ? "[Sample] server shard: TryTransferMyServerAsync ok."
                : "[Sample] server shard: TryTransferMyServerAsync failed (target missing, allow_transfers=false, 등).");
            return moved;
        }

        /// <summary>RPC ts_server_now 로 DB 서버 시각을 가져옵니다. 로그인 세션 없이 호출 가능합니다.</summary>
        private async Task<bool> RunServerTimeExampleAsync()
        {
            if (!await SupabaseClient.EnsureInitializedAsync())
            {
                Debug.LogWarning("[Sample] server time skipped: SDK not initialized.");
                return false;
            }

            var r = await SupabaseClient.GetServerUtcNowAsync();
            if (r == null || !r.IsSuccess)
            {
                Debug.LogWarning("[Sample] server time failed: " + (r?.ErrorMessage ?? "null")
                    + " (Sql/supabase_server_time.sql 적용 여부 확인)");
                return false;
            }

            Debug.Log("[Sample] server time (UTC): " + r.Data.ToString("o"));
            return true;
        }

        /// <summary>
        /// 설정된 유예 기간(SupabaseSettings.withdrawalRequestDelayDays)으로 탈퇴를 요청합니다.
        /// 실제 withdrawn_at 계산은 서버 RPC(ts_request_withdrawal)가 처리합니다.
        /// </summary>
        private async Task<bool> RunWithdrawalRequestExampleAsync()
        {
            if (!SupabaseClient.IsLoggedIn)
            {
                Debug.LogWarning("[Sample] withdrawal request skipped: sign in first.");
                return false;
            }

            var ok = await SupabaseClient.TryRequestMyWithdrawalAsync();
            Debug.Log(ok
                ? "[Sample] withdrawal request success. 서버가 유예 기간 기준으로 withdrawn_at을 예약했고, 앱은 즉시 로그아웃 처리했습니다."
                : "[Sample] withdrawal request failed. Sql/supabase_withdrawal_request.sql 적용 및 profiles/RLS를 확인하세요.");
            return ok;
        }

        private async Task<bool> RunWithdrawalStatusExampleAsync()
        {
            if (!SupabaseClient.IsLoggedIn)
            {
                var cached = SupabaseClient.GetStoredWithdrawalGateStatus();
                if (cached == null || string.IsNullOrWhiteSpace(cached.WithdrawnAtIso))
                {
                    Debug.LogWarning("[Sample] withdrawal status skipped: sign in first (or no cached gate status).");
                    return false;
                }

                Debug.Log($"[Sample] cached gate status. displayName={cached.DisplayName}, withdrawn_at={cached.WithdrawnAtIso}, remain_sec={cached.SecondsRemaining}");
                return true;
            }

            var status = await SupabaseClient.TryGetMyWithdrawalStatusAsync();
            if (status == null)
            {
                Debug.LogWarning("[Sample] withdrawal status failed.");
                return false;
            }

            Debug.Log(
                $"[Sample] withdrawal status. displayName={status.DisplayName}, is_scheduled={status.IsScheduled}, "
                + $"withdrawn_at={status.WithdrawnAtIso}, remain_sec={status.SecondsRemaining}, server_now={status.ServerNowIso}");
            return true;
        }

        private async Task<bool> RunWithdrawalCancelRedeemExampleAsync()
        {
            // cancel_token은 탈퇴 예약 계정으로 로그인할 때 게이트에서 발급·저장됨.
            // 1) 로그인 상태면 issue로 토큰 발급 후 세션 정리
            // 2) 저장된 토큰으로 redeem
            if (SupabaseClient.IsLoggedIn)
            {
                var token = await SupabaseClient.TryRequestWithdrawalCancelTokenAsync(defaultValue: null);
                if (string.IsNullOrWhiteSpace(token))
                {
                    Debug.LogWarning("[Sample] withdrawal cancel issue failed (예약 중 계정인지 확인).");
                    return false;
                }

                SupabaseClient.ClearSession();
                Debug.Log("[Sample] withdrawal cancel token issued, session cleared. proceeding redeem...");
            }

            var ok = await SupabaseClient.TryRedeemWithdrawalCancelAsync();
            Debug.Log(ok
                ? "[Sample] withdrawal cancel redeem success. now sign in again."
                : "[Sample] withdrawal cancel redeem failed. token missing/expired or server not deployed.");
            return ok;
        }

        private async Task<bool> RunRemoteConfigExampleAsync()
        {
            // CreateRemoteConfigReader: 캐시가 신선하면 즉시, 만료됐으면 서버 응답 대기 후 반환.
            // 실제 프로젝트에서는 필드로 선언해두고 재사용합니다:
            //   private readonly Func<Task<SampleRemoteConfigData>> _getConfig =
            //       Supabase.CreateRemoteConfigReader<SampleRemoteConfigData>("my_key");
            var reader = SupabaseClient.CreateRemoteConfigReader<SampleRemoteConfigData>(remoteConfigKey);
            var cfg = await reader();

            SupabaseClient.TryGetRemoteConfigRaw(remoteConfigKey, out var raw);

            if (cfg == null)
            {
                Debug.LogWarning("[Sample] remote config reader returned null (key=" + remoteConfigKey + "). raw: " + raw);
                return false;
            }

            Debug.Log("[Sample] remote config reader OK. value=" + cfg.value + " message=" + cfg.message + " raw: " + raw);
            return true;
        }

        private Task<bool> RunRemoteConfigBindingExampleAsync()
        {
            // RemoteConfigBinding: OnEnable에서 이미 생성됨. Value로 현재 캐시 값을 읽습니다.
            var cfg = _remoteConfigBinding?.Value;
            SupabaseClient.TryGetRemoteConfigRaw(remoteConfigKey, out var raw);

            if (cfg == null)
            {
                Debug.Log("[Sample] RemoteConfigBinding.Value = null (아직 첫 fetch 완료 전). raw: " + raw);
                return Task.FromResult(false);
            }

            Debug.Log("[Sample] RemoteConfigBinding.Value OK. value=" + cfg.value + " message=" + cfg.message);
            return Task.FromResult(true);
        }

        private async Task<bool> RunFunctionExampleAsync()
        {
            var result = await SupabaseClient.TryInvokeFunctionAsync<object>(
                functionName,
                new { bannerId = "asd", drawCount = 4, seed = 15 },
                defaultValue: null);

            var ok = result != null;
            Debug.Log(ok ? "[Sample] function example success." : "[Sample] function example failed.");
            return ok;
        }

        /// <summary>
        /// Unity IAP v5 초기화 (Android/iOS 통합). IAPFacade를 생성하고 OnGrantItemAsync 콜백을 설정합니다.
        /// M 키로 실행. 미처리 구매가 있으면 자동으로 재검증됩니다.
        /// </summary>
        private async Task<bool> RunInitializeIAPExampleAsync()
        {
            if (_iapFacade?.IsInitialized == true)
            {
                Debug.Log("[Sample] IAP already initialized.");
                return true;
            }

            _iapFacade?.Dispose();
            _iapFacade = SupabaseClient.CreateIAP();

            // 아이템 지급 콜백 — 실제 게임에서는 여기서 인벤토리에 아이템 추가
            _iapFacade.OnGrantItemAsync = (order, response, isResuming) =>
            {
                var productId = order.CartOrdered?.Items()?[0].Product.definition.id ?? "unknown";

                if (skipConfirmForTest && !isResuming)
                {
                    Debug.LogWarning("[Sample] [TEST] skipConfirmForTest=true → Confirm 생략. 미처리 상태로 남김.");
                    Debug.LogWarning("[Sample] [TEST] M 키로 IAP 재초기화하면 미처리 구매가 자동 재처리됩니다.");
                    return Task.FromResult(false);   // false → SDK가 ConfirmPurchase 호출 안 함 → Pending 유지
                }

                Debug.Log($"[Sample] [OK] 아이템 지급: product={productId}, order_id={response.order_id}, store={response.store}");

                if (response.already_verified)
                    Debug.Log("[Sample] already_verified=true: 이전에 이미 검증된 영수증입니다. 중복 지급 없음 (정상).");

                return Task.FromResult(true);        // true → SDK가 ConfirmPurchase 호출 (소모품 소비)
            };

            _iapFacade.OnPurchaseFailed += order => Debug.LogError($"[Sample] [FAIL] 구매 실패: {order}");

            var ok = await _iapFacade.InitializeAsync(new[] { demoPurchaseProductId });
            if (ok) Debug.Log("[Sample] IAP initialized. Press B to purchase.");
            else    Debug.LogWarning("[Sample] IAP initialization failed.");
            return ok;
        }

        /// <summary>결제 프로세스 시작 (B 키). M 키로 IAP 초기화 후 사용.</summary>
        private Task<bool> RunVerifyPurchaseExampleAsync()
        {
            if (!SupabaseClient.IsLoggedIn)
            {
                Debug.LogWarning("[Sample] purchase skipped: sign in first.");
                return Task.FromResult(false);
            }

            if (_iapFacade?.IsInitialized != true)
            {
                Debug.LogWarning("[Sample] purchase skipped: IAP not initialized (press M first).");
                return Task.FromResult(false);
            }

            var productId = demoPurchaseProductId?.Trim();
            if (string.IsNullOrEmpty(productId))
            {
                Debug.LogWarning("[Sample] purchase skipped: Inspector의 Demo Purchase Product Id를 입력하세요.");
                return Task.FromResult(false);
            }

            _iapFacade.Purchase(productId);
            return Task.FromResult(true);
        }

#if TRUESOFT_APPLE_AUTH_AVAILABLE
        private async Task<bool> RunAppleLoginExampleAsync()
        {
            var ok = await SupabaseClient.TrySignInWithAppleAsync();
            if (!ok) { Debug.Log("[Sample] Apple login failed."); return false; }

            Debug.Log("[Sample] Apple login success.");
            return true;
        }

        private async Task<bool> RunAppleLinkExampleAsync()
        {
            var ok = await SupabaseClient.TryLinkAppleToCurrentAnonymousAsync();
            Debug.Log(ok ? "[Sample] Apple link success." : "[Sample] Apple link failed.");
            return ok;
        }
#endif

        private async Task RunAllExamplesAsync()
        {
            // 주의: SupabaseRuntime이 자동으로 게임 시작 절차(초기화 + 세션 복원)를 수행하므로,
            // 여기서는 TryStartAsync()를 호출할 필요가 없습니다. API를 바로 사용 가능합니다.

            await RunServerTimeExampleAsync();
            await RunLoginExampleAsync();
            await RunLoadUserSaveExampleAsync();
            await RunSaveUserSaveExampleAsync();
            await RunPublicNicknameExampleAsync();
            await RunWithdrawalRequestExampleAsync();
            await RunWithdrawalStatusExampleAsync();
            await RunRemoteConfigExampleAsync();
            await RunFunctionExampleAsync();

            Debug.Log("[Sample] all examples finished.");
        }
    }
}
