# 데이터 스키마 (Data Schema)

## account_id vs user_id

> [!IMPORTANT]
> 게임 코드는 항상 `account_id`만 사용합니다. `user_id`는 운영·감사 툴 전용입니다.  
> 두 값을 혼동하면 RLS 정책이 적용되지 않거나 조회가 실패할 수 있습니다.

| 필드 | 의미 | 사용 주체 |
|------|------|----------|
| `account_id` | `auth.users.id`. 현재 로그인 세션 식별자. RLS 기준. | 게임 클라이언트 |
| `user_id` | 플레이어(사람) 단위 불변 ID. 재가입 후에도 동일하게 유지 가능. | 운영·감사 |

같은 사람의 히스토리를 묶는 것은 운영 툴에서 `user_id`로 처리합니다.

---

## 재가입 동작

| 경우 | `user_id` | 게임 데이터 행 |
|------|-----------|----------------|
| 동일 Google 재가입 | 같게 유지 가능 | 새 `account_id`로 새 행 INSERT |
| 다른 계정 | 다른 `user_id` | 새 행 INSERT |
| 탈퇴 후 | 행에 남음 | `account_id` → NULL, 게임 접근 불가 |

> [!NOTE]
> 탈퇴 후 재가입해도 이전 세이브·프로필은 자동으로 복구되지 않습니다.

---

## DataSchema 유틸리티

`DataSchema` 정적 클래스는 `[DataColumn]` 어노테이션 기반 reflection 헬퍼를 제공합니다.

| 메서드 | 설명 |
|--------|------|
| `GetSelectColumnsCsv<T>()` | PostgREST `select=` 파라미터용 CSV 생성 |
| `BuildPatch<T>(prev, curr)` | 두 스냅샷을 비교해 변경된 컬럼만 딕셔너리로 반환 |
| `CloneRow<T>(src)` | `[DataColumn]` 멤버만 복사한 새 인스턴스 반환 |
| `CopyInto<T>(dst, src)` | `[DataColumn]` 멤버를 `src`에서 `dst`로 복사 (ref 유지) |
| `ResolveTableName<T>()` | 유저 세이브 테이블명 `"user_data"` 반환 (고정값) |
| `UserDataTableName` | 상수 `"user_data"` |

`CloneRow` / `CopyInto`는 `StaticUserSave<TRow>` 내부에서 스냅샷 관리에 사용됩니다.

```csharp
var snapshot = DataSchema.CloneRow(current);          // 스냅샷 복사
DataSchema.CopyInto(current, newRow);                 // 기존 ref 유지하며 값만 덮어쓰기
var patch = DataSchema.BuildPatch(snapshot, current); // 변경분만 추출
```

---

## 플레이어 테이블 SQL 실행 순서

`Sql/player/` 폴더에서 번호 순으로 실행합니다.

| 파일 | 내용 |
|------|------|
| `01_servers.sql` | 게임 서버 목록·ts_default_server_id·ts_server_now |
| `02_profiles.sql` | 플레이어 프로필·표시 이름(닉네임)·세션 |
| `03_anonymous_recovery.sql` | 익명 계정 복구 |
| `04_user_data.sql` | 세이브 공통 인프라·user_data 테이블·필드 보호 |
| `05_account_management.sql` | 서버 이주·탈퇴 예약·취소·상태 조회 |
| `06_mails.sql` | 우편함 |
| `07_purchases.sql` | IAP 구매 검증 기록 |
| `08_remote_config.sql` | Remote Config |
| `09_cron_jobs.sql` | 크론 잡 |
| `99_verify.sql` | 스키마 검증 (선택) |

---

## 서버 이주 (server_id)

**유저 자가 이주:**
```csharp
await Supabase.TryTransferMyServerAsync("KR1");
```

**운영/Retool (Secret 키 전용):**
```
POST {SUPABASE_URL}/rest/v1/rpc/ts_admin_transfer_user_server
Header: apikey: <Secret 키>, Authorization: Bearer <Secret 키>
Body: {"p_account_id":"<uuid>","p_target_server_code":"KR1","p_reason":"support_ticket_123"}
```

이주 대상 서버의 `allow_transfers`가 false이거나 닉네임 중복이면 실패합니다.

---

## 법적 데이터 보관 설계

탈퇴 시 삭제할 데이터와 법령·분쟁 대응으로 보관할 데이터를 스키마 단위로 분리하는 것을 권장합니다.

| 구분 | 처리 |
|------|------|
| 운영 데이터 (`profiles`, `user_saves`, `chat_messages` 등) | 탈퇴 완료 시 삭제 또는 비식별화 |
| 법정 보존 데이터 (결제 요약, 감사 로그) | 별도 스키마(`compliance`)에 최소 필드만 보관, 서비스 롤만 접근 |

- 운영 테이블은 `auth.users`에 `ON DELETE CASCADE`로 묶어 계정 삭제 시 함께 제거합니다.
- 보관 테이블은 `auth.users`에 FK를 두지 않고 `user_id`만 보유해 계정 삭제 후에도 유지합니다.
- 보관 항목·기간·목적은 법무·회계와 확인 후 개인정보 처리방침에 기재하세요.
