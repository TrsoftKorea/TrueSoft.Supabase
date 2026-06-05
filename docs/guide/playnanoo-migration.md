# PlayNanoo 이관

PlayNanoo 기반 라이브 서비스를 SDK로 전환할 때 사용하는 브릿지 패턴입니다.  
두 백엔드를 동시에 운영하다가 PlayNanoo를 완전히 제거하는 흐름을 지원합니다.

---

## 동작 방식

`PlayNanooRuntime`는 `SupabaseRuntime`을 상속하며, `Awake` 시점에 `SupabaseSDK` 내부에 인터셉터를 등록합니다.  
이후 게임 코드가 `Supabase.TrySignInAnonymouslyAsync()` 등을 호출하면 **PlayNanoo 로그인이 먼저 실행된 뒤 SDK 로그인이 이어집니다.**

브릿지가 없으면 인터셉터도 없으므로, **게임 코드는 이관 전·중·후 동일합니다.**

| 로그인 | 이관 중 | PlayNanoo 제거 후 |
|--------|---------|-----------------|
| 익명 | `await Supabase.TrySignInAnonymouslyAsync()` | 변경 없음 |
| Google | `await Supabase.TrySignInWithGoogleAsync()` | 변경 없음 |
| Apple | `await Supabase.TrySignInWithAppleIdTokenAsync(token)` | 변경 없음 |
| 로그아웃 | `await Supabase.TrySignOutFullyAsync()` | 변경 없음 |
| 탈퇴 | `await Supabase.TryRequestMyWithdrawalAsync()` | 변경 없음 |

::: info
Google 로그인은 `Supabase.TrySignInWithGoogleAsync()`가 SDK 내부에서 토큰을 받아 `TrySignInWithGoogleIdTokenAsync(token)`을 호출하므로, 해당 단계에서 인터셉터가 자동으로 동작합니다. 별도 브릿지 메서드가 필요 없습니다.
:::

---

## 준비

1. Package Manager **Samples** 탭에서 **PlayNanoo 이관**을 Import합니다.
2. 씬에서 `SupabaseRuntime` 대신 `PlayNanooRuntime` 컴포넌트를 배치합니다.
3. Inspector에서 **Nanoo Storage Key**를 PlayNanoo 콘솔에 등록한 키로 변경합니다.

`StaticUserSave<TRow>` 인스턴스는 SDK가 자동으로 연결합니다. 별도 서브클래스 파일이 필요 없습니다.

---

## 씬 설정

기존 `SupabaseRuntime` 컴포넌트를 **제거**하고 `PlayNanooRuntime`을 배치합니다.

::: warning
`SupabaseRuntime`과 `PlayNanooRuntime`을 동시에 씬에 두지 마세요. `SupabaseRuntime`은 싱글턴으로 동작합니다.
:::

---

## 로그인

```csharp
// 게스트(익명) — PlayNanoo + SDK 동시 처리
await Supabase.TrySignInAnonymouslyAsync();

// Google — SDK가 토큰 획득 후 PlayNanoo SocialSignIn + SDK 로그인 자동 처리
await Supabase.TrySignInWithGoogleAsync();

// Apple (iOS)
await Supabase.TrySignInWithAppleIdTokenAsync(idToken);

// Apple (Android) — PlayNanoo WebView로 토큰 획득
playNanooRuntime.StartAppleSignInAndroid();
```

### 로그인 완료 감지

```csharp
SupabaseRuntime.SubscribeAutoLoginCompleted(OnReady);

void OnReady(bool success)
{
    if (success) InitGame();
    else         ShowLoginScreen();
}
```

---

## 로그아웃

```csharp
await Supabase.TrySignOutFullyAsync();
```

PlayNanoo 토큰 해지 → SDK 로그아웃 순서로 자동 처리됩니다.

---

## 탈퇴 / 복구

### 탈퇴 신청

```csharp
await Supabase.TryRequestMyWithdrawalAsync();
```

### 복구 흐름

로그인 시 탈퇴 예약 계정이 감지되면 `OnWithdrawalPending` 이벤트가 발행됩니다.

```csharp
playNanooRuntime.OnWithdrawalPending += withdrawalKey =>
{
    ShowWithdrawalRestoreDialog(withdrawalKey);
};
```

플레이어가 복구를 선택하면:

```csharp
playNanooRuntime.RestoreWithdrawal(withdrawalKey);
```

| 로그인 유형 | 복구 후 동작 |
|-------------|------------|
| 게스트 | `Supabase.TrySignInAnonymouslyAsync()` 자동 재호출 |
| Google / Apple | `OnWithdrawalRestored` 이벤트 발행 → 개발자가 재인증 UI 표시 |

```csharp
playNanooRuntime.OnWithdrawalRestored += () =>
{
    ShowSocialLoginScreen();
};
```

---

## 데이터 동기화

로그인 성공 시 자동으로 실행됩니다.

```
SDK 행 없음 (신규 유저)
  └─ PlayNanoo 데이터 있음 → SDK에 이관 후 ApplyRow
  └─ PlayNanoo 데이터 없음 → TryLoadAsync (빈 행 생성)

SDK 행 있음 (기존 유저)
  └─ lastCheckTime > updated_at → PlayNanoo 최신 → SDK 갱신 후 ApplyRow
  └─ lastCheckTime ≤ updated_at → SDK 최신 → ApplyRow 후 PlayNanoo 갱신
```

### SDK 저장 후 PlayNanoo 동기화

```csharp
playNanooRuntime.SaveCurrentToNanoo();
```

---

## PlayNanoo 제거 후

1. `PlayNanooRuntime.cs` 삭제
2. 씬에 `SupabaseRuntime` 배치
3. 게임 코드 변경 없음

::: tip
`Supabase.*` 로그인 호출은 브릿지 제거 전후 완전히 동일합니다.  
씬 컴포넌트만 교체하면 됩니다.
:::
