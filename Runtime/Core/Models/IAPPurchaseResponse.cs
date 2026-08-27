using System;

namespace TrueBase.Core.Models
{
    /// <summary>스토어 공통 결제 검증 응답. <c>store</c> 필드로 Google·Apple을 구분합니다.</summary>
    [Serializable]
    public sealed class IAPPurchaseResponse
    {
        public bool   ok;
        public bool   already_verified;
        /// <summary>이 주문을 게임이 이미 지급 완료로 표시한 경우 true. 소모품 중복 지급 판단용.</summary>
        public bool   already_granted;
        public string order_id;       // Google: orderId / Apple: transaction_id
        public string product_id;     // Apple only (Google omits this field)
        public string reason;
        public string store;          // "google_play" | "apple_app_store"
    }
}
