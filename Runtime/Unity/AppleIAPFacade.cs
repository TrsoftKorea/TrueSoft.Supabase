#if TRUESOFT_IAP_AVAILABLE
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Truesoft.Supabase.Core.Models;
using Unity.Services.Core;
using UnityEngine;
using UnityEngine.Purchasing;

namespace Truesoft.Supabase.Unity
{
    /// <summary>
    /// Unity IAP v5 + Apple App Store 영수증 서버 검증 파사드.
    /// 초기화·영수증 파싱·Supabase 검증·ConfirmPurchase를 SDK 내부에서 처리합니다.
    /// </summary>
    /// <remarks>
    /// 사용 방법:
    /// <code>
    /// var iap = await Supabase.CreateAppleIAPAsync(
    ///     productIds: new[] { "com.mygame.item" },
    ///     onGrant: async (productId, isResuming) =>
    ///     {
    ///         await MyInventory.GiveItemAsync(productId);
    ///         return true; // true → SDK가 ConfirmPurchase 호출 (소모품 소비)
    ///                      // false → Pending 유지 → 다음 InitializeAsync에서 재처리
    ///     });
    /// </code>
    ///
    /// 씬 언로드 시 반드시 <see cref="Dispose"/>를 호출하세요.
    /// </remarks>
    public sealed class AppleIAPFacade : IDisposable
    {
        // ── 의존성 ────────────────────────────────────────────────────────────
        private readonly Func<string, string, Task<(bool success, AppleIAPPurchaseResponse value)>> _verifyAsync;

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
        ///   <item>반환: <c>true</c> → SDK가 ConfirmPurchase 호출 / <c>false</c> → Pending 유지</item>
        /// </list>
        /// </summary>
        public Func<string, bool, Task<bool>> OnGrantItemAsync { get; set; }

        /// <summary>구매 실패 알림 (선택). UI 표시 등에 사용.</summary>
        public event Action<FailedOrder> OnPurchaseFailed;

        /// <summary>SDK IAP 초기화 완료 여부.</summary>
        public bool IsInitialized => _isInitialized;

        // ── 생성자 (internal — Supabase.CreateAppleIAP()로만 생성) ──────────
        internal AppleIAPFacade(
            Func<string, string, Task<(bool success, AppleIAPPurchaseResponse value)>> verifyAsync)
        {
            _verifyAsync = verifyAsync ?? throw new ArgumentNullException(nameof(verifyAsync));
        }

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
                Debug.LogWarning("[Supabase.IAP.Apple] Disposed 상태에서 InitializeAsync를 호출했습니다.");
                return false;
            }

