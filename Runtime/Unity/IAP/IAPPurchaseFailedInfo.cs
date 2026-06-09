namespace TrueBase.Unity
{
    /// <summary>
    /// IAP 구매 실패 정보. Unity IAP v4/v5 공통 타입으로 <see cref="BaseIAPFacade.OnPurchaseFailed"/>에 사용됩니다.
    /// </summary>
    public sealed class IAPPurchaseFailedInfo
    {
        /// <summary>실패한 상품 ID.</summary>
        public string ProductId;
        /// <summary>실패 사유 문자열.</summary>
        public string FailureReason;
    }
}
