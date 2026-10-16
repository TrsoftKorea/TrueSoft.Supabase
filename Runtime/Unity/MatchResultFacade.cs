using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TrueBase.Core.Auth;
using TrueBase.Core.Common;
using TrueBase.Core.Data;

namespace TrueBase.Unity
{
    /// <summary>매치 기록 신고·조회. 보상 설정 등록·수정은 운영 전용입니다.</summary>
    internal sealed class MatchResultFacade
    {
        private readonly SupabaseMatchResultService _matchResult;
        private readonly Func<SupabaseSession> _sessionGetter;

        public MatchResultFacade(SupabaseMatchResultService matchResult, Func<SupabaseSession> sessionGetter = null)
        {
            _matchResult = matchResult ?? throw new ArgumentNullException(nameof(matchResult));
            _sessionGetter = sessionGetter;
        }

        public async Task<SupabaseResult<MatchResultReportOutcome>> ReportResultAsync(
            string sessionId, string gameCode, bool isWin, string opponentAccountId, double? score)
        {
            var token = RequireToken();
            if (token == null)
                return SupabaseResult<MatchResultReportOutcome>.Fail(SupabaseErrorCode.NotSignedIn);

            return await _matchResult.ReportResultAsync(token, sessionId, gameCode, isWin, opponentAccountId, score);
        }

        public async Task<SupabaseResult> ReportSoloResultAsync(string sessionId, string gameCode, bool isWin, double? score)
        {
            var token = RequireToken();
            if (token == null)
                return SupabaseResult.Fail(SupabaseErrorCode.NotSignedIn);

            return await _matchResult.ReportSoloResultAsync(token, sessionId, gameCode, isWin, score);
        }

        public async Task<SupabaseResult<MatchStats>> GetStatsAsync(string gameCode)
        {
            var token = RequireToken();
            if (token == null)
                return SupabaseResult<MatchStats>.Fail(SupabaseErrorCode.NotSignedIn);

            return await _matchResult.GetStatsAsync(token, gameCode);
        }

        public async Task<SupabaseResult<IReadOnlyList<MatchRecord>>> GetHistoryAsync(
            string gameCode, int limit, DateTimeOffset? before)
        {
            var token = RequireToken();
            if (token == null)
                return SupabaseResult<IReadOnlyList<MatchRecord>>.Fail(SupabaseErrorCode.NotSignedIn);

            return await _matchResult.GetHistoryAsync(token, gameCode, limit, before);
        }

        /// <summary>현재 세션의 액세스 토큰. 세션이 없거나 토큰이 비어 있으면 null.</summary>
        private string RequireToken()
        {
            var session = _sessionGetter?.Invoke();
            if (session == null || string.IsNullOrWhiteSpace(session.AccessToken))
                return null;

            return session.AccessToken;
        }
    }
}
