using System.Collections.Generic;
using System.Threading.Tasks;
using TrueBase.Core.Common;
using TrueBase.Core.Data;
using TrueBase.Unity;
using UnityEngine;

/// <summary>
/// 매치 로비 예제 컴포넌트. SupabaseRuntime이 씬에 있어야 합니다.
///
/// 초대 대상은 친구가 아니어도 됩니다 — 아무 계정 ID나 Inspector의 <c>inviteAccountId</c>에
/// 넣으면 됩니다. 도배는 서버가 대기 초대 상한과 연속 초대 간격으로 막습니다.
///
/// 샘플을 여러 개 함께 쓰면 단축키가 겹칩니다. 그때는 <b>Tab</b> 으로 키를 받을 샘플을 고르세요.
/// 씬에 샘플이 하나뿐이면 그냥 눌러도 됩니다.
///
/// 키보드 단축키 (Play Mode):
///   1 — 익명 로그인
///   2 — 로비 생성 + 초대
///   3 — 내 로비 목록
///   4 — 내가 초대받은 로비 중 첫 번째 수락
///   5 — 방금 만든 로비에 추가 초대
///   6 — 방금 만든 로비에서 내 참가자 칸 지정
///   7 — 방금 만든 로비 시작
///   8 — 방금 만든 로비 취소
///   9 — 방금 만든 로비 나가기
///   0 — 로비 대화 보내기
///   - — 로비 대화 읽기
/// </summary>
public sealed class SampleMatchLobby : MonoBehaviour
{
    private const string Tag = "[Supabase.MatchLobby]";

    [Tooltip("매치 종류를 구분하는 코드. 실제 보상·연결과 무관한 예제용 값입니다.")]
    [SerializeField] private string gameCode = "arena_1v1";

    [Tooltip("추가로 초대할 계정 ID. 친구가 아니어도 됩니다.")]
    [SerializeField] private string inviteAccountId = "";

    [Tooltip("0번 키로 로비 대화에 보낼 내용.")]
    [SerializeField] private string chatMessage = "다들 준비됐나요?";

    [Tooltip("방 이름. 비워 두면 이름 없는 방으로 만듭니다.")]
    [SerializeField] private string lobbyName = "같이 하실 분";

    [Tooltip("정원. 2~64.")]
    [SerializeField] private int maxMembers = 4;

    private string _lobbyId;

    private void Update()
    {
        // 여러 샘플을 한 씬에 놓으면 단축키가 겹친다. Tab 으로 고른 대상만 키를 읽는다.
        if (!SampleFocus.IsActive(this)) return;

        if (Input.GetKeyDown(KeyCode.Alpha1)) _ = SignInAsync();
        if (Input.GetKeyDown(KeyCode.Alpha2)) _ = CreateAsync();
        if (Input.GetKeyDown(KeyCode.Alpha3)) _ = ShowMyLobbiesAsync();
        if (Input.GetKeyDown(KeyCode.Alpha4)) _ = AcceptFirstInviteAsync();
        if (Input.GetKeyDown(KeyCode.Alpha5)) _ = InviteMoreAsync();
        if (Input.GetKeyDown(KeyCode.Alpha6)) _ = SetMyMetaAsync();
        if (Input.GetKeyDown(KeyCode.Alpha7)) _ = StartAsync();
        if (Input.GetKeyDown(KeyCode.Alpha8)) _ = CancelAsync();
        if (Input.GetKeyDown(KeyCode.Alpha9)) _ = LeaveAsync();
        if (Input.GetKeyDown(KeyCode.Alpha0)) _ = SendChatAsync();
        if (Input.GetKeyDown(KeyCode.Minus))  _ = ShowChatAsync();
    }

    /// <summary>1 — 익명 로그인.</summary>
    private async Task SignInAsync()
    {
        var ok = await Supabase.SignInAnonymouslyAsync();
        if (!ok) { Debug.LogWarning($"{Tag} 로그인 실패: {ok.ErrorCode}"); return; }

        Debug.Log($"{Tag} 로그인됨. UserId = {Supabase.UserId}");
    }

