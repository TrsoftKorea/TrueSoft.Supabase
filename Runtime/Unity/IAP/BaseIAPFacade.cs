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
        private bool _disposed;


        /// <summary>
        /// 아이템 지급 콜백 (필수 설정).
        /// <list type="bullet">
        ///   <item>인자 1: <c>string productId</c> — 구매된 상품 ID</item>
        ///   <item>인자 2: <c>bool alreadyGranted</c> — 서버가 이 주문을 이미 지급 완료로 표시했으면 true. 소모품 중복 지급 판단용</item>
        ///   <item>반환: <c>true</c> → SDK가 ConfirmPurchase 호출 / <c>false</c> → Pending 유지</item>
        /// </list>
        /// </summary>
        public Func<string, bool, Task<bool>> OnGrantItemAsync { get; set; }

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
                return SupabaseResult.Fail(SupabaseErrorCode.IapDisposed);
            }

            if (productIds == null || productIds.Length == 0)
            {
                Debug.LogWarning($"{LogTag} productIds가 비어 있습니다.");
                return SupabaseResult.Fail(SupabaseErrorCode.IapProductIdsEmpty);
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
                    return SupabaseResult.Fail(SupabaseErrorCode.IapServicesInitFailed);
                }
            }

            _isInitialized   = false;

            _storeController = UnityIAPServices.StoreController();

            _storeController.OnProductsFetched      += OnProductsFetchedHandler;
            _storeController.OnProductsFetchFailed  += OnProductsFetchFailedHandler;
            _storeController.OnPurchasesFetched     += OnPurchasesFetchedHandler;
            _storeController.OnPurchasesFetchFailed += OnPurchasesFetchFailedHandler;
            _storeController.OnPurchasePending      += OnPurchasePendingHandler;
            _storeController.OnPurchaseConfirmed    += OnPurchaseConfirmedHandler;
            _storeController.OnPurchaseFailed       += OnPurchaseFailedHandler;

            await _storeController.Connect();
            OnStoreConnected(_storeController);

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
                return SupabaseResult.Fail(SupabaseErrorCode.IapInitTimeout);
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

        /// <summary>
        /// 스토어 카탈로그에서 상품 정보를 조회합니다. 네트워크 호출 없이 <see cref="InitializeAsync"/>가
        /// 이미 받아온 정보를 그대로 읽습니다.
        /// </summary>
        /// <param name="productId">조회할 상품 ID.</param>
        /// <returns>카탈로그에 있으면 정보, 없으면(초기화 전이거나 잘못된 ID) <c>null</c>.</returns>
        public IAPProductInfo GetProductInfo(string productId)
        {
            var product = _storeController?.GetProductById(productId);
            if (product == null) return null;

            return new IAPProductInfo
            {
                ProductId     = product.definition.id,
                Title         = product.metadata.localizedTitle,
                Description   = product.metadata.localizedDescription,
                PriceString   = product.metadata.localizedPriceString,
                Price         = product.metadata.localizedPrice,
                CurrencyCode  = product.metadata.isoCurrencyCode,
                IsAvailable   = product.availableToPurchase,
            };
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
        protected abstract Task ProcessPendingOrderAsync(PendingOrder pendingOrder);

        /// <summary>로그 접두사. 서브클래스에서 재정의하세요.</summary>
        protected virtual string LogTag => "[Supabase.IAP]";

        /// <summary>
        /// 스토어 연결 직후 호출됩니다. Google Play 파사드는 이 시점에 현재 계정을
        /// <c>SetObfuscatedAccountId</c>로 심어, 결제 시점 계정과 검증 시점 계정이 달라지는 것을
        /// 서버가 감지하게 합니다(계정 전환 중 미처리 주문이 다른 계정으로 넘어가 오지급되는 것을 방지).
        /// </summary>
        protected virtual void OnStoreConnected(StoreController controller) { }


        /// <summary>
        /// 아이템 지급 콜백을 호출하고, 성공 시 소모품을 소비합니다.
        /// 콜백이 false를 반환하거나 예외를 던지면 Pending 상태로 남겨 다음 초기화 때 재처리됩니다.
        /// </summary>
        /// <param name="productId">구매된 상품 ID.</param>
        /// <param name="orderId">주문 고유 ID(Google은 orderId, Apple은 transaction_id). 지급 완료 기록에만 내부적으로 사용.</param>
        /// <param name="alreadyGranted">서버가 이 주문을 이미 지급 완료로 표시했으면 true. 중복 지급 방지 판단용.</param>
        /// <param name="pendingOrder">소비(<c>ConfirmPurchase</c>) 대상 주문.</param>
        protected async Task GrantAndConfirmAsync(
            string productId, string orderId, bool alreadyGranted, PendingOrder pendingOrder)
        {
            if (OnGrantItemAsync == null)
            {
                Debug.LogWarning($"{LogTag} OnGrantItemAsync가 설정되지 않았습니다. 구매가 Pending 상태로 남습니다.");
                return;
            }

            bool granted;
            try
            {
                granted = await OnGrantItemAsync(productId, alreadyGranted);
            }
            catch (Exception e)
            {
                Debug.LogError($"{LogTag} OnGrantItemAsync 예외: {e.Message}");
                granted = false;
            }

            if (granted)
            {
                // 소비 처리 전에 서버에 지급 완료를 기록한다 — 실패해도(네트워크 등) 지급 자체는
                // 이미 끝났으므로 소비까지는 그대로 진행한다.
                if (!string.IsNullOrEmpty(orderId))
                    await SupabaseSDK.TryMarkPurchaseGrantedAsync(orderId);
                _storeController?.ConfirmPurchase(pendingOrder);
            }
            else
                Debug.LogWarning($"{LogTag} 아이템 지급 실패 또는 생략. product={productId} — Pending 유지.");
        }

        // Unity IAP v5 이벤트 핸들러 (공통)

        private void OnProductsFetchedHandler(List<Product> products)
            => _storeController.FetchPurchases();

        private void OnProductsFetchFailedHandler(ProductFetchFailed failure)
            => Debug.LogWarning($"{LogTag} 상품 조회 실패: {failure.FailureReason}");

        private void OnPurchasesFetchedHandler(Orders orders)
            => _isInitialized = true;

        private void OnPurchasesFetchFailedHandler(PurchasesFetchFailureDescription failure)
        {
            Debug.LogWarning($"{LogTag} 구매 이력 조회 실패: {failure.FailureReason} — {failure.Message}");
            _isInitialized = true; // 실패해도 초기화 완료 처리 (신규 구매는 가능)
        }

        private void OnPurchasePendingHandler(PendingOrder pendingOrder)
            => _ = ProcessPendingOrderAsync(pendingOrder);

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
