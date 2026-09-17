using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TrueBase.Core.Auth;
using TrueBase.Core.Common;
using TrueBase.Core.Data;

namespace TrueBase.Unity
{
    /// <summary>로그인 세션을 사용하는 친구 API.</summary>
    internal sealed class FriendFacade
    {
        private readonly SupabaseFriendService _friend;
        private readonly Func<SupabaseSession> _sessionGetter;

        /// <param name="friend">REST 호출을 수행할 서비스. null이면 예외.</param>
        /// <param name="sessionGetter">현재 세션 제공자. null이면 세션 없는 오버로드는 <c>auth_not_signed_in</c>으로 실패합니다.</param>
        public FriendFacade(SupabaseFriendService friend, Func<SupabaseSession> sessionGetter = null)
        {
            _friend = friend ?? throw new ArgumentNullException(nameof(friend));
            _sessionGetter = sessionGetter;
        }

        public async Task<SupabaseResult<FriendSearchResult>> SearchAsync(string nickname)
        {
            var token = RequireToken(_sessionGetter?.Invoke());
            if (token == null)
                return SupabaseResult<FriendSearchResult>.Fail(SupabaseErrorCode.NotSignedIn);

            return await _friend.SearchAsync(token, nickname);
        }

        public async Task<SupabaseResult<FriendRequestSendOutcome>> SendRequestAsync(string targetAccountId)
        {
            var token = RequireToken(_sessionGetter?.Invoke());
            if (token == null)
                return SupabaseResult<FriendRequestSendOutcome>.Fail(SupabaseErrorCode.NotSignedIn);

            return await _friend.SendRequestAsync(token, targetAccountId);
        }

        public async Task<SupabaseResult<IReadOnlyList<FriendRequestSummary>>> ListRequestsAsync(
            FriendRequestDirection direction = FriendRequestDirection.Incoming)
        {
            var token = RequireToken(_sessionGetter?.Invoke());
            if (token == null)
                return SupabaseResult<IReadOnlyList<FriendRequestSummary>>.Fail(SupabaseErrorCode.NotSignedIn);

            return await _friend.ListRequestsAsync(token, direction);
        }

        public async Task<SupabaseResult> RespondAsync(string requestId, bool accept)
        {
            var token = RequireToken(_sessionGetter?.Invoke());
            if (token == null)
                return SupabaseResult.Fail(SupabaseErrorCode.NotSignedIn);

            return await _friend.RespondAsync(token, requestId, accept);
        }

        public async Task<SupabaseResult> CancelRequestAsync(string requestId)
        {
            var token = RequireToken(_sessionGetter?.Invoke());
            if (token == null)
                return SupabaseResult.Fail(SupabaseErrorCode.NotSignedIn);

            return await _friend.CancelRequestAsync(token, requestId);
        }

        public async Task<SupabaseResult<IReadOnlyList<FriendSummary>>> ListFriendsAsync()
        {
            var token = RequireToken(_sessionGetter?.Invoke());
            if (token == null)
                return SupabaseResult<IReadOnlyList<FriendSummary>>.Fail(SupabaseErrorCode.NotSignedIn);

            return await _friend.ListFriendsAsync(token);
        }

        public async Task<SupabaseResult> RemoveAsync(string friendAccountId)
        {
            var token = RequireToken(_sessionGetter?.Invoke());
            if (token == null)
                return SupabaseResult.Fail(SupabaseErrorCode.NotSignedIn);

            return await _friend.RemoveAsync(token, friendAccountId);
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
