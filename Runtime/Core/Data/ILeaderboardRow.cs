using System.Collections.Generic;

namespace TrueBase.Core.Data
{
    /// <summary>
    /// 리더보드별 플레이어 데이터 행. 클래스 생성기가 만든 행 타입이 구현합니다.
    /// <c>Supabase.SubmitLeaderboardScoreAsync(score, row)</c>처럼 행을 그대로 넘기면
    /// SDK가 <see cref="LeaderboardCode"/>와 <see cref="ToData"/>로 리더보드 코드·값 사전을 뽑아 씁니다.
    /// </summary>
    public interface ILeaderboardRow
    {
        /// <summary>이 행이 대응하는 리더보드 코드.</summary>
        string LeaderboardCode { get; }

        /// <summary>점수 기록·데이터 수정 시 넘길 컬럼명 → 값 사전.</summary>
        IReadOnlyDictionary<string, object> ToData();
    }
}
