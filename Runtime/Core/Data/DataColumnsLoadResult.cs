namespace Truesoft.Supabase.Core.Data
{
    /// <summary>
    /// 데이터 테이블 명시 컬럼 select 로드 결과. HTTP 성공 후 본인 행이 0건이면 <see cref="HasRow"/>는 <c>false</c>이고
    /// <see cref="Row"/>는 <c>new T()</c>입니다. 인증 실패·HTTP 오류·파싱 실패는 <see cref="Truesoft.Supabase.Core.Common.SupabaseResult{T}"/> 단계에서 실패로 처리됩니다.
    /// </summary>
    public sealed class DataColumnsLoadResult<T> where T : class, new()
    {
        public bool HasRow { get; }
        public T Row { get; }

        public DataColumnsLoadResult(bool hasRow, T row)
        {
            HasRow = hasRow;
            Row = row ?? new T();
        }
    }
}
