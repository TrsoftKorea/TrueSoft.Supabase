#if UNITY_IAP_V5
using System;
using System.Threading.Tasks;
using TrueBase.Core.Models;
using UnityEngine;
using UnityEngine.Purchasing;

namespace TrueBase.Unity
{
    /// <summary>
    /// Unity IAP v5 + Apple App Store 영수증 서버 검증 파사드.
    /// 초기화·영수증 파싱·Supabase 검증·ConfirmPurchase를 SDK 내부에서 처리합니다.
    /// StoreKit 2 (iOS 15+): JWS 토큰 검증. StoreKit 1 (iOS 14 이하, IAP 5.1+ forceStoreKit1): receipt 검증.
    /// </summary>
    /// <remarks>
    /// 사용 방법:
    /// <code>
    /// var iap = await SupabaseIAP.CreateAppleIAPAsync(
    ///     productIds: new[] { "com.mygame.item" },
    ///     onGrant: async (productId, isResuming, alreadyVerified) =>
    ///     {
    ///         await MyInventory.GiveItemAsync(productId);
    ///         return true;
    ///     });
    /// </code>
    /// 씬 언로드 시 반드시 <see cref="BaseIAPFacade.Dispose"/>를 호출하세요.
    /// </remarks>
    public sealed class AppleIAPFacade : BaseIAPFacade
    {
        private readonly Func<string, string, Task<(bool success, AppleIAPPurchaseResponse value)>> _verifyJwsAsync;
        private readonly Func<string, string, Task<(bool success, AppleIAPPurchaseResponse value)>> _verifyReceiptAsync;

        protected override string LogTag => "[Supabase.IAP.Apple]";

        // ── 생성자 (internal — SupabaseIAP.CreateAppleIAP()로만 생성) ──────────
        internal AppleIAPFacade(
            Func<string, string, Task<(bool success, AppleIAPPurchaseResponse value)>> verifyJwsAsync,
            Func<string, string, Task<(bool success, AppleIAPPurchaseResponse value)>> verifyReceiptAsync = null)
        {
            _verifyJwsAsync     = verifyJwsAsync;
            _verifyReceiptAsync = verifyReceiptAsync;
        }

        // ── 서버 검증 ─────────────────────────────────────────────────────────

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

            // StoreKit 2 (iOS 15+): JWS 경로
            var appleInfo = pendingOrder.Info as IAppleOrderInfo;
            var jws = appleInfo?.jwsRepresentation;

            if (!string.IsNullOrEmpty(jws))
            {
                if (_verifyJwsAsync == null)
                {
                    Debug.LogWarning($"{LogTag} JWS 검증 함수가 설정되지 않았습니다. product={productId}");
                    return;
                }

                var (success, response) = await _verifyJwsAsync(jws, productId);
                if (!success || response == null) { Debug.LogWarning($"{LogTag} 서버 검증 실패. product={productId}"); return; }
                if (!response.ok) { Debug.LogWarning($"{LogTag} Apple이 구매를 거부했습니다. reason={response.reason}, product={productId}"); return; }
                await GrantAndConfirmAsync(productId, isResuming, response.already_verified, pendingOrder);
                return;
            }

            // StoreKit 1 폴백 (iOS 14 이하, IAP 5.1+ forceStoreKit1 활성화 시)
            if (_verifyReceiptAsync == null)
            {
                Debug.LogWarning($"{LogTag} JWS를 가져올 수 없고 SK1 검증 함수도 없습니다. product={productId}");
                return;
            }

            var receipt = ExtractAppleReceiptPayload(pendingOrder.Info?.Receipt);
            if (string.IsNullOrEmpty(receipt))
            {
                Debug.LogWarning($"{LogTag} Apple 영수증 Payload를 추출할 수 없습니다. product={productId}");
                return;
            }

            {
                var (success, response) = await _verifyReceiptAsync(receipt, productId);
                if (!success || response == null) { Debug.LogWarning($"{LogTag} 서버 검증(SK1) 실패. product={productId}"); return; }
                if (!response.ok) { Debug.LogWarning($"{LogTag} Apple이 구매를 거부했습니다(SK1). reason={response.reason}, product={productId}"); return; }
                await GrantAndConfirmAsync(productId, isResuming, response.already_verified, pendingOrder);
            }
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

        [Serializable]
        private sealed class AppleReceiptWrapper { public string Payload; }
    }
}
#endif
