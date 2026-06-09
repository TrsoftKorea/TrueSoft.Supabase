#if !UNITY_IAP_V5
using System;
using System.Threading.Tasks;
using TrueBase.Core.Models;
using UnityEngine;
using UnityEngine.Purchasing;

namespace TrueBase.Unity
{
    /// <summary>
    /// Unity IAP v4 + Google Play 영수증 서버 검증 파사드.
    /// 초기화·영수증 파싱·Supabase 검증·ConfirmPurchase를 SDK 내부에서 처리합니다.
    /// </summary>
    /// <remarks>
    /// 사용 방법:
    /// <code>
    /// var iap = await SupabaseIAP.CreateGooglePlayIAPAsync(
    ///     productIds: new[] { "com.mygame.item" },
    ///     onGrant: async (productId, isResuming, alreadyVerified) =>
    ///     {
    ///         await MyInventory.GiveItemAsync(productId);
    ///         return true;
    ///     });
    /// </code>
    /// 씬 언로드 시 반드시 <see cref="BaseIAPFacade.Dispose"/>를 호출하세요.
    /// </remarks>
    public sealed class GooglePlayIAPFacade : BaseIAPFacade
    {
        private readonly Func<string, string, long, string, Task<(bool success, GooglePlayPurchaseResponse value)>> _verifyAsync;

        // ── 생성자 (internal — SupabaseIAP.CreateGooglePlayIAP()로만 생성) ─────
        internal GooglePlayIAPFacade(
            Func<string, string, long, string, Task<(bool success, GooglePlayPurchaseResponse value)>> verifyAsync)
        {
            _verifyAsync = verifyAsync ?? throw new ArgumentNullException(nameof(verifyAsync));
        }

        // ── 서버 검증 ─────────────────────────────────────────────────────────

        protected override async Task ProcessPurchaseAsync(PurchaseEventArgs args)
        {
            var productId     = args.purchasedProduct.definition.id;
            var purchaseToken = GooglePlayReceiptParser.ExtractPurchaseToken(args.purchasedProduct.receipt);

            if (string.IsNullOrEmpty(purchaseToken))
            {
                Debug.LogWarning($"{LogTag} purchaseToken 추출 실패. product={productId}");
                return;
            }

            var priceAmount   = (long)args.purchasedProduct.metadata.localizedPrice;
            var priceCurrency = args.purchasedProduct.metadata.isoCurrencyCode;

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

            await GrantAndConfirmAsync(productId, false, response.already_verified, args.purchasedProduct);
        }
    }
}
#endif
