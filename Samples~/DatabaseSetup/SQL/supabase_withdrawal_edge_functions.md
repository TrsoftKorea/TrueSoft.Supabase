# Supabase Edge Functions - 보안 강화 마이그레이션 가이드

> **주요 변경사항 (2025-04)**  
> `service_role` 키는 **레거시**이며 새 `SUPABASE_SECRET_KEY` (또는 `sb_secret_...`)로 교체했습니다.  
> 일부 기능은 `pg_cron`으로 대체되어 Edge Function이 제거되었습니다.

---

## 환경 변수 설정

### 이전 (.env)
```bash
SUPABASE_SERVICE_ROLE_KEY=eyJhbG...  # 레거시, 제거됨
SUPABASE_ANON_KEY=eyJhbG...
SUPABASE_URL=https://<project>.supabase.co
```

### 이후 (.env)
```bash
# 필수
SUPABASE_URL=https://<project>.supabase.co
SUPABASE_ANON_KEY=eyJhbG...

# 관리자 API용 (service_role 대체)
# 주의: SUPABASE_ 접두사는 예약어이므로 사용 불가
SECRET_KEY=sb_secret_...    # Dashboard → API → Secret Keys (이름: SECRET_KEY)

# 기능별 키
CANCEL_TOKEN_SECRET=your-random-secret-here
CANCEL_TOKEN_TTL_SECONDS=900

# 제거됨 (예약어 충돌로 삭제 불가, 무시)
# SUPABASE_SECRET_KEY  
```

---

## Secret Key 발급 방법

> ⚠️ **주의**: `SUPABASE_` 접두사는 예약어입니다. `SECRET_KEY` 등 다른 이름을 사용하세요.

1. **Supabase Dashboard** 접속
2. **Project Settings** → **API**
3. **Secret Keys** 섹션에서 **Generate new secret key**
4. 키 이름: `SECRET_KEY` (⚠️ `SUPABASE_SECRET_KEY`는 예약어 충돌)
5. 생성된 `sb_secret_...` 키를 복사하여 설정

---

## 함수별 변경사항 요약

| 함수 | 변경 내용 | 환경 변수 |
|------|----------|----------|
| `cleanup-expired-mails` | **삭제** → `pg_cron` 직접 RPC | 불필요 |
| `withdrawal-cleanup` | **삭제** → `pg_cron` 직접 RPC | 불필요 |
| `withdrawal-cancel-redeem` | 비로그인 취소 → 토큰 sub로 `SECRET_KEY` 호출 | `SECRET_KEY` |
| `withdrawal-cancel-issue` | `service_role` 제거 | `SUPABASE_ANON_KEY`만 |
| `withdrawal-guard` | `service_role` → `SECRET_KEY` | `SECRET_KEY` |
| `displayname-set` | `service_role` → `SECRET_KEY` | `SECRET_KEY` |

---

## SQL 설정 단계

### 1. pg_cron Extension 활성화

```sql
-- SQL Editor에서 실행
CREATE EXTENSION IF NOT EXISTS pg_cron;
```

### 2. 새 RPC 함수 생성

```bash
# SQL 파일 순서대로 실행
1. 08_withdrawal.sql      -- ts_withdrawal_cancel_redeem
2. 13_cron_jobs.sql      -- ts_withdrawal_cleanup_batch + cron 설정
```

또는 SQL Editor에서 각 파일 내용을 복사하여 실행합니다.

### 3. Cron Job 확인

```sql
-- 등록된 job 확인
SELECT * FROM cron.job;

-- 실행 로그 확인
SELECT * FROM cron.job_run_details 
WHERE jobname IN ('cleanup-expired-mails', 'withdrawal-cleanup')
ORDER BY start_time DESC
LIMIT 20;
```

---

## Edge Function 배포

### 1. 환경 변수 업데이트

```bash
# Supabase CLI
supabase secrets set SUPABASE_SECRET_KEY="sb_secret_..."

# (선택) 기존 service_role 제거
supabase secrets unset SUPABASE_SERVICE_ROLE_KEY
```

### 2. 함수 배포

```bash
# 사용 중인 함수만 배포
supabase functions deploy withdrawal-cancel-redeem
supabase functions deploy withdrawal-cancel-issue
supabase functions deploy withdrawal-guard
supabase functions deploy displayname-set

# Deprecated 함수는 배포하지 않거나 삭제
# supabase functions delete cleanup-expired-mails
# supabase functions delete withdrawal-cleanup
```

---

## 검증 체크리스트

### SQL RPC 테스트
```sql
-- 탈퇴 취소 RPC (Edge Function이 토큰 sub를 넘겨 service_role로 호출)
SELECT ts_withdrawal_cancel_redeem('00000000-0000-0000-0000-000000000000');

-- 탈퇴 정리 RPC (pg_cron 내부용)
SELECT ts_withdrawal_cleanup_batch(10);
```

### Edge Function 테스트
```bash
# withdrawal-cancel-redeem (JWT 필요)
curl -X POST https://<project>.supabase.co/functions/v1/withdrawal-cancel-redeem \
  -H "Authorization: Bearer <user-jwt>" \
  -H "Content-Type: application/json" \
  -d '{"cancel_token": "..."}'

# withdrawal-guard (JWT 필요, 내부에서 Secret Key 사용)
curl -X POST https://<project>.supabase.co/functions/v1/withdrawal-guard \
  -H "Authorization: Bearer <user-jwt>"
```

---

## 문제 해결

### "Secret Key not found" 오류
- Dashboard에서 Secret Key가 생성되었는지 확인
- `supabase secrets list`로 설정 확인

### "profile_not_found" / "withdrawal_not_scheduled" 오류 (ts_withdrawal_cancel_redeem)
- 취소는 비로그인 상태에서 토큰의 `sub`(account_id)로 처리됨 → JWT 불필요
- 토큰 발급 이후 이미 취소됐거나 예약이 만료되지 않았는지 확인
- `SUPABASE_SECRET_KEYS` 시크릿이 설정돼 있는지 확인(service_role 대체)

### Cron job 미실행
- `SELECT * FROM cron.job;`으로 등록 확인
- `SELECT * FROM cron.job_run_details;`로 오류 로그 확인

---

## 보안 체크리스트

- [ ] `SUPABASE_SERVICE_ROLE_KEY`가 모든 환경에서 제거됨
- [ ] `SECRET_KEY`가 서버/Edge Function에만 존재 (⚠️ `SUPABASE_SECRET_KEY`는 예약어)
- [ ] 클라이언트(Unity, Web)에는 `ANON_KEY`만 존재
- [ ] Secret Key가 Git 저장소에 커밋되지 않음
- [ ] `.env` 파일이 `.gitignore`에 포함됨
