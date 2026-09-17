using System.Collections.Generic;
using System.Threading.Tasks;
using TrueBase.Core.Common;
using TrueBase.Core.Data;
using TrueBase.Unity;
using UnityEngine;

/// <summary>
/// 친구 예제 컴포넌트. SupabaseRuntime이 씬에 있어야 합니다.
///
/// 두 계정이 있어야 왕복(요청 → 수락)까지 볼 수 있습니다 — 에디터 두 개를 띄우거나,
/// 빌드한 클라이언트 하나와 에디터를 같이 쓰세요. 혼자서도 검색·목록·귓속말 조회까지는
/// 확인할 수 있습니다.
///
/// 샘플을 여러 개 함께 쓰면 단축키가 겹칩니다. 그때는 <b>Tab</b> 으로 키를 받을 샘플을 고르세요.
/// 씬에 샘플이 하나뿐이면 그냥 눌러도 됩니다.
///
/// 키보드 단축키 (Play Mode):
///   1 — 익명 로그인
///   2 — 닉네임 검색
///   3 — 검색 결과에 친구 요청 보내기
///   4 — 받은 요청 목록
///   5 — 받은 요청 중 첫 번째 수락
///   6 — 친구 목록
///   7 — 친구 목록 중 첫 번째에게 귓속말 보내기
///   8 — 그 친구와의 대화 조회
/// </summary>
public sealed class SampleFriend : MonoBehaviour
{
    private const string Tag = "[Supabase.Friend]";

    [Tooltip("검색할 닉네임. 정확히 일치해야 합니다.")]
    [SerializeField] private string targetNickname = "친구닉네임";

    [Tooltip("귓속말로 보낼 내용.")]
    [SerializeField] private string message = "안녕!";

    private string _lastSearchedAccountId;
    private IReadOnlyList<FriendSummary> _friends = new List<FriendSummary>();
    private long _lastDirectMessageId;

    private void Update()
    {
        // 여러 샘플을 한 씬에 놓으면 단축키가 겹친다. Tab 으로 고른 대상만 키를 읽는다.
        if (!SampleFocus.IsActive(this)) return;

        if (Input.GetKeyDown(KeyCode.Alpha1)) _ = SignInAsync();
        if (Input.GetKeyDown(KeyCode.Alpha2)) _ = SearchAsync();
        if (Input.GetKeyDown(KeyCode.Alpha3)) _ = SendRequestAsync();
        if (Input.GetKeyDown(KeyCode.Alpha4)) _ = ShowIncomingRequestsAsync();
        if (Input.GetKeyDown(KeyCode.Alpha5)) _ = AcceptFirstRequestAsync();
        if (Input.GetKeyDown(KeyCode.Alpha6)) _ = ShowFriendsAsync();
        if (Input.GetKeyDown(KeyCode.Alpha7)) _ = SendDirectMessageAsync();
        if (Input.GetKeyDown(KeyCode.Alpha8)) _ = ShowDirectMessagesAsync();
    }

    /// <summary>1 — 익명 로그인.</summary>
    private async Task SignInAsync()
    {
        var ok = await Supabase.SignInAnonymouslyAsync();
        if (!ok) { Debug.LogWarning($"{Tag} 로그인 실패: {ok.ErrorCode}"); return; }

        Debug.Log($"{Tag} 로그인됨. UserId = {Supabase.UserId}");
    }

    /// <summary>2 — 닉네임 검색. 결과의 계정 ID를 3·7·8에서 재사용합니다.</summary>
    private async Task SearchAsync()
    {
        var r = await Supabase.SearchFriendAsync(targetNickname);
        if (!r.IsSuccess) { Debug.LogWarning($"{Tag} 검색 실패: {MessageFor(r.Reason, r.ErrorCode)}"); return; }

        _lastSearchedAccountId = r.Data.AccountId;
        Debug.Log($"{Tag} 찾음: {r.Data.Name} ({r.Data.AccountId}) — 관계: {r.Data.Relation}");
    }

    /// <summary>3 — 직전 검색 결과에 친구 요청. 먼저 2번으로 검색해야 합니다.</summary>
    private async Task SendRequestAsync()
    {
        if (string.IsNullOrEmpty(_lastSearchedAccountId)) { Debug.Log($"{Tag} 먼저 2번으로 검색하세요."); return; }

        var r = await Supabase.SendFriendRequestAsync(_lastSearchedAccountId);
        if (!r.IsSuccess) { Debug.LogWarning($"{Tag} 요청 실패: {MessageFor(r.Reason, r.ErrorCode)}"); return; }

        Debug.Log(r.Data.Accepted
            ? $"{Tag} 상대가 이미 요청을 보내놔서 바로 친구가 되었습니다."
            : $"{Tag} 요청을 보냈습니다. 상대가 수락하면 친구가 됩니다.");
    }

