#if UNITY_IAP_V5
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TrueBase.Core.Common;
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

        private StoreController _storeController;
        private bool _isInitialized;
        private bool _isFetchingPurchases;
        private int  _resumingPurchaseCount;
        private bool _disposed;


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


        /// <summary>
        /// Unity IAP를 초기화합니다.
        /// 스토어 연결 → 상품 조회 → 미처리 구매 자동 재검증까지 수행합니다.
        /// <see cref="OnGrantItemAsync"/>를 설정한 뒤 호출하세요.
        /// </summary>
        /// <param name="productIds">등록할 소모품(Consumable) 상품 ID 목록.</param>
        /// <param name="timeoutMs">초기화 완료 대기 최대 시간(ms). 기본 10초.</param>
        public async Task<SupabaseResult> InitializeAsync(string[] productIds, int timeoutMs = 10_000)
        {
            if (_disposed)
            {
                Debug.LogWarning($"{LogTag} Disposed 상태에서 InitializeAsync를 호출했습니다.");
                return SupabaseResult.Fail(SupabaseFailReason.IapDisposed);
            }

            if (productIds == null || productIds.Length == 0)
            {
                Debug.LogWarning($"{LogTag} productIds가 비어 있습니다.");
                return SupabaseResult.Fail(SupabaseFailReason.IapProductIdsEmpty);
            }

            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                try
                {
                    await UnityServices.InitializeAsync();
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"{LogTag} Unity Services 초기화 실패: {e.Message}");
                    return SupabaseResult.Fail(SupabaseFailReason.IapServicesInitFailed);
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
            {
                Debug.LogWarning($"{LogTag} 초기화 타임아웃.");
                return SupabaseResult.Fail(SupabaseFailReason.IapInitTimeout);
            }

            return SupabaseResult.Ok;
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


        /// <summary>
        /// 플랫폼별 토큰 추출 → 서버 검증 → 아이템 지급을 수행합니다.
        /// 구현 완료 후 <see cref="GrantAndConfirmAsync"/>를 호출하세요.
        /// </summary>
        /// <param name="pendingOrder">Unity IAP가 전달한 미확정 주문. 영수증·상품 정보가 들어 있습니다.</param>
        /// <param name="isResuming">앱 재시작 후 미처리 주문 재처리 중이면 true, 방금 발생한 신규 구매면 false.</param>
        protected abstract Task ProcessPendingOrderAsync(PendingOrder pendingOrder, bool isResuming);

        /// <summary>로그 접두사. 서브클래스에서 재정의하세요.</summary>
        protected virtual string LogTag => "[Supabase.IAP]";


        /// <summary>
        /// 아이템 지급 콜백을 호출하고, 성공 시 소모품을 소비합니다.
        /// 콜백이 false를 반환하거나 예외를 던지면 Pending 상태로 남겨 다음 초기화 때 재처리됩니다.
        /// </summary>
        /// <param name="productId">구매된 상품 ID.</param>
        /// <param name="isResuming">앱 재시작 후 재처리 중이면 true.</param>
        /// <param name="alreadyVerified">서버 DB에 이미 검증 기록이 있으면 true. 중복 지급 방지 판단용.</param>
        /// <param name="pendingOrder">소비(<c>ConfirmPurchase</c>) 대상 주문.</param>
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

        // Unity IAP v5 이벤트 핸들러 (공통)

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
            string productId;
            try   { productId = order.CartOrdered.Items()[0].Product.definition.id; }
            catch { productId = "unknown"; }
            var info = new IAPPurchaseFailedInfo { ProductId = productId, FailureReason = order.ToString() };
            Debug.LogWarning($"{LogTag} 구매 실패: product={info.ProductId}, reason={info.FailureReason}");
            OnPurchaseFailed?.Invoke(info);
        }
    }
}
#endif
