# 데이터 동기화

## 자동 동기화 흐름

로그인 성공 시 SDK와 플레이나누 데이터가 자동으로 동기화됩니다.

```
플레이나누를 읽지 못함 → 아무것도 하지 않음 (이번 로그인은 플레이나누 쓰기 차단)

SDK 행 없음 (신규 유저)
  └─ 플레이나누 데이터 있음 → SDK에 이관 후 서버 정본을 다시 읽어 로컬 반영
  └─ 플레이나누 데이터 없음 → LoadAsync (빈 행 생성)

SDK 행 있음 (기존 유저)
  └─ 플레이나누에 updated_at 없음 (이관 전 순수 플레이나누 데이터) → 플레이나누 우선 → SDK 갱신 후 로컬 반영
  └─ 플레이나누 updated_at > DB updated_at → 플레이나누 최신 → SDK 갱신 후 로컬 반영
  └─ DB updated_at ≥ 플레이나누 updated_at → SDK 최신 → 로컬 반영 후 플레이나누 갱신
```

## 플레이나누에 쓰는 시점 {#write}

**게임이 세이브를 저장해도 플레이나누에는 곧바로 가지 않습니다.** `Supabase.SaveNowAsync()`·`RequestSave()`는 SDK 서버에만 씁니다. 플레이나누에 반영되는 것은 **다음 로그인의 동기화**에서 SDK가 최신으로 판정될 때입니다.

더 일찍 반영해야 하면 게임이 직접 부릅니다.

```csharp
// 씬 전환·앱 종료 등 원하는 시점에
TrueBaseNanoo.SaveNow();
```

씬에 배치한 것이 `PlayNanooRuntime`이든 `PlayNanooLegacyRuntime`이든 같은 호출로 동작합니다. 런타임이 없으면 경고만 남기고 아무것도 하지 않습니다.

::: danger SaveToNanoo에 직접 JSON을 넘기지 마세요
런타임에는 `SaveToNanoo(string json)`도 열려 있지만, 넘긴 JSON을 **그대로** 씁니다. 세이브 클래스가 만든 것이 아닌 값을 넘기면 플레이나누 원본이 그 값으로 덮이고, 플레이나누 스토리지에는 이력이 없어 되돌릴 수 없습니다. 게임 코드에서는 `TrueBaseNanoo.SaveNow()`만 쓰세요.
:::

## 플레이나누 원본을 지우지 않기 위한 방어 {#write-guard}

마지막 줄(SDK 최신 → 플레이나누 갱신)은 **플레이나누 데이터를 덮어씁니다.** 판단이 틀리면 원본이 사라지므로 두 가지를 막아 둡니다.

**읽기 실패와 데이터 없음을 구분합니다.** 플레이나누 스토리지를 읽지 못한 로그인에서는 위 흐름도의 첫 줄로 빠져 아무것도 하지 않고, 그 로그인 동안 플레이나누 쓰기를 막습니다. 다음 로그인에서 다시 맞춥니다.

**서버 정본을 못 읽었으면 쓰지 않습니다.** `TrueBaseNanoo.SaveNow()`는 로컬이 서버 데이터를 반영한 뒤에만 동작합니다. 로그인·동기화가 끝나기 전에 부르면 경고만 남기고 넘어갑니다 — 그 시점 로컬은 기본값이라 내보내면 원본이 지워집니다.

::: warning 이미 덮어써진 데이터는 SDK가 되돌리지 못합니다
플레이나누 스토리지에는 이력이 없어, 덮어쓰면 그걸로 끝입니다. 이관을 시험할 때는 **지워져도 되는 계정**으로 하세요.
:::

## 비교 필드 커스터마이징 {#compare-by}

`updated_at` 대신 다른 필드로 최신 여부를 비교하려면 `CompareBy`를 등록합니다.

```csharp
// 게임 시작 시 1회 (첫 로그인 전)
PlayerSave.UseNanooConverters(map => map
    .CompareBy(r => r.lastSyncedAt, fallbackUtcOffsetHours: 9));   // 한국 표준시
```

값이 `DateTime`·`DateTimeOffset`이면 그대로 쓰고, 그 외 타입은 문자열로 변환해 파싱합니다. 값에 `Z`나 `+09:00` 같은 시간대 정보가 이미 있으면 그걸 그대로 쓰고, 없을 때만 `fallbackUtcOffsetHours`를 적용합니다. 기본값은 UTC를 뜻하는 0입니다 — 기기 시간대에 따라 비교 결과가 달라지는 걸 막기 위함입니다. 필드를 감싼 변환식도 됩니다: `r => DateTime.FromOADate(r.serverDataTime)`.

::: warning UseNanooConverters를 쓰면 CompareBy는 필수입니다
`UseNanooConverters`로 `.Field(...)` 등 뭐라도 등록했다면 `.CompareBy(...)`도 반드시 함께 등록해야 합니다. 빠뜨리면 `updated_at`으로 조용히 넘어가지 않고 첫 사용 시점에 예외가 발생합니다. `updated_at` 그대로 쓰려면 `.CompareBy(r => r.updated_at)`을 명시하세요. `UseNanooConverters`를 아예 안 쓰는 경우는 예외가 아니며, 그때는 지금처럼 `updated_at`이 기본 비교 기준입니다.
:::

## 데이터 변환 커스터마이징

`int` · `string` 등 단순 필드와 정상적인 JSON 배열/객체는 자동으로 변환됩니다. **특정 필드를 플레이나누에서 다른 형태로 저장**해야 하면, **코드에서** `PlayerSave.UseNanooConverters(...)`로 그 필드 변환만 등록합니다.

::: tip 첫 로그인/동기화 전에 한 번 호출
부트스트랩·로그인 매니저 등에서 게임 시작 시 1회 호출하면 됩니다.
:::

예를 들어 `List<int>` `[2, 3]`을 플레이나누엔 `"2_3"`으로 저장할 때:

```csharp
using System.Linq;

// 게임 시작 시 1회 (첫 로그인 전)
PlayerSave.UseNanooConverters(map => map
    .Field(r => r.itemIds,                                  // 필드 선택식 — 키 하드코딩·타입 지정 불필요
        v => string.Join("_", v),                           // List<int> → "2_3"
        s => s.Split('_').Select(int.Parse).ToList())       // "2_3" → List<int>
    .Field(r => r.isVip,                                    // 필드 더 필요하면 한 줄씩
        v => v ? "Y" : "N",
        s => s == "Y")
    .CompareBy(r => r.updated_at));                          // Field를 하나라도 등록했다면 필수
```

- **필드 선택식**, 즉 `r => r.itemIds`로 키 문자열 대신 필드를 직접 가리키므로 `List<int>`·`bool` 같은 타입도 자동 추론됩니다.
- 등록한 **그 필드만** 가공되고 나머지는 자동 처리됩니다. 동기화 비교 기준은 `.CompareBy`로 별도 지정합니다 — [비교 필드 커스터마이징](#compare-by) 참고.
- 플레이나누 직렬화·역직렬화 양쪽에 적용되며, **REST/DB 저장·로드에는 영향이 없습니다.**

단순한 키명 차이는 C# 필드명을 카멜케이스인 플레이나누 키에 맞추면 자동으로 연결됩니다. DB 컬럼명은 `[DataColumn]`으로 별도 지정하므로 영향이 없습니다.
