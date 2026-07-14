using System;
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
    /// 로그인은 자동 실행되지 않습니다. 원하는 타이밍에 <see cref="Supabase.TriggerAutoLoginAsync"/>를 직접 호출하세요.
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


        protected virtual void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Debug.LogWarning("[Supabase] Duplicate SupabaseRuntime detected. Destroying duplicate object.");
                Destroy(gameObject);
                return;
            }

            _instance = this;

            // 자동 로그인 후처리 훅 등록(서브클래스가 OnAfterAutoLoginAsync를 override) → Supabase.TriggerAutoLoginAsync가 호출.
            SupabaseSDK.AfterAutoLoginHook = OnAfterAutoLoginAsync;

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
            {
                _instance = null;
                if (SupabaseSDK.AfterAutoLoginHook == (Func<bool, Task<bool>>)OnAfterAutoLoginAsync)
                    SupabaseSDK.AfterAutoLoginHook = null;
            }
        }

        protected virtual void Update()
        {
            if (!Supabase.IsInitialized)
                return;

            SupabaseSDK.TickUserSaveAutoSync(Time.realtimeSinceStartup);
            SupabaseSDK.TickRemoteConfigKeyPolls(Time.realtimeSinceStartup);
            SupabaseSDK.TickSessionRefresh(Time.realtimeSinceStartup);
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

        /// <summary>
        /// Supabase 자동 로그인 성공 후 <see cref="Supabase.TriggerAutoLoginAsync"/> 내부에서 호출됩니다(UserSave 로드는 이 이후 개발자가 직접 수행).
        /// 서브클래스에서 추가 세션 복원 로직을 구현하세요. false 반환 시 자동 로그인 실패로 처리됩니다.
        /// </summary>
        protected virtual Task<bool> OnAfterAutoLoginAsync(bool success) => Task.FromResult(success);



        /// <summary>
        /// 씬에 <see cref="GoogleLoginBridge"/>가 없으면 <c>DontDestroyOnLoad</c> GameObject로 생성합니다.
        /// Android 네이티브 플러그인이 <c>UnitySendMessage</c>로 콜백을 보낼 대상이 항상 존재하도록 보장합니다.
        /// </summary>
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