    /// <summary>2 — 로비 생성 + 초대. 결과의 LobbyId를 이후 단축키에서 재사용합니다.</summary>
    private async Task CreateAsync()
    {
        var invited = string.IsNullOrEmpty(inviteAccountId) ? null : new[] { inviteAccountId };

        // metadata는 서버가 해석하지 않는다 — 게임이 정한 값을 그대로 담아 두고 목록에서 다시 읽는다.
        var meta = new Dictionary<string, object> { ["map"] = "desert", ["mode"] = "ranked" };

        var r = await Supabase.CreateMatchLobbyAsync(gameCode, invited, lobbyName, maxMembers, meta);
        if (!r.IsSuccess) { Debug.LogWarning($"{Tag} 생성 실패: {MessageFor(r.Reason, r.ErrorCode)}"); return; }

        _lobbyId = r.Data.LobbyId;
        Debug.Log($"{Tag} 로비 생성됨. LobbyId = {_lobbyId} (SessionId도 같은 값 — 매치 결과 신고에 재사용 가능)");
    }

    /// <summary>3 — 내가 호스트거나 멤버인 진행 중 로비 목록.</summary>
    private async Task ShowMyLobbiesAsync()
    {
        var r = await Supabase.ListMatchLobbiesAsync();
        if (!r.IsSuccess) { Debug.LogWarning($"{Tag} 조회 실패: {r.ErrorCode}"); return; }

        Debug.Log($"{Tag} 로비 {r.Data.Count}개");
        foreach (var lobby in r.Data)
        {
            var map = lobby.Metadata != null && lobby.Metadata.TryGetValue("map", out var v) ? v : "없음";
            Debug.Log($"{Tag}  {lobby.Name ?? "이름 없음"} — {lobby.GameCode} / {lobby.Status} / 정원 {lobby.MaxMembers} / map={map}");
            foreach (var m in lobby.Members)
            {
                var team = m.Metadata != null && m.Metadata.TryGetValue("team", out var t) ? t : "없음";
                Debug.Log($"{Tag}    - {m.Name}: {m.Status} (team={team})");
            }
        }
    }

    /// <summary>4 — 내가 초대받은 로비 중 첫 번째를 수락.</summary>
    private async Task AcceptFirstInviteAsync()
    {
        var list = await Supabase.ListMatchLobbiesAsync();
        if (!list.IsSuccess) { Debug.LogWarning($"{Tag} 조회 실패: {list.ErrorCode}"); return; }

        foreach (var lobby in list.Data)
        {
            foreach (var m in lobby.Members)
            {
                if (m.AccountId != Supabase.UserId || m.Status != MatchLobbyMemberState.Invited)
                    continue;

                var r = await Supabase.RespondMatchLobbyAsync(lobby.LobbyId, accept: true);
                Debug.Log(r.IsSuccess
                    ? $"{Tag} {lobby.LobbyId} 수락함."
                    : $"{Tag} 수락 실패: {r.ErrorCode}");
                return;
            }
        }

        Debug.Log($"{Tag} 받은 초대가 없습니다.");
    }

    /// <summary>5 — 2번에서 만든 로비에 같은 대상을 다시 초대(중복 확인용 예제).</summary>
    private async Task InviteMoreAsync()
    {
        if (string.IsNullOrEmpty(_lobbyId)) { Debug.Log($"{Tag} 먼저 2번으로 로비를 만드세요."); return; }
        if (string.IsNullOrEmpty(inviteAccountId)) { Debug.Log($"{Tag} inviteAccountId를 채우세요."); return; }

        var r = await Supabase.InviteToMatchLobbyAsync(_lobbyId, inviteAccountId);
        Debug.Log(r.IsSuccess ? $"{Tag} 초대함." : $"{Tag} {MessageFor(r.Reason, r.ErrorCode)}");
    }

    /// <summary>6 — 2번에서 만든 로비에서 내 참가자 칸 지정. 값 자체는 서버가 해석하지 않습니다.</summary>
    private async Task SetMyMetaAsync()
    {
        if (string.IsNullOrEmpty(_lobbyId)) { Debug.Log($"{Tag} 먼저 2번으로 로비를 만드세요."); return; }

        var meta = new Dictionary<string, object> { ["team"] = "a", ["ready"] = true };
        var r = await Supabase.SetMatchLobbyMemberMetaAsync(_lobbyId, Supabase.UserId, meta);
        Debug.Log(r.IsSuccess ? $"{Tag} 내 칸을 team=a, ready=true로 지정함." : $"{Tag} 지정 실패: {r.ErrorCode}");
    }

