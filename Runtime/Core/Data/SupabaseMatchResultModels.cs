using Newtonsoft.Json;

namespace TrueBase.Core.Data
{
    /// <summary><see cref="MatchResultReportOutcome.Status"/>가 나타내는 처리 상태.</summary>
    public enum MatchResultStatus
    {
        /// <summary>상대의 신고를 아직 받지 못했습니다. 오류가 아니라 대기 상태입니다.</summary>
        Pending,

        /// <summary>이번 호출로 교차검증을 통과해 지급 처리를 마쳤습니다.</summary>
        Paid,

        /// <summary>이전에 이미 지급 처리가 끝난 세션을 재신고했습니다(멱등).</summary>
        AlreadyPaid,
    }

    /// <summary><c>ts_match_report_result</c> RPC 결과.</summary>
    public sealed class MatchResultReportOutcome
    {
        [JsonProperty("status")]
        public string StatusRaw { get; set; }

        /// <summary>이번 신고자 본인에게 보상 우편이 지급되었는지. <see cref="Status"/>가 <see cref="MatchResultStatus.Pending"/>이면 항상 false.</summary>
        [JsonProperty("rewarded")]
        public bool Rewarded { get; set; }

        public MatchResultStatus Status => StatusRaw switch
        {
            "paid" => MatchResultStatus.Paid,
            "already_paid" => MatchResultStatus.AlreadyPaid,
            _ => MatchResultStatus.Pending,
        };
    }

    /// <summary>
    /// <c>ts_match_win_count</c> 결과. 무효 판정(mismatch)된 판은 어디에도 세지 않습니다.
    /// <see cref="Verified"/>만 상대 신고와 맞춰 본 값이고, <see cref="Pending"/>·<see cref="Bot"/>은 본인 신고뿐이라 조작할 수 있습니다.
    /// </summary>
    public sealed class MatchWinCount
    {
        /// <summary>상대 신고와 맞춰 본 승리.</summary>
        [JsonProperty("verified")]
        public int Verified { get; set; }

        /// <summary>상대가 아직 신고하지 않은 승리. 상대가 다르게 신고하면 빠집니다.</summary>
        [JsonProperty("pending")]
        public int Pending { get; set; }

        /// <summary>봇에게 이긴 판.</summary>
        [JsonProperty("bot")]
        public int Bot { get; set; }

        /// <summary>세 값의 합.</summary>
        public int Total => Verified + Pending + Bot;
    }
}
