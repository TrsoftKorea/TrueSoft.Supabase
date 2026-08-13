using System;
using System.Threading.Tasks;
using TrueBase.Core.Auth;
using TrueBase.Core.Common;
using TrueBase.Core.Data;

namespace TrueBase.Unity
{
    /// <summary>매치 결과 신고(<c>ts_match_report_result</c>). 보상 설정 등록·수정은 운영 전용입니다.</summary>
    internal sealed class MatchResultFacade
    {
        private readonly SupabaseMatchResultService _matchResult;
        private readonly Func<SupabaseSession> _sessionGetter;

        public MatchResultFacade(SupabaseMatchResultService matchResult, Func<SupabaseSession> sessionGetter = null)
        {
            _matchResult = matchResult ?? throw new ArgumentNullException(nameof(matchResult));
            _sessionGetter = sessionGetter;
        }

        public Task<SupabaseResult<MatchResultReportOutcome>> ReportResultAsync(
            string sessionId, string gameCode, bool isWin, string opponentAccountId) =>
            ReportResultAsync(_sessionGetter?.Invoke(), sessionId, gameCode, isWin, opponentAccountId);

        public async Task<SupabaseResult<MatchResultReportOutcome>> ReportResultAsync(
            SupabaseSession session, string sessionId, string gameCode, bool isWin, string opponentAccountId)
        {
            var token = RequireToken(session);
            if (token == null)
                return SupabaseResult<MatchResultReportOutcome>.Fail("auth_not_signed_in");

            return await _matchResult.ReportResultAsync(token, sessionId, gameCode, isWin, opponentAccountId);
        }

        /// <summary>세션에서 액세스 토큰을 추출합니다. 세션이 null이거나 토큰이 비어 있으면 null.</summary>
        private static string RequireToken(SupabaseSession session)
        {
            if (session == null || string.IsNullOrWhiteSpace(session.AccessToken))
                return null;

            return session.AccessToken;
        }
    }
}
