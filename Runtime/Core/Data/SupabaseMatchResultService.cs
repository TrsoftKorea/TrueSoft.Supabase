using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using TrueBase.Core.Common;
using TrueBase.Core.Http;

namespace TrueBase.Core.Data
{
    /// <summary>
    /// 매치 결과 신고 RPC. <c>ts_match_report_result</c> 하나뿐입니다 — 신고·상호 교차검증·보상 지급(우편함 경유)을
    /// 서버가 한 번에 처리하므로 별도의 "검증 요청"·"지급 요청" API가 없습니다.
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
        /// </summary>
        /// <param name="accessToken">현재 로그인 세션의 액세스 토큰. 필수.</param>
        /// <param name="sessionId">게임이 부여한 매치 세션 식별자(예: Photon 룸 이름). 서버는 형식을 검증하지 않습니다.</param>
        /// <param name="gameCode">보상 설정을 구분하는 코드. 운영이 <c>match_reward_configs</c>에 미리 등록해야 합니다.</param>
        /// <param name="isWin">본인 기준 승패.</param>
        /// <param name="opponentAccountId">상대로 지목하는 계정 ID.</param>
        public async Task<SupabaseResult<MatchResultReportOutcome>> ReportResultAsync(
            string accessToken,
            string sessionId,
            string gameCode,
            bool isWin,
            string opponentAccountId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
                return SupabaseResult<MatchResultReportOutcome>.Fail(SupabaseErrorCode.MatchSessionIdEmpty);

            if (string.IsNullOrWhiteSpace(gameCode))
                return SupabaseResult<MatchResultReportOutcome>.Fail(SupabaseErrorCode.MatchGameCodeEmpty);

            if (string.IsNullOrWhiteSpace(opponentAccountId))
                return SupabaseResult<MatchResultReportOutcome>.Fail(SupabaseErrorCode.MatchOpponentRequired);

            var body = JsonConvert.SerializeObject(new
            {
                p_session_id = sessionId.Trim(),
                p_game_code = gameCode.Trim(),
                p_is_win = isWin,
                p_opponent_account_id = opponentAccountId.Trim()
            });

            var r = await SupabaseRestHelpers.CallRpcAsync(
                _httpClient, _supabaseUrl, _publishableKey, accessToken, "ts_match_report_result", body);
            if (!r.IsSuccess)
                return SupabaseResult<MatchResultReportOutcome>.Fail(r.ErrorCode);

            try
            {
                var outcome = JsonConvert.DeserializeObject<MatchResultReportOutcome>(r.Data);
                return SupabaseResult<MatchResultReportOutcome>.Success(outcome ?? new MatchResultReportOutcome { StatusRaw = "pending" });
            }
            catch (Exception e)
            {
                return SupabaseResult<MatchResultReportOutcome>.Fail("match_report_result_parse:" + e.Message);
            }
        }
    }
}
