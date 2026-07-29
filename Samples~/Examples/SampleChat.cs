using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using TrueBase.Core.Common;
using TrueBase.Core.Data;
using TrueBase.Unity;
using UnityEngine;

/// <summary>
/// 채팅 예제 컴포넌트. SupabaseRuntime이 씬에 있어야 합니다 — 구독 폴링을 Update에서 돌립니다.
///
/// 채널은 운영(Retool)에서 만듭니다. 설치 시 기본으로 두 개가 들어 있습니다.
///   shout  — 외치기. 전체 공개, 100자
///   server — 서버 채팅. 같은 서버끼리, 200자
///
/// 화면 좌측 상단 디버그 패널에서 조회 간격·수신 현황·내 서버를 실시간으로 볼 수 있습니다.
/// 간격이 늘어나는지, 구독을 끊으면 조회가 멈추는지 여기서 바로 확인됩니다.
///
/// 샘플을 여러 개 함께 쓰면 단축키가 겹칩니다. 그때는 <b>Tab</b> 으로 키를 받을 샘플을 고르세요.
/// 씬에 샘플이 하나뿐이면 그냥 눌러도 됩니다.
///
/// 키보드 단축키 (Play Mode):
///   1 — 익명 로그인
///   2 — 채널 목록
///   3 — 구독 시작 (채팅창 열기)
///   4 — 구독 해제 (채팅창 닫기)
///   5 — 외치기로 발송
///   6 — 서버 채팅으로 발송
///   7 — 지난 대화 다시 불러오기
///   0 — 현재 상태를 콘솔에 출력
/// </summary>
public sealed class SampleChat : MonoBehaviour
{
    private const string Tag = "[Supabase.Chat]";

    [Tooltip("보낼 메시지. 뒤에 일련번호가 붙습니다.")]
    [SerializeField] private string message = "안녕하세요";

    [Tooltip("좌측 상단에 디버그 패널을 그립니다.")]
    [SerializeField] private bool showDebugPanel = true;

    private ChatSubscription _sub;
    private int _sent;

    // ─── 디버그 상태 ──────────────────────────────────────────────────────────
    private ServerInfo _server;
    private readonly Dictionary<string, int> _receivedByChannel = new Dictionary<string, int>();
    private float _lastReceivedTime = -1f;
    private string _lastReceivedText = "";
    private float _minIntervalSeen = float.MaxValue;
    private float _maxIntervalSeen = 0f;

    private void Update()
    {
        // 여러 샘플을 한 씬에 놓으면 단축키가 겹친다. Tab 으로 고른 대상만 키를 읽는다.
        if (!SampleFocus.IsActive(this)) return;

        if (Input.GetKeyDown(KeyCode.Alpha1)) _ = SignInAsync();
        if (Input.GetKeyDown(KeyCode.Alpha2)) _ = ShowChannelsAsync();
        if (Input.GetKeyDown(KeyCode.Alpha3)) OpenChat();
        if (Input.GetKeyDown(KeyCode.Alpha4)) CloseChat();
        if (Input.GetKeyDown(KeyCode.Alpha5)) _ = SendAsync("shout");
        if (Input.GetKeyDown(KeyCode.Alpha6)) _ = SendAsync("server");
        if (Input.GetKeyDown(KeyCode.Alpha7)) _sub?.Reload();
        if (Input.GetKeyDown(KeyCode.Alpha0)) Debug.Log($"{Tag} 상태\n{BuildStatusText()}");

        TrackInterval();
    }

    /// <summary>구독은 반드시 정리합니다. 안 하면 씬을 떠나도 폴링이 계속 돕니다.</summary>
    private void OnDestroy() => CloseChat();

    /// <summary>조회 간격이 실제로 늘었다 줄었다 하는지 관찰합니다.</summary>
    private void TrackInterval()
    {
        if (_sub == null || _sub.IsDisposed) return;

        var v = _sub.CurrentIntervalSeconds;
        if (v < _minIntervalSeen) _minIntervalSeen = v;
        if (v > _maxIntervalSeen) _maxIntervalSeen = v;
    }

    /// <summary>1 — 익명 로그인. 서버 채팅 범위 확인용으로 내 서버도 함께 읽어 둡니다.</summary>
    private async Task SignInAsync()
    {
        var ok = await Supabase.SignInAnonymouslyAsync();
        if (!ok) { Debug.LogWarning($"{Tag} 로그인 실패: {ok.ErrorCode}"); return; }

        Debug.Log($"{Tag} 로그인됨. UserId = {Supabase.UserId}");

        var s = await Supabase.GetServerInfoAsync();
        if (s.IsSuccess) _server = s.Data;
    }

    /// <summary>2 — 채널 목록. 한 번 받으면 SDK가 캐시하므로 매번 불러도 됩니다.</summary>
    private async Task ShowChannelsAsync()
    {
        var r = await Supabase.GetChatChannelsAsync();
        if (!r.IsSuccess) { Debug.LogWarning($"{Tag} 채널 조회 실패: {r.ErrorCode}"); return; }

        foreach (var ch in r.Data)
        {
            var slow = ch.SlowModeSeconds > 0 ? $"{ch.SlowModeSeconds}초 간격" : "간격 제한 없음";
            Debug.Log($"{Tag}  {ch.Code} — {ch.DisplayName} / {ch.Kind} / 최대 {ch.MaxLength}자 / {slow}");
        }
    }

