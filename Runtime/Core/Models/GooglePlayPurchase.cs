using System;

namespace TrueBase.Core.Models
{
    /// <summary>Google Play 결제 검증 요청. Google 결제 검증 Edge Function에 전송합니다.</summary>
    [Serializable]
    public sealed class GooglePlayPurchaseRequest
    {
        public string purchase_token;
        public string product_id;
        public string package_name;
        public long   price_amount;   // 클라이언트 ProductDetails.localizedPrice (정수, 0 = 미제공)
        public string price_currency; // ISO 4217 통화 코드 (예: "KRW", "USD"). null = 미제공
    }

    /// <summary>Google Play 결제 검증 응답.</summary>
    [Serializable]
    public sealed class GooglePlayPurchaseResponse
    {
        public bool ok;
        /// <summary>동일 purchase_token으로 이미 검증된 경우 true.</summary>
        public bool already_verified;
        /// <summary>이 주문을 게임이 이미 지급 완료로 표시한 경우 true. 소모품 중복 지급 판단용.</summary>
        public bool already_granted;
        public string order_id;
        public string reason;
    }

}
