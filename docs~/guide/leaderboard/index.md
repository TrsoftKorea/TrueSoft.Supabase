# 리더보드

플레이어 점수를 모아 순위를 매기는 랭킹 시스템입니다. 리더보드 자체는 어드민(Retool)에서 만들고, 게임은 조회와 기록만 합니다.

## 리더보드 성격 정하기 {#kinds}

리더보드는 **초기화 주기**와 **종료 시각**이라는 두 개의 독립된 축으로 성격이 정해집니다.

| 초기화 주기 | 종료 시각 | 성격 |
|-------------|-----------|------|
| 없음 | 없음 | 영구 누적 랭킹 |
| 없음 | 있음 | 시즌 랭킹. 종료되면 순위가 고정됨 |
| 일간·주간·월간 | 없음 | 주기 랭킹이 계속 반복 |
| 일간·주간·월간 | 있음 | 이벤트 기간 동안만 도는 주기 랭킹 |

주기가 돌면 **회차**가 올라가고 순위가 새로 시작됩니다. 지난 회차 기록은 사라지지 않으므로 언제든 다시 조회할 수 있습니다.

## 점수 기록 방식 {#record-type}

같은 플레이어가 여러 번 기록할 때 어떻게 반영할지 리더보드마다 정합니다.

| 방식 | 동작 |
|------|------|
| 최고 | 더 높은 점수일 때만 갱신 |
| 최저 | 더 낮은 점수일 때만 갱신. 기록 시간처럼 작을수록 좋은 값에 사용 |
| 최신 | 제출할 때마다 덮어쓰기 |
| 누적 | 제출값을 계속 더하기 |

정렬 방향은 기록 방식과 **독립**입니다. "최저 기록을 남기고 낮은 순으로 정렬" 같은 조합이 가능합니다.

::: info 동점 처리
점수가 같으면 **그 점수에 먼저 도달한 플레이어가 위**입니다. 고정 규칙이라 설정할 수 없습니다. 같은 점수를 여러 번 제출해도 순위가 밀리지 않습니다.
:::

## 서버 분리 {#scope}

리더보드마다 전체 통합과 서버별 중 하나를 고릅니다. 서버별이면 현재 접속 중인 서버의 플레이어끼리만 순위를 겨룹니다. 서버를 옮겨도 기존 회차 기록은 이전 서버 순위에 그대로 남고, 새 기록부터 새 서버에 반영됩니다.

## 메서드

| 메서드 | 설명 |
|--------|------|
| [`GetLeaderboardTablesAsync`](/guide/leaderboard/tables) | 사용 가능한 리더보드 목록 |
| [`GetLeaderboardTableAsync`](/guide/leaderboard/table) | 리더보드 1건의 설정·회차 상태 |
| [`SubmitLeaderboardScoreAsync`](/guide/leaderboard/submit) | 점수 기록 |
| [`GetLeaderboardRangeAsync`](/guide/leaderboard/range) | 순위 범위 조회 |
| [`GetLeaderboardPlayerAsync`](/guide/leaderboard/player) | 플레이어 1명의 순위 |
| [`SetLeaderboardPlayerDataAsync`](/guide/leaderboard/set-player-data) | 본인 추가 데이터 수정 |
| [`DeleteMyLeaderboardScoreAsync`](/guide/leaderboard/delete-my-score) | 본인 기록 삭제 |

플레이어 데이터 필드를 쓰려면 [플레이어 데이터 필드](/guide/leaderboard/columns)를 먼저 읽으세요.

::: warning 점수는 클라이언트가 제출합니다
게임이 자기 점수를 직접 서버에 올리는 구조입니다. 변조된 점수를 막으려면 게임 서버나 Edge Function에서 검증한 뒤 기록하도록 별도 설계가 필요합니다.
:::
