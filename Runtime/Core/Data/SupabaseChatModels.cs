using System;
using Newtonsoft.Json;

namespace TrueBase.Core.Data
{
    /// <summary>채팅 채널 정의. 운영자가 정하는 값이라 세션 중에는 바뀌지 않습니다.</summary>
    public sealed class ChatChannelInfo
    {
        [JsonProperty("code")]
        public string Code { get; set; }

        /// <summary><c>global</c>(누구나) 또는 <c>server</c>(같은 서버끼리).</summary>
        [JsonProperty("kind")]
        public string Kind { get; set; }

        [JsonProperty("display_name")]
        public string DisplayName { get; set; }

        /// <summary>보낼 수 있는 최대 글자 수. 입력창 제한에 그대로 쓰세요.</summary>
        [JsonProperty("max_length")]
        public int MaxLength { get; set; }

        /// <summary>같은 사람의 연속 채팅 최소 간격(초). 0이면 제한 없음.</summary>
        [JsonProperty("slow_mode_seconds")]
        public int SlowModeSeconds { get; set; }
    }

    /// <summary>채팅 메시지 한 건.</summary>
    public sealed class ChatMessage
    {
        /// <summary>조회 커서. 채널 안에서 시간순으로 증가합니다.</summary>
        [JsonProperty("id")]
        public long Id { get; set; }

        [JsonProperty("account_id")]
        public string AccountId { get; set; }

        [JsonProperty("user_id")]
        public string UserId { get; set; }

        /// <summary>보낸 시점의 닉네임. 이후 개명해도 바뀌지 않습니다.</summary>
        [JsonProperty("display_name")]
        public string DisplayName { get; set; }

        /// <summary>본문. <see cref="Deleted"/>가 true면 null입니다.</summary>
        [JsonProperty("content")]
        public string Content { get; set; }

        /// <summary>운영자가 숨긴 메시지입니다. 본문 대신 삭제 안내를 표시하세요.</summary>
        [JsonProperty("deleted")]
        public bool Deleted { get; set; }

        [JsonProperty("created_at")]
        public DateTimeOffset CreatedAt { get; set; }
    }

    /// <summary>발송 결과. 방금 보낸 메시지의 커서를 돌려줍니다.</summary>
    public sealed class ChatSendResult
    {
        [JsonProperty("id")]
        public long Id { get; set; }

        [JsonProperty("created_at")]
        public DateTimeOffset CreatedAt { get; set; }
    }
}
