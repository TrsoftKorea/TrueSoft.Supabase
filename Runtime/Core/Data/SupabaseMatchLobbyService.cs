using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using TrueBase.Core.Common;
using TrueBase.Core.Http;

namespace TrueBase.Core.Data
{
    /// <summary>
    /// 매치 로비 RPC. 팀·상대·같은 대기방인지는 게임마다 다르므로 이 서비스는
    /// "누가 로비에 있고 어떤 상태인지"만 다루고, metadata 해석과 실제 접속은 게임이 합니다.
    /// 로비 ID를 그대로 <see cref="SupabaseMatchResultService.ReportResultAsync"/>의 sessionId로 쓸 수 있습니다.
    /// 알림은 폴링 전제입니다 — <see cref="ListMyAsync"/>를 주기적으로 불러 상태 변화를 감지하세요.
    /// </summary>
    public sealed class SupabaseMatchLobbyService
    {
        private readonly string _supabaseUrl;
        private readonly string _publishableKey;
        private readonly ISupabaseHttpClient _httpClient;

        public SupabaseMatchLobbyService(
            string supabaseUrl,
            string publishableKey,
            ISupabaseHttpClient httpClient)
        {
            _supabaseUrl = (supabaseUrl ?? string.Empty).TrimEnd('/');
            _publishableKey = publishableKey ?? string.Empty;
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        /// <summary>로비를 만들고 지정한 계정들을 초대합니다. 초대 대상은 친구가 아니어도 됩니다.</summary>
        /// <param name="invitedAccountIds">null이거나 비어 있으면 호스트만 있는 로비를 만듭니다(나중에 <see cref="InviteAsync"/>로 추가 가능).</param>
        /// <param name="name">방 이름. 최대 40자, null이면 이름 없는 방.</param>
        /// <param name="maxMembers">정원. null이면 8명. 2~64.</param>
        /// <param name="metadata">맵·규칙 등 게임이 정하는 자유 값. 서버는 해석하지 않습니다.</param>
        public async Task<SupabaseResult<MatchLobbyCreateOutcome>> CreateAsync(
            string accessToken, string gameCode, IEnumerable<string> invitedAccountIds = null,
            string name = null, int? maxMembers = null, IReadOnlyDictionary<string, object> metadata = null)
        {
            if (string.IsNullOrWhiteSpace(gameCode))
                return SupabaseResult<MatchLobbyCreateOutcome>.Fail(SupabaseErrorCode.MatchGameCodeEmpty);
            if (maxMembers.HasValue && (maxMembers.Value < 2 || maxMembers.Value > 64))
                return SupabaseResult<MatchLobbyCreateOutcome>.Fail(SupabaseErrorCode.MatchLobbyMaxMembersInvalid);
            if (name != null && name.Trim().Length > 40)
                return SupabaseResult<MatchLobbyCreateOutcome>.Fail(SupabaseErrorCode.MatchLobbyNameTooLong);

            var ids = (invitedAccountIds ?? Enumerable.Empty<string>())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .ToArray();

            var body = JsonConvert.SerializeObject(new
            {
                p_game_code = gameCode.Trim(),
                p_invited_account_ids = ids,
                p_name = string.IsNullOrWhiteSpace(name) ? null : name.Trim(),
                p_max_members = maxMembers,
                p_metadata = metadata
            });
            var r = await CallRpcAsync(accessToken, "ts_match_lobby_create", body);
            if (!r.IsSuccess)
                return SupabaseResult<MatchLobbyCreateOutcome>.Fail(r.ErrorCode);

            try
            {
                var outcome = JsonConvert.DeserializeObject<MatchLobbyCreateOutcome>(r.Data);
                return SupabaseResult<MatchLobbyCreateOutcome>.Success(outcome);
            }
            catch (Exception e)
            {
                return SupabaseResult<MatchLobbyCreateOutcome>.Fail("match_lobby_create_parse:" + e.Message);
            }
        }

        /// <summary>열려 있는 로비에 한 명을 추가 초대합니다(호스트 전용).</summary>
        public async Task<SupabaseResult> InviteAsync(string accessToken, string lobbyId, string accountId)
        {
            if (string.IsNullOrWhiteSpace(lobbyId))
                return SupabaseResult.Fail(SupabaseErrorCode.MatchLobbyNotFound);
            if (string.IsNullOrWhiteSpace(accountId))
                return SupabaseResult.Fail(SupabaseErrorCode.FriendTargetRequired);

            var body = JsonConvert.SerializeObject(new { p_lobby_id = lobbyId.Trim(), p_account_id = accountId.Trim() });
            var r = await CallRpcAsync(accessToken, "ts_match_lobby_invite", body);
            return r.IsSuccess ? SupabaseResult.Ok : SupabaseResult.Fail(r.ErrorCode);
        }

        /// <summary>로비 초대에 응답합니다.</summary>
        public async Task<SupabaseResult> RespondAsync(string accessToken, string lobbyId, bool accept)
        {
            if (string.IsNullOrWhiteSpace(lobbyId))
                return SupabaseResult.Fail(SupabaseErrorCode.MatchLobbyNotFound);

            var body = JsonConvert.SerializeObject(new { p_lobby_id = lobbyId.Trim(), p_accept = accept });
            var r = await CallRpcAsync(accessToken, "ts_match_lobby_respond", body);
            return r.IsSuccess ? SupabaseResult.Ok : SupabaseResult.Fail(r.ErrorCode);
        }

        /// <summary>로비를 나갑니다. 호스트가 나가면 로비 전체가 취소됩니다.</summary>
        public async Task<SupabaseResult> LeaveAsync(string accessToken, string lobbyId)
        {
            if (string.IsNullOrWhiteSpace(lobbyId))
                return SupabaseResult.Fail(SupabaseErrorCode.MatchLobbyNotFound);

            var body = JsonConvert.SerializeObject(new { p_lobby_id = lobbyId.Trim() });
            var r = await CallRpcAsync(accessToken, "ts_match_lobby_leave", body);
            return r.IsSuccess ? SupabaseResult.Ok : SupabaseResult.Fail(r.ErrorCode);
        }

        /// <summary>
        /// 참가자 칸을 통째로 바꿉니다. 호스트는 누구 것이든, 참가자는 자기 것만 바꿀 수 있습니다.
        /// 팀·진영·준비 상태 등 의미는 게임이 정합니다.
        /// </summary>
        public async Task<SupabaseResult> SetMemberMetaAsync(
            string accessToken, string lobbyId, string accountId, IReadOnlyDictionary<string, object> metadata)
        {
            if (string.IsNullOrWhiteSpace(lobbyId))
                return SupabaseResult.Fail(SupabaseErrorCode.MatchLobbyNotFound);
            if (string.IsNullOrWhiteSpace(accountId))
                return SupabaseResult.Fail(SupabaseErrorCode.MatchLobbyMemberNotFound);

            var body = JsonConvert.SerializeObject(new
            {
                p_lobby_id = lobbyId.Trim(),
                p_account_id = accountId.Trim(),
                p_metadata = metadata
            });
            var r = await CallRpcAsync(accessToken, "ts_match_lobby_set_member_meta", body);
            return r.IsSuccess ? SupabaseResult.Ok : SupabaseResult.Fail(r.ErrorCode);
        }

        /// <summary>로비 시작을 알립니다(호스트 전용). 실제 접속·연결은 게임이 처리합니다.</summary>
        public async Task<SupabaseResult> StartAsync(string accessToken, string lobbyId)
        {
            if (string.IsNullOrWhiteSpace(lobbyId))
                return SupabaseResult.Fail(SupabaseErrorCode.MatchLobbyNotFound);

            var body = JsonConvert.SerializeObject(new { p_lobby_id = lobbyId.Trim() });
            var r = await CallRpcAsync(accessToken, "ts_match_lobby_start", body);
            return r.IsSuccess ? SupabaseResult.Ok : SupabaseResult.Fail(r.ErrorCode);
        }

        /// <summary>로비를 취소합니다(호스트 전용).</summary>
        public async Task<SupabaseResult> CancelAsync(string accessToken, string lobbyId)
        {
            if (string.IsNullOrWhiteSpace(lobbyId))
                return SupabaseResult.Fail(SupabaseErrorCode.MatchLobbyNotFound);

            var body = JsonConvert.SerializeObject(new { p_lobby_id = lobbyId.Trim() });
            var r = await CallRpcAsync(accessToken, "ts_match_lobby_cancel", body);
            return r.IsSuccess ? SupabaseResult.Ok : SupabaseResult.Fail(r.ErrorCode);
        }

        /// <summary>
        /// 내가 호스트거나 멤버인 진행 중(open·started) 로비 목록. 알림이 폴링 전제이므로
        /// 주기적으로 호출해 상태 변화(초대 수락·시작·취소)를 감지하는 용도입니다.
        /// </summary>
        public async Task<SupabaseResult<IReadOnlyList<MatchLobbySummary>>> ListMyAsync(string accessToken)
        {
            var r = await CallRpcAsync(accessToken, "ts_match_lobby_list_my", "{}");
            if (!r.IsSuccess)
                return SupabaseResult<IReadOnlyList<MatchLobbySummary>>.Fail(r.ErrorCode);

            try
            {
                var list = JsonConvert.DeserializeObject<List<MatchLobbySummary>>(r.Data)
                           ?? new List<MatchLobbySummary>();
                return SupabaseResult<IReadOnlyList<MatchLobbySummary>>.Success(list);
            }
            catch (Exception e)
            {
                return SupabaseResult<IReadOnlyList<MatchLobbySummary>>.Fail("match_lobby_list_my_parse:" + e.Message);
            }
        }

        // -------------------------------------------------------------------
        // 공통 RPC 호출
        // -------------------------------------------------------------------

        private Task<SupabaseResult<string>> CallRpcAsync(string accessToken, string rpcName, string bodyJson) =>
            SupabaseRestHelpers.CallRpcAsync(_httpClient, _supabaseUrl, _publishableKey, accessToken, rpcName, bodyJson);
    }
}
