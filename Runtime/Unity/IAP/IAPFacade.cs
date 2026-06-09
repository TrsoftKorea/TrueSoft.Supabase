using System;
using System.Threading.Tasks;
using TrueBase.Core.Models;
using UnityEngine;
using UnityEngine.Purchasing;

namespace TrueBase.Unity
{
    /// <summary>
    /// Unity IAP v5 + Supabase Edge Function 영수증 서버 검증 통합 파사드.
    /// Android(Google Play)와 iOS(Apple App Store)를 플랫폼 자동 감지로 처리합니다.
    /// 게임 코드에서 #if UNITY_ANDROID / #elif UNITY_IOS 분기를 제거할 수 있습니다.
    /// </summary>
    /// <remarks>
    /// 사용 방법:
    /// <code>
    /// var iap = await SupabaseIAP.CreateIAPAsync(
    ///     productIds: new[] { "com.mygame.item" },
    ///     onGrant: async (productId, isResuming, alreadyVerified) =>
    ///     {
    ///         await MyInventory.GiveItemAsync(productId);
    ///         return true; // true → SDK가 ConfirmPurchase 호출 (소모품 소비)
    ///                      // false → Pending 유지 → 다음 InitializeAsync에서 재처리
    ///     });
    /// </code>
    /// 씬 언로드 시 반드시 <see cref="BaseIAPFacade.Dispose"/>를 호출하세요.
    /// </remarks>
    public sealed class IAPFacade : BaseIAPFacade
    {
        private readonly Func<string, string, long, string, Task<(bool success, IAPPurchaseResponse value)>> _verifyAsync;

        // ── 생성자 (internal — SupabaseIAP.CreateIAP()로만 생성) ─────────────
        internal IAPFacade(
            Func<string, string, long, string, Task<(bool success, IAPPurchaseResponse value)>> verifyAsync)
        {
            _verifyAsync = verifyAsync ?? throw new ArgumentNullException(nameof(verifyAsync));
        }

        // ── 서버 검증 (플랫폼 자동 감지) ─────────────────────────────────────

        protected override async Task ProcessPendingOrderAsync(PendingOrder pendingOrder, bool isResuming)
        {
            if (pendingOrder == null)
            {
                Debug.LogWarning($"{LogTag} PendingOrder가 null입니다.");
                return;
            }

            var cartItems = pendingOrder.CartOrdered?.Items();
            if (cartItems == null || cartItems.Count == 0)
            {
                Debug.LogWarning($"{LogTag} CartOrdered.Items()가 비어 있습니다.");
                return;
            }

            var productId = cartItems[0].Product.definition.id;

            string token;
            long   priceAmount;
            string priceCurrency;

#if UNITY_ANDROID
            var receipt = pendingOrder.Info?.Receipt;
            if (string.IsNullOrEmpty(receipt))
            {
                Debug.LogWarning($"{LogTag} Receipt가 비어 있습니다.");
                return;
            }
            token = GooglePlayIAPFacade.ExtractPurchaseToken(receipt);
            if (string.IsNullOrEmpty(token))
            {
                Debug.LogWarning($"{LogTag} purchaseToken 추출 실패. product={productId}");
                return;
            }
            priceAmount   = (long)(cartItems[0].Product.metadata.localizedPrice);
            priceCurrency = cartItems[0].Product.metadata.isoCurrencyCode;
#elif UNITY_IOS
            var appleInfo = pendingOrder.Info as IAppleOrderInfo;
            token = appleInfo?.jwsRepresentation;
            if (string.IsNullOrEmpty(token))
            {
                Debug.LogWarning($"{LogTag} JWS를 가져올 수 없습니다. iOS 15+ 기기에서만 지원됩니다. product={productId}");
                return;
            }
            priceAmount   = 0;
            priceCurrency = null;
#else
            Debug.LogWarning($"{LogTag} 지원되지 않는 플랫폼입니다.");
            return;
#endif

            var (success, response) = await _verifyAsync(token, productId, priceAmount, priceCurrency);

            if (!success || response == null)
            {
                Debug.LogWarning($"{LogTag} 서버 검증 실패. product={productId}");
                return;
            }

            if (!response.ok)
            {
                Debug.LogWarning($"{LogTag} 구매를 거부했습니다. reason={response.reason}, product={productId}");
                return;
            }

            await GrantAndConfirmAsync(productId, isResuming, response.already_verified, pendingOrder);
        }
    }
}
