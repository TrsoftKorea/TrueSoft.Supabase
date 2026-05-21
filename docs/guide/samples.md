# 샘플

Package Manager의 **Samples** 탭에서 **Import**를 눌러 예제 씬과 스크립트를 프로젝트로 가져옵니다.

임포트 후 `Assets/Samples/Truesoft Supabase SDK/<버전>/Examples/` 폴더에 아래 세 파일이 생성됩니다.

| 파일 | 설명 |
|------|------|
| `ExampleSupabaseScenarios.cs` | 인증·세이브·RemoteConfig·프로필 키보드 단축키 테스트 |
| `SamplePlayerSave.cs` | `StaticUserSave` 최소 구현 예시 |
| `SampleIAPScenarios.cs` | IAP 서버 검증 예시 (`TRUESOFT_IAP_AVAILABLE` 필요) |

---

## ExampleSupabaseScenarios

`SupabaseRuntime`이 있는 씬에 이 컴포넌트를 추가하면 Play Mode에서 키보드로 각 기능을 테스트할 수 있습니다.

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

---

## SamplePlayerSave

`StaticUserSave`를 상속해 DB 컬럼을 C# 프로퍼티에 연결하는 최소 예시입니다.

```csharp
[Serializable]
public sealed class Row
{
    [DataColumn("level")] public int level;
    [DataColumn("coins")] public int coins;
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
await Supabase.TryFlushAllUserSaveImmediateAsync();       // 즉시 저장
```

새 컬럼을 추가할 때는 `Row` 클래스에 `[DataColumn]` 필드만 추가하면 됩니다.

---

## SampleIAPScenarios

`#if TRUESOFT_IAP_AVAILABLE` 블록으로 감싸져 있습니다. `com.unity.purchasing` 5.2.1 이상을 설치하면 심볼이 자동으로 정의되어 이 파일이 활성화됩니다.

**사전 준비:**
1. `com.unity.purchasing` 5.2.1 이상 설치
2. Google Service Account / Apple Shared Secret → Supabase Secrets 등록
3. Edge Function 배포: Supabase 대시보드 > Edge Functions에서 함수를 생성하고 **Database Setup** 샘플 > `EdgeFunctions/` 안의 코드를 붙여넣기
4. **Database Setup** 샘플 > `SQL/player/07_purchases.sql` 실행
5. Inspector에서 `productId` 입력

**구매 흐름:**

```csharp
// 1. 로그인 후 IAP 초기화
_iapFacade = await Supabase.CreateIAPAsync(
    productIds: new[] { productId },
    onGrant:    OnGrantItemAsync,
    onFailed:   OnPurchaseFailed);

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

> [!IMPORTANT]
> `alreadyVerified=true`는 서버에 이미 검증 기록이 있는 경우입니다(지급 후 크래시). DB에서 지급 여부를 확인해 중복 지급을 방지하세요.
