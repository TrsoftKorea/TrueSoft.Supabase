using System.Threading.Tasks;
using TrueBase.Core.Common;
using TrueBase.Core.Data;
using TrueBase.Unity;
using UnityEngine;

/// <summary>
/// 매치 로비(친구 초대) 예제 컴포넌트. SupabaseRuntime이 씬에 있어야 합니다.
///
/// 초대 대상은 반드시 내 친구여야 합니다 — 먼저 <see cref="SampleFriend"/>로 친구를 맺고,
/// 그 계정 ID를 Inspector의 <c>inviteAccountId</c>에 넣으세요.
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
///   6 — 방금 만든 로비에서 내 역할 태그 지정
///   7 — 방금 만든 로비 시작
///   8 — 방금 만든 로비 취소
///   9 — 방금 만든 로비 나가기
/// </summary>
public sealed class SampleMatchLobby : MonoBehaviour
{
    private const string Tag = "[Supabase.MatchLobby]";

    [Tooltip("매치 종류를 구분하는 코드. 실제 보상·연결과 무관한 예제용 값입니다.")]
    [SerializeField] private string gameCode = "arena_1v1";

    [Tooltip("초대할 친구의 계정 ID. 먼저 SampleFriend로 친구를 맺어야 합니다.")]
    [SerializeField] private string inviteAccountId = "";

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
        if (Input.GetKeyDown(KeyCode.Alpha6)) _ = SetMyRoleAsync();
        if (Input.GetKeyDown(KeyCode.Alpha7)) _ = StartAsync();
        if (Input.GetKeyDown(KeyCode.Alpha8)) _ = CancelAsync();
        if (Input.GetKeyDown(KeyCode.Alpha9)) _ = LeaveAsync();
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
        var r = await Supabase.CreateMatchLobbyAsync(gameCode, invited);
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
            Debug.Log($"{Tag}  {lobby.LobbyId} — {lobby.GameCode} / {lobby.Status}");
            foreach (var m in lobby.Members)
                Debug.Log($"{Tag}    - {m.Name}: {m.Status} (role={m.RoleTag ?? "없음"})");
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

    /// <summary>6 — 2번에서 만든 로비에서 내 역할 태그 지정. 값 자체는 서버가 해석하지 않습니다.</summary>
    private async Task SetMyRoleAsync()
    {
        if (string.IsNullOrEmpty(_lobbyId)) { Debug.Log($"{Tag} 먼저 2번으로 로비를 만드세요."); return; }

        var r = await Supabase.SetMatchLobbyMemberRoleAsync(_lobbyId, Supabase.UserId, "team_a");
        Debug.Log(r.IsSuccess ? $"{Tag} 역할 태그를 team_a로 지정함." : $"{Tag} 지정 실패: {r.ErrorCode}");
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

    /// <summary>실패 사유를 유저에게 보여줄 문구로 바꿉니다.</summary>
    private static string MessageFor(SupabaseReason reason, string errorCode) => reason switch
    {
        SupabaseReason.MatchGameCodeEmpty         => "게임 코드를 채우세요.",
        SupabaseReason.MatchLobbyInviteNotFriend  => "초대 대상이 친구가 아닙니다.",
        SupabaseReason.MatchLobbyNotOpen          => "로비가 이미 끝났습니다.",
        SupabaseReason.MatchLobbyFull             => "로비 정원이 찼습니다.",
        SupabaseReason.NotSignedIn                => "먼저 로그인하세요.",
        _                                         => $"실패했습니다: {errorCode}",
    };
}
