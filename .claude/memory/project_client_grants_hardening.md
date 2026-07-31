---
name: project_client_grants_hardening
description: anon·authenticated 테이블 권한 최소화(15_grants_hardening.sql). 테이블 추가 후 재실행 필수.
metadata: 
  node_type: memory
  type: project
  originSessionId: 6f192039-fca3-497f-bd1f-9da182660d0d
  modified: 2026-07-22T05:35:44.758Z
---

Supabase 기본 권한은 public 스키마의 **모든 테이블**에 `anon`·`authenticated`까지 ALL을 부여한다. 실제 차단은 RLS가 하지만 **TRUNCATE는 RLS를 우회**하므로 권한 자체를 제거해야 한다. 2026-07-22에 양 프로젝트(마왕 `wxivrmvtpufeczltward`·데슬 `owumqjyctqhuyailqutd`)를 정리하고 `Samples~/DatabaseSetup/SQL/player/15_grants_hardening.sql`로 캐노니컬화했다.

**목표 상태**(이 외에는 anon·authenticated 권한 없음):

| 테이블 | authenticated | anon |
|--------|---------------|------|
| game_servers · remote_config | SELECT | SELECT |
| game_items · mails · purchases · ts_protected_fields | SELECT | — |
| display_names · user_profiles | SELECT, INSERT, UPDATE | — |
| user_sessions · user_data | SELECT, INSERT, UPDATE, DELETE | — |

정책 0개라 클라이언트 권한을 아예 남기지 않는 테이블: `account_closures`, `anonymous_recovery_tokens`, `mail_batches`, `mail_categories`, `mail_schedules`, `user_ban_messages`, `user_data_logs`, `withdrawal_delete_queue`.

- **`alter default privileges in schema public revoke all on tables from anon, authenticated` 를 양 프로젝트에 적용**했다. 그래서 새 테이블은 권한 0으로 생성되고, 15번 파일을 **재실행할 필요가 없다**(사용자가 "매번 실행은 의도에 맞지 않다"고 지적 → 근본 차단으로 전환).
- **새 테이블에 클라이언트 접근이 필요하면 그 테이블의 SQL 파일에서 명시적으로 grant** 한다. RLS 정책만 만들고 grant를 빠뜨리면 PostgREST가 권한 오류를 낸다 — 이게 의도된 explicit-allow 방식.
- 쓰기 RLS 정책은 전부 `auth.uid()`를 요구해 anon은 통과 불가. 그래서 anon은 읽기 2개만 남겼다.
- `service_role` 권한은 건드리지 않았다(엣지 함수·RPC가 사용).
- 남은 예외: `supabase_admin` 소유 기본 권한 항목에는 여전히 anon·authenticated ALL이 있다. supabase_admin이 만든 테이블에만 적용돼 실사용 경로는 아니고, postgres 권한으로는 수정 불가.
## 함수 권한 (같은 날 함께 정리)

`alter default privileges ... revoke all on functions from anon, authenticated` 도 적용했다. 기존 함수는 전부 회수 후 SDK가 실제 호출하는 20개만 재부여 — **anon+auth 7개**(`auth_user_server_id`·`ts_default_server_id`·`ts_server_now`·`ts_is_display_name_available`·`ts_anon_recovery_*` 3개), **authenticated 전용 13개**.

**핵심 함정: `revoke ... from public` 만으로는 부족하다.** PUBLIC 유사롤만 회수되고 기본 권한이 anon·authenticated에 직접 부여한 EXECUTE는 남는다. 반드시 `from public, anon, authenticated`.

이 때문에 실제로 뚫려 있던 것들:
- `admin_add/drop/update_user_data_column` — SECURITY DEFINER·인증검사 없음. anon이 user_data 컬럼 추가·삭제 가능이었다.
- `ts_withdrawal_cleanup_batch`, `ts_run_due_mail_schedules`, `rls_auto_enable`
- **데슬에만**: `ts_protect_field`/`ts_unprotect_field` — SECURITY DEFINER로 CHECK 제약을 조작. 로그인 유저가 재화 최소값 보호를 스스로 해제할 수 있었다. 본문의 `auth.uid()`는 제약식 문자열이지 권한 검사가 아니다. 마왕은 이미 막혀 있어 프로젝트 간 불일치였다.

`auth_user_server_id`·`ts_default_server_id`는 **RLS 정책·컬럼 DEFAULT에서 평가되므로 회수하면 안 된다**(회수 시 로그인·프로필 생성이 깨진다).

[[feedback_projects_identical_structure]] · [[feedback_sql_apply]]