    /// <summary>7 — 2번에서 만든 로비 시작. 실제 접속은 게임이 처리합니다.</summary>
    private async Task StartAsync()
    {
        if (string.IsNullOrEmpty(_lobbyId)) { Debug.Log($"{Tag} 먼저 2번으로 로비를 만드세요."); return; }

        var r = await Supabase.StartMatchLobbyAsync(_lobbyId);
        Debug.Log(r.IsSuccess ? $"{Tag} 시작 알림. 이제 게임을 붙이면 됩니다." : $"{Tag} 시작 실패: {r.ErrorCode}");
    }

    /// <summary>8 — 2번에서 만든 로비 취소.</summary>
    private async Task CancelAsync()
    {
        if (string.IsNullOrEmpty(_lobbyId)) { Debug.Log($"{Tag} 먼저 2번으로 로비를 만드세요."); return; }

        var r = await Supabase.CancelMatchLobbyAsync(_lobbyId);
        Debug.Log(r.IsSuccess ? $"{Tag} 취소함." : $"{Tag} 취소 실패: {r.ErrorCode}");
    }

    /// <summary>9 — 2번에서 만든 로비 나가기. 호스트가 나가면 로비 전체가 취소됩니다.</summary>
    private async Task LeaveAsync()
    {
        if (string.IsNullOrEmpty(_lobbyId)) { Debug.Log($"{Tag} 먼저 2번으로 로비를 만드세요."); return; }

        var r = await Supabase.LeaveMatchLobbyAsync(_lobbyId);
        Debug.Log(r.IsSuccess ? $"{Tag} 나감." : $"{Tag} 나가기 실패: {r.ErrorCode}");
    }

    /// <summary>0 — 로비 참가자끼리 보는 대화에 한 줄 보냅니다.</summary>
    private async Task SendChatAsync()
    {
        if (string.IsNullOrEmpty(_lobbyId)) { Debug.Log($"{Tag} 먼저 2번으로 로비를 만드세요."); return; }

        var r = await Supabase.SendLobbyChatAsync(_lobbyId, chatMessage);
        if (!r.IsSuccess) { Debug.Log($"{Tag} 대화 실패: {MessageFor(r.Reason, r.ErrorCode)}"); return; }

        Debug.Log($"{Tag} 보냈습니다.");
    }

    /// <summary>- — 이 로비의 대화를 처음부터 다시 읽습니다.</summary>
    private async Task ShowChatAsync()
    {
        if (string.IsNullOrEmpty(_lobbyId)) { Debug.Log($"{Tag} 먼저 2번으로 로비를 만드세요."); return; }

        var r = await Supabase.GetLobbyChatAsync(_lobbyId);
        if (!r.IsSuccess) { Debug.Log($"{Tag} 대화 조회 실패: {MessageFor(r.Reason, r.ErrorCode)}"); return; }

        if (r.Data.Count == 0) { Debug.Log($"{Tag} 아직 대화가 없습니다."); return; }

        foreach (var m in r.Data)
            Debug.Log($"{Tag} {m.DisplayName}: {m.Content}");
    }

    /// <summary>실패 사유를 유저에게 보여줄 문구로 바꿉니다.</summary>
    private static string MessageFor(SupabaseReason reason, string errorCode) => reason switch
    {
        SupabaseReason.MatchGameCodeEmpty         => "게임 코드를 채우세요.",
        SupabaseReason.MatchLobbyInviteLimitReached => "상대가 받은 초대가 너무 많습니다. 나중에 다시 시도하세요.",
        SupabaseReason.MatchLobbyInviteTooFast    => "초대를 너무 빨리 보냈습니다. 잠시 뒤 다시 시도하세요.",
        SupabaseReason.MatchLobbyNotOpen          => "로비가 이미 끝났습니다.",
        SupabaseReason.MatchLobbyFull             => "로비 정원이 찼습니다.",
        SupabaseReason.MatchLobbyNameTooLong      => "방 이름은 40자까지 넣을 수 있습니다.",
        SupabaseReason.NotSignedIn                => "먼저 로그인하세요.",
        _                                         => $"실패했습니다: {errorCode}",
    };
}
