using System;

namespace TrueBase.Core.Models
{
    [Serializable]
    public sealed class AppleIAPPurchaseRequest
    {
        public string receipt_data;   // StoreKit 1: Unity IAP receipt의 Payload (base64 앱 영수증)
        public string jws_token;      // StoreKit 2: jwsRepresentation (iOS 15+)
        public string product_id;     // 기대하는 상품 ID
        public string bundle_id;      // 앱 Bundle ID (null이면 Edge Function에서 env var 사용)
    }

    [Serializable]
    public sealed class AppleIAPPurchaseResponse
    {
        public bool ok;
        /// <summary>동일 transaction_id로 이미 검증된 경우 true.</summary>
        public bool already_verified;
        public string transaction_id;
        public string product_id;
        /// <summary>소모품은 항상 0 (purchased). 검증 실패 시 -1.</summary>
        public int purchase_state;
        public string reason;
    }
}
