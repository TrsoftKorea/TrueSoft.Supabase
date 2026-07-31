using System;

namespace TrueBase.Core.Models
{
    /// <summary>StoreKit 2 영수증 검증 요청. Apple 결제 검증 Edge Function에 전송합니다.</summary>
    [Serializable]
    public sealed class AppleIAPPurchaseRequest
    {
        public string jws_token;  // StoreKit 2: jwsRepresentation (iOS 15+)
        public string product_id; // 기대하는 상품 ID
        public string bundle_id;  // 앱 Bundle ID (null이면 Application.identifier 사용)
    }

    /// <summary>StoreKit 1 영수증 검증 요청. base64 영수증 blob으로 검증합니다.</summary>
    [Serializable]
    public sealed class AppleIAPLegacyPurchaseRequest
    {
        public string receipt;    // SK1: base64 encoded receipt blob (Unity IAP receipt의 Payload 필드)
        public string product_id; // 기대하는 상품 ID
        public string bundle_id;  // 앱 Bundle ID (null이면 Application.identifier 사용)
    }

    /// <summary>Apple 결제 검증 응답.</summary>
    [Serializable]
    public sealed class AppleIAPPurchaseResponse
    {
        public bool ok;
        /// <summary>동일 transaction_id로 이미 검증된 경우 true.</summary>
        public bool already_verified;
        public string transaction_id;
        public string product_id;
        public string reason;
    }
}
