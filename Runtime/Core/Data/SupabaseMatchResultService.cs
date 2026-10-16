using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using TrueBase.Core.Common;
using TrueBase.Core.Http;

namespace TrueBase.Core.Data
{
    /// <summary>
    /// 매치 기록 RPC. 대인전 신고(<c>ts_match_report_result</c>)는 신고·상호 교차검증·보상 지급(우편함 경유)을
    /// 서버가 한 번에 처리하므로 별도의 "검증 요청"·"지급 요청" API가 없습니다. 상대 없는 판 기록과 본인 기록 조회는 지급과 무관합니다.
    /// </summary>
    public sealed class SupabaseMatchResultService
    {
        private readonly string _supabaseUrl;
        private readonly string _publishableKey;
        private readonly ISupabaseHttpClient _httpClient;

        public SupabaseMatchResultService(
            string supabaseUrl,
            string publishableKey,
            ISupabaseHttpClient httpClient)
        {
            _supabaseUrl = (supabaseUrl ?? string.Empty).TrimEnd('/');
            _publishableKey = publishableKey ?? string.Empty;
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        /// <summary>
        /// 본인이 겪은 매치 결과를 신고합니다. 같은 (세션 ID, 본인 계정)을 다시 신고해도 중복 지급되지 않습니다(멱등).
        /// 상대가 아직 신고하지 않았으면 <see cref="MatchResultStatus.Pending"/>으로 성공합니다 — 오류가 아닙니다.
        /// 상호 지목이 어긋나거나 승패 결과가 게임 코드의 검증 정책과 맞지 않으면 <see cref="SupabaseErrorCode.MatchResultMismatch"/>로 실패합니다.
        /// 이때 서버는 그 판을 무효로 저장한 뒤 상태로 알려 주므로(예외로 알리면 무효 기록까지 되돌려진다) 여기서 실패로 바꾼다.
        /// </summary>
        /// <param name="accessToken">현재 로그인 세션의 액세스 토큰. 필수.</param>
        /// <param name="sessionId">게임이 부여한 매치 세션 식별자(예: Photon 룸 이름). 서버는 형식을 검증하지 않습니다.</param>
        /// <param name="gameCode">보상 설정을 구분하는 코드. 운영이 <c>match_reward_configs</c>에 미리 등록해야 합니다.</param>
        /// <param name="isWin">본인 기준 승패.</param>
        /// <param name="opponentAccountId">상대로 지목하는 계정 ID.</param>
        /// <param name="score">게임이 정하는 판 점수. 상대 신고와 맞춰 보지 않습니다. 재신고하면 처음 값이 남습니다.</param>
        public async Task<SupabaseResult<MatchResultReportOutcome>> ReportResultAsync(
            string accessToken,
            string sessionId,
            string gameCode,
            bool isWin,
            string opponentAccountId,
            double? score = null)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
                return SupabaseResult<MatchResultReportOutcome>.Fail(SupabaseErrorCode.MatchSessionIdEmpty);

            if (string.IsNullOrWhiteSpace(gameCode))
                return SupabaseResult<MatchResultReportOutcome>.Fail(SupabaseErrorCode.MatchGameCodeEmpty);

            if (string.IsNullOrWhiteSpace(opponentAccountId))
                return SupabaseResult<MatchResultReportOutcome>.Fail(SupabaseErrorCode.MatchOpponentRequired);

            if (!IsValidScore(score))
                return SupabaseResult<MatchResultReportOutcome>.Fail(SupabaseErrorCode.MatchScoreInvalid);

            var body = JsonConvert.SerializeObject(new
            {
                p_session_id = sessionId.Trim(),
                p_game_code = gameCode.Trim(),
                p_is_win = isWin,
                p_opponent_account_id = opponentAccountId.Trim(),
                p_score = score
            });

            var r = await SupabaseRestHelpers.CallRpcAsync(
                _httpClient, _supabaseUrl, _publishableKey, accessToken, "ts_match_report_result", body);
            if (!r.IsSuccess)
                return SupabaseResult<MatchResultReportOutcome>.Fail(r.ErrorCode);

            try
            {
                var outcome = JsonConvert.DeserializeObject<MatchResultReportOutcome>(r.Data);
                if (outcome?.StatusRaw == "mismatch")
                    return SupabaseResult<MatchResultReportOutcome>.Fail(SupabaseErrorCode.MatchResultMismatch);
                return SupabaseResult<MatchResultReportOutcome>.Success(outcome ?? new MatchResultReportOutcome { StatusRaw = "pending" });
            }
            catch (Exception e)
            {
                return SupabaseResult<MatchResultReportOutcome>.Fail("match_report_result_parse:" + e.Message);
            }
        }

