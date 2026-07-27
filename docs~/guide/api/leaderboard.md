# 리더보드 API

| 메서드 | 설명 |
|--------|------|
| [`GetLeaderboardTablesAsync`](/guide/leaderboard/tables) | 사용 가능한 리더보드 목록 |
| [`GetLeaderboardTableAsync`](/guide/leaderboard/table) | 리더보드 설정·회차 상태 |
| [`SubmitLeaderboardScoreAsync`](/guide/leaderboard/submit) | 점수 기록 |
| [`GetLeaderboardRangeAsync`](/guide/leaderboard/range) | 순위 범위 조회(최대 100건) |
| [`GetLeaderboardPlayerAsync`](/guide/leaderboard/player) | 플레이어 1명의 순위 |
| [`SetLeaderboardPlayerDataAsync`](/guide/leaderboard/set-player-data) | 본인 추가 데이터 수정 |
| [`DeleteMyLeaderboardScoreAsync`](/guide/leaderboard/delete-my-score) | 본인 기록 삭제 |

리더보드 생성·수정·삭제와 컬럼 관리는 어드민(Retool) 전용이라 SDK에 없습니다.

::: tip
순위에 함께 표시할 값은 [플레이어 데이터 필드](/guide/leaderboard/columns)로 주고받습니다.
:::
