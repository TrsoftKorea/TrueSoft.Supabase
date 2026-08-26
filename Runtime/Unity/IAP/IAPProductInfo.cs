namespace TrueBase.Unity
{
    /// <summary>
    /// 스토어 카탈로그에서 조회한 상품 정보. Unity IAP v4/v5 공통 타입으로
    /// <see cref="BaseIAPFacade.GetProductInfo"/>가 반환합니다. <see cref="InitializeAsync"/> 이후에만 값이 채워집니다.
    /// </summary>
    public sealed class IAPProductInfo
    {
        /// <summary>상품 ID.</summary>
        public string ProductId;
        /// <summary>스토어에 등록된 상품명(로컬라이즈).</summary>
        public string Title;
        /// <summary>스토어에 등록된 상품 설명(로컬라이즈).</summary>
        public string Description;
        /// <summary>통화 기호가 포함된 가격 문자열. 예: <c>"₩1,200"</c>.</summary>
        public string PriceString;
        /// <summary>가격(통화 소수점 단위, micros 아님).</summary>
        public decimal Price;
        /// <summary>ISO 4217 통화 코드. 예: <c>"KRW"</c>.</summary>
        public string CurrencyCode;
        /// <summary>스토어에서 구매 가능한 상품인지. false면 상품 ID가 잘못됐거나 스토어에서 비활성화된 상태입니다.</summary>
        public bool IsAvailable;
    }
}
