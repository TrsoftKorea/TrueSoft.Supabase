namespace TrueBase.Core.Data
{
    /// <summary>
    /// 유저 데이터 필드의 저장 우선순위입니다.
    /// <see cref="DataColumnAttribute"/>의 <c>priority</c> 파라미터로 지정합니다.
    /// </summary>
    /// <remarks>
    /// 기본 쿨다운 (<see cref="SupabaseSettings"/>에서 변경 가능):
    /// <list type="table">
    /// <item><term>Urgent (높음)</term><description>1 s — 레벨, 튜토리얼 완료 등 중요 데이터</description></item>
    /// <item><term>Normal (보통)</term><description>5 s — 일반 진행 데이터</description></item>
    /// <item><term>Lazy   (낮음)</term><description>30 s — 골드 등 자주 변하고 중요도가 낮은 데이터</description></item>
    /// </list>
    /// dirty 필드가 여러 우선순위를 가지면 가장 높은(Urgent에 가까운) 쿨다운을 사용합니다.
    /// </remarks>
    public enum DataSavePriority
    {
        /// <summary>높음 — 빠른 저장 (기본 1 s). 레벨·튜토리얼 완료 등 중요 데이터.</summary>
        Urgent = 0,

        /// <summary>보통 — 중간 저장 주기 (기본 5 s). 우선순위 미지정 시 기본값.</summary>
        Normal = 1,

        /// <summary>낮음 — 느린 저장 (기본 30 s). 골드 등 자주 변하고 중요도가 낮은 데이터.</summary>
        Lazy = 2,
    }
}
