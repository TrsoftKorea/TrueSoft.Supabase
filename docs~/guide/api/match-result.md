# 매치 결과 API

| 메서드 | 설명 |
|--------|------|
| [`ReportMatchResultAsync`](/guide/match-result/report) | 본인이 겪은 매치 결과 신고 |
| [`ReportBotMatchResultAsync`](/guide/match-result/report-bot) | 봇 상대 판 기록. 지급 없음 |
| [`GetMatchWinCountAsync`](/guide/match-result/win-count) | 내 승리 수를 검증 상태별로 조회 |

매치 결과가 게임마다 갖는 보상 정책은 Retool 어드민에서 등록·수정합니다.

::: tip
보상은 응답이 아니라 [우편](/guide/mailbox/)으로 지급됩니다. 지급이 이루어지면 우편함을 새로 조회하세요.
:::
