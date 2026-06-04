# 공개 프로필 (Public Profile)

---

## 닉네임

```csharp
// 중복 확인 (현재 내 닉네임은 사용 가능으로 나옴)
bool available = await Supabase.TryIsDisplayNameAvailableAsync("Player123");

// 설정
await Supabase.TrySetMyDisplayNameAsync("Player123");

// 다른 사용자 닉네임 조회
string name = await Supabase.TryGetPublicDisplayNameAsync(userId);
```

닉네임은 클라이언트에서 최대 64자로 잘립니다.  
DB 유니크 인덱스는 `lower(trim(display_name))` 기준입니다.

## 프로필 조회

```csharp
var profile = await Supabase.TryGetPublicProfileAsync(userId);
// profile.display_name, profile.withdrawn_at 등
```

## 탈퇴 표시

```csharp
await Supabase.TryRequestMyWithdrawalAsync();   // 탈퇴 예약 (유예 기간 후 처리)
await Supabase.TryGetMyWithdrawalStatusAsync(); // 탈퇴 상태 조회
await Supabase.TryClearMyWithdrawalAsync();     // 탈퇴 예약 취소
```

탈퇴 예약 철회 토큰 방식:

```csharp
var token = await Supabase.TryRequestWithdrawalCancelTokenAsync();
// 이메일 등으로 토큰 전달 후
await Supabase.TryRedeemWithdrawalCancelAsync(token);
```

유예 기간은 `SupabaseSettings.withdrawalRequestDelayDays`에서 설정합니다.

## 서버 샤드 이주

```csharp
Supabase.SetCurrentServerCode("KR1");
await Supabase.TryTransferMyServerAsync("GLOBAL");
```

운영/Retool에서 특정 계정을 이주시킬 때는 RPC `ts_admin_transfer_user_server`를 Secret 키로 호출합니다.  
자세한 내용은 [DataSchema.md](DataSchema.md)를 참고하세요.

## SQL

- [`Sql/player/02_profiles.sql`](../Sql/player/02_profiles.sql) — user_profiles, display_names, user_sessions
- [`Sql/player/05_account_management.sql`](../Sql/player/05_account_management.sql) — 탈퇴 RPC, 서버 이주 RPC
