#if UNITY_IAP_V5
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
        private readonly Func<string, string, Task<(bool success, IAPPurchaseResponse value)>> _verifyReceiptAsync;

        // ── 생성자 (internal — SupabaseIAP.CreateIAP()로만 생성) ─────────────
        internal IAPFacade(
            Func<string, string, long, string, Task<(bool success, IAPPurchaseResponse value)>> verifyAsync,
            Func<string, string, Task<(bool success, IAPPurchaseResponse value)>> verifyReceiptAsync = null)
        {
            _verifyAsync        = verifyAsync ?? throw new ArgumentNullException(nameof(verifyAsync));
            _verifyReceiptAsync = verifyReceiptAsync;
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
                Debug.LogWarning($"{LogTag} Receipt가 비어 있습니다. product={productId}");
                return;
            }
            token = GooglePlayReceiptParser.ExtractPurchaseToken(receipt);
            if (string.IsNullOrEmpty(token))
            {
                Debug.LogWarning($"{LogTag} purchaseToken 추출 실패. product={productId}");
                return;
            }
            priceAmount   = (long)(cartItems[0].Product.metadata.localizedPrice);
            priceCurrency = cartItems[0].Product.metadata.isoCurrencyCode;

            var (success, response) = await _verifyAsync(token, productId, priceAmount, priceCurrency);
            if (!success || response == null) { Debug.LogWarning($"{LogTag} 서버 검증 실패. product={productId}"); return; }
            if (!response.ok) { Debug.LogWarning($"{LogTag} 구매를 거부했습니다. reason={response.reason}, product={productId}"); return; }
            await GrantAndConfirmAsync(productId, isResuming, response.already_verified, pendingOrder);

#elif UNITY_IOS
            var appleInfo = pendingOrder.Info as IAppleOrderInfo;
            var jws = appleInfo?.jwsRepresentation;

            if (!string.IsNullOrEmpty(jws))
            {
                // StoreKit 2 경로 (iOS 15+)
                priceAmount   = 0;
                priceCurrency = null;
                var (success, response) = await _verifyAsync(jws, productId, priceAmount, priceCurrency);
                if (!success || response == null) { Debug.LogWarning($"{LogTag} 서버 검증 실패. product={productId}"); return; }
                if (!response.ok) { Debug.LogWarning($"{LogTag} 구매를 거부했습니다. reason={response.reason}, product={productId}"); return; }
                await GrantAndConfirmAsync(productId, isResuming, response.already_verified, pendingOrder);
            }
            else
            {
                // StoreKit 1 폴백 (iOS 14 이하, IAP 5.1+ forceStoreKit1 활성화 시)
                if (_verifyReceiptAsync == null)
                {
                    Debug.LogWarning($"{LogTag} JWS를 가져올 수 없고 SK1 검증 함수도 없습니다. product={productId}");
                    return;
                }
                var receiptPayload = ExtractAppleReceiptPayload(pendingOrder.Info?.Receipt);
                if (string.IsNullOrEmpty(receiptPayload))
                {
                    Debug.LogWarning($"{LogTag} Apple 영수증 Payload를 추출할 수 없습니다. product={productId}");
                    return;
                }
                var (success, response) = await _verifyReceiptAsync(receiptPayload, productId);
                if (!success || response == null) { Debug.LogWarning($"{LogTag} 서버 검증(SK1) 실패. product={productId}"); return; }
                if (!response.ok) { Debug.LogWarning($"{LogTag} 구매를 거부했습니다(SK1). reason={response.reason}, product={productId}"); return; }
                await GrantAndConfirmAsync(productId, isResuming, response.already_verified, pendingOrder);
            }
#else
            Debug.LogWarning($"{LogTag} 지원되지 않는 플랫폼입니다.");
#endif
        }

        // ── 헬퍼 ──────────────────────────────────────────────────────────────

        private static string ExtractAppleReceiptPayload(string unityReceipt)
        {
            if (string.IsNullOrWhiteSpace(unityReceipt)) return null;
            try
            {
                var wrapper = UnityEngine.JsonUtility.FromJson<AppleReceiptWrapper>(unityReceipt);
                return wrapper?.Payload;
            }
            catch { return null; }
        }

        [System.Serializable]
        private sealed class AppleReceiptWrapper { public string Payload; }
    }
}
#endif
