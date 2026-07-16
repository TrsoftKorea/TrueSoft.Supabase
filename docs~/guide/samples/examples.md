# Examples

`SupabaseRuntime`이 있는 씬에 `ExampleSupabaseScenarios` 컴포넌트를 추가하면  
Play Mode에서 키보드로 각 기능을 즉시 테스트할 수 있습니다.

## 키보드 단축키

| 키 | 동작 |
|----|------|
| `Q` | 익명 로그인 |
| `I` | Google 로그인 (Android 네이티브) |
| `P` | 익명 계정에 Google 연동 |
| `K` | Google 연동 해제 |
| `B` | Apple 로그인 (플랫폼 자동) |
| `H` | 익명 계정에 Apple 연동 (iOS 네이티브) |
| `L` | Apple 연동 해제 |
| `W` | 로그아웃 |
| `O` | 세션 복원 |
| `R` | 유저 데이터 전체 로드 |
| `V` | 유저 데이터 즉시 저장 |
| `F` | 레벨 +1 (자동 저장 시연) |
| `X` | 세이브 삭제 (기본값 리셋 + 재로드) |
| `T` | RemoteConfig Reader 호출 |
| `U` | RemoteConfig Binding 값 출력 |
| `E` | RemoteConfig Listener 시작/종료 토글 |
| `N` | 닉네임 설정 후 내 프로필 조회 (displayname Edge Function 배포 필요) |
| `A` | 현재 세션 상태 출력 (IsAnonymous, UserId, DisplayName 등) |
| `J` | 서버 시간 조회 |
| `G` | 차단 정보 조회 |
| `D` | 탈퇴 신청 |
| `S` | 탈퇴 상태 조회 |
| `C` | 탈퇴 취소 |

::: info
Apple 로그인(`B`)·연동(`H`)은 실기기 빌드에서만 동작합니다(에디터 미지원). 설정은 [Apple 로그인](/guide/social/apple/)을 참고하세요.
:::

## SamplePlayerSave

`StaticUserSave`를 상속해 DB 컬럼을 C# 프로퍼티에 연결하는 최소 예시 클래스로, `ExampleSupabaseScenarios.cs` 안에 함께 정의돼 있습니다(별도 파일이 아닙니다). 실제 프로젝트에서는 [클래스 생성기](/guide/user-data/class-gen)로 만드세요.

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
await Supabase.SaveAllAsync(timeoutMs: 5000);           // 즉시 저장
```

## SampleIAPScenarios

`com.unity.purchasing`(최신 권장, 최소 4.0.0)를 설치하면 자동으로 활성화됩니다.

**사전 준비:**
1. `com.unity.purchasing` 설치 — 최신 권장(최소 4.0.0)
2. [Database Setup](/guide/start/database-setup) 완료
3. Inspector에서 `productId` 입력

**구매 흐름:**

```csharp
// 1. 로그인 후 IAP 초기화
var result = await SupabaseIAP.CreateIAPAsync(
    productIds: new[] { productId },
    onGrant:    OnGrantItemAsync,
    onFailed:   OnPurchaseFailed,
    timeoutMs:  10_000);
if (!result.IsSuccess) return;
_iapFacade = result.Data;

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

::: tip
`alreadyVerified` 처리는 [중복 지급 방지](/guide/iap/advanced#duplicate-grant)를 참고하세요.
:::

## SampleMailbox

`SupabaseRuntime`이 있는 씬에 `SampleMailbox` 컴포넌트를 붙이면 우편함 조회·수령·삭제를 [분류](/guide/mailbox/#category)별로 테스트할 수 있습니다.

**사전 준비:**
1. [Database Setup](/guide/start/database-setup) 완료
2. Inspector에서 `Demo Category`·`Demo Item Key` 확인(기본값 `event`·`gold`)
3. 키 `1`로 로그인 후 출력된 `UserId` 계정에 테스트 메일 발송 — 발송은 어드민(Retool)/SQL Editor에서만 가능하며, 발송 시 `category`·`items[].key`를 위 Inspector 값과 맞춥니다

| 키 | 동작 |
|----|------|
| `1` | 익명 로그인 + 발송 대상 `UserId` 출력 |
| `2` | 전체 목록 조회 |
| `3` | `Demo Category` 분류만 조회 |
| `4` | 분류별 현황(`ByCategory`) 출력 |
| `5` | 첫 미수령 메일 보상 수령 |
| `6` | `Demo Category` 분류 일괄 수령 |
| `7` | `Demo Category` 읽은 우편 일괄 삭제 |
| `8` | 첫 삭제 가능 메일 삭제 |

::: tip
키 `5`·`6`의 보상 수령은 `Demo Item Key`와 일치하는 [아이템 핸들러](/guide/mailbox/item-handler)가 등록돼 있을 때만 성공합니다. 샘플은 `Awake`에서 로그만 남기는 데모 핸들러를 자동 등록합니다.
:::
