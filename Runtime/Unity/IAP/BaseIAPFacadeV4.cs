#if !UNITY_IAP_V5
using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Purchasing;

namespace TrueBase.Unity
{
    /// <summary>
    /// IAP 파사드 공통 기반 클래스 (Unity IAP v4).
    /// IStoreListener 기반 초기화·이벤트·소비를 담당합니다.
    /// 플랫폼별 토큰 추출 및 서버 검증은 <see cref="ProcessPurchaseAsync"/>에서 구현합니다.
    /// </summary>
    /// <remarks>
    /// 직접 사용하지 말고 <see cref="SupabaseIAP"/>를 통해 생성하세요.
    /// </remarks>
    public abstract class BaseIAPFacade : IStoreListener, IDisposable
    {
        // ── Unity IAP v4 상태 ─────────────────────────────────────────────────

        protected IStoreController Controller;
        private bool _isInitialized;
        private bool _disposed;
        private TaskCompletionSource<bool> _initTcs;

        // ── 공개 API ──────────────────────────────────────────────────────────

        /// <summary>
        /// 아이템 지급 콜백 (필수 설정).
        /// <list type="bullet">
        ///   <item>인자 1: <c>string productId</c> — 구매된 상품 ID</item>
        ///   <item>인자 2: <c>bool isResuming</c> — 앱 재시작 후 미처리 주문 재처리 중이면 true</item>
        ///   <item>인자 3: <c>bool alreadyVerified</c> — 서버 DB에 이미 검증 기록이 있으면 true (크래시 후 재처리 감지)</item>
        ///   <item>반환: <c>true</c> → SDK가 ConfirmPurchase 호출 / <c>false</c> → Pending 유지</item>
        /// </list>
        /// </summary>
        public Func<string, bool, bool, Task<bool>> OnGrantItemAsync { get; set; }

        /// <summary>구매 실패 알림 (선택). UI 표시 등에 사용.</summary>
        public event Action<IAPPurchaseFailedInfo> OnPurchaseFailed;

        /// <summary>SDK IAP 초기화 완료 여부.</summary>
        public bool IsInitialized => _isInitialized;

        // ── 공개 메서드 ────────────────────────────────────────────────────────

        /// <summary>
        /// Unity IAP를 초기화합니다.
        /// 스토어 연결 → 상품 조회 → 미처리 구매 자동 재검증까지 수행합니다.
        /// <see cref="OnGrantItemAsync"/>를 설정한 뒤 호출하세요.
        /// </summary>
        /// <param name="productIds">등록할 소모품(Consumable) 상품 ID 목록.</param>
        /// <param name="timeoutMs">초기화 완료 대기 최대 시간(ms). 기본 10초.</param>
        public async Task<bool> InitializeAsync(string[] productIds, int timeoutMs = 10_000)
        {
            if (_disposed)
            {
                Debug.LogWarning($"{LogTag} Disposed 상태에서 InitializeAsync를 호출했습니다.");
                return false;
            }

            if (productIds == null || productIds.Length == 0)
            {
                Debug.LogWarning($"{LogTag} productIds가 비어 있습니다.");
                return false;
            }

            _isInitialized = false;
            _initTcs       = new TaskCompletionSource<bool>();

            var builder = ConfigurationBuilder.Instance(StandardPurchasingModule.Instance());
            foreach (var id in productIds)
                if (!string.IsNullOrWhiteSpace(id))
                    builder.AddProduct(id, ProductType.Consumable);

            UnityPurchasing.Initialize(this, builder);

            var timeoutTask   = Task.Delay(timeoutMs);
            var completedTask = await Task.WhenAny(_initTcs.Task, timeoutTask);
            if (completedTask == timeoutTask)
            {
                Debug.LogWarning($"{LogTag} 초기화 타임아웃.");
                return false;
            }

            return await _initTcs.Task;
        }

