using System;
using UnityEngine;

namespace TrueBase.Unity
{
    /// <summary>
    /// Unity IAP Google Play 영수증에서 purchaseToken을 추출합니다. v4/v5 공통 유틸리티.
    /// </summary>
    internal static class GooglePlayReceiptParser
    {
        /// <summary>
        /// Unity IAP 영수증 JSON에서 Google Play purchaseToken을 추출합니다.
        /// receipt 구조: {"Store":"GooglePlay","Payload":"{\"json\":\"{...purchaseToken...}\"}"}
        /// </summary>
        /// <param name="unityReceipt">Unity IAP가 제공한 영수증 원문. null/공백이거나 파싱 실패 시 null 반환.</param>
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

        [Serializable] private sealed class ReceiptWrapper { public string Payload; }
        [Serializable] private sealed class ReceiptPayload { public string json; }
        [Serializable] private sealed class PurchaseData   { public string purchaseToken; }
    }
}
