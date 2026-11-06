---
name: log-usage
description: Supabase 로그 조회(Log Query) 사용량이 어디서 나왔는지 날짜별로 대조한다. "로그 사용량", "로그 폭증", "Log Query" 를 물으면 쓴다.
---

# 로그 조회 사용량 대조

Supabase는 로그 조회를 **프로젝트별·날짜별 합계로만** 보여 준다. 누가, 어느 화면에서, 몇 번 읽었는지는 어디에도 안 남는다. 그래서 원인은 합계와 활동 기록을 **날짜로 맞대서** 좁힌다. 단정하지 말고 무엇을 대조했는지 함께 말한다.

## 순서

1. **값 읽기:** 크롬에서 프로젝트마다 사용량 화면(`/dashboard/org/ayfrenpjekvnlxcyeazo/usage?projectRef=<ref>`)을 열고 `read-chart.js` 내용을 `javascript_tool` 에 그대로 넣는다. 화면에 표시되는 글자만 읽고 로그인 토큰은 건드리지 않는다. 화면 캡처는 자주 멈추니 쓰지 않는다.
2. **기록과 대조:** 돌려받은 JSON 을 `node .claude/skills/log-usage/report.mjs record '<JSON>'` 로 장부(`ledger.jsonl`, 깃 제외)에 쌓고, `node .claude/skills/log-usage/report.mjs 2026-10-09` 로 프로젝트별·날짜별 값과 Claude 호출을 함께 본다. 다 끝나면 탭을 닫는다.
3. **콘솔 호출:** 두 프로젝트에 같은 SQL을 돌린다.
   ```sql
   select to_char(hour at time zone 'Asia/Seoul','MM-DD') kst_day, sum(calls) calls, max(calls) peak_hour
   from usage_hourly where kind='function' and target='admin-api' group by 1 order by 1;
   ```
4. **로그를 읽는 코드:** 저장소가 아니라 **배포된 것**을 본다. DB 함수·크론은 `pg_proc`·`cron.job` 글자 검색, 서버 함수는 `npx supabase functions download <이름> --project-ref <ref> --use-api --workdir <임시 폴더>` 로 받아 `analytics/endpoints|logs\.all|api\.supabase\.com` 을 찾는다. 대조군(`schema.getDraft`)도 같이 찾아 검색이 살아 있는지 본다.
5. **대시보드 접속:** 계정 활동 기록(`/dashboard/account/audit`). 로그 조회 자체는 안 남고 접속 흔적만 남는다.

## 행동 하나의 사용량 재기

사용량 화면은 1시간마다 갱신된다. 값을 기록한다 → 행동을 하나만 한다 → 2시간쯤 뒤 다시 기록한다. 보고서의 "지난 기록 뒤 늘어난 양"이 그 행동의 몫이다. 같은 시간에 다른 조회가 없었는지 함께 본다. 재는 것 자체도 사용량을 쓰니 사용자가 원할 때만 한다. 실시간 보기를 잴 때는 앱이 꺼져도 크롬에서 계속 도니, 켜기 전에 정해진 시간 뒤 `about:blank` 로 옮기는 타이머부터 건다.

## 함정

- 날짜별 합이 화면 합계보다 적다(2026-11-06: DevilSlayer 12.859GB, ProjectR 1.587GB). 어느 날 몫인지는 그래프에 없고 이유도 모른다 — 보고서가 "그래프에 없는 몫"으로 따로 보인다.
- 값을 못 읽으면 `record` 가 거절한다. 조직 화면(`projectRef` 없음)의 값은 대조에만 쓴다.

- 조회 도구(`query_logs`)를 조사에 쓰면 그 자체가 사용량을 늘린다. 1시간 범위로만, 꼭 필요할 때만.
- 사용량 값은 조회마다 알 수 없다(응답에 안 실린다). 한 번의 크기는 기록 전후 차이로만 어림한다.
- 날짜 기준(UTC·한국)은 화면에 표시가 없다.
- 그래프 가로축에는 사용량이 있는 날만 나온다. 축에 없는 날은 0이고, 이것이 가장 좋은 대조 자료다 — 활동이 있었는데 0인 날이면 그 활동은 원인이 아니다(콘솔 호출은 이렇게 제외했다, 11/3·11/4).
- 화면의 내부 통로를 로그인 토큰으로 직접 부르는 것은 브라우저 권한 검사가 막는다. 우회하지 않는다.
