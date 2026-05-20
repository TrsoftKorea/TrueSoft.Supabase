namespace Truesoft.Supabase.Core.Data
{
    /// <summary>
    /// 유저 데이터 필드의 저장 우선순위입니다.
    /// <see cref="DataColumnAttribute"/>의 <c>priority</c> 파라미터로 지정합니다.
    /// </summary>
    /// <remarks>
    /// 기본 쿨다운:
    /// <list type="table">
    /// <item><term>Urgent</term><description>1 s — 레벨, 튜토리얼 완료 등 중요 데이터</description></item>
    /// <item><term>Normal</term><description>5 s — 일반 진행 데이터</description></item>
    /// <item><term>Lazy</term>  <description>30 s — 골드 등 자주 변하고 중요도가 낮은 데이터</description></item>
    /// </list>
    /// dirty 필드가 여러 우선순위를 가지면 가장 높은(Urgent에 가까운) 쿨다운을 사용합니다.
    /// </remarks>
    public enum DataSavePriority
    {
        /// <summary>중요 데이터 — 빠른 저장 (기본 1 s).</summary>
        Urgent = 0,

        /// <summary>일반 데이터 — 중간 저장 주기 (기본 5 s). 기본값.</summary>
        Normal = 1,

        /// <summary>자주 변하고 중요도가 낮은 데이터 — 느린 저장 (기본 30 s).</summary>
        Lazy = 2,
    }
}
