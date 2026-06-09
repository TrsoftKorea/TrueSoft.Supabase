#if !UNITY_IAP_V5
using System;
using System.Threading.Tasks;
using TrueBase.Core.Models;
using UnityEngine;
using UnityEngine.Purchasing;

namespace TrueBase.Unity
{
    /// <summary>
    /// Unity IAP v4 + Supabase Edge Function 영수증 서버 검증 통합 파사드.
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
        private readonly Func<string, string, long, string, Task<(bool success, IAPPurchaseResponse value)>> _verifyGoogleAsync;
        private readonly Func<string, string, Task<(bool success, IAPPurchaseResponse value)>> _verifyAppleAsync;

        // ── 생성자 (internal — SupabaseIAP.CreateIAP()로만 생성) ─────────────
        internal IAPFacade(
            Func<string, string, long, string, Task<(bool success, IAPPurchaseResponse value)>> verifyGoogleAsync,
            Func<string, string, Task<(bool success, IAPPurchaseResponse value)>> verifyAppleAsync)
        {
            _verifyGoogleAsync = verifyGoogleAsync ?? throw new ArgumentNullException(nameof(verifyGoogleAsync));
            _verifyAppleAsync  = verifyAppleAsync  ?? throw new ArgumentNullException(nameof(verifyAppleAsync));
        }

        // ── 서버 검증 (플랫폼 자동 감지) ─────────────────────────────────────

        protected override async Task ProcessPurchaseAsync(PurchaseEventArgs args)
        {
            var productId = args.purchasedProduct.definition.id;

#if UNITY_ANDROID
            var token = GooglePlayReceiptParser.ExtractPurchaseToken(args.purchasedProduct.receipt);
            if (string.IsNullOrEmpty(token))
            {
                Debug.LogWarning($"{LogTag} purchaseToken 추출 실패. product={productId}");
                return;
            }
            var priceAmount   = (long)args.purchasedProduct.metadata.localizedPrice;
            var priceCurrency = args.purchasedProduct.metadata.isoCurrencyCode;
            var (success, response) = await _verifyGoogleAsync(token, productId, priceAmount, priceCurrency);
            if (!success || response == null) { Debug.LogWarning($"{LogTag} 서버 검증 실패. product={productId}"); return; }
            if (!response.ok) { Debug.LogWarning($"{LogTag} 구매를 거부했습니다. reason={response.reason}, product={productId}"); return; }
            await GrantAndConfirmAsync(productId, false, response.already_verified, args.purchasedProduct);

#elif UNITY_IOS
            var receipt = AppleIAPFacade.ExtractAppleReceiptPayload(args.purchasedProduct.receipt);
            if (string.IsNullOrEmpty(receipt))
            {
                Debug.LogWarning($"{LogTag} Apple 영수증 Payload를 추출할 수 없습니다. product={productId}");
                return;
            }
            var (ok, resp) = await _verifyAppleAsync(receipt, productId);
            if (!ok || resp == null) { Debug.LogWarning($"{LogTag} 서버 검증 실패. product={productId}"); return; }
            if (!resp.ok) { Debug.LogWarning($"{LogTag} 구매를 거부했습니다. reason={resp.reason}, product={productId}"); return; }
            await GrantAndConfirmAsync(productId, false, resp.already_verified, args.purchasedProduct);

#else
            Debug.LogWarning($"{LogTag} 지원되지 않는 플랫폼입니다.");
            await System.Threading.Tasks.Task.CompletedTask;
#endif
        }
    }
}
#endif
