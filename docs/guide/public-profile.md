# 공개 프로필 (Public Profile)

공개 프로필은 닉네임 설정·변경, 계정 탈퇴 등 플레이어 계정의 수명 주기를 관리하는 기능입니다.  
닉네임은 전체 고유이며, Edge Function을 통해 서버에서 검증됩니다.

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

