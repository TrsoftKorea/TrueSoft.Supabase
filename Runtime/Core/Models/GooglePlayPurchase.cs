using System;

namespace TrueBase.Core.Models
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

}
