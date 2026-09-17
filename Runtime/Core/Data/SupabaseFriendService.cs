using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using TrueBase.Core.Common;
using TrueBase.Core.Http;

namespace TrueBase.Core.Data
{
    /// <summary>
    /// 친구 RPC. 닉네임 검색 <c>ts_friend_search</c>, 요청 전송·조회·응답·취소, 친구 목록·삭제.
    /// 친구 관계는 별도 테이블이 아니라 요청 행이 <c>accepted</c> 상태로 남는 것으로 표현됩니다.
    /// </summary>
    public sealed class SupabaseFriendService
    {
        private readonly string _supabaseUrl;
        private readonly string _publishableKey;
        private readonly ISupabaseHttpClient _httpClient;

        public SupabaseFriendService(
            string supabaseUrl,
            string publishableKey,
            ISupabaseHttpClient httpClient)
        {
            _supabaseUrl = (supabaseUrl ?? string.Empty).TrimEnd('/');
            _publishableKey = publishableKey ?? string.Empty;
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        /// <summary>닉네임 정확 일치(대소문자 무시)로 유저를 찾습니다. 이미 친구·요청 중인지도 같이 돌려줍니다.</summary>
        public async Task<SupabaseResult<FriendSearchResult>> SearchAsync(string accessToken, string nickname)
        {
            if (string.IsNullOrWhiteSpace(nickname))
                return SupabaseResult<FriendSearchResult>.Fail(SupabaseErrorCode.FriendNicknameEmpty);

            var body = JsonConvert.SerializeObject(new { p_nickname = nickname.Trim() });
            var r = await CallRpcAsync(accessToken, "ts_friend_search", body);
            if (!r.IsSuccess)
                return SupabaseResult<FriendSearchResult>.Fail(r.ErrorCode);

            try
            {
                var result = JsonConvert.DeserializeObject<FriendSearchResult>(r.Data);
                return SupabaseResult<FriendSearchResult>.Success(result);
            }
            catch (Exception e)
            {
                return SupabaseResult<FriendSearchResult>.Fail("friend_search_parse:" + e.Message);
            }
        }

        /// <summary>
        /// 친구 요청을 보냅니다. 상대가 이미 나에게 보낸 pending 요청이 있으면 즉시 상호 수락되어
        /// <see cref="FriendRequestSendOutcome.Accepted"/>가 true로 돌아옵니다.
        /// </summary>
        public async Task<SupabaseResult<FriendRequestSendOutcome>> SendRequestAsync(string accessToken, string targetAccountId)
        {
            if (string.IsNullOrWhiteSpace(targetAccountId))
                return SupabaseResult<FriendRequestSendOutcome>.Fail(SupabaseErrorCode.FriendTargetRequired);

            var body = JsonConvert.SerializeObject(new { p_target_account_id = targetAccountId.Trim() });
            var r = await CallRpcAsync(accessToken, "ts_friend_request_send", body);
            if (!r.IsSuccess)
                return SupabaseResult<FriendRequestSendOutcome>.Fail(r.ErrorCode);

            try
            {
                var outcome = JsonConvert.DeserializeObject<FriendRequestSendOutcome>(r.Data);
                return SupabaseResult<FriendRequestSendOutcome>.Success(outcome);
            }
            catch (Exception e)
            {
                return SupabaseResult<FriendRequestSendOutcome>.Fail("friend_request_send_parse:" + e.Message);
            }
        }

        /// <summary>대기 중인 친구 요청 목록(받은 것 또는 보낸 것).</summary>
        public async Task<SupabaseResult<IReadOnlyList<FriendRequestSummary>>> ListRequestsAsync(
            string accessToken, FriendRequestDirection direction = FriendRequestDirection.Incoming)
        {
            var body = JsonConvert.SerializeObject(new
            {
                p_direction = direction == FriendRequestDirection.Outgoing ? "outgoing" : "incoming"
            });
            var r = await CallRpcAsync(accessToken, "ts_friend_requests_list", body);
            if (!r.IsSuccess)
                return SupabaseResult<IReadOnlyList<FriendRequestSummary>>.Fail(r.ErrorCode);

            try
            {
                var list = JsonConvert.DeserializeObject<List<FriendRequestSummary>>(r.Data)
                           ?? new List<FriendRequestSummary>();
                return SupabaseResult<IReadOnlyList<FriendRequestSummary>>.Success(list);
            }
            catch (Exception e)
            {
                return SupabaseResult<IReadOnlyList<FriendRequestSummary>>.Fail("friend_requests_list_parse:" + e.Message);
            }
        }

        /// <summary>받은 친구 요청을 수락하거나 거절합니다.</summary>
        public async Task<SupabaseResult> RespondAsync(string accessToken, string requestId, bool accept)
        {
            if (string.IsNullOrWhiteSpace(requestId))
                return SupabaseResult.Fail(SupabaseErrorCode.FriendRequestNotFound);

            var body = JsonConvert.SerializeObject(new { p_request_id = requestId.Trim(), p_accept = accept });
            var r = await CallRpcAsync(accessToken, "ts_friend_request_respond", body);
            return r.IsSuccess ? SupabaseResult.Ok : SupabaseResult.Fail(r.ErrorCode);
        }

        /// <summary>내가 보낸 pending 요청을 취소합니다.</summary>
        public async Task<SupabaseResult> CancelRequestAsync(string accessToken, string requestId)
        {
            if (string.IsNullOrWhiteSpace(requestId))
                return SupabaseResult.Fail(SupabaseErrorCode.FriendRequestNotFound);

            var body = JsonConvert.SerializeObject(new { p_request_id = requestId.Trim() });
            var r = await CallRpcAsync(accessToken, "ts_friend_request_cancel", body);
            return r.IsSuccess ? SupabaseResult.Ok : SupabaseResult.Fail(r.ErrorCode);
        }

        /// <summary>내 친구 목록.</summary>
        public async Task<SupabaseResult<IReadOnlyList<FriendSummary>>> ListFriendsAsync(string accessToken)
        {
            var r = await CallRpcAsync(accessToken, "ts_friends_list", "{}");
            if (!r.IsSuccess)
                return SupabaseResult<IReadOnlyList<FriendSummary>>.Fail(r.ErrorCode);

            try
            {
                var list = JsonConvert.DeserializeObject<List<FriendSummary>>(r.Data)
                           ?? new List<FriendSummary>();
                return SupabaseResult<IReadOnlyList<FriendSummary>>.Success(list);
            }
            catch (Exception e)
            {
                return SupabaseResult<IReadOnlyList<FriendSummary>>.Fail("friends_list_parse:" + e.Message);
            }
        }

        /// <summary>친구를 삭제합니다. 이후 다시 요청을 보낼 수 있습니다.</summary>
        public async Task<SupabaseResult> RemoveAsync(string accessToken, string friendAccountId)
        {
            if (string.IsNullOrWhiteSpace(friendAccountId))
                return SupabaseResult.Fail(SupabaseErrorCode.FriendNotFound);

            var body = JsonConvert.SerializeObject(new { p_friend_account_id = friendAccountId.Trim() });
            var r = await CallRpcAsync(accessToken, "ts_friend_remove", body);
            return r.IsSuccess ? SupabaseResult.Ok : SupabaseResult.Fail(r.ErrorCode);
        }

        // -------------------------------------------------------------------
        // 공통 RPC 호출
        // -------------------------------------------------------------------

        private Task<SupabaseResult<string>> CallRpcAsync(string accessToken, string rpcName, string bodyJson) =>
            SupabaseRestHelpers.CallRpcAsync(_httpClient, _supabaseUrl, _publishableKey, accessToken, rpcName, bodyJson);
    }
}
