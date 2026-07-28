using TrueBase.Core.Data;

/// <summary>
/// 리더보드 예제가 쓰는 리더보드 타입입니다. <b>클래스 생성기가 만드는 파일과 같은 형태</b>로,
/// 샘플이 바로 컴파일되도록 손으로 적어 둔 것입니다.
///
/// 실제 게임에서는 이렇게 직접 쓰지 말고 <c>TrueSoft > Supabase > 클래스 생성 > 리더보드</c>로
/// 생성하세요. Retool에 만든 리더보드 코드와 등록 필드를 그대로 읽어 만들어 줍니다.
///
/// 보시다시피 <b>메서드가 없습니다</b> — 속성과 필드 선언뿐입니다.
/// 조작은 전부 <c>Supabase</c> 파사드에서 합니다:
/// <code>
/// await Supabase.SubmitScoreAsync&lt;ArenaLeaderboard&gt;(1250);
/// await Supabase.GetRanksAsync&lt;ArenaLeaderboard&gt;(1, 10);
/// </code>
/// </summary>
[Leaderboard("arena")]
public sealed partial class ArenaLeaderboard : ILeaderboard
{
    /// <summary>
    /// 순위와 함께 주고받는 플레이어 데이터입니다.
    /// Retool 리더보드 &gt; 필드 탭에서 켠 필드만 넣을 수 있고, 켜지 않은 필드를 보내면 서버가 기록을 거부합니다.
    /// <para>
    /// 이 샘플은 <c>char_level</c> 하나만 씁니다. Retool에서 다른 이름으로 등록했다면 여기를 맞추거나,
    /// 필드를 등록하지 않았다면 이 중첩 클래스를 통째로 지우고 점수만 기록하세요.
    /// </para>
    /// </summary>
    public sealed partial class Row
    {
        [DataColumn("char_level")] public int CharLevel;
    }
}
