# 플레이나누 이관

플레이나누 기반 라이브 서비스를 SDK로 전환할 때 사용하는 브릿지 패턴입니다.  
두 백엔드를 동시에 운영하다가 플레이나누를 완전히 제거하는 흐름을 지원합니다.

---

## 동작 방식

`PlayNanooRuntimeBase`가 `SupabaseRuntime`을 상속하며, `Awake` 시점에 `SupabaseSDK` 내부에 인터셉터를 등록합니다.  
이후 게임 코드가 `Supabase.TrySignInAnonymouslyAsync()` 등을 호출하면 **플레이나누 로그인이 먼저 실행된 뒤 SDK 로그인이 이어집니다.**

브릿지가 없으면 인터셉터도 없으므로, **게임 코드는 이관 전·중·후 동일합니다.**

::: info 롤백
플레이나누 로그인 성공 후 Supabase 로그인이 실패하면 플레이나누도 자동으로 로그아웃 처리됩니다.  
**로그인·계정 연동 시에만 적용**됩니다. 로그아웃·탈퇴는 한쪽 실패 시 롤백 없이 경고 로그만 출력됩니다.
:::

---

## 설치

1. Package Manager **Samples** 탭에서 **PlayNANOO Migration**을 Import합니다.
2. 씬에서 `SupabaseRuntime` 대신 SDK 버전에 맞는 컴포넌트를 하나 배치합니다.

| 구현체 | 사용 API |
|--------|---------|
| `PlayNanooRuntime` | `AccountManagerV20240401.*` (신버전) |
| `PlayNanooLegacyRuntime` | `AccountGuestSignIn` / `AccountManager.*` (구버전) |

3. Inspector에서 **Nanoo Storage Key**를 플레이나누 콘솔에 등록한 키로 변경합니다.

::: warning
`SupabaseRuntime`과 `PlayNanooRuntime` / `PlayNanooLegacyRuntime`을 동시에 씬에 두지 마세요.
:::

---

## 로그인

로그인 API는 이관 전·중·후 동일합니다. 자세한 내용은 [인증](./auth.md)을 참고하세요.

Android에서 Apple 로그인은 플레이나누 WebView를 통해 토큰을 획득한 뒤 호출합니다.

```csharp
// Apple (Android) — 플레이나누 WebView로 토큰 획득
playNanooRuntime.StartAppleSignInAndroid();
```

플레이나누 로그인이 성공하면 아래 프로퍼티를 사용할 수 있습니다.

| 프로퍼티 | 설명 |
|---------|------|
| `PlayNanooRuntimeBase.UserId` | 플레이나누 uuid. 로그인 전에는 null |
| `PlayNanooRuntimeBase.OpenId` | 플레이나누 openid. SDK가 반환하지 않으면 null |

### 자동 로그인

`SupabaseRuntime.TriggerAutoLoginAsync()`는 플레이나누 런타임이 있을 때 두 세션을 모두 복원합니다.

1. Supabase 리프레시 토큰으로 세션 복원
2. 저장된 플레이나누 액세스 토큰으로 `TokenSignIn` 호출
3. 둘 다 성공하면 `true` 반환

플레이나누 복원이 실패하면 Supabase 세션도 롤백한 뒤 `false`를 반환합니다. 두 세션이 항상 동시에 유효하도록 보장합니다.

::: warning
`TrySignOutFullyAsync()`를 호출하면 Supabase와 플레이나누 액세스 토큰이 모두 삭제됩니다. 플레이나누 액세스 토큰 유효기간은 24시간이므로 그 이후에는 자동 로그인이 실패하며 플레이어가 직접 로그인해야 합니다.
:::

---

## 로그아웃

로그아웃 API는 이관 전·중·후 동일합니다. [인증](./auth.md)을 참고하세요.  
플레이나누 토큰 해지 → SDK 로그아웃 순서로 자동 처리됩니다.

---

## 탈퇴 / 복구

탈퇴 신청과 복구 흐름은 SDK 단독 사용과 동일합니다.

로그인 시 탈퇴 예약 계정이 감지되면 `OnWithdrawalPending` 이벤트가 발행됩니다. 플레이어가 복구를 선택하면 `RestoreWithdrawal(withdrawalKey)`를 호출합니다.

