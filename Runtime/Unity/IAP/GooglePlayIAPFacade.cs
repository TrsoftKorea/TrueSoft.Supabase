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

        protected override async Task ProcessPendingOrderAsync(PendingOrder pendingOrder, bool isResuming)
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
            var purchaseToken = ExtractPurchaseToken(receipt);
            if (string.IsNullOrEmpty(purchaseToken))
            {
                Debug.LogWarning($"{LogTag} purchaseToken 추출 실패. product={productId}");
                return;
            }

            var priceAmount   = (long)(cartItems[0].Product.metadata.localizedPrice);
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

            await GrantAndConfirmAsync(productId, isResuming, response.already_verified, pendingOrder);
        }

        // ── Google Play 영수증 파싱 ────────────────────────────────────────────

        /// <summary>
        /// Unity IAP 영수증 JSON에서 Google Play purchaseToken을 추출합니다.
        /// receipt 구조: {"Store":"GooglePlay","Payload":"{\"json\":\"{...purchaseToken...}\"}"}
        /// </summary>
        internal static string ExtractPurchaseToken(string unityReceipt)
        {
            if (string.IsNullOrWhiteSpace(unityReceipt)) return null;
            try
            {
                var wrapper = JsonUtility.FromJson<ReceiptWrapper>(unityReceipt);
                if (string.IsNullOrWhiteSpace(wrapper?.Payload)) return null;

                var payload = JsonUtility.FromJson<ReceiptPayload>(wrapper.Payload);
                if (string.IsNullOrWhiteSpace(payload?.json)) return null;

                var data = JsonUtility.FromJson<PurchaseData>(payload.json);
                return data?.purchaseToken;
            }
            catch { return null; }
        }

        [Serializable] private sealed class ReceiptWrapper  { public string Payload; }
        [Serializable] private sealed class ReceiptPayload  { public string json; public string signature; }
        [Serializable] private sealed class PurchaseData
        {
            public string orderId; public string packageName; public string productId;
            public long   purchaseTime; public int purchaseState; public string purchaseToken;
            public bool   acknowledged;
        }
    }
}
