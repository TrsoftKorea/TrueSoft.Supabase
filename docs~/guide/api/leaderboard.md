# 리더보드 API

| 메서드 | 설명 |
|--------|------|
| [`GetLeaderboardsAsync`](/guide/leaderboard/tables) | 사용 가능한 리더보드 목록 |
| [`GetLeaderboardAsync<T>`](/guide/leaderboard/table) | 리더보드 1건의 설정·회차 상태 |
| [`SubmitScoreAsync<T>`](/guide/leaderboard/submit) | 점수 기록 |
| [`SubmitScoreAsync`](/guide/leaderboard/submit-row) | 점수 기록 · 생성 클래스 |
| [`GetRanksAsync<T>`](/guide/leaderboard/range) | 순위 범위 조회(최대 100건) |
| [`GetRankAsync<T>`](/guide/leaderboard/player) | 플레이어 1명의 순위 |
| [`SetRowAsync`](/guide/leaderboard/set-player-data) | 본인 추가 데이터 수정 |
| [`DeleteMyScoreAsync<T>`](/guide/leaderboard/delete-my-score) | 본인 기록 삭제 |

리더보드 생성·수정·삭제와 컬럼 관리는 어드민(Retool) 전용이라 SDK에 없습니다.

::: tip
리더보드는 [생성한 클래스](/guide/leaderboard/columns#generate) 타입으로 지정합니다. 순위에 함께 표시할 값도 그 클래스의 `Row`로 주고받습니다.
:::
