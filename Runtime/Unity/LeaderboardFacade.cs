using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TrueBase.Core.Auth;
using TrueBase.Core.Common;
using TrueBase.Core.Data;

namespace TrueBase.Unity
{
    /// <summary>
    /// 로그인 세션을 사용하는 리더보드 API.
    /// 리더보드 정의·컬럼 관리는 운영(Retool) 전용이라 여기에 없습니다.
    /// </summary>
    public sealed class LeaderboardFacade
    {
        private readonly SupabaseLeaderboardService _leaderboard;
        private readonly Func<SupabaseSession> _sessionGetter;

        /// <param name="leaderboard">REST 호출을 수행할 서비스. null이면 예외.</param>
        /// <param name="sessionGetter">현재 세션 제공자. null이면 세션 없는 오버로드는 <c>auth_not_signed_in</c>으로 실패합니다.</param>
        public LeaderboardFacade(SupabaseLeaderboardService leaderboard, Func<SupabaseSession> sessionGetter = null)
        {
            _leaderboard = leaderboard ?? throw new ArgumentNullException(nameof(leaderboard));
            _sessionGetter = sessionGetter;
        }

        /// <summary>사용 가능한 리더보드 목록을 조회합니다. 비활성·종료된 리더보드는 제외됩니다.</summary>
        public Task<SupabaseResult<IReadOnlyList<LeaderboardTable>>> GetTablesAsync() =>
            GetTablesAsync(_sessionGetter?.Invoke());

        public async Task<SupabaseResult<IReadOnlyList<LeaderboardTable>>> GetTablesAsync(SupabaseSession session)
        {
            var token = RequireToken(session);
            if (token == null)
                return SupabaseResult<IReadOnlyList<LeaderboardTable>>.Fail("auth_not_signed_in");

            return await _leaderboard.GetTablesAsync(token);
        }

        /// <summary>리더보드 1건의 정의와 현재 회차 상태를 조회합니다.</summary>
        /// <param name="code">리더보드 코드.</param>
        public Task<SupabaseResult<LeaderboardTable>> GetTableAsync(string code) =>
            GetTableAsync(_sessionGetter?.Invoke(), code);

        public async Task<SupabaseResult<LeaderboardTable>> GetTableAsync(SupabaseSession session, string code)
        {
            var token = RequireToken(session);
            if (token == null)
                return SupabaseResult<LeaderboardTable>.Fail("auth_not_signed_in");

            return await _leaderboard.GetTableAsync(token, code);
        }

        /// <summary>
        /// 본인 점수를 기록합니다. 최고·최저·최신·누적 중 어떤 방식으로 반영될지는
        /// 운영이 리더보드에 설정한 값에 따라 서버에서 결정됩니다.
        /// </summary>
        /// <param name="code">리더보드 코드.</param>
        /// <param name="score">제출할 점수.</param>
        /// <param name="data">플레이어 데이터 컬럼 값. 이 리더보드에 등록된 컬럼만 허용됩니다. (기본값: null)</param>
        public Task<SupabaseResult<LeaderboardSubmitResult>> SubmitScoreAsync(
            string code,
            double score,
            IReadOnlyDictionary<string, object> data = null) =>
            SubmitScoreAsync(_sessionGetter?.Invoke(), code, score, data);

        public async Task<SupabaseResult<LeaderboardSubmitResult>> SubmitScoreAsync(
            SupabaseSession session,
            string code,
            double score,
            IReadOnlyDictionary<string, object> data = null)
        {
            var token = RequireToken(session);
            if (token == null)
                return SupabaseResult<LeaderboardSubmitResult>.Fail("auth_not_signed_in");

            return await _leaderboard.SubmitScoreAsync(token, code, score, data);
        }

        /// <summary>순위 범위를 조회합니다. 한 번에 최대 100건까지 반환됩니다.</summary>
        /// <param name="start">시작 순위. 1부터. (기본값: 1)</param>
        /// <param name="end">끝 순위. 시작+99를 넘으면 잘립니다. (기본값: 100)</param>
        /// <param name="rotationCount">조회할 회차. null이면 현재 회차. (기본값: null)</param>
        public Task<SupabaseResult<IReadOnlyList<LeaderboardEntry>>> GetRangeAsync(
            string code,
            int start = 1,
            int end = 100,
            int? rotationCount = null) =>
            GetRangeAsync(_sessionGetter?.Invoke(), code, start, end, rotationCount);

        public async Task<SupabaseResult<IReadOnlyList<LeaderboardEntry>>> GetRangeAsync(
            SupabaseSession session,
            string code,
            int start = 1,
            int end = 100,
            int? rotationCount = null)
        {
            var token = RequireToken(session);
            if (token == null)
                return SupabaseResult<IReadOnlyList<LeaderboardEntry>>.Fail("auth_not_signed_in");

            return await _leaderboard.GetRangeAsync(token, code, start, end, rotationCount);
        }

        /// <summary>
        /// 플레이어 1명의 순위를 조회합니다. 기록이 없으면 실패가 아니라
        /// <see cref="LeaderboardPlayerEntry.Registered"/>가 false인 결과로 성공합니다.
        /// </summary>
        /// <param name="accountId">조회할 계정. null이면 본인. (기본값: null)</param>
        /// <param name="rotationCount">조회할 회차. null이면 현재 회차. (기본값: null)</param>
        public Task<SupabaseResult<LeaderboardPlayerEntry>> GetPlayerAsync(
            string code,
            string accountId = null,
            int? rotationCount = null) =>
            GetPlayerAsync(_sessionGetter?.Invoke(), code, accountId, rotationCount);

        public async Task<SupabaseResult<LeaderboardPlayerEntry>> GetPlayerAsync(
            SupabaseSession session,
            string code,
            string accountId = null,
            int? rotationCount = null)
        {
            var token = RequireToken(session);
            if (token == null)
                return SupabaseResult<LeaderboardPlayerEntry>.Fail("auth_not_signed_in");

            return await _leaderboard.GetPlayerAsync(token, code, accountId, rotationCount);
        }

        /// <summary>본인 기록의 등록 컬럼을 수정합니다. 점수는 바뀌지 않습니다.</summary>
        public Task<SupabaseResult> SetPlayerDataAsync(
            string code,
            IReadOnlyDictionary<string, object> data = null,
            int? rotationCount = null) =>
            SetPlayerDataAsync(_sessionGetter?.Invoke(), code, data, rotationCount);

        public async Task<SupabaseResult> SetPlayerDataAsync(
            SupabaseSession session,
            string code,
            IReadOnlyDictionary<string, object> data = null,
            int? rotationCount = null)
        {
            var token = RequireToken(session);
            if (token == null)
                return SupabaseResult.Fail("auth_not_signed_in");

            return await _leaderboard.SetPlayerDataAsync(token, code, data, rotationCount);
        }

        /// <summary>본인 기록을 삭제합니다. 기록이 없으면 아무것도 하지 않고 성공합니다.</summary>
        public Task<SupabaseResult> DeleteMyScoreAsync(string code, int? rotationCount = null) =>
            DeleteMyScoreAsync(_sessionGetter?.Invoke(), code, rotationCount);

        public async Task<SupabaseResult> DeleteMyScoreAsync(
            SupabaseSession session,
            string code,
            int? rotationCount = null)
        {
            var token = RequireToken(session);
            if (token == null)
                return SupabaseResult.Fail("auth_not_signed_in");

            return await _leaderboard.DeleteMyScoreAsync(token, code, rotationCount);
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
