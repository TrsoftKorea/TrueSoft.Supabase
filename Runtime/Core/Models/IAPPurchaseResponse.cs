using System;

namespace TrueBase.Core.Models
{
    [Serializable]
    public sealed class IAPPurchaseResponse
    {
        public bool   ok;
        public bool   already_verified;
        public string order_id;       // Google: orderId / Apple: transaction_id
        public string product_id;     // Apple only (Google omits this field)
        public int    purchase_state; // 0=purchased, -1=검증 실패
        public string reason;
        public string store;          // "google_play" | "apple_app_store"
    }
}
