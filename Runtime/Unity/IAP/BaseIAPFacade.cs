using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unity.Services.Core;
using UnityEngine;
using UnityEngine.Purchasing;

namespace TrueBase.Unity
{
    /// <summary>
    /// IAP 파사드 공통 기반 클래스.
    /// Unity IAP v5 인프라(초기화·이벤트·소비)를 담당합니다.
    /// 플랫폼별 토큰 추출 및 서버 검증은 <see cref="ProcessPendingOrderAsync"/>에서 구현합니다.
    /// </summary>
    /// <remarks>
    /// 직접 사용하지 말고 <see cref="SupabaseIAP"/>를 통해 생성하세요.
    /// </remarks>
    public abstract class BaseIAPFacade : IDisposable
    {
        // ── Unity IAP v5 상태 ─────────────────────────────────────────────────

        private StoreController _storeController;
        private bool _isInitialized;
        private bool _isFetchingPurchases;
        private int  _resumingPurchaseCount;
        private bool _disposed;

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
        public event Action<FailedOrder> OnPurchaseFailed;

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

#if UNITY_IOS
            if (System.Version.TryParse(UnityEngine.iOS.Device.systemVersion, out var iosVer)
                && iosVer < new System.Version(15, 0))
            {
                Debug.LogError($"{LogTag} iOS {UnityEngine.iOS.Device.systemVersion} 은 지원되지 않습니다. 최소 iOS 15가 필요합니다.");
                return false;
            }
#endif

            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                try
                {
                    await UnityServices.InitializeAsync();
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"{LogTag} Unity Services 초기화 실패: {e.Message}");
                    return false;
                }
            }

            _isInitialized         = false;
            _isFetchingPurchases   = false;
            _resumingPurchaseCount = 0;

            _storeController = UnityIAPServices.StoreController();

            _storeController.OnProductsFetched      += OnProductsFetchedHandler;
            _storeController.OnProductsFetchFailed  += OnProductsFetchFailedHandler;
            _storeController.OnPurchasesFetched     += OnPurchasesFetchedHandler;
            _storeController.OnPurchasesFetchFailed += OnPurchasesFetchFailedHandler;
            _storeController.OnPurchasePending      += OnPurchasePendingHandler;
            _storeController.OnPurchaseConfirmed    += OnPurchaseConfirmedHandler;
            _storeController.OnPurchaseFailed       += OnPurchaseFailedHandler;

            await _storeController.Connect();

            var defs = productIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => new ProductDefinition(id, ProductType.Consumable))
                .ToList();

            _storeController.FetchProducts(defs);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (!_isInitialized && sw.ElapsedMilliseconds < timeoutMs)
                await Task.Delay(50);

            if (!_isInitialized)
                Debug.LogWarning($"{LogTag} 초기화 타임아웃.");

            return _isInitialized;
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

            if (!_isInitialized || _storeController == null)
            {
                Debug.LogWarning($"{LogTag} IAP가 초기화되지 않았습니다. InitializeAsync를 먼저 호출하세요.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(productId))
            {
                Debug.LogWarning($"{LogTag} productId가 비어 있습니다.");
                return false;
            }

            _storeController.PurchaseProduct(productId);
            return true;
        }

        /// <summary>이벤트 핸들러를 해제합니다. 씬 언로드 시 반드시 호출하세요.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_storeController != null)
            {
                _storeController.OnProductsFetched      -= OnProductsFetchedHandler;
                _storeController.OnProductsFetchFailed  -= OnProductsFetchFailedHandler;
                _storeController.OnPurchasesFetched     -= OnPurchasesFetchedHandler;
                _storeController.OnPurchasesFetchFailed -= OnPurchasesFetchFailedHandler;
                _storeController.OnPurchasePending      -= OnPurchasePendingHandler;
                _storeController.OnPurchaseConfirmed    -= OnPurchaseConfirmedHandler;
                _storeController.OnPurchaseFailed       -= OnPurchaseFailedHandler;
                _storeController = null;
            }

            OnGrantItemAsync = null;
            OnPurchaseFailed = null;
        }

        // ── 서브클래스 구현 ────────────────────────────────────────────────────

        /// <summary>
        /// 플랫폼별 토큰 추출 → 서버 검증 → 아이템 지급을 수행합니다.
        /// 구현 완료 후 <see cref="GrantAndConfirmAsync"/>를 호출하세요.
        /// </summary>
        protected abstract Task ProcessPendingOrderAsync(PendingOrder pendingOrder, bool isResuming);

        /// <summary>로그 접두사. 서브클래스에서 재정의하세요.</summary>
        protected virtual string LogTag => "[Supabase.IAP]";

        // ── 공통 지급 + 소비 ──────────────────────────────────────────────────

        /// <summary>
        /// 아이템 지급 콜백을 호출하고, 성공 시 소모품을 소비합니다.
        /// </summary>
        protected async Task GrantAndConfirmAsync(
            string productId, bool isResuming, bool alreadyVerified, PendingOrder pendingOrder)
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
                _storeController?.ConfirmPurchase(pendingOrder);
            else
                Debug.LogWarning($"{LogTag} 아이템 지급 실패 또는 생략. product={productId} — Pending 유지.");
        }

        // ── Unity IAP v5 이벤트 핸들러 (공통) ───────────────────────────────────

        private void OnProductsFetchedHandler(List<Product> products)
        {
            // FetchPurchases 호출 전에 플래그 세팅
            // — OnPurchasePending이 OnPurchasesFetched보다 먼저 올 경우 isResuming을 올바르게 판별하기 위함
            _isFetchingPurchases = true;
            _storeController.FetchPurchases();
        }

        private void OnProductsFetchFailedHandler(ProductFetchFailed failure)
            => Debug.LogWarning($"{LogTag} 상품 조회 실패: {failure.FailureReason}");

        private void OnPurchasesFetchedHandler(Orders orders)
        {
            _resumingPurchaseCount = orders.PendingOrders?.Count ?? 0;
            _isFetchingPurchases   = false;
            _isInitialized         = true;
        }

        private void OnPurchasesFetchFailedHandler(PurchasesFetchFailureDescription failure)
        {
            Debug.LogWarning($"{LogTag} 구매 이력 조회 실패: {failure.FailureReason} — {failure.Message}");
            _isFetchingPurchases = false;
            _isInitialized       = true; // 실패해도 초기화 완료 처리 (신규 구매는 가능)
        }

        private void OnPurchasePendingHandler(PendingOrder pendingOrder)
        {
            // isResuming 이중 판별 (Unity IAP v5 타이밍 이슈 대응):
            // - _isFetchingPurchases == true : OnPurchasePending이 OnPurchasesFetched보다 먼저 온 경우
            // - _resumingPurchaseCount > 0   : OnPurchasePending이 OnPurchasesFetched 이후에 온 경우
            var isResuming = _isFetchingPurchases || _resumingPurchaseCount > 0;
            if (_resumingPurchaseCount > 0) _resumingPurchaseCount--;

            _ = ProcessPendingOrderAsync(pendingOrder, isResuming);
        }

        private void OnPurchaseConfirmedHandler(Order _) { }

        private void OnPurchaseFailedHandler(FailedOrder order)
        {
            Debug.LogWarning($"{LogTag} 구매 실패: {order}");
            OnPurchaseFailed?.Invoke(order);
        }
    }
}
