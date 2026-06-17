# 샘플

Unity Package Manager의 **Samples** 탭에서 필요한 샘플만 골라 Import합니다.

| 샘플 | 용도 |
|------|------|
| **Database Setup** | Supabase 프로젝트 초기 설정용 SQL·Edge Function 소스. 설정 완료 후 삭제 |
| **Examples** | 인증·세이브·RemoteConfig 등 주요 기능을 Play Mode 키보드로 바로 테스트 |
| **플레이나누 이관** | 플레이나누와 SDK를 병행 운영하다가 단계적으로 SDK로 전환하는 패턴 예제 |

---

## Database Setup

초기 DB 스키마와 Edge Function 소스를 담은 파일 묶음입니다.

사용 방법은 [Database Setup](./getting-started.md#database-setup)을 참고하세요.

---

## Examples

`SupabaseRuntime`이 있는 씬에 `ExampleSupabaseScenarios` 컴포넌트를 추가하면  
Play Mode에서 키보드로 각 기능을 즉시 테스트할 수 있습니다.

| 키 | 동작 |
|----|------|
| `Q` | 익명 로그인 |
| `I` | Google 로그인 (Android 네이티브) |
| `P` | 익명 계정에 Google 연동 |
| `W` | 로그아웃 |
| `R` | 유저 데이터 전체 로드 |
| `V` | 유저 데이터 즉시 저장 |
| `F` | 레벨 +1 (자동 저장 시연) |
| `T` | RemoteConfig Reader 호출 |
| `U` | RemoteConfig Binding 값 출력 |
| `E` | RemoteConfig Listener 시작/종료 토글 |
| `N` | 닉네임 설정 후 내 프로필 조회 (displayname Edge Function 배포 필요) |
| `A` | 현재 세션 상태 출력 (IsAnonymous, UserId, DisplayName 등) |
| `J` | 서버 시간 조회 |

### SamplePlayerSave

`StaticUserSave`를 상속해 DB 컬럼을 C# 프로퍼티에 연결하는 최소 예시입니다.

```csharp
[Serializable]
[JsonObject(MemberSerialization.Fields)]   // Newtonsoft가 internal 필드 저장/로드
public sealed class Row
{
    [DataColumn("level")] internal int level;   // 필드는 internal — 프로퍼티로 접근
    [DataColumn("coins")] internal int coins;
}

public static int Level
{
    get => Instance.Current.level;
    set { if (Instance.Current.level == value) return;
          Instance.Current.level = value; Instance.MarkDirty(); }
}
```

프로퍼티에 값을 쓰면 `MarkDirty()`가 자동 호출되고, `SupabaseRuntime`이 쿨타임 주기로 자동 저장합니다.  
씬 전환·로그아웃 직전처럼 즉시 저장이 필요한 시점에는 명시적으로 호출합니다.

```csharp
SamplePlayerSave.Level += 1;                             // 변경 → 자동 저장 예약
await Supabase.TrySaveAllAsync(timeoutMs: 5000);           // 즉시 저장
```

### SampleIAPScenarios

`com.unity.purchasing` 4.x 이상을 설치하면 자동으로 활성화됩니다.

**사전 준비:**
1. `com.unity.purchasing` 4.x 이상 설치
2. [Database Setup](./getting-started.md#database-setup) 완료
3. Inspector에서 `productId` 입력

**구매 흐름:**

```csharp
// 1. 로그인 후 IAP 초기화
_iapFacade = await SupabaseIAP.CreateIAPAsync(
    productIds: new[] { productId },
    onGrant:    OnGrantItemAsync,
    onFailed:   OnPurchaseFailed,
    timeoutMs:  10_000);

// 2. 구매 시작
_iapFacade.Purchase(productId);

// 3. 서버 검증 성공 → 아이템 지급 콜백
private async Task<bool> OnGrantItemAsync(string productId, bool isResuming, bool alreadyVerified)
{
    // alreadyVerified=true → 지급 후 크래시 케이스. DB로 중복 지급 여부 확인 권장
    await MyInventory.GiveItemAsync(productId);
    return true; // true 반환 시 SDK가 ConfirmPurchase(소비) 호출
}
```

::: warning
`alreadyVerified=true`는 서버에 이미 검증 기록이 있는 경우입니다(지급 후 크래시). DB에서 지급 여부를 확인해 중복 지급을 방지하세요.
:::

---

## 플레이나누 이관

플레이나누와 SDK를 동시에 운영하면서 단계적으로 SDK로 전환할 때 사용합니다.  
`SupabaseRuntime` 대신 `PlayNanooRuntime`을 씬에 배치하면, 게임 코드의 `Supabase.*` 호출이 자동으로 플레이나누를 경유합니다.

**지원 기능:**
- 게스트·Google·Apple 로그인
- 익명 계정 → Google·Apple 연동
- 로그아웃, 탈퇴 예약
- 탈퇴 복구 (`OnWithdrawalPending` · `OnWithdrawalRestored` 이벤트)
- `updated_at` 기반 PlayNANOO ↔ SDK 데이터 동기화

자세한 사용법은 [플레이나누 이관](./playnanoo-migration.md)을 참고하세요.