    /// <summary>3 — 채팅창 열기. 두 채널을 함께 구독해도 조회는 한 번에 묶여 나갑니다.</summary>
    private void OpenChat()
    {
        if (_sub != null) { Debug.Log($"{Tag} 이미 구독 중입니다."); return; }

        var r = Supabase.SubscribeChat(new[] { "shout", "server" }, OnMessages);
        if (!r.IsSuccess) { Debug.LogWarning($"{Tag} 구독 실패: {r.ErrorCode}"); return; }

        _sub = r.Data;
        _minIntervalSeen = float.MaxValue;
        _maxIntervalSeen = 0f;
        Debug.Log($"{Tag} 구독 시작. 조용하면 조회 간격이 최대 10초까지 늘어납니다.");
    }

    /// <summary>4 — 채팅창 닫기. 이후로는 조회가 아예 나가지 않아야 합니다.</summary>
    private void CloseChat()
    {
        if (_sub == null) return;

        _sub.Dispose();
        _sub = null;
        Debug.Log($"{Tag} 구독 해제. 폴링이 멈춥니다. 이제 새 메시지를 보내도 도착하지 않아야 정상입니다.");
    }

    /// <summary>새 메시지 도착. 오래된 순으로 옵니다.</summary>
    private void OnMessages(string channelCode, IReadOnlyList<ChatMessage> messages)
    {
        _receivedByChannel.TryGetValue(channelCode, out var n);
        _receivedByChannel[channelCode] = n + messages.Count;
        _lastReceivedTime = Time.realtimeSinceStartup;

        foreach (var m in messages)
        {
            var body = m.Deleted ? "(삭제된 메시지)" : m.Content;
            var name = string.IsNullOrEmpty(m.DisplayName) ? "이름 없음" : m.DisplayName;
            _lastReceivedText = $"[{channelCode}] {name}: {body}";
            Debug.Log($"{Tag} {_lastReceivedText}");
        }
    }

    /// <summary>5·6 — 발송. 실패 사유는 Reason으로 분기해 안내합니다.</summary>
    private async Task SendAsync(string channelCode)
    {
        var text = $"{message} {++_sent}";
        var r = await Supabase.SendChatAsync(channelCode, text);
        if (r.IsSuccess)
        {
            // 내가 보낸 메시지도 구독 콜백으로 되돌아옵니다. 따로 화면에 넣을 필요가 없습니다.
            Debug.Log($"{Tag} 보냄: {text}");
            return;
        }

        Debug.Log($"{Tag} {MessageFor(r.Reason, r.ErrorCode)}");
    }

    // ─── 디버그 패널 ──────────────────────────────────────────────────────────

    private void OnGUI()
    {
        if (!showDebugPanel) return;

        const int w = 400;
        var h = 160 + _receivedByChannel.Count * 18;
        GUI.Box(new Rect(10, 10, w, h), "채팅 디버그");
        GUI.Label(new Rect(20, 32, w - 20, h - 40), BuildStatusText());
    }

    private string BuildStatusText()
    {
        var sb = new StringBuilder();

        sb.AppendLine(Supabase.IsLoggedIn
            ? $"로그인  {Supabase.UserId.Substring(0, Math.Min(8, Supabase.UserId.Length))}"
            : "로그인  안 됨  (1번)");

        sb.AppendLine(string.IsNullOrEmpty(_server.ServerCode)
            ? "내 서버  (모름)"
            : $"내 서버  {_server.ServerCode}");

        if (_sub == null || _sub.IsDisposed)
        {
            sb.AppendLine("구독  없음  (3번)  → 조회가 나가지 않음");
        }
        else
        {
            sb.AppendLine($"구독  {string.Join(", ", _sub.ChannelCodes)}");
            sb.AppendLine($"조회 간격  {_sub.CurrentIntervalSeconds:0.0}초");
            if (_maxIntervalSeen > 0f)
                sb.AppendLine($"간격 범위  {_minIntervalSeen:0.0} ~ {_maxIntervalSeen:0.0}초");
        }

        var total = 0;
        foreach (var pair in _receivedByChannel)
        {
            sb.AppendLine($"수신  {pair.Key}  {pair.Value}건");
            total += pair.Value;
        }
        if (total == 0)
            sb.AppendLine("수신  없음");

        sb.AppendLine(_lastReceivedTime < 0f
            ? "마지막 수신  없음"
            : $"마지막 수신  {Time.realtimeSinceStartup - _lastReceivedTime:0}초 전");

        if (!string.IsNullOrEmpty(_lastReceivedText))
            sb.AppendLine(_lastReceivedText);

        return sb.ToString();
    }

    /// <summary>실패 사유를 유저에게 보여줄 문구로 바꿉니다.</summary>
    private static string MessageFor(SupabaseReason reason, string errorCode) => reason switch
    {
        SupabaseReason.ChatMessageEmpty      => "보낼 내용을 입력하세요.",
        SupabaseReason.ChatMessageTooLong    => "글자 수를 넘었습니다.",
        SupabaseReason.ChatMuted             => "채팅이 제한된 상태입니다.",
        SupabaseReason.ChatTooFast           => "조금 뒤에 다시 보내세요.",
        SupabaseReason.ChatChannelInactive   => "지금은 사용할 수 없는 채널입니다.",
        SupabaseReason.ChatChannelNotFound   => "존재하지 않는 채널입니다.",
        SupabaseReason.ChatScopeUnavailable  => "서버가 정해지지 않아 서버 채팅을 쓸 수 없습니다.",
        SupabaseReason.NotSignedIn           => "먼저 로그인하세요.",
        _                                    => $"보내지 못했습니다: {errorCode}",
    };
}
