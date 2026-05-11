using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Truesoft.Supabase.Unity;
using Truesoft.Supabase.Unity.Auth.Google;
using UnityEngine;
using UnityEngine.Serialization;

namespace Truesoft.Supabase.Unity.Config
{
    /// <summary>
    /// Supabase SDK의 "씬 실행 정책"을 제어하는 런타임 컴포넌트입니다.
    /// - 초기화 시점
    /// - 앱 시작 자동 로그인 시도
    /// - RemoteConfig: Cold Start(시작 시 fetch 없음), 키 단위 백그라운드 폴링
    /// 설계: 1키 = 1설정묶음(JSON) = 1폴링주기 (category 없음)
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [AddComponentMenu("TrueSoft/Supabase/Supabase 런타임")]
    public sealed class SupabaseRuntime : MonoBehaviour
    {
        private static SupabaseRuntime _instance;

        [Header("설정")]
        [Label("설정 에셋")]
        [Tooltip("SupabaseSettings. 비우면 Resources에서 로드.")]
        [SerializeField] private SupabaseSettings settings;

        [Header("씬")]
        [Label("씬 유지 (DontDestroyOnLoad)")]
        [Tooltip("DontDestroyOnLoad로 유지.")]
        [FormerlySerializedAs("dontDestroyOnLoad")]
        [SerializeField] private bool dontDestroyOnLoad = true;

        [Header("세션")]
        [Label("세션 자동 복원")]
        [Tooltip("OnEnable 시 저장된 세션을 자동으로 복원합니다. false이면 TriggerSessionRestoreAsync()를 직접 호출해야 합니다.")]
        [FormerlySerializedAs("autoRestoreSessionOnEnable")]
        [SerializeField] private bool autoRestoreSessionOnEnable = true;

        [Header("원격 설정 (RemoteConfig)")]
        [Label("원격 설정 사용")]
        [Tooltip("런타임 동기화 사용. Cold Start: 시작 시 RemoteConfig를 가져오지 않습니다.")]
        [FormerlySerializedAs("enableRemoteConfig")]
        [SerializeField] private bool enableRemoteConfig = true;

        [Label("온디맨드 폴링 지연 (초)")]
        [FormerlySerializedAs("pollIntervalSeconds")]
        [FormerlySerializedAs("remoteConfigOnDemandPushbackSeconds")]
        [Tooltip("TryRefreshRemoteConfigAsync / RefreshRemoteConfigOnDemandAsync 호출 후 카테고리 폴링 시각을 이 시간(초)만큼 뒤로 미룹니다. 0 이하면 SDK에서 60초로 처리합니다.")]
        [SerializeField] private float remoteConfigOnDemandPushbackSeconds = 60f;

        [Label("키별 폴링 주기 오버라이드")]
        [FormerlySerializedAs("remoteConfigKeyPollOverrides")]
        [Tooltip("키별 폴링 주기 오버라이드. 비우면 DB remote_config.poll_interval_seconds만 사용.")]
        [SerializeField] private List<RemoteConfigKeyPollOverrideEntry> remoteConfigKeyPollOverrides = new List<RemoteConfigKeyPollOverrideEntry>();

        [Header("유저 세이브 자동 저장")]
        [Label("자동 동기화 사용")]
        [Tooltip("정적 세이브 자동 동기화 사용.")]
        [FormerlySerializedAs("enableUserSaveAutoSync")]
        [SerializeField] private bool enableUserSaveAutoSync = true;

        [Label("자동 저장 쿨타임 (초)")]
        [FormerlySerializedAs("userSaveAutoSyncCooldownSeconds")]
        [Tooltip("자동 저장 쿨타임(초).")]
        [SerializeField] private float userSaveAutoSyncCooldownSeconds = 1f;

        /// <summary>
        /// 세션 복원 시도가 완료되면 발행됩니다. bool: 복원 성공 여부.
        /// autoRestoreSessionOnEnable(자동) 또는 <see cref="TriggerSessionRestoreAsync"/>(수동) 모두 이 이벤트를 발행합니다.
        /// </summary>
        /// <remarks>
        /// <c>OnEnable()</c>에서 구독하고 <c>OnDisable()</c>에서 해제하세요.
        /// 구독 시점에 이미 완료된 경우를 대비해 <see cref="IsSessionRestoreCompleted"/>를 함께 확인하세요.
        /// <code>
        /// void OnEnable()
        /// {
        ///     SupabaseRuntime.OnSessionRestored += OnSessionRestored;
        ///     if (SupabaseRuntime.IsSessionRestoreCompleted)
        ///         OnSessionRestored(SupabaseRuntime.SessionRestoreResult);
        /// }
        /// void OnDisable()
        /// {
        ///     SupabaseRuntime.OnSessionRestored -= OnSessionRestored;
        /// }
        /// </code>
        /// </remarks>
        public static event Action<bool> OnSessionRestored;

