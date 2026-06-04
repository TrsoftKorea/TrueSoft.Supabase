using System;
using System.Collections;
using System.Threading.Tasks;
using TrueBase.Unity;
using TrueBase.Unity.Auth.Google;
using UnityEngine;

namespace TrueBase.Unity.Config
{
    /// <summary>
    /// Supabase SDK의 "씬 실행 정책"을 제어하는 런타임 컴포넌트입니다.
    /// - 초기화 시점
    /// - RemoteConfig: Cold Start(시작 시 fetch 없음), 키 단위 백그라운드 폴링
    /// 설계: 1키 = 1설정묶음(JSON) = 1폴링주기 (category 없음)
    ///
    /// 로그인은 자동 실행되지 않습니다. 원하는 타이밍에 <see cref="TriggerAutoLoginAsync"/>를 직접 호출하세요.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [AddComponentMenu("TrueSoft/Supabase/SupabaseRuntime")]
    public class SupabaseRuntime : MonoBehaviour
    {

        private static SupabaseRuntime _instance;

        [Header("설정")]
        [Label("설정 에셋")]
        [Tooltip("SupabaseSettings. 비우면 Resources에서 로드.")]
        [SerializeField] private SupabaseSettings settings;

        /// <summary>
        /// 로그인 시도가 완료되면 발행됩니다. bool: 복원 성공 또는 이미 로그인 상태이면 true입니다.
        /// 로그인 성공 시 등록된 모든 <c>StaticUserSave</c> 로드가 완료된 뒤 발행됩니다.
        /// <see cref="TriggerAutoLoginAsync"/> 호출 시 이 이벤트를 발행합니다.
        /// </summary>
        /// <remarks>
        /// 구독 시점에 이미 완료된 경우를 자동으로 처리하는 <see cref="SubscribeAutoLoginCompleted"/>를 사용하면
        /// 보일러플레이트 없이 한 줄로 구독할 수 있습니다.
        /// </remarks>
        public static event Action<bool> OnAutoLoginCompleted;

        /// <summary>로그인 시도가 완료되었는지 여부.</summary>
        public static bool IsAutoLoginCompleted { get; private set; }

        /// <summary>
        /// 로그인 결과. 복원 성공 또는 이미 로그인 상태이면 true입니다.
        /// <see cref="IsAutoLoginCompleted"/>가 true일 때만 유효합니다.
        /// </summary>
        public static bool AutoLoginResult { get; private set; }

        protected virtual void Awake()
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

            // SupabaseSettings의 우선순위별 저장 주기를 전역에 적용
            Supabase.ConfigureUserSavePriorityCooldowns(
                settings.urgentSaveCooldownSeconds,
                settings.normalSaveCooldownSeconds,
                settings.lazySaveCooldownSeconds);

            DontDestroyOnLoad(gameObject);

            EnsureGoogleLoginBridge();
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
            var task = AutoLoginAndMaybeLoadAsync();
            yield return new WaitUntil(() => task.IsCompleted);
        }

        /// <summary>
        /// 저장된 세션으로 로그인을 시도합니다. 완료 시 <see cref="OnAutoLoginCompleted"/> 이벤트를 발행합니다.
        /// 원하는 타이밍(인트로 완료 후, 로그인 화면 등)에 직접 호출하세요.
        /// </summary>
        public static Task TriggerAutoLoginAsync() => AutoLoginAndMaybeLoadAsync();

        private static async Task AutoLoginAndMaybeLoadAsync()
        {
            var ok = await Supabase.TryAutoLoginOnStartAsync();

            if (ok)
                await Supabase.TryLoadAllUserSavesAsync();

            SetAutoLoginCompleted(ok);
        }

        /// <summary>
        /// 로그인 완료 콜백을 등록합니다. 이미 완료된 경우 즉시 호출합니다.
        /// <c>OnEnable()</c>에서 호출하고 <c>OnDisable()</c>에서 <see cref="UnsubscribeAutoLoginCompleted"/>로 해제하세요.
        /// </summary>
        /// <example><code>
        /// void OnEnable() => SupabaseRuntime.SubscribeAutoLoginCompleted(HandleAutoLoginCompleted);
        /// void OnDisable() => SupabaseRuntime.UnsubscribeAutoLoginCompleted(HandleAutoLoginCompleted);
        /// </code></example>
        public static void SubscribeAutoLoginCompleted(Action<bool> callback)
        {
            if (IsAutoLoginCompleted)
            {
                callback?.Invoke(AutoLoginResult);
                return;
            }
            OnAutoLoginCompleted += callback;
        }

        /// <summary>
        /// <see cref="SubscribeAutoLoginCompleted"/>로 등록한 콜백을 해제합니다.
        /// </summary>
        public static void UnsubscribeAutoLoginCompleted(Action<bool> callback)
        {
            OnAutoLoginCompleted -= callback;
        }

        private static void SetAutoLoginCompleted(bool result)
        {
            if (IsAutoLoginCompleted) return;
            IsAutoLoginCompleted = true;
            AutoLoginResult = result || Supabase.IsLoggedIn;
            OnAutoLoginCompleted?.Invoke(AutoLoginResult);
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
