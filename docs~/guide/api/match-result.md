# 매치 결과 API

| 메서드 | 설명 |
|--------|------|
| [`ReportMatchResultAsync`](/guide/match-result/report) | 본인이 겪은 2인 판 결과 신고 |
| [`ReportSoloMatchResultAsync`](/guide/match-result/report-solo) | 상대 없는 판 기록. 지급 없음 |
| [`GetMatchStatsAsync`](/guide/match-result/stats) | 내 승·패 수와 점수 최댓값·최솟값 |
| [`GetMatchHistoryAsync`](/guide/match-result/history) | 내 기록을 최근 순으로 |

매치 결과가 게임마다 갖는 보상 정책은 Retool 어드민에서 등록·수정합니다.

::: tip
보상은 응답이 아니라 [우편](/guide/mailbox/)으로 지급됩니다. 지급이 이루어지면 우편함을 새로 조회하세요.
:::