| 로그인 유형 | 복구 후 동작 |
|-------------|------------|
| 게스트 | `TrySignInAnonymouslyAsync()` 자동 재호출 → 성공 시 `TryClearMyWithdrawalAsync()` 자동 호출 |
| Google / Apple | `OnWithdrawalRestored` 이벤트 발행 → 개발자가 재인증 UI 표시 후 `TryClearMyWithdrawalAsync()` 직접 호출 |

---

## 인앱 결제

`PlayNanooRuntime`이 씬에 있으면 IAP 결제도 **플레이나누 → SDK 순서**로 자동 처리됩니다.  
게임 코드(`SupabaseIAP.CreateIAPAsync(...)`)는 변경 없이 동작합니다.

플레이나누 검증이 실패하면 SDK 검증은 실행되지 않고 구매가 중단됩니다.

::: warning iOS SK1
플레이나누 IAP는 StoreKit 1 영수증만 지원합니다. `PlayNanooRuntime`은 `Awake`에서 자동으로 SK1을 강제합니다.  
Unity IAP **5.0.x**는 SK1 강제가 불가능하므로 iOS 15+에서 플레이나누 IAP가 작동하지 않습니다. **4.x 또는 5.1 이상**을 사용하세요.
:::

---

## 데이터 동기화

로그인 성공 시 자동으로 실행됩니다.  
플레이나누 Storage JSON은 camelCase 키를 사용합니다. DB 컬럼도 camelCase로 생성하면 별도 매핑 없이 자동으로 연결됩니다.

```
SDK 행 없음 (신규 유저)
  └─ 플레이나누 데이터 있음 → SDK에 이관 후 ApplyRow
  └─ 플레이나누 데이터 없음 → TryLoadAsync (빈 행 생성)

SDK 행 있음 (기존 유저)
  └─ 플레이나누에 updated_at 없음 (이관 전 순수 플레이나누 데이터) → 플레이나누 우선 → SDK 갱신 후 ApplyRow
  └─ 플레이나누 updated_at > DB updated_at → 플레이나누 최신 → SDK 갱신 후 ApplyRow
  └─ DB updated_at ≥ 플레이나누 updated_at → SDK 최신 → ApplyRow 후 플레이나누 갱신
```

SDK 저장 이후 플레이나누에도 자동으로 반영됩니다.

### 필드명 규칙

플레이나누 Storage JSON은 camelCase 키를 사용합니다. SDK의 기본 변환은 C# 필드명을 그대로 JSON 키로 사용하므로, **C# 필드명을 camelCase로 선언하면 별도 매핑 없이 자동으로 연결됩니다.**

DB 컬럼명은 `[DataColumn]`으로 별도 지정하므로 C# 필드명과 달라도 됩니다.

```csharp
[Serializable]
public sealed class Row
{
    [DataColumn("player_level")] public int playerLevel;      // DB: player_level, 플레이나누: playerLevel
    [DataColumn("item_ids")]     public List<int> itemIds;    // DB: item_ids,     플레이나누: itemIds
}
```

### 데이터 변환 커스터마이징

camelCase 필드명으로 처리하기 어려운 타입이 있거나 플레이나누 키명이 다를 때 사용합니다.

```csharp
PlayerSave.RegisterNanooConverters(
    nanooToDb: json =>
    {
        var src = JsonConvert.DeserializeObject<NanooData>(json);
        return new PlayerSave.Row
        {
            playerLevel = src.playerLevel,
            itemIds     = src.itemList,
        };
    },
    dbToNanoo: row => JsonConvert.SerializeObject(new NanooData
    {
        playerLevel = row.playerLevel,
        itemList    = row.itemIds,
    })
);
```

`dbToNanoo`를 생략하면 DB → 플레이나누 방향은 기본 직렬화를 사용합니다.

::: warning
`NanooDeserializeJson` / `NanooSerializeJson`을 서브클래스에서 override한 경우, 등록된 변환 함수보다 override가 우선합니다.
:::

---

## 플레이나누 제거 후

1. `PlayNanooRuntimeBase.cs` / `PlayNanooRuntime.cs` / `PlayNanooLegacyRuntime.cs` 삭제
2. 씬에 `SupabaseRuntime` 배치
3. 게임 코드 변경 없음
