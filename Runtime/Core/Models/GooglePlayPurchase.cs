using System;

namespace Truesoft.Supabase.Core.Models
{
    [Serializable]
    public sealed class GooglePlayPurchaseRequest
    {
        public string purchase_token;
        public string product_id;
        public string package_name;
    }

    [Serializable]
    public sealed class GooglePlayPurchaseResponse
    {
        public bool ok;
        /// <summary>동일 purchase_token으로 이미 검증된 경우 true.</summary>
        public bool already_verified;
        public string order_id;
        /// <summary>0=purchased, 1=cancelled, 2=pending. 검증 실패 시 -1.</summary>
        public int purchase_state;
        public string reason;
    }

    // ── Unity IAP v5 영수증 3단계 파싱 모델 (GooglePlayIAPFacade 내부용) ──────────

    /// <summary>Unity IAP가 발급하는 영수증 최상위 래퍼.</summary>
    [Serializable]
    internal sealed class GooglePlayReceiptWrapper
    {
        public string Payload;      // JSON string → GooglePlayPayload
    }

    /// <summary>Google Play Payload 구조.</summary>
    [Serializable]
    internal sealed class GooglePlayPayload
    {
        public string json;         // JSON string → GooglePlayPurchaseData
        public string signature;
    }

    /// <summary>Google Play 구매 데이터 (Payload.json 파싱).</summary>
    [Serializable]
    internal sealed class GooglePlayPurchaseData
    {
        public string orderId;
        public string packageName;
        public string productId;
        public long   purchaseTime;
        public int    purchaseState;    // 0=purchased, 1=cancelled, 2=pending
        public string purchaseToken;
        public bool   acknowledged;
    }
}
