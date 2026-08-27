#if !UNITY_IAP_V5
using System;
using System.Threading.Tasks;
using TrueBase.Core.Models;
using UnityEngine;
using UnityEngine.Purchasing;

namespace TrueBase.Unity
{
    /// <summary>
    /// Unity IAP v4 + Apple App Store SK1 영수증 서버 검증 파사드.
    /// 초기화·영수증 파싱·Supabase 검증·ConfirmPurchase를 SDK 내부에서 처리합니다.
    /// </summary>
    /// <remarks>
    /// 사용 방법:
    /// <code>
    /// var result = await SupabaseIAP.CreateAppleIAPAsync(
    ///     productIds: new[] { "com.mygame.item" },
    ///     onGrant: async (productId, orderId, alreadyGranted) =>
    ///     {
    ///         await MyInventory.GiveItemAsync(productId);
    ///         return true;
    ///     });
    /// if (!result.IsSuccess) return;
    /// var iap = result.Data; // 이후 구매: iap.Purchase(productId)
    /// </code>
    /// 씬 언로드 시 반드시 <see cref="BaseIAPFacade.Dispose"/>를 호출하세요.
    /// </remarks>
    public sealed class AppleIAPFacade : BaseIAPFacade
    {
        private readonly Func<string, string, Task<(bool success, AppleIAPPurchaseResponse value)>> _verifyReceiptAsync;

        protected override string LogTag => "[Supabase.IAP.Apple]";

        // 생성자 (internal — SupabaseIAP.CreateAppleIAP()로만 생성)

        /// <param name="verifyReceiptAsync">
        /// Apple SK1 검증 함수. (base64 영수증 Payload, productId) → (success, response). 필수.
        /// </param>
        internal AppleIAPFacade(
            Func<string, string, Task<(bool success, AppleIAPPurchaseResponse value)>> verifyReceiptAsync)
        {
            _verifyReceiptAsync = verifyReceiptAsync ?? throw new ArgumentNullException(nameof(verifyReceiptAsync));
        }


        protected override async Task ProcessPurchaseAsync(PurchaseEventArgs args)
        {
            var productId = args.purchasedProduct.definition.id;

            // SK1: Unity IAP 영수증 Payload 추출
            var receipt = ExtractAppleReceiptPayload(args.purchasedProduct.receipt);
            if (string.IsNullOrEmpty(receipt))
            {
                Debug.LogWarning($"{LogTag} Apple 영수증 Payload를 추출할 수 없습니다. product={productId}");
                return;
            }

            var (success, response) = await _verifyReceiptAsync(receipt, productId);

            if (!success || response == null)
            {
                Debug.LogWarning($"{LogTag} 서버 검증 실패. product={productId}");
                return;
            }

            if (!response.ok)
            {
                Debug.LogWarning($"{LogTag} Apple이 구매를 거부했습니다. reason={response.reason}, product={productId}");
                return;
            }

            await GrantAndConfirmAsync(productId, response.transaction_id, response.already_granted, args.purchasedProduct);
        }


        /// <summary>
        /// Unity IAP 통합 영수증 JSON(<c>{"Store":...,"Payload":...}</c>)에서 base64 SK1 영수증 Payload를 추출합니다.
        /// </summary>
        /// <param name="unityReceipt">Unity IAP가 제공한 영수증 원문. null/공백이거나 파싱 실패 시 null 반환.</param>
        internal static string ExtractAppleReceiptPayload(string unityReceipt)
        {
            if (string.IsNullOrWhiteSpace(unityReceipt)) return null;
            try
            {
                var wrapper = JsonUtility.FromJson<AppleReceiptWrapper>(unityReceipt);
                return wrapper?.Payload;
            }
            catch { return null; }
        }

        [Serializable]
        private sealed class AppleReceiptWrapper { public string Payload; }
    }
}
#endif