        /// <summary>
        /// 결제창을 표시합니다.
        /// 결제 완료 후 <see cref="OnGrantItemAsync"/>가 자동 호출됩니다.
        /// </summary>
        public bool Purchase(string productId)
        {
            if (_disposed)
            {
                Debug.LogWarning($"{LogTag} Disposed 상태에서 Purchase를 호출했습니다.");
                return false;
            }

            if (!_isInitialized || Controller == null)
            {
                Debug.LogWarning($"{LogTag} IAP가 초기화되지 않았습니다. InitializeAsync를 먼저 호출하세요.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(productId))
            {
                Debug.LogWarning($"{LogTag} productId가 비어 있습니다.");
                return false;
            }

            Controller.InitiatePurchase(productId);
            return true;
        }

        /// <summary>이벤트 핸들러를 해제합니다. 씬 언로드 시 반드시 호출하세요.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed        = true;
            Controller       = null;
            OnGrantItemAsync = null;
            OnPurchaseFailed = null;
        }

        // ── IStoreListener 구현 ──────────────────────────────────────────────

        public void OnInitialized(IStoreController controller, IExtensionProvider extensions)
        {
            Controller     = controller;
            _isInitialized = true;
            _initTcs?.TrySetResult(true);
        }

        public void OnInitializeFailed(InitializationFailureReason error)
        {
            Debug.LogWarning($"{LogTag} 초기화 실패: {error}");
            _initTcs?.TrySetResult(false);
        }

        public void OnInitializeFailed(InitializationFailureReason error, string message)
        {
            Debug.LogWarning($"{LogTag} 초기화 실패: {error} — {message}");
            _initTcs?.TrySetResult(false);
        }

        public PurchaseProcessingResult ProcessPurchase(PurchaseEventArgs args)
        {
            _ = ProcessPurchaseInternalAsync(args);
            return PurchaseProcessingResult.Pending;
        }

        private async Task ProcessPurchaseInternalAsync(PurchaseEventArgs args)
        {
            try { await ProcessPurchaseAsync(args); }
            catch (Exception e) { Debug.LogError($"{LogTag} ProcessPurchaseAsync 예외: {e.Message}"); }
        }

        // IStoreListener.OnPurchaseFailed — explicit implementation to avoid naming conflict with event
        void IStoreListener.OnPurchaseFailed(Product product, PurchaseFailureReason failureReason)
        {
            var info = new IAPPurchaseFailedInfo
            {
                ProductId     = product?.definition?.id ?? "unknown",
                FailureReason = failureReason.ToString(),
            };
            Debug.LogWarning($"{LogTag} 구매 실패: product={info.ProductId}, reason={info.FailureReason}");
            OnPurchaseFailed?.Invoke(info);
        }

        // ── 서브클래스 구현 ────────────────────────────────────────────────────

        /// <summary>
        /// 플랫폼별 토큰 추출 → 서버 검증 → 아이템 지급을 수행합니다.
        /// 구현 완료 후 <see cref="GrantAndConfirmAsync"/>를 호출하세요.
        /// </summary>
        protected abstract Task ProcessPurchaseAsync(PurchaseEventArgs args);

        /// <summary>로그 접두사. 서브클래스에서 재정의하세요.</summary>
        protected virtual string LogTag => "[Supabase.IAP]";

        // ── 공통 지급 + 소비 ──────────────────────────────────────────────────

        /// <summary>
        /// 아이템 지급 콜백을 호출하고, 성공 시 소모품을 소비합니다.
        /// </summary>
        protected async Task GrantAndConfirmAsync(
            string productId, bool isResuming, bool alreadyVerified, Product product)
        {
            if (OnGrantItemAsync == null)
            {
                Debug.LogWarning($"{LogTag} OnGrantItemAsync가 설정되지 않았습니다. 구매가 Pending 상태로 남습니다.");
                return;
            }

            bool granted;
            try
            {
                granted = await OnGrantItemAsync(productId, isResuming, alreadyVerified);
            }
            catch (Exception e)
            {
                Debug.LogError($"{LogTag} OnGrantItemAsync 예외: {e.Message}");
                granted = false;
            }

            if (granted)
                Controller?.ConfirmPendingPurchase(product);
            else
                Debug.LogWarning($"{LogTag} 아이템 지급 실패 또는 생략. product={productId} — Pending 유지.");
        }
    }
}
#endif
