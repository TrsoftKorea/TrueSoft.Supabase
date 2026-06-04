# PlayNanoo 이관

PlayNanoo 기반 라이브 서비스를 SDK로 전환할 때 사용하는 브릿지 패턴입니다.  
두 백엔드를 동시에 운영하다가 PlayNanoo를 완전히 제거하는 흐름을 지원합니다.

---

## 동작 방식

`PlayNanooMigrationBridge`는 `SupabaseRuntime`을 상속하며, `Awake` 시점에 `SupabaseSDK` 내부에 인터셉터를 등록합니다.  
이후 게임 코드가 `Supabase.TrySignInAnonymouslyAsync()` 등을 호출하면, **PlayNanoo 로그인이 먼저 실행된 뒤 SDK 로그인이 이어집니다.**

게임 코드는 `Supabase.*`를 그대로 사용합니다. 브릿지를 제거하면 인터셉터도 함께 사라지고, 같은 호출이 SDK 기본 흐름으로 동작합니다.

| 이관 중 | PlayNanoo 제거 후 |
|---------|-----------------|
| `await Supabase.TrySignInAnonymouslyAsync()` | 변경 없음 |
| `await bridge.TrySignInWithGoogleAsync()` | `await Supabase.TrySignInWithGoogleAsync()` |
| `await Supabase.TrySignInWithAppleIdTokenAsync(token)` | 변경 없음 |
| `await Supabase.TrySignOutFullyAsync()` | 변경 없음 |
| `await Supabase.TryRequestMyWithdrawalAsync()` | 변경 없음 |

::: info
Google 로그인만 브릿지 메서드(`bridge.TrySignInWithGoogleAsync()`)를 사용합니다.  
Google OAuth는 PlayNanoo의 브라우저 흐름을 거쳐 토큰을 받아야 하므로, SDK 기본 흐름과 시작점이 다릅니다.  
토큰 수신 후에는 `Supabase.TrySignInWithGoogleIdTokenAsync(token)`이 자동으로 호출됩니다 (이 단계는 인터셉터가 처리합니다).
:::

---

## 준비

Package Manager **Samples** 탭에서 **PlayNanoo 이관**을 Import한 뒤, 두 곳을 교체합니다.

```csharp
// PlayNanooMigrationBridge.cs 상단
private const string NanooStorageKey = "save";  // ← PlayNanoo 콘솔 스토리지 키로 교체
```

```csharp
// SyncDataAfterLogin, SaveToNanoo 등에서 사용하는 타입
YourSaveData  // ← 생성기로 만든 실제 세이브 클래스명으로 전체 교체
```

Google 로그인을 사용하는 경우 Inspector의 **Google Client Id** 필드에 웹 OAuth 클라이언트 ID를 입력합니다.

---

## 씬 설정

기존 `SupabaseRuntime` 컴포넌트를 **제거**하고 `PlayNanooMigrationBridge`를 배치합니다.

::: warning
두 컴포넌트를 동시에 씬에 두지 마세요. `SupabaseRuntime`은 싱글턴으로 동작합니다.
:::

---

## 로그인

### 게스트(익명)

```csharp
await Supabase.TrySignInAnonymouslyAsync();
```

PlayNanoo 게스트 로그인 → SDK 익명 로그인 → 데이터 동기화 순서로 자동 처리됩니다.

### Google

```csharp
// 브릿지 컴포넌트 참조 (씬에서 GetComponent 또는 Inspector 연결)
await bridge.TrySignInWithGoogleAsync();
```

PlayNanoo OAuth 브라우저 → 토큰 수신 → PlayNanoo SocialSignIn + SDK 로그인 → 데이터 동기화까지 자동 처리됩니다.

### Apple

```csharp
// iOS: 외부 SDK(예: AppleAuthManager)로 idToken 획득 후
await Supabase.TrySignInWithAppleIdTokenAsync(idToken);

// Android: PlayNanoo 내장 WebView 사용
bridge.StartAppleSignInAndroid();
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
await Supabase.TryRequestMyWithdrawalAsync();  // PlayNanoo + SDK 동시 처리
```

### 복구 흐름

로그인 시 탈퇴 예약 계정이 감지되면 `OnWithdrawalPending` 이벤트가 발행됩니다.

```csharp
bridge.OnWithdrawalPending += withdrawalKey =>
{
    ShowWithdrawalRestoreDialog(withdrawalKey);
};
```

플레이어가 복구를 선택하면:

```csharp
bridge.RestoreWithdrawal(withdrawalKey);
```

복구 완료 후 동작:

| 로그인 유형 | 동작 |
|-------------|------|
| 게스트 | `Supabase.TrySignInAnonymouslyAsync()` 자동 재호출 |
| Google / Apple | `OnWithdrawalRestored` 이벤트 발행 → 개발자가 재인증 UI 표시 |

```csharp
bridge.OnWithdrawalRestored += () =>
{
    ShowSocialLoginScreen();
};
```

---

## 데이터 동기화

로그인 성공 시 자동으로 실행됩니다. 아래 로직을 따릅니다.

```
SDK 행 없음 (신규 유저)
  └─ PlayNanoo 데이터 있음 → SDK에 이관 후 ApplyRow
  └─ PlayNanoo 데이터 없음 → TryLoadAsync (빈 행 생성)

SDK 행 있음 (기존 유저)
  └─ lastCheckTime > updated_at → PlayNanoo 최신 → SDK 갱신 후 ApplyRow
  └─ lastCheckTime ≤ updated_at → SDK 최신 → ApplyRow 후 PlayNanoo 갱신
```

::: info
`lastCheckTime`은 PlayNanoo Storage JSON 안의 타임스탬프 필드입니다.  
`updated_at`은 SDK DB 테이블의 자동 관리 컬럼으로, 생성기가 Row 클래스에 자동으로 포함합니다.
:::

### SDK 저장 후 PlayNanoo 동기화

```csharp
bridge.SaveToNanoo(YourSaveData.Instance.Current);
```

---

## PlayNanoo 제거 후

1. `PlayNanooMigrationBridge.cs` 삭제
2. 씬에 `SupabaseRuntime` 배치
3. `bridge.TrySignInWithGoogleAsync()` → `await Supabase.TrySignInWithGoogleAsync()` 교체
4. 나머지 `Supabase.*` 호출은 변경 없음
5. `YourSaveData.*` 접근 코드는 변경 없음

::: tip
Google 로그인 한 곳만 교체하면 됩니다.  
`bridge.` 접두사를 `await Supabase.`로 바꾸는 것이 전부입니다.
:::
