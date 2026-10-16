using System;
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

    /// <summary>매치 기록 한 판이 어디까지 확인됐는지.</summary>
    public enum MatchRecordStatus
    {
        /// <summary>상대 신고와 맞춰 봤습니다.</summary>
        Verified,

        /// <summary>상대가 아직 신고하지 않았습니다. 상대가 다르게 신고하면 <see cref="Mismatch"/>가 됩니다.</summary>
        Pending,

        /// <summary>상대 신고와 맞지 않아 무효가 됐습니다.</summary>
        Mismatch,

        /// <summary>상대 없는 판(봇·혼자)이라 맞춰 볼 수 없습니다.</summary>
        Solo,
    }

    /// <summary>
    /// 확인 단계 하나의 요약. 점수는 어느 단계든 본인이 보낸 값이며, 점수를 보낸 판이 없으면 null입니다.
    /// </summary>
    public sealed class MatchStatsBucket
    {
        [JsonProperty("wins")]
        public int Wins { get; set; }

        [JsonProperty("losses")]
        public int Losses { get; set; }

        [JsonProperty("max_score")]
        public double? MaxScore { get; set; }

        [JsonProperty("min_score")]
        public double? MinScore { get; set; }
    }

    /// <summary>
    /// <c>ts_match_stats</c> 결과. 무효(mismatch) 판은 어디에도 세지 않습니다.
    /// <see cref="Verified"/>만 상대 신고와 맞춰 본 판이고, <see cref="Pending"/>·<see cref="Solo"/>는 본인 신고뿐이라 조작할 수 있습니다.
    /// </summary>
    public sealed class MatchStats
    {
        [JsonProperty("verified")]
        public MatchStatsBucket Verified { get; set; } = new MatchStatsBucket();

        [JsonProperty("pending")]
        public MatchStatsBucket Pending { get; set; } = new MatchStatsBucket();

        [JsonProperty("solo")]
        public MatchStatsBucket Solo { get; set; } = new MatchStatsBucket();

        /// <summary>세 단계를 합친 값.</summary>
        public MatchStatsBucket Total => new MatchStatsBucket
        {
            Wins = Verified.Wins + Pending.Wins + Solo.Wins,
            Losses = Verified.Losses + Pending.Losses + Solo.Losses,
            MaxScore = MaxOf(Verified.MaxScore, Pending.MaxScore, Solo.MaxScore),
            MinScore = MinOf(Verified.MinScore, Pending.MinScore, Solo.MinScore),
        };

        private static double? MaxOf(params double?[] values)
        {
            double? best = null;
            foreach (var v in values)
                if (v.HasValue && (!best.HasValue || v.Value > best.Value)) best = v;
            return best;
        }

        private static double? MinOf(params double?[] values)
        {
            double? best = null;
            foreach (var v in values)
                if (v.HasValue && (!best.HasValue || v.Value < best.Value)) best = v;
            return best;
        }
    }

    /// <summary><c>ts_match_history</c>가 돌려주는 매치 기록 한 판.</summary>
    public sealed class MatchRecord
    {
        [JsonProperty("session_id")]
        public string SessionId { get; set; }

        [JsonProperty("game_code")]
        public string GameCode { get; set; }

        [JsonProperty("is_win")]
        public bool IsWin { get; set; }

        /// <summary>신고할 때 보낸 점수. 보내지 않았으면 null.</summary>
        [JsonProperty("score")]
        public double? Score { get; set; }

        /// <summary>상대 계정 ID. 상대 없는 판이면 null.</summary>
        [JsonProperty("opponent_account_id")]
        public string OpponentAccountId { get; set; }

        [JsonProperty("reported_at")]
        public DateTimeOffset ReportedAt { get; set; }

        [JsonProperty("status")]
        public string StatusRaw { get; set; }

        public MatchRecordStatus Status => StatusRaw switch
        {
            "verified" => MatchRecordStatus.Verified,
            "mismatch" => MatchRecordStatus.Mismatch,
            "solo" => MatchRecordStatus.Solo,
            _ => MatchRecordStatus.Pending,
        };
    }
}