        /// <summary>세션 복원 시도가 완료되었는지 여부.</summary>
        public static bool IsSessionRestoreCompleted { get; private set; }

        /// <summary>
        /// 세션 복원 결과. true면 복원 성공, false면 실패 또는 저장된 세션 없음.
        /// <see cref="IsSessionRestoreCompleted"/>가 true일 때만 유효합니다.
        /// </summary>
        public static bool SessionRestoreResult { get; private set; }

        private Coroutine _lifecycleRoutine;
        private bool _remoteConfigPollSettingsApplied;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Debug.LogWarning("[Supabase] Duplicate SupabaseRuntime detected. Destroying duplicate object.");
                Destroy(gameObject);
                return;
            }

            _instance = this;

            if (settings == null)
            {
                settings = Resources.Load<SupabaseSettings>("SupabaseSettings");
            }

            if (settings == null)
            {
                Debug.LogWarning(
                    "[Supabase] SupabaseSettings를 찾을 수 없습니다(인스펙터 미할당 또는 Resources 로드 실패).\n"
                    + SupabaseUnitySetupHelp.InitializationChecklistKo);
                return;
            }

            var bootstrap = new SupabaseUnityBootstrap();
            bootstrap.Initialize(settings);

            Supabase.ConfigureUserSaveAutoSyncCooldown(userSaveAutoSyncCooldownSeconds);

            if (dontDestroyOnLoad)
                DontDestroyOnLoad(gameObject);

            EnsureGoogleLoginBridge();
        }

        private void OnEnable()
        {
            if (autoRestoreSessionOnEnable && _lifecycleRoutine == null)
                _lifecycleRoutine = StartCoroutine(RunLifecycle());
        }

        private void OnDisable()
        {
            if (_lifecycleRoutine != null)
            {
                StopCoroutine(_lifecycleRoutine);
                _lifecycleRoutine = null;
            }
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        private void Update()
        {
            if (enableUserSaveAutoSync && Supabase.IsInitialized)
                SupabaseSDK.TickUserSaveAutoSync(Time.realtimeSinceStartup);

            if (!enableRemoteConfig || !Supabase.IsInitialized)
                return;

            EnsureRemoteConfigPollSettingsApplied();
            SupabaseSDK.TickRemoteConfigKeyPolls(Time.realtimeSinceStartup);
        }

        private void EnsureRemoteConfigPollSettingsApplied()
        {
            if (_remoteConfigPollSettingsApplied)
                return;

            _remoteConfigPollSettingsApplied = true;
            var pushback = remoteConfigOnDemandPushbackSeconds <= 0f ? 60f : remoteConfigOnDemandPushbackSeconds;
            SupabaseSDK.UpdateRemoteConfigPollIntervalSeconds(pushback);
            SupabaseSDK.ApplyRemoteConfigKeyPollOverrides(remoteConfigKeyPollOverrides);
        }

        private void OnApplicationPause(bool pause)
        {
            if (!pause || !enableUserSaveAutoSync)
                return;

            Supabase.RequestImmediateUserSaveStaticFlushAll();
        }

        private void OnApplicationQuit()
        {
            if (!enableUserSaveAutoSync)
                return;

            Supabase.RequestImmediateUserSaveStaticFlushAll();
        }

        private IEnumerator RunLifecycle()
        {
            while (!Supabase.IsInitialized)
                yield return null;

            var autoLoginTask = Supabase.TryAutoLoginOnStartAsync();
            yield return new WaitUntil(() => autoLoginTask.IsCompleted);

            SetSessionRestoreCompleted(autoLoginTask.Result);

            // RemoteConfig: Cold Start — 시작 시 fetch 없음. 폴링은 Update에서 TickRemoteConfigKeyPolls.
        }

        /// <summary>
        /// 세션 복원을 수동으로 시작합니다. 완료 시 <see cref="OnSessionRestored"/> 이벤트를 발행합니다.
        /// autoRestoreSessionOnEnable이 false일 때 원하는 타이밍에 호출합니다.
        /// </summary>
        public static async Task TriggerSessionRestoreAsync()
        {
            var ok = await Supabase.TryAutoLoginOnStartAsync();
            SetSessionRestoreCompleted(ok);
        }

        private static void SetSessionRestoreCompleted(bool result)
        {
            IsSessionRestoreCompleted = true;
            SessionRestoreResult = result;
            OnSessionRestored?.Invoke(result);
        }

        private void EnsureGoogleLoginBridge()
        {
            var existing = FindFirstObjectByType<GoogleLoginBridge>();
            if (existing != null)
                return;

            var go = new GameObject("TruesoftGoogleLoginBridge");
            if (dontDestroyOnLoad)
                DontDestroyOnLoad(go);

            go.AddComponent<GoogleLoginBridge>();
        }
    }
}
