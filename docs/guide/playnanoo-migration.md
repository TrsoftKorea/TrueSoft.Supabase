# PlayNANOO Migration

PlayNANOO 기반 라이브 서비스를 SDK로 전환할 때 사용하는 브릿지 패턴입니다.  
두 백엔드를 동시에 운영하다가 PlayNANOO를 완전히 제거하는 흐름을 지원합니다.

---

## 동작 방식

`PlayNanooRuntime`는 `SupabaseRuntime`을 상속하며, `Awake` 시점에 `SupabaseSDK` 내부에 인터셉터를 등록합니다.  
이후 게임 코드가 `Supabase.TrySignInAnonymouslyAsync()` 등을 호출하면 **PlayNANOO 로그인이 먼저 실행된 뒤 SDK 로그인이 이어집니다.**

브릿지가 없으면 인터셉터도 없으므로, **게임 코드는 이관 전·중·후 동일합니다.**

| 로그인 | 이관 중 | PlayNANOO 제거 후 |
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

1. Package Manager **Samples** 탭에서 **PlayNANOO Migration**을 Import합니다.
2. 씬에서 `SupabaseRuntime` 대신 `PlayNanooRuntime` 컴포넌트를 배치합니다.
3. Inspector에서 **Nanoo Storage Key**를 PlayNANOO 콘솔에 등록한 키로 변경합니다.

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
// 게스트(익명) — PlayNANOO + SDK 동시 처리
await Supabase.TrySignInAnonymouslyAsync();

// Google — SDK가 토큰 획득 후 PlayNANOO SocialSignIn + SDK 로그인 자동 처리
await Supabase.TrySignInWithGoogleAsync();

// Apple (iOS)
await Supabase.TrySignInWithAppleIdTokenAsync(idToken);

// Apple (Android) — PlayNANOO WebView로 토큰 획득
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

PlayNANOO 토큰 해지 → SDK 로그아웃 순서로 자동 처리됩니다.

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

## DB 컬럼 설정

PlayNANOO Storage JSON은 **camelCase** 키를 사용합니다 (`bgmVolume`, `cameraShake` 등).  
SDK Row 클래스 필드명과 일치시키려면 DB 컬럼도 camelCase로 생성합니다.

::: warning
PostgreSQL은 따옴표 없이 camelCase를 쓰면 자동으로 소문자로 변환합니다.  
컬럼 생성 시 반드시 **큰따옴표**로 감싸야 합니다.
:::

```sql
ALTER TABLE user_data
  ADD COLUMN "bgmVolume"          int     NOT NULL DEFAULT 0,
  ADD COLUMN "sfxVolume"          int     NOT NULL DEFAULT 0,
  ADD COLUMN "cameraShake"        boolean NOT NULL DEFAULT true,
  ADD COLUMN "showFont"           boolean NOT NULL DEFAULT true,
  ADD COLUMN "fontSplit"          boolean NOT NULL DEFAULT true,
  ADD COLUMN "showSkillDirect"    boolean NOT NULL DEFAULT true,
  ADD COLUMN "showMysticDirect"   boolean NOT NULL DEFAULT true;
```

컬럼명을 camelCase로 통일하면 DB 응답 JSON, PlayNANOO Storage JSON, C# 필드명이 모두 일치해  
`[JsonProperty]`나 커스텀 역직렬화 없이 자동으로 매핑됩니다.

생성기로 Row 클래스를 만들면 필드명이 DB 컬럼명(`bgmVolume`)과 동일하게 생성됩니다:

```csharp
public sealed partial class PlayerSave : StaticUserSave<PlayerSave.Row>
{
    [Serializable]
    public sealed class Row
    {
        [DataColumn("bgmVolume")]       public int  bgmVolume;
        [DataColumn("sfxVolume")]       public int  sfxVolume;
        [DataColumn("cameraShake")]     public bool cameraShake;
        [DataColumn("showFont")]        public bool showFont;
        [DataColumn("fontSplit")]       public bool fontSplit;
        [DataColumn("showSkillDirect")] public bool showSkillDirect;
        [DataColumn("showMysticDirect")] public bool showMysticDirect;
        [DataColumn("updated_at")]      public string updated_at;
    }
}
```

::: info
`updated_at`은 관례상 snake_case를 유지합니다. SDK 내부에서 자동으로 처리되므로 변경하지 않아도 됩니다.
:::

---

## 데이터 동기화

로그인 성공 시 자동으로 실행됩니다.

```
SDK 행 없음 (신규 유저)
  └─ PlayNANOO 데이터 있음 → SDK에 이관 후 ApplyRow
  └─ PlayNANOO 데이터 없음 → TryLoadAsync (빈 행 생성)

SDK 행 있음 (기존 유저)
  └─ PlayNANOO에 updated_at 없음 (이관 전 순수 PlayNANOO 데이터) → PlayNANOO 우선 → SDK 갱신 후 ApplyRow
  └─ PlayNANOO updated_at > DB updated_at → PlayNANOO 최신 → SDK 갱신 후 ApplyRow
  └─ DB updated_at ≥ PlayNANOO updated_at → SDK 최신 → ApplyRow 후 PlayNANOO 갱신
```

### SDK 저장 후 PlayNANOO 동기화

```csharp
playNanooRuntime.SaveCurrentToNanoo();
```

---

## PlayNANOO 제거 후

1. `PlayNanooRuntime.cs` 삭제
2. 씬에 `SupabaseRuntime` 배치
3. 게임 코드 변경 없음

::: tip
`Supabase.*` 로그인 호출은 브릿지 제거 전후 완전히 동일합니다.  
씬 컴포넌트만 교체하면 됩니다.
:::
