using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TrueBase.Core.Auth;
using TrueBase.Core.Common;
using TrueBase.Core.Data;

namespace TrueBase.Unity
{
    /// <summary>로그인 세션을 사용하는 매치 로비 API.</summary>
    internal sealed class MatchLobbyFacade
    {
        private readonly SupabaseMatchLobbyService _lobby;
        private readonly Func<SupabaseSession> _sessionGetter;

        /// <param name="lobby">REST 호출을 수행할 서비스. null이면 예외.</param>
        /// <param name="sessionGetter">현재 세션 제공자. null이면 세션 없는 오버로드는 <c>auth_not_signed_in</c>으로 실패합니다.</param>
        public MatchLobbyFacade(SupabaseMatchLobbyService lobby, Func<SupabaseSession> sessionGetter = null)
        {
            _lobby = lobby ?? throw new ArgumentNullException(nameof(lobby));
            _sessionGetter = sessionGetter;
        }

        public async Task<SupabaseResult<MatchLobbyCreateOutcome>> CreateAsync(
            string gameCode, IEnumerable<string> invitedAccountIds = null,
            string name = null, int? maxMembers = null, IReadOnlyDictionary<string, object> metadata = null)
        {
            var token = RequireToken(_sessionGetter?.Invoke());
            if (token == null)
                return SupabaseResult<MatchLobbyCreateOutcome>.Fail(SupabaseErrorCode.NotSignedIn);

            return await _lobby.CreateAsync(token, gameCode, invitedAccountIds, name, maxMembers, metadata);
        }

        public async Task<SupabaseResult> InviteAsync(string lobbyId, string accountId)
        {
            var token = RequireToken(_sessionGetter?.Invoke());
            if (token == null)
                return SupabaseResult.Fail(SupabaseErrorCode.NotSignedIn);

            return await _lobby.InviteAsync(token, lobbyId, accountId);
        }

        public async Task<SupabaseResult> RespondAsync(string lobbyId, bool accept)
        {
            var token = RequireToken(_sessionGetter?.Invoke());
            if (token == null)
                return SupabaseResult.Fail(SupabaseErrorCode.NotSignedIn);

            return await _lobby.RespondAsync(token, lobbyId, accept);
        }

        public async Task<SupabaseResult> LeaveAsync(string lobbyId)
        {
            var token = RequireToken(_sessionGetter?.Invoke());
            if (token == null)
                return SupabaseResult.Fail(SupabaseErrorCode.NotSignedIn);

            return await _lobby.LeaveAsync(token, lobbyId);
        }

        public async Task<SupabaseResult> SetMemberMetaAsync(
            string lobbyId, string accountId, IReadOnlyDictionary<string, object> metadata)
        {
            var token = RequireToken(_sessionGetter?.Invoke());
            if (token == null)
                return SupabaseResult.Fail(SupabaseErrorCode.NotSignedIn);

            return await _lobby.SetMemberMetaAsync(token, lobbyId, accountId, metadata);
        }

        public async Task<SupabaseResult> StartAsync(string lobbyId)
        {
            var token = RequireToken(_sessionGetter?.Invoke());
            if (token == null)
                return SupabaseResult.Fail(SupabaseErrorCode.NotSignedIn);

            return await _lobby.StartAsync(token, lobbyId);
        }

        public async Task<SupabaseResult> CancelAsync(string lobbyId)
        {
            var token = RequireToken(_sessionGetter?.Invoke());
            if (token == null)
                return SupabaseResult.Fail(SupabaseErrorCode.NotSignedIn);

            return await _lobby.CancelAsync(token, lobbyId);
        }

        public async Task<SupabaseResult<IReadOnlyList<MatchLobbySummary>>> ListMyAsync()
        {
            var token = RequireToken(_sessionGetter?.Invoke());
            if (token == null)
                return SupabaseResult<IReadOnlyList<MatchLobbySummary>>.Fail(SupabaseErrorCode.NotSignedIn);

            return await _lobby.ListMyAsync(token);
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
