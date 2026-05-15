using System;
using System.Collections;
using System.Threading.Tasks;
using Truesoft.Supabase.Unity;
using Truesoft.Supabase.Unity.Auth.Google;
using UnityEngine;

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
        private const float UserSaveAutoSyncCooldownSeconds = 1f;

        private static SupabaseRuntime _instance;

        [Header("설정")]
        [Label("설정 에셋")]
        [Tooltip("SupabaseSettings. 비우면 Resources에서 로드.")]
        [SerializeField] private SupabaseSettings settings;

        [Header("세션")]
        [Label("세션 자동 복원")]
        [Tooltip("Awake 시 저장된 세션을 자동으로 복원합니다. false이면 TriggerSessionRestoreAsync()를 직접 호출해야 합니다.")]
        [SerializeField] private bool autoRestoreSessionOnAwake = true;

        /// <summary>
        /// 세션 복원 시도가 완료되면 발행됩니다. bool: 복원 성공 또는 이미 로그인 상태이면 true입니다.
        /// 로그인 성공 시 등록된 모든 <c>StaticUserSave</c> 로드가 완료된 뒤 발행됩니다.
        /// autoRestoreSessionOnEnable(자동) 또는 <see cref="TriggerSessionRestoreAsync"/>(수동) 모두 이 이벤트를 발행합니다.
        /// </summary>
        /// <remarks>
        /// 구독 시점에 이미 완료된 경우를 자동으로 처리하는 <see cref="SubscribeSessionRestored"/>를 사용하면
        /// 보일러플레이트 없이 한 줄로 구독할 수 있습니다.
        /// </remarks>
        public static event Action<bool> OnSessionRestored;

        /// <summary>세션 복원 시도가 완료되었는지 여부.</summary>
        public static bool IsSessionRestoreCompleted { get; private set; }

        /// <summary>
        /// 세션 복원 결과. 복원 성공 또는 이미 로그인 상태이면 true입니다.
        /// <see cref="IsSessionRestoreCompleted"/>가 true일 때만 유효합니다.
        /// </summary>
        public static bool SessionRestoreResult { get; private set; }

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

            Supabase.ConfigureUserSaveAutoSyncCooldown(UserSaveAutoSyncCooldownSeconds);

            DontDestroyOnLoad(gameObject);

            EnsureGoogleLoginBridge();

            if (autoRestoreSessionOnAwake)
                StartCoroutine(RunLifecycle());
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        private void Update()
        {
            if (!Supabase.IsInitialized)
                return;

            SupabaseSDK.TickUserSaveAutoSync(Time.realtimeSinceStartup);
            SupabaseSDK.TickRemoteConfigKeyPolls(Time.realtimeSinceStartup);
        }

        private void OnApplicationPause(bool pause)
        {
            if (!pause)
                return;

            Supabase.RequestImmediateUserSaveStaticFlushAll();
        }

        private void OnApplicationQuit()
        {
            Supabase.RequestImmediateUserSaveStaticFlushAll();
        }

        private IEnumerator RunLifecycle()
        {
            var task = RestoreSessionAndMaybeLoadAsync();
            yield return new WaitUntil(() => task.IsCompleted);
        }

        /// <summary>
        /// 세션 복원을 수동으로 시작합니다. 완료 시 <see cref="OnSessionRestored"/> 이벤트를 발행합니다.
        /// autoRestoreSessionOnEnable이 false일 때 원하는 타이밍에 호출합니다.
        /// </summary>
        public static Task TriggerSessionRestoreAsync() => RestoreSessionAndMaybeLoadAsync();

        private static async Task RestoreSessionAndMaybeLoadAsync()
        {
            var ok = await Supabase.TryAutoLoginOnStartAsync();

            if (ok)
                await Supabase.TryLoadAllUserSavesAsync();

            SetSessionRestoreCompleted(ok);
        }

        /// <summary>
        /// 세션 복원 완료 콜백을 등록합니다. 이미 완료된 경우 즉시 호출합니다.
        /// <c>OnEnable()</c>에서 호출하고 <c>OnDisable()</c>에서 <see cref="UnsubscribeSessionRestored"/>로 해제하세요.
        /// </summary>
        /// <example><code>
        /// void OnEnable() => SupabaseRuntime.SubscribeSessionRestored(HandleSessionRestored);
        /// void OnDisable() => SupabaseRuntime.UnsubscribeSessionRestored(HandleSessionRestored);
        /// </code></example>
        public static void SubscribeSessionRestored(Action<bool> callback)
        {
            if (IsSessionRestoreCompleted)
            {
                callback?.Invoke(SessionRestoreResult);
                return;
            }
            OnSessionRestored += callback;
        }

        /// <summary>
        /// <see cref="SubscribeSessionRestored"/>로 등록한 콜백을 해제합니다.
        /// </summary>
        public static void UnsubscribeSessionRestored(Action<bool> callback)
        {
            OnSessionRestored -= callback;
        }

        private static void SetSessionRestoreCompleted(bool result)
        {
            if (IsSessionRestoreCompleted) return;
            IsSessionRestoreCompleted = true;
            SessionRestoreResult = result || Supabase.IsLoggedIn;
            OnSessionRestored?.Invoke(SessionRestoreResult);
        }

        private void EnsureGoogleLoginBridge()
        {
            var existing = FindFirstObjectByType<GoogleLoginBridge>();
            if (existing != null)
                return;

            var go = new GameObject("TruesoftGoogleLoginBridge");
            DontDestroyOnLoad(go);

            go.AddComponent<GoogleLoginBridge>();
        }
    }
}
