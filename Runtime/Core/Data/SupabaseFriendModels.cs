using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace TrueBase.Core.Data
{
    /// <summary>닉네임 검색·친구 목록 조회에서 상대와의 관계.</summary>
    public enum FriendRelation
    {
        /// <summary>친구도 아니고 대기 중 요청도 없습니다.</summary>
        None,

        /// <summary>이미 친구입니다.</summary>
        Friends,

        /// <summary>내가 보낸 요청이 대기 중입니다.</summary>
        RequestSent,

        /// <summary>상대가 보낸 요청이 대기 중입니다(내가 수락·거절 가능).</summary>
        RequestReceived,
    }

    /// <summary><c>ts_friend_search</c> 결과.</summary>
    public sealed class FriendSearchResult
    {
        [JsonProperty("account_id")]
        public string AccountId { get; set; }

        [JsonProperty("display_name")]
        public string Name { get; set; }

        [JsonProperty("status")]
        public string RelationRaw { get; set; }

        public FriendRelation Relation => RelationRaw switch
        {
            "friends" => FriendRelation.Friends,
            "request_sent" => FriendRelation.RequestSent,
            "request_received" => FriendRelation.RequestReceived,
            _ => FriendRelation.None,
        };
    }

    /// <summary><c>ts_friend_request_send</c> 결과. 상대가 이미 나에게 보낸 요청이 있었으면 즉시 <see cref="FriendRequestSendOutcome.Accepted"/>가 true입니다.</summary>
    public sealed class FriendRequestSendOutcome
    {
        [JsonProperty("request_id")]
        public string RequestId { get; set; }

        [JsonProperty("status")]
        public string StatusRaw { get; set; }

        /// <summary>true면 상호 요청이 즉시 친구로 성립된 것입니다. false면 상대 응답 대기(pending).</summary>
        public bool Accepted => StatusRaw == "accepted";
    }

    /// <summary>대기 중인 친구 요청 하나(받은 것 또는 보낸 것).</summary>
    public sealed class FriendRequestSummary
    {
        [JsonProperty("request_id")]
        public string RequestId { get; set; }

        /// <summary>상대 계정 ID — 받은 요청이면 보낸 사람, 보낸 요청이면 받는 사람.</summary>
        [JsonProperty("account_id")]
        public string AccountId { get; set; }

        [JsonProperty("display_name")]
        public string Name { get; set; }

        [JsonProperty("created_at")]
        public DateTimeOffset CreatedAt { get; set; }
    }

    /// <summary>친구 한 명.</summary>
    public sealed class FriendSummary
    {
        [JsonProperty("account_id")]
        public string AccountId { get; set; }

        [JsonProperty("display_name")]
        public string Name { get; set; }

        /// <summary>친구가 된 시각.</summary>
        [JsonProperty("since")]
        public DateTimeOffset Since { get; set; }
    }

    /// <summary><see cref="SupabaseFriendService.ListRequestsAsync"/>의 조회 방향.</summary>
    public enum FriendRequestDirection
    {
        /// <summary>내가 받은 요청.</summary>
        Incoming,

        /// <summary>내가 보낸 요청.</summary>
        Outgoing,
    }
}
