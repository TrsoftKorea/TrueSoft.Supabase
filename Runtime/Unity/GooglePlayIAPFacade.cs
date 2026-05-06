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
    /// Unity IAP v5 + Google Play 영수증 서버 검증 파사드.
    /// 초기화·영수증 파싱·Supabase 검증·ConfirmPurchase를 SDK 내부에서 처리합니다.
    /// </summary>
    /// <remarks>
    /// 사용 방법:
    /// <code>
    /// var iap = Supabase.CreateGooglePlayIAP();
    /// iap.OnGrantItemAsync = async (order, resp, isResuming) =>
    /// {
    ///     // 아이템 지급 로직
    ///     return true; // true → SDK가 ConfirmPurchase 호출 (소모품 소비)
    ///                  // false → Pending 유지 → 다음 InitializeAsync에서 재처리
    /// };
    /// await iap.InitializeAsync(new[] { "com.mygame.item" });
    /// </code>
    ///
    /// 씬 언로드 시 반드시 <see cref="Dispose"/>를 호출하세요.
    /// </remarks>
    public sealed class GooglePlayIAPFacade : IDisposable
    {
        // ── 의존성 ────────────────────────────────────────────────────────────
        private readonly Func<string, string, Task<(bool success, GooglePlayPurchaseResponse value)>> _verifyAsync;

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
        ///   <item>인자 1: <see cref="PendingOrder"/> — Unity IAP 주문 (영수증·상품 ID 접근)</item>
        ///   <item>인자 2: <see cref="GooglePlayPurchaseResponse"/> — Supabase 서버 검증 응답</item>
        ///   <item>인자 3: <c>bool isResuming</c> — 앱 재시작 후 미처리 주문 재처리 중이면 true</item>
        ///   <item>반환: <c>true</c> → SDK가 ConfirmPurchase 호출 / <c>false</c> → Pending 유지</item>
        /// </list>
        /// </summary>
        public Func<PendingOrder, GooglePlayPurchaseResponse, bool, Task<bool>> OnGrantItemAsync { get; set; }

        /// <summary>구매 실패 알림 (선택). UI 표시 등에 사용.</summary>
        public event Action<FailedOrder> OnPurchaseFailed;

        /// <summary>SDK IAP 초기화 완료 여부.</summary>
        public bool IsInitialized => _isInitialized;

        // ── 생성자 (internal — Supabase.CreateGooglePlayIAP()로만 생성) ─────────
        internal GooglePlayIAPFacade(
            Func<string, string, Task<(bool success, GooglePlayPurchaseResponse value)>> verifyAsync)
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
                Debug.LogWarning("[Supabase.IAP] Disposed 상태에서 InitializeAsync를 호출했습니다.");
                return false;
            }

            if (productIds == null || productIds.Length == 0)
            {
                Debug.LogWarning("[Supabase.IAP] productIds가 비어 있습니다.");
                return false;
            }

            // Unity Services 초기화 (이미 된 경우 무시)
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                try
                {
                    await UnityServices.InitializeAsync();
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[Supabase.IAP] Unity Services 초기화 실패: " + e.Message);
                    return false;
                }
            }

            _isInitialized       = false;
            _isFetchingPurchases = false;
            _resumingPurchaseCount = 0;

            _storeController = UnityIAPServices.StoreController();

            // 이벤트 핸들러 등록
            _storeController.OnProductsFetched      += OnProductsFetchedHandler;
            _storeController.OnProductsFetchFailed  += OnProductsFetchFailedHandler;
            _storeController.OnPurchasesFetched     += OnPurchasesFetchedHandler;
            _storeController.OnPurchasesFetchFailed += OnPurchasesFetchFailedHandler;
            _storeController.OnPurchasePending      += OnPurchasePendingHandler;
            _storeController.OnPurchaseConfirmed    += OnPurchaseConfirmedHandler;
            _storeController.OnPurchaseFailed       += OnPurchaseFailedHandler;

            // 1단계: 스토어 연결
            await _storeController.Connect();

            // 2단계: 상품 조회 → OnProductsFetchedHandler에서 FetchPurchases 호출
            var defs = productIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => new ProductDefinition(id, ProductType.Consumable))
                .ToList();

            _storeController.FetchProducts(defs);

            // 3단계 완료(OnPurchasesFetched / OnPurchasesFetchFailed)까지 대기
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (!_isInitialized && sw.ElapsedMilliseconds < timeoutMs)
                await Task.Delay(50);

            if (!_isInitialized)
                Debug.LogWarning("[Supabase.IAP] 초기화 타임아웃.");

            return _isInitialized;
        }

        /// <summary>
        /// Google Play 결제창을 표시합니다.
        /// 결제 완료 후 <see cref="OnGrantItemAsync"/>가 자동 호출됩니다.
        /// </summary>
        public bool Purchase(string productId)
        {
            if (_disposed)
            {
                Debug.LogWarning("[Supabase.IAP] Disposed 상태에서 Purchase를 호출했습니다.");
                return false;
            }

            if (!_isInitialized || _storeController == null)
            {
                Debug.LogWarning("[Supabase.IAP] IAP가 초기화되지 않았습니다. InitializeAsync를 먼저 호출하세요.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(productId))
            {
                Debug.LogWarning("[Supabase.IAP] productId가 비어 있습니다.");
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
            // 3단계: 미처리 구매 조회
            // FetchPurchases 호출 전에 플래그 세팅
            // — OnPurchasePending이 OnPurchasesFetched보다 먼저 올 경우 isResuming을 올바르게 판별하기 위함
            _isFetchingPurchases = true;
            _storeController.FetchPurchases();
        }

        private void OnProductsFetchFailedHandler(ProductFetchFailed failure)
        {
            Debug.LogWarning("[Supabase.IAP] 상품 조회 실패: " + failure.FailureReason);
        }

        private void OnPurchasesFetchedHandler(Orders orders)
        {
            _resumingPurchaseCount = orders.PendingOrders?.Count ?? 0;
            // OnPurchasePending이 OnPurchasesFetched 이후에 오는 경우를 위해 플래그 해제
            _isFetchingPurchases = false;
            _isInitialized       = true;
        }

        private void OnPurchasesFetchFailedHandler(PurchasesFetchFailureDescription failure)
        {
            Debug.LogWarning($"[Supabase.IAP] 구매 이력 조회 실패: {failure.FailureReason} — {failure.Message}");
            _isFetchingPurchases = false;
            _isInitialized       = true;    // 실패해도 초기화 완료 처리 (신규 구매는 가능)
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
            Debug.LogWarning("[Supabase.IAP] 구매 실패: " + order);
            OnPurchaseFailed?.Invoke(order);
        }

        // ── 내부 검증 로직 ────────────────────────────────────────────────────

        private async Task ProcessPendingOrderAsync(PendingOrder pendingOrder, bool isResuming)
        {
            if (pendingOrder == null)
            {
                Debug.LogWarning("[Supabase.IAP] PendingOrder가 null입니다.");
                return;
            }

            // 1. receipt + productId 추출 (Unity IAP v5)
            var receipt   = pendingOrder.Info?.Receipt;
            var cartItems = pendingOrder.CartOrdered?.Items();

            if (string.IsNullOrEmpty(receipt))
            {
                Debug.LogWarning("[Supabase.IAP] Receipt가 비어 있습니다.");
                return;
            }

            if (cartItems == null || cartItems.Count == 0)
            {
                Debug.LogWarning("[Supabase.IAP] CartOrdered.Items()가 비어 있습니다.");
                return;
            }

            var productId = cartItems[0].Product.definition.id;

            // 2. purchaseToken 3단계 파싱
            var purchaseToken = ExtractPurchaseToken(receipt);
            if (string.IsNullOrEmpty(purchaseToken))
            {
                Debug.LogWarning($"[Supabase.IAP] purchaseToken 추출 실패. product={productId}");
                return;
            }

            // 3. Supabase Edge Function 서버 검증
            var (success, response) = await _verifyAsync(purchaseToken, productId);

            if (!success || response == null)
            {
                Debug.LogWarning($"[Supabase.IAP] 서버 검증 실패. product={productId}");
                return;
            }

            if (!response.ok)
            {
                Debug.LogWarning($"[Supabase.IAP] Google이 구매를 거부했습니다. reason={response.reason}, product={productId}");
                return;
            }

            // 4. 게임 코드 아이템 지급 위임
            if (OnGrantItemAsync == null)
            {
                Debug.LogWarning("[Supabase.IAP] OnGrantItemAsync가 설정되지 않았습니다. 구매가 Pending 상태로 남습니다.");
                return;
            }

            bool granted;
            try
            {
                granted = await OnGrantItemAsync(pendingOrder, response, isResuming);
            }
            catch (Exception e)
            {
                Debug.LogError("[Supabase.IAP] OnGrantItemAsync 예외: " + e.Message);
                granted = false;
            }

            // 5. 지급 성공 시만 ConfirmPurchase (소모품 소비)
            // 실패 시 Pending 유지 → 다음 InitializeAsync에서 재처리
            if (granted)
                _storeController?.ConfirmPurchase(pendingOrder);
            else
                Debug.LogWarning($"[Supabase.IAP] 아이템 지급 실패 또는 생략. product={productId} — Pending 유지.");
        }

        /// <summary>
        /// Unity IAP 영수증 JSON에서 Google Play purchaseToken을 추출합니다.
        /// receipt 구조: {"Store":"GooglePlay","Payload":"{\"json\":\"{...purchaseToken...}\"}"}
        /// </summary>
        internal static string ExtractPurchaseToken(string unityReceipt)
        {
            if (string.IsNullOrWhiteSpace(unityReceipt))
                return null;
            try
            {
                var wrapper = JsonUtility.FromJson<ReceiptWrapper>(unityReceipt);
                if (string.IsNullOrWhiteSpace(wrapper?.Payload)) return null;

                var payload = JsonUtility.FromJson<ReceiptPayload>(wrapper.Payload);
                if (string.IsNullOrWhiteSpace(payload?.json)) return null;

                var data = JsonUtility.FromJson<PurchaseData>(payload.json);
                return data?.purchaseToken;
            }
            catch
            {
                return null;
            }
        }

        // ── Google Play 영수증 3단계 파싱용 private nested class ─────────────────
        // 이 클래스들은 ExtractPurchaseToken 내부에서만 사용됩니다.

        [Serializable]
        private sealed class ReceiptWrapper
        {
            public string Payload;
        }

        [Serializable]
        private sealed class ReceiptPayload
        {
            public string json;
            public string signature;
        }

        [Serializable]
        private sealed class PurchaseData
        {
            public string orderId;
            public string packageName;
            public string productId;
            public long   purchaseTime;
            public int    purchaseState;
            public string purchaseToken;
            public bool   acknowledged;
        }
    }
}
#endif // TRUESOFT_IAP_AVAILABLE
