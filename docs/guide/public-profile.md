# 공개 프로필 (Public Profile)

공개 프로필은 닉네임 설정·변경, 계정 탈퇴 등 플레이어 계정의 수명 주기를 관리하는 기능입니다.  
닉네임은 전체 고유이며, Edge Function을 통해 서버에서 검증됩니다.

---

## Edge Function 배포

닉네임·탈퇴 취소 기능은 Edge Function이 배포되어 있어야 동작합니다.  
소스 위치: **Database Setup** 샘플 > `EdgeFunctions/`

### 기능별 필요 함수

| 기능 | Edge Function |
|------|--------------|
| 닉네임 설정 | `displayname-set` |
| 닉네임 조회 | `displayname-get` |
| 탈퇴 취소 토큰 발급 | `withdrawal-cancel-issue` |
| 탈퇴 취소 토큰 사용 | `withdrawal-cancel-redeem` |
| 로그인 시 탈퇴 계정 자동 처리 | `withdrawal-guard` |

### 배포 순서

아래 과정을 각 함수마다 반복합니다.

1. Supabase 대시보드 > **Edge Functions** > **Deploy a new function** 클릭
2. 함수 이름을 정확히 입력하고 생성
3. **Database Setup** 샘플을 임포트한 후 `EdgeFunctions/<함수명>/index.ts` 파일을 열어 전체 내용 복사
4. 에디터에 붙여넣고 **Deploy** 클릭

배포할 함수 목록:

| 함수 이름 |
|-----------|
| `displayname-get` |
| `displayname-set` |
| `withdrawal-cancel-issue` |
| `withdrawal-cancel-redeem` |
| `withdrawal-guard` |

### Secrets 설정

대시보드 **Edge Functions > Secrets**에 등록합니다.

| 시크릿 키 | 값 형식 | 필요 함수 |
|----------|---------|----------|
| `SUPABASE_PUBLISHABLE_KEYS` | `{"default":"<Publishable Key>"}` | 전체 |
| `SUPABASE_SECRET_KEYS` | `{"default":"<Secret Key>"}` | `displayname-set`, `withdrawal-guard` |
| `CANCEL_TOKEN_SECRET` | 랜덤 문자열 32자 이상 | `withdrawal-cancel-issue`, `withdrawal-cancel-redeem` |

> [!TIP]
> `CANCEL_TOKEN_SECRET`은 `openssl rand -base64 32`로 생성합니다.  
> `withdrawal-cancel-issue`와 `withdrawal-cancel-redeem` 양쪽에 **동일한 값**을 설정해야 합니다.

> [!WARNING]
> `SUPABASE_SECRET_KEYS`의 Secret Key는 절대 클라이언트에 노출하지 마세요.

```bash
supabase secrets set SUPABASE_PUBLISHABLE_KEYS='{"default":"sb_publishable_..."}'
supabase secrets set SUPABASE_SECRET_KEYS='{"default":"sb_secret_..."}'
supabase secrets set CANCEL_TOKEN_SECRET="your-random-secret-here"
```

---

## 닉네임

```csharp
// 중복 확인 (현재 내 닉네임은 사용 가능으로 나옴)
bool available = await Supabase.TryIsDisplayNameAvailableAsync("Player123");

// 설정
await Supabase.TrySetMyDisplayNameAsync("Player123");

// 다른 사용자 닉네임 조회
// userId = 조회 대상의 auth.users.id (리더보드·매칭 결과 등에서 얻은 값)
string name = await Supabase.TryGetPublicDisplayNameAsync(userId);

// 내 ID 확인
string myId = Supabase.Session?.User?.Id;
```

닉네임은 클라이언트에서 최대 64자로 잘립니다.

---

## 프로필 조회

```csharp
// userId = 조회 대상의 auth.users.id
var profile = await Supabase.TryGetPublicProfileAsync(userId);
// profile.DisplayName, profile.IsWithdrawn 등
```

---

## 탈퇴 처리

```csharp
await Supabase.TryRequestMyWithdrawalAsync();   // 탈퇴 예약 (유예 기간 후 처리)
await Supabase.TryGetMyWithdrawalStatusAsync(); // 탈퇴 상태 조회
await Supabase.TryClearMyWithdrawalAsync();     // 탈퇴 예약 취소
```

유예 기간은 `SupabaseSettings.withdrawalRequestDelayDays`에서 설정합니다.

> [!NOTE]
> 로그인 시 탈퇴 유예 기간이 만료된 계정은 `withdrawal-guard` Edge Function이 자동으로 처리합니다.  
> 이 함수가 배포되지 않으면 탈퇴 완료 계정이 다시 로그인될 수 있습니다.

### 탈퇴 취소 (토큰 방식)

```csharp
var token = await Supabase.TryRequestWithdrawalCancelTokenAsync();
// 이메일 등으로 토큰 전달 후
await Supabase.TryRedeemWithdrawalCancelAsync(token);
```

> [!IMPORTANT]
> `TryRequestWithdrawalCancelTokenAsync`·`TryRedeemWithdrawalCancelAsync`는  
> `withdrawal-cancel-issue` / `withdrawal-cancel-redeem` Edge Function이 필요합니다.

---

## 서버 샤드 이주

```csharp
Supabase.SetCurrentServerCode("KR1");
await Supabase.TryTransferMyServerAsync("GLOBAL");
```

운영/Retool에서 특정 계정을 이주시킬 때는 RPC `ts_admin_transfer_user_server`를 Secret 키로 호출합니다.  
요청 형식은 [데이터 스키마 > 서버 이주](./data-schema.md#서버-이주-server_id)를 참고하세요.

---

## SQL

- `SQL/player/02_profiles.sql` — profiles 테이블 · 닉네임 유니크 인덱스
- `SQL/player/05_account_management.sql` — 탈퇴 이력 · 취소 RPC

Database Setup 샘플을 임포트한 후 위 파일을 실행합니다.
