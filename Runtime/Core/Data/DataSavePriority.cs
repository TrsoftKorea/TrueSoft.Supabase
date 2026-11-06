namespace TrueBase.Core.Data
{
    /// <summary>
    /// 유저 데이터 필드의 저장 우선순위입니다.
    /// <see cref="DataColumnAttribute"/>의 <c>priority</c> 파라미터로 지정합니다.
    /// </summary>
    /// <remarks>
    /// 기본 쿨다운 (<see cref="SupabaseSettings"/>에서 변경 가능):
    /// <list type="table">
    /// <item><term>Urgent (높음)</term><description>1 s — 어디서 바뀌든 빨리 저장해야 하고 드물게 바뀌는 데이터</description></item>
    /// <item><term>Normal (보통)</term><description>5 s — 대부분의 데이터</description></item>
    /// <item><term>Lazy   (낮음)</term><description>30 s — 방치 보상처럼 자주 바뀌는 데이터</description></item>
    /// </list>
    /// 우선순위는 저장을 시작하는 시점만 정합니다. 전송에는 그때까지 바뀐 칸이 우선순위와 무관하게 모두 실립니다.
    /// dirty 필드가 여러 우선순위를 가지면 가장 높은(Urgent에 가까운) 쿨다운을 사용합니다.
    /// 잃으면 안 되는 행동은 우선순위를 올리지 말고 그 자리에서 즉시 저장합니다.
    /// </remarks>
    public enum DataSavePriority
    {
        /// <summary>높음 — 빠른 저장 (기본 1 s). 어디서 바뀌든 빨리 저장해야 하고 드물게 바뀌는 데이터.</summary>
        Urgent = 0,

        /// <summary>보통 — 중간 저장 주기 (기본 5 s). 우선순위 미지정 시 기본값.</summary>
        Normal = 1,

        /// <summary>낮음 — 느린 저장 (기본 30 s). 방치 보상처럼 자주 바뀌는 데이터.</summary>
        Lazy = 2,
    }
}
