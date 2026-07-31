---
name: project_retool_pg_param_appearance_order
description: 트루베이스 Retool 앱의 Supabase pg 래퍼는 $N을 번호가 아니라 SQL 등장 순서로 바인딩한다
metadata: 
  node_type: memory
  type: project
  originSessionId: 74935e87-21ea-4a80-a168-080a9abeaccd
---

트루베이스 Retool React 앱(`b518a11a-5d80-...`)의 `getDb(target).query(sql, valuesArray)` 래퍼는 파라미터를 **`$N` 번호가 아니라 SQL 텍스트에 등장하는 순서**대로 `valuesArray`에 바인딩한다. 즉 placeholder 번호가 등장 순서와 어긋나면 값이 뒤바뀐다.

**증상:** `banUser.ts`의 `UPDATE auth.users SET banned_until = $2::timestamptz WHERE id = $1::uuid` + `[accountId, bannedUntil]` 에서, `$2`(banned_until)가 `$1`보다 **먼저 등장**해 배열[0]=accountId(UUID)가 timestamptz 자리로 들어가 `invalid input syntax for type timestamp with time zone: "<uuid>"` 에러. 번호상으론 맞아 보여서 재게시·가드로도 안 잡혔다(값은 각각 유효, 드라이버 단계에서 뒤바뀜).

**Why:** `getPlayers`·`getPurchases` 등 정상 함수는 placeholder가 전부 `$1,$2,$3…` = 등장 순서라 두 바인딩 방식 어느 쪽이든 문제가 없었음. `banUser`만 `$2`가 `$1`보다 앞서는 유일한 쿼리라 이것만 깨짐. Retool AI는 표준 $N 바인딩을 가정해 오진(“이미 고쳐짐”).

**How to apply:** 이 앱의 백엔드 함수에서 SQL을 쓸 때 **placeholder 번호를 반드시 등장 순서와 일치**시킨다($1=먼저 나오는 값). 그러면 표준/등장-순서 양쪽 모두 안전. 예: `SET banned_until = $1::timestamptz WHERE id = $2::uuid` + `[untilIso, accountId]`. 여러 컬럼을 UPDATE할 땐 SET 절 컬럼 순서대로 $1,$2… 부여. 관련: [[project_retool_project_switching_methodB]]
