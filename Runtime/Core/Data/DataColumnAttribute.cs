using System;

namespace TrueBase.Core.Data
{
    /// <summary>
    /// 데이터 테이블 컬럼과 C# 멤버를 묶습니다.
    /// <see cref="DataSchema"/>가 <c>select</c> CSV와 PATCH 키를 자동 생성할 때 사용합니다.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    public sealed class DataColumnAttribute : Attribute
    {
        /// <param name="columnName">
        /// Postgres/PostgREST 컬럼명(예: <c>gold</c>, <c>coin_count</c>).
        /// 비우면 멤버 이름을 그대로 씁니다.
        /// </param>
        /// <param name="priority">
        /// 저장 우선순위. 높을수록(Urgent) 더 빠른 주기로 서버에 전송됩니다.
        /// dirty 필드 중 가장 높은 우선순위의 쿨다운이 사용됩니다.
        /// </param>
        public DataColumnAttribute(string columnName = null,
            DataSavePriority priority = DataSavePriority.Normal)
        {
            ColumnName = string.IsNullOrWhiteSpace(columnName) ? null : columnName.Trim();
            Priority   = priority;
        }

        /// <summary>DB 컬럼명. null이면 대상 멤버의 이름을 사용합니다.</summary>
        public string ColumnName { get; }

        /// <summary>저장 우선순위. 기본값 <see cref="DataSavePriority.Normal"/>.</summary>
        public DataSavePriority Priority { get; }
    }
}