        /// <summary>
        /// 상대 없는 판(봇·혼자)을 기록합니다. 맞춰 볼 상대 신고가 없으므로 보상은 지급하지 않습니다. 같은 세션 재신고는 멱등입니다.
        /// 같은 세션을 대인전으로 이미 신고했으면 <see cref="SupabaseErrorCode.MatchResultMismatch"/>로 실패합니다.
        /// </summary>
        public async Task<SupabaseResult> ReportSoloResultAsync(
            string accessToken,
            string sessionId,
            string gameCode,
            bool isWin,
            double? score = null)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
                return SupabaseResult.Fail(SupabaseErrorCode.MatchSessionIdEmpty);

            if (string.IsNullOrWhiteSpace(gameCode))
                return SupabaseResult.Fail(SupabaseErrorCode.MatchGameCodeEmpty);

            if (!IsValidScore(score))
                return SupabaseResult.Fail(SupabaseErrorCode.MatchScoreInvalid);

            var body = JsonConvert.SerializeObject(new
            {
                p_session_id = sessionId.Trim(),
                p_game_code = gameCode.Trim(),
                p_is_win = isWin,
                p_score = score
            });

            var r = await SupabaseRestHelpers.CallRpcAsync(
                _httpClient, _supabaseUrl, _publishableKey, accessToken, "ts_match_report_solo_result", body,
                allowEmptyBody: true);
            return r.IsSuccess ? SupabaseResult.Ok : SupabaseResult.Fail(r.ErrorCode);
        }

        /// <summary>본인의 <paramref name="gameCode"/> 승·패 수와 점수 최댓값·최솟값을 확인 단계별로 돌려줍니다.</summary>
        public async Task<SupabaseResult<MatchStats>> GetStatsAsync(string accessToken, string gameCode)
        {
            if (string.IsNullOrWhiteSpace(gameCode))
                return SupabaseResult<MatchStats>.Fail(SupabaseErrorCode.MatchGameCodeEmpty);

            var body = JsonConvert.SerializeObject(new { p_game_code = gameCode.Trim() });

            var r = await SupabaseRestHelpers.CallRpcAsync(
                _httpClient, _supabaseUrl, _publishableKey, accessToken, "ts_match_stats", body);
            if (!r.IsSuccess)
                return SupabaseResult<MatchStats>.Fail(r.ErrorCode);

            try
            {
                var stats = JsonConvert.DeserializeObject<MatchStats>(r.Data);
                return SupabaseResult<MatchStats>.Success(stats ?? new MatchStats());
            }
            catch (Exception e)
            {
                return SupabaseResult<MatchStats>.Fail("match_stats_parse:" + e.Message);
            }
        }

        /// <summary>
        /// 본인의 매치 기록을 최근 순으로 돌려줍니다. <paramref name="gameCode"/>가 비면 모든 게임 코드입니다.
        /// 다음 쪽은 마지막 기록의 <see cref="MatchRecord.ReportedAt"/>을 <paramref name="before"/>로 넘깁니다. 서버가 1~100개로 자릅니다.
        /// </summary>
        public async Task<SupabaseResult<IReadOnlyList<MatchRecord>>> GetHistoryAsync(
            string accessToken,
            string gameCode = null,
            int limit = 20,
            DateTimeOffset? before = null)
        {
            var body = JsonConvert.SerializeObject(new
            {
                p_game_code = string.IsNullOrWhiteSpace(gameCode) ? null : gameCode.Trim(),
                p_limit = limit,
                p_before = before
            });

            var r = await SupabaseRestHelpers.CallRpcAsync(
                _httpClient, _supabaseUrl, _publishableKey, accessToken, "ts_match_history", body);
            if (!r.IsSuccess)
                return SupabaseResult<IReadOnlyList<MatchRecord>>.Fail(r.ErrorCode);

            try
            {
                var list = JsonConvert.DeserializeObject<List<MatchRecord>>(r.Data) ?? new List<MatchRecord>();
                return SupabaseResult<IReadOnlyList<MatchRecord>>.Success(list);
            }
            catch (Exception e)
            {
                return SupabaseResult<IReadOnlyList<MatchRecord>>.Fail("match_history_parse:" + e.Message);
            }
        }

        // NaN 이 한 판이라도 저장되면 그 단계의 max·min 이 영영 NaN 이 된다(Postgres 는 NaN 을 가장 큰 값으로 친다).
        private static bool IsValidScore(double? score) =>
            !score.HasValue || !(double.IsNaN(score.Value) || double.IsInfinity(score.Value));
    }
}
