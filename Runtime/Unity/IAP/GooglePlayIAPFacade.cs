#if UNITY_IAP_V5
using System;
using System.Threading.Tasks;
using TrueBase.Core.Models;
using UnityEngine;
using UnityEngine.Purchasing;

namespace TrueBase.Unity
{
    /// <summary>
    /// Unity IAP v5 + Google Play 영수증 서버 검증 파사드.
    /// 초기화·영수증 파싱·Supabase 검증·ConfirmPurchase를 SDK 내부에서 처리합니다.
    /// </summary>
    /// <remarks>
    /// 사용 방법:
    /// <code>
    /// var result = await SupabaseIAP.CreateGooglePlayIAPAsync(
    ///     productIds: new[] { "com.mygame.item" },
    ///     onGrant: async (productId, alreadyGranted) =>
    ///     {
    ///         await MyInventory.GiveItemAsync(productId);
    ///         return true;
    ///     });
    /// if (!result.IsSuccess) return;
    /// var iap = result.Data; // 이후 구매: iap.Purchase(productId)
    /// </code>
    /// 씬 언로드 시 반드시 <see cref="BaseIAPFacade.Dispose"/>를 호출하세요.
    /// </remarks>
    public sealed class GooglePlayIAPFacade : BaseIAPFacade
    {
        private readonly Func<string, string, long, string, Task<(bool success, GooglePlayPurchaseResponse value)>> _verifyAsync;

        protected override string LogTag => "[Supabase.IAP.Google]";

        // 생성자 (internal — SupabaseIAP.CreateGooglePlayIAP()로만 생성)

        /// <param name="verifyAsync">
        /// Google Play 검증 함수. (purchaseToken, productId, priceAmount, priceCurrency) → (success, response).
        /// priceAmount는 micros(주 단위 ×1,000,000) 정수, priceCurrency는 ISO 4217 코드. 필수.
        /// </param>
        internal GooglePlayIAPFacade(
            Func<string, string, long, string, Task<(bool success, GooglePlayPurchaseResponse value)>> verifyAsync)
        {
            _verifyAsync = verifyAsync ?? throw new ArgumentNullException(nameof(verifyAsync));
        }


        protected override void OnStoreConnected(StoreController controller)
        {
            var accountId = SupabaseSDK.CurrentAccountId;
            if (string.IsNullOrEmpty(accountId)) return;
            controller.GooglePlayStoreExtendedService?.SetObfuscatedAccountId(accountId);
        }

        protected override async Task ProcessPendingOrderAsync(PendingOrder pendingOrder)
        {
            if (pendingOrder == null)
            {
                Debug.LogWarning($"{LogTag} PendingOrder가 null입니다.");
                return;
            }

            var receipt   = pendingOrder.Info?.Receipt;
            var cartItems = pendingOrder.CartOrdered?.Items();

            if (string.IsNullOrEmpty(receipt))
            {
                Debug.LogWarning($"{LogTag} Receipt가 비어 있습니다.");
                return;
            }

            if (cartItems == null || cartItems.Count == 0)
            {
                Debug.LogWarning($"{LogTag} CartOrdered.Items()가 비어 있습니다.");
                return;
            }

            var productId     = cartItems[0].Product.definition.id;
            var purchaseToken = GooglePlayReceiptParser.ExtractPurchaseToken(receipt);
            if (string.IsNullOrEmpty(purchaseToken))
            {
                Debug.LogWarning($"{LogTag} purchaseToken 추출 실패. product={productId}");
                return;
            }

            // micros(주 단위 ×1,000,000, 정수)로 전송 — 소수점 통화($0.99 등)가 0으로 잘리지 않도록.
            var priceAmount   = (long)decimal.Round(cartItems[0].Product.metadata.localizedPrice * 1000000m);
            var priceCurrency = cartItems[0].Product.metadata.isoCurrencyCode;

            var (success, response) = await _verifyAsync(purchaseToken, productId, priceAmount, priceCurrency);

            if (!success || response == null)
            {
                Debug.LogWarning($"{LogTag} 서버 검증 실패. product={productId}");
                return;
            }

            if (!response.ok)
            {
                Debug.LogWarning($"{LogTag} Google이 구매를 거부했습니다. reason={response.reason}, product={productId}");
                return;
            }

            await GrantAndConfirmAsync(productId, response.order_id, response.already_granted, pendingOrder);
        }
    }
}
#endif
