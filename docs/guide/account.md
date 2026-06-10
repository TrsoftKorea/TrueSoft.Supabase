# 계정

닉네임 설정·변경, 계정 탈퇴 등 플레이어 계정의 수명 주기를 관리하는 기능입니다.  
닉네임은 서버에서 중복 여부를 검증하므로 전체 고유합니다.

---

## 닉네임

플레이어가 처음 게임을 시작하면 닉네임이 없는 상태입니다. 닉네임 설정 화면에서 중복 확인 후 저장하는 흐름이 일반적입니다.

```csharp
// 내 닉네임 — 로그인 후 자동 캐시된 프로필에서 조회
string myName = Supabase.MyProfile.DisplayName;

// 중복 확인 — 설정 전에 호출. 현재 내 닉네임은 사용 가능으로 나옴
bool available = await Supabase.TryIsDisplayNameAvailableAsync("Player123");

// 설정
await Supabase.TrySetMyDisplayNameAsync("Player123");

// 다른 플레이어 닉네임 조회
// userId = 조회 대상의 ID (리더보드·매칭 결과 등에서 얻은 값)
string name = await Supabase.TryGetPublicDisplayNameAsync(userId, defaultValue: "");
```

닉네임은 최대 64자이며, 초과 시 클라이언트에서 자동으로 잘립니다.

---

## 프로필 조회

로그인이 완료되면 플레이어 정보가 자동으로 조회·캐시됩니다. 별도 API 호출 없이 바로 사용할 수 있습니다.  
사용 가능한 프로퍼티 목록은 [인증 > 로그인 후 사용 가능한 값](./auth.md#로그인-후-사용-가능한-값)을 참고하세요.

다른 플레이어의 프로필을 조회할 때는 별도 API를 사용합니다. 리더보드나 매칭 결과에서 얻은 상대방 ID를 전달합니다.

```csharp
var profile = await Supabase.TryGetPublicProfileAsync(userId);
```

---

## 탈퇴 처리

즉시 삭제하지 않고 일정 유예 기간 후에 처리됩니다. 유예 기간 동안 플레이어가 탈퇴를 취소할 수 있습니다.

```csharp
await Supabase.TryRequestMyWithdrawalAsync();   // 탈퇴 예약
await Supabase.TryGetMyWithdrawalStatusAsync(); // 예약 상태 및 남은 시간 조회
await Supabase.TryClearMyWithdrawalAsync();     // 예약 취소
```

유예 기간은 `SupabaseSettings.withdrawalRequestDelayDays`에서 설정합니다.

::: info
유예 기간이 만료된 계정은 로그인 시 자동으로 처리됩니다.  
[Edge Function 배포](./getting-started.md#edge-function-deploy)가 완료되어 있어야 합니다.
:::

### 탈퇴 취소 — 토큰 방식

유예 기간이 지나 이미 탈퇴가 완료된 경우, 토큰을 이용해 계정을 복구할 수 있습니다.  
서버에서 토큰을 발급받아 이메일 등으로 전달하고, 플레이어가 해당 토큰으로 취소를 완료하는 방식입니다.

```csharp
// 탈퇴 취소 토큰 발급 — 플레이어에게 전달
var token = await Supabase.TryRequestWithdrawalCancelTokenAsync();

// 플레이어가 토큰을 입력해 취소 완료
await Supabase.TryRedeemWithdrawalCancelAsync(token);
```

::: warning
[Edge Function 배포](./getting-started.md#edge-function-deploy)가 완료되어 있어야 합니다.
:::

---

## 서버 이주

플레이어를 다른 서버로 이동시킵니다. 서버별로 닉네임 고유성이 관리되므로, 이주 대상 서버에 같은 닉네임이 이미 존재하면 실패합니다.

```csharp
await Supabase.TryTransferMyServerAsync("GLOBAL", reason: null);
```