            if (productIds == null || productIds.Length == 0)
            {
                Debug.LogWarning("[Supabase.IAP.Apple] productIds가 비어 있습니다.");
                return false;
            }

            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                try
                {
                    await UnityServices.InitializeAsync();
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[Supabase.IAP.Apple] Unity Services 초기화 실패: " + e.Message);
                    return false;
                }
            }

            _isInitialized       = false;
            _isFetchingPurchases = false;
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
                Debug.LogWarning("[Supabase.IAP.Apple] 초기화 타임아웃.");

            return _isInitialized;
        }

        /// <summary>
        /// Apple App Store 결제창을 표시합니다.
        /// 결제 완료 후 <see cref="OnGrantItemAsync"/>가 자동 호출됩니다.
        /// </summary>
        public bool Purchase(string productId)
        {
            if (_disposed)
            {
                Debug.LogWarning("[Supabase.IAP.Apple] Disposed 상태에서 Purchase를 호출했습니다.");
                return false;
            }

            if (!_isInitialized || _storeController == null)
            {
                Debug.LogWarning("[Supabase.IAP.Apple] IAP가 초기화되지 않았습니다. InitializeAsync를 먼저 호출하세요.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(productId))
            {
                Debug.LogWarning("[Supabase.IAP.Apple] productId가 비어 있습니다.");
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

        // ── Unity IAP v5 이벤트 핸들러 ───────────────────────────────────────

        private void OnProductsFetchedHandler(List<Product> products)
        {
            _isFetchingPurchases = true;
            _storeController.FetchPurchases();
        }

        private void OnProductsFetchFailedHandler(ProductFetchFailed failure)
        {
            Debug.LogWarning("[Supabase.IAP.Apple] 상품 조회 실패: " + failure.FailureReason);
        }

        private void OnPurchasesFetchedHandler(Orders orders)
        {
            _resumingPurchaseCount = orders.PendingOrders?.Count ?? 0;
            _isFetchingPurchases = false;
            _isInitialized       = true;
        }

        private void OnPurchasesFetchFailedHandler(PurchasesFetchFailureDescription failure)
        {
            Debug.LogWarning($"[Supabase.IAP.Apple] 구매 이력 조회 실패: {failure.FailureReason} — {failure.Message}");
            _isFetchingPurchases = false;
            _isInitialized       = true;
        }

        private void OnPurchasePendingHandler(PendingOrder pendingOrder)
        {
            var isResuming = _isFetchingPurchases || _resumingPurchaseCount > 0;
            if (_resumingPurchaseCount > 0) _resumingPurchaseCount--;

            _ = ProcessPendingOrderAsync(pendingOrder, isResuming);
        }

        private void OnPurchaseConfirmedHandler(Order _) { }

        private void OnPurchaseFailedHandler(FailedOrder order)
        {
            Debug.LogWarning("[Supabase.IAP.Apple] 구매 실패: " + order);
            OnPurchaseFailed?.Invoke(order);
        }

        // ── 내부 검증 로직 ────────────────────────────────────────────────────

        private async Task ProcessPendingOrderAsync(PendingOrder pendingOrder, bool isResuming)
        {
            if (pendingOrder == null)
            {
                Debug.LogWarning("[Supabase.IAP.Apple] PendingOrder가 null입니다.");
                return;
            }

            var receipt   = pendingOrder.Info?.Receipt;
            var cartItems = pendingOrder.CartOrdered?.Items();

            if (string.IsNullOrEmpty(receipt))
            {
                Debug.LogWarning("[Supabase.IAP.Apple] Receipt가 비어 있습니다.");
                return;
            }

            if (cartItems == null || cartItems.Count == 0)
            {
                Debug.LogWarning("[Supabase.IAP.Apple] CartOrdered.Items()가 비어 있습니다.");
                return;
            }

            var productId = cartItems[0].Product.definition.id;

            var receiptData = ExtractReceiptPayload(receipt);
            if (string.IsNullOrEmpty(receiptData))
            {
                Debug.LogWarning($"[Supabase.IAP.Apple] receipt payload 추출 실패. product={productId}");
                return;
            }

            var (success, response) = await _verifyAsync(receiptData, productId);

            if (!success || response == null)
            {
                Debug.LogWarning($"[Supabase.IAP.Apple] 서버 검증 실패. product={productId}");
                return;
            }

            if (!response.ok)
            {
                Debug.LogWarning($"[Supabase.IAP.Apple] Apple이 구매를 거부했습니다. reason={response.reason}, product={productId}");
                return;
            }

            if (OnGrantItemAsync == null)
            {
                Debug.LogWarning("[Supabase.IAP.Apple] OnGrantItemAsync가 설정되지 않았습니다. 구매가 Pending 상태로 남습니다.");
                return;
            }

            bool granted;
            try
            {
                granted = await OnGrantItemAsync(productId, isResuming);
            }
            catch (Exception e)
            {
                Debug.LogError("[Supabase.IAP.Apple] OnGrantItemAsync 예외: " + e.Message);
                granted = false;
            }

            if (granted)
                _storeController?.ConfirmPurchase(pendingOrder);
            else
                Debug.LogWarning($"[Supabase.IAP.Apple] 아이템 지급 실패 또는 생략. product={productId} — Pending 유지.");
        }

        /// <summary>
        /// Unity IAP 영수증 JSON에서 Apple App Store receipt payload(base64)를 추출합니다.
        /// receipt 구조: {"Store":"AppleAppStore","Payload":"&lt;base64&gt;"}
        /// </summary>
        internal static string ExtractReceiptPayload(string unityReceipt)
        {
            if (string.IsNullOrWhiteSpace(unityReceipt))
                return null;
            try
            {
                var wrapper = JsonUtility.FromJson<ReceiptWrapper>(unityReceipt);
                return string.IsNullOrWhiteSpace(wrapper?.Payload) ? null : wrapper.Payload;
            }
            catch
            {
                return null;
            }
        }

        [Serializable]
        private sealed class ReceiptWrapper
        {
            public string Payload;
        }
    }
}
#endif // TRUESOFT_IAP_AVAILABLE