    /// <summary>4 — 내가 받은 대기 중 요청 목록.</summary>
    private async Task ShowIncomingRequestsAsync()
    {
        var r = await Supabase.GetFriendRequestsAsync(FriendRequestDirection.Incoming);
        if (!r.IsSuccess) { Debug.LogWarning($"{Tag} 조회 실패: {r.ErrorCode}"); return; }

        Debug.Log($"{Tag} 받은 요청 {r.Data.Count}건");
        foreach (var req in r.Data)
            Debug.Log($"{Tag}  {req.Name} ({req.RequestId})");
    }

    /// <summary>5 — 받은 요청 중 첫 번째를 수락. 먼저 4번으로 목록을 확인하세요.</summary>
    private async Task AcceptFirstRequestAsync()
    {
        var list = await Supabase.GetFriendRequestsAsync(FriendRequestDirection.Incoming);
        if (!list.IsSuccess || list.Data.Count == 0) { Debug.Log($"{Tag} 받은 요청이 없습니다."); return; }

        var first = list.Data[0];
        var r = await Supabase.RespondFriendRequestAsync(first.RequestId, accept: true);
        Debug.Log(r.IsSuccess
            ? $"{Tag} {first.Name}과 친구가 되었습니다."
            : $"{Tag} 수락 실패: {r.ErrorCode}");
    }

    /// <summary>6 — 친구 목록. 결과를 캐시해 7·8에서 첫 번째 친구를 재사용합니다.</summary>
    private async Task ShowFriendsAsync()
    {
        var r = await Supabase.GetFriendsAsync();
        if (!r.IsSuccess) { Debug.LogWarning($"{Tag} 조회 실패: {r.ErrorCode}"); return; }

        _friends = r.Data;
        Debug.Log($"{Tag} 친구 {_friends.Count}명");
        foreach (var f in _friends)
            Debug.Log($"{Tag}  {f.Name} ({f.AccountId})");
    }

    /// <summary>7 — 친구 목록 중 첫 번째에게 귓속말. 먼저 6번으로 목록을 받으세요.</summary>
    private async Task SendDirectMessageAsync()
    {
        if (_friends.Count == 0) { Debug.Log($"{Tag} 먼저 6번으로 친구 목록을 받으세요."); return; }

        var target = _friends[0];
        var r = await Supabase.SendDirectChatAsync(target.AccountId, message);
        Debug.Log(r.IsSuccess
            ? $"{Tag} {target.Name}에게 보냄: {message}"
            : $"{Tag} {MessageFor(r.Reason, r.ErrorCode)}");
    }

    /// <summary>8 — 7번에서 보낸 상대와의 대화 조회.</summary>
    private async Task ShowDirectMessagesAsync()
    {
        if (_friends.Count == 0) { Debug.Log($"{Tag} 먼저 6번으로 친구 목록을 받으세요."); return; }

        var target = _friends[0];
        var r = await Supabase.GetDirectChatAsync(target.AccountId, _lastDirectMessageId);
        if (!r.IsSuccess) { Debug.LogWarning($"{Tag} 조회 실패: {r.ErrorCode}"); return; }

        foreach (var m in r.Data)
        {
            Debug.Log($"{Tag} [{target.Name}] {m.DisplayName}: {(m.Deleted ? "(삭제된 메시지)" : m.Content)}");
            _lastDirectMessageId = m.Id;
        }
    }

    /// <summary>실패 사유를 유저에게 보여줄 문구로 바꿉니다.</summary>
    private static string MessageFor(SupabaseReason reason, string errorCode) => reason switch
    {
        SupabaseReason.FriendNicknameNotFound     => "없는 닉네임입니다.",
        SupabaseReason.FriendSelfRequest          => "본인입니다.",
        SupabaseReason.FriendAlreadyFriends       => "이미 친구입니다.",
        SupabaseReason.FriendRequestAlreadySent   => "이미 요청을 보냈습니다.",
        SupabaseReason.FriendNotFound             => "친구가 아닙니다.",
        SupabaseReason.ChatMessageTooLong         => "글자 수를 넘었습니다.",
        SupabaseReason.ChatMuted                  => "채팅이 제한된 상태입니다.",
        SupabaseReason.NotSignedIn                => "먼저 로그인하세요.",
        _                                         => $"실패했습니다: {errorCode}",
    };
}
