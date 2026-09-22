using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace TrueBase.Core.Data
{
    /// <summary>매치 로비 상태.</summary>
    public enum MatchLobbyState
    {
        /// <summary>초대·수락을 받는 중.</summary>
        Open,

        /// <summary>호스트가 시작을 알렸습니다. 실제 접속은 게임이 처리합니다.</summary>
        Started,

        /// <summary>호스트가 취소했거나 호스트가 나갔습니다.</summary>
        Cancelled,

        /// <summary>아무도 응답하지 않아 시간 초과로 닫혔습니다.</summary>
        Expired,
    }

    /// <summary>로비 멤버 상태.</summary>
    public enum MatchLobbyMemberState
    {
        /// <summary>초대받아 응답 대기 중.</summary>
        Invited,

        /// <summary>참가 수락함.</summary>
        Accepted,

        /// <summary>초대를 거절함.</summary>
        Declined,

        /// <summary>참가 후 나감.</summary>
        Left,
    }

    /// <summary><c>ts_match_lobby_create</c> 결과.</summary>
    public sealed class MatchLobbyCreateOutcome
    {
        [JsonProperty("lobby_id")]
        public string LobbyId { get; set; }

        /// <summary><see cref="LobbyId"/>와 같은 값. 기존 매치 결과 신고(<c>ReportResultAsync</c>)의 sessionId로 그대로 쓸 수 있습니다.</summary>
        [JsonProperty("session_id")]
        public string SessionId { get; set; }
    }

    /// <summary>로비 멤버 한 명.</summary>
    public sealed class MatchLobbyMember
    {
        [JsonProperty("account_id")]
        public string AccountId { get; set; }

        [JsonProperty("display_name")]
        public string Name { get; set; }

        [JsonProperty("status")]
        public string StatusRaw { get; set; }

        /// <summary>서버가 의미를 두지 않는 자유 칸입니다 — 팀·진영·캐릭터·준비 상태 등 게임이 정해서 씁니다. 지정한 적이 없으면 빈 사전.</summary>
        [JsonProperty("metadata")]
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();

        public MatchLobbyMemberState Status => StatusRaw switch
        {
            "accepted" => MatchLobbyMemberState.Accepted,
            "declined" => MatchLobbyMemberState.Declined,
            "left" => MatchLobbyMemberState.Left,
            _ => MatchLobbyMemberState.Invited,
        };
    }

    /// <summary>로비 하나(<c>ts_match_lobby_list_my</c> 등에서 반환).</summary>
    public sealed class MatchLobbySummary
    {
        [JsonProperty("lobby_id")]
        public string LobbyId { get; set; }

        [JsonProperty("session_id")]
        public string SessionId { get; set; }

        [JsonProperty("game_code")]
        public string GameCode { get; set; }

        /// <summary>방 이름. 만들 때 넘기지 않았으면 null.</summary>
        [JsonProperty("name")]
        public string Name { get; set; }

        /// <summary>서버가 의미를 두지 않는 자유 칸입니다 — 맵·규칙·모드 등 게임이 정해서 씁니다. 지정한 적이 없으면 빈 사전.</summary>
        [JsonProperty("metadata")]
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();

        [JsonProperty("max_members")]
        public int MaxMembers { get; set; }

        [JsonProperty("host_account_id")]
        public string HostAccountId { get; set; }

        [JsonProperty("status")]
        public string StatusRaw { get; set; }

        [JsonProperty("created_at")]
        public DateTimeOffset CreatedAt { get; set; }

        /// <summary>아무도 수락하지 않으면 이 시각에 자동으로 닫힙니다. 대기 시간은 운영 콘솔에서 정합니다.</summary>
        [JsonProperty("expires_at")]
        public DateTimeOffset ExpiresAt { get; set; }

        [JsonProperty("started_at")]
        public DateTimeOffset? StartedAt { get; set; }

        [JsonProperty("members")]
        public List<MatchLobbyMember> Members { get; set; } = new List<MatchLobbyMember>();

        public MatchLobbyState Status => StatusRaw switch
        {
            "started" => MatchLobbyState.Started,
            "cancelled" => MatchLobbyState.Cancelled,
            "expired" => MatchLobbyState.Expired,
            _ => MatchLobbyState.Open,
        };
    }
}
