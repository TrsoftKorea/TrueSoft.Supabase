# PlayNanoo 이관

PlayNanoo 기반 라이브 서비스를 SDK로 전환할 때 사용하는 브릿지 패턴입니다.  
두 백엔드를 동시에 운영하다가 PrepNanoo를 완전히 제거하는 흐름을 지원합니다.

---

## 동작 방식

`PlayNanooMigrationBridge`는 `SupabaseRuntime`을 상속합니다.  
씬에 `SupabaseRuntime` 대신 이 컴포넌트 하나만 배치하면, SDK의 모든 자동 동기화·RemoteConfig 폴링 기능을 그대로 유지하면서 PlayNanoo 로그인·저장을 함께 처리합니다.

| 기능 | 처리 방식 |
|------|-----------|
| 로그인 | PlayNanoo + SDK 동시 로그인, 한 번의 호출로 완료 |
| 데이터 동기화 | `lastCheckTime`(PlayNanoo) vs `updated_at`(SDK) 비교 후 최신 쪽으로 덮어씀 |
| 로그아웃 | PlayNanoo 토큰 해지 → SDK 세션 해제 순서로 처리 |
| 탈퇴 | PlayNanoo + SDK 동시 탈퇴 예약 |
| 탈퇴 복구 | 게스트: 자동 재로그인 / Google·Apple: `OnWithdrawalRestored` 이벤트 |

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
bridge.GuestSignIn();
```

PlayNanoo 게스트 로그인 → SDK 익명 로그인 → 데이터 동기화를 순서대로 처리합니다.

### Google

```csharp
// Step 1: 브라우저 열기
bridge.StartGoogleSignIn(clientId);

// Step 2: 토큰 수신은 자동 처리됨
// Android: DeepLink 콜백
// iOS: SetGoogleAuthCallback 콜백
```

토큰 수신 후 PlayNanoo SocialSignIn → SDK `TrySignInWithGoogleIdTokenAsync` → 데이터 동기화까지 자동으로 완료됩니다.

### Apple

```csharp
// iOS: 외부 SDK(예: AppleAuthManager)로 idToken 획득 후 호출
bridge.CompleteAppleSignIn(idToken);

// Android: PlayNanoo 내장 WebView 사용
bridge.StartAppleSignInAndroid();
```

### 로그인 완료 감지

SDK의 기존 이벤트를 그대로 사용합니다.

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
bridge.SignOut();
```

PlayNanoo 토큰 해지 → SDK `TrySignOutFullyAsync()` 순서로 처리됩니다.

---

## 탈퇴 / 복구

### 탈퇴 신청

```csharp
bridge.RequestWithdrawal(periodDays: 15);  // PlayNanoo + SDK 동시 처리
```

### 복구 흐름

로그인 시 탈퇴 예약 계정이 감지되면 `OnWithdrawalPending` 이벤트가 발행됩니다.

```csharp
bridge.OnWithdrawalPending += withdrawalKey =>
{
    // UI: "탈퇴 예약된 계정입니다. 복구하시겠습니까?"
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
| 게스트 | `GuestSignIn()`을 자동으로 재호출 |
| Google / Apple | `OnWithdrawalRestored` 이벤트 발행 → 개발자가 재인증 UI 표시 |

```csharp
bridge.OnWithdrawalRestored += () =>
{
    // Google / Apple 재인증 UI 표시 후 CompleteGoogleSignIn 또는 CompleteAppleSignIn 호출
    ShowSocialLoginScreen();
};
```

---

## 데이터 동기화

로그인 성공 시 `SyncDataAfterLogin()`이 자동으로 실행됩니다. 아래 로직을 따릅니다.

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

SDK 데이터가 변경됐을 때 PlayNanoo에도 반영하려면 직접 호출합니다.

```csharp
bridge.SaveToNanoo(YourSaveData.Instance.Current);
```

---

## PlayNanoo 제거 후

전환이 완료되면:

1. `PlayNanooMigrationBridge.cs` 삭제
2. 씬에 `SupabaseRuntime` 배치
3. 로그인 코드를 SDK 직접 호출 (`TrySignInAnonymouslyAsync` 등)로 교체
4. `YourSaveData.*` 접근 코드는 변경 없음

::: tip
데이터 접근 코드(`YourSaveData.Level` 등)는 브릿지 제거 전후 동일합니다.  
로그인·로그아웃 호출부만 교체하면 됩니다.
:::
