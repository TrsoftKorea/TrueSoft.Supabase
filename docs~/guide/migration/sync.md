# 데이터 동기화

## 자동 동기화 흐름

로그인 성공 시 SDK와 플레이나누 데이터가 자동으로 동기화됩니다.

```
SDK 행 없음 (신규 유저)
  └─ 플레이나누 데이터 있음 → SDK에 이관 후 서버 정본을 다시 읽어 로컬 반영
  └─ 플레이나누 데이터 없음 → LoadAsync (빈 행 생성)

SDK 행 있음 (기존 유저)
  └─ 플레이나누에 updated_at 없음 (이관 전 순수 플레이나누 데이터) → 플레이나누 우선 → SDK 갱신 후 로컬 반영
  └─ 플레이나누 updated_at > DB updated_at → 플레이나누 최신 → SDK 갱신 후 로컬 반영
  └─ DB updated_at ≥ 플레이나누 updated_at → SDK 최신 → 로컬 반영 후 플레이나누 갱신
```

SDK 저장 이후 플레이나누에도 자동으로 반영됩니다.

## 비교 필드 커스터마이징

`updated_at` 대신 다른 필드로 최신 여부를 비교하려면 `CompareBy`를 등록합니다.

```csharp
// 게임 시작 시 1회 (첫 로그인 전)
PlayerSave.UseNanooConverters(map => map
    .CompareBy(r => r.lastSyncedAt, fallbackUtcOffsetHours: 9));   // 한국 표준시
```

값을 문자열로 변환해 `DateTimeOffset`으로 파싱하므로 `string`·`DateTime` 등 타입은 상관없습니다. 값에 `Z`나 `+09:00` 같은 시간대 정보가 이미 있으면 그걸 그대로 쓰고, 없을 때만 `fallbackUtcOffsetHours`(기본값 0=UTC)를 적용합니다 — 기기 시간대에 따라 비교 결과가 달라지는 걸 막기 위함입니다.

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
        s => s == "Y"));
```

- **필드 선택식**(`r => r.itemIds`)으로 키 문자열 대신 필드를 직접 가리키므로 타입(`List<int>`·`bool`)도 자동 추론됩니다.
- 등록한 **그 필드만** 가공되고, 나머지(및 `updated_at`)는 자동 처리 → 동기화 비교도 그대로 유지됩니다.
- 플레이나누 직렬화·역직렬화 양쪽에 적용되며, **REST/DB 저장·로드에는 영향이 없습니다.**

단순한 키명 차이는 C# 필드명을 플레이나누 키(camelCase)에 맞추면 자동으로 연결됩니다. DB 컬럼명은 `[DataColumn]`으로 별도 지정하므로 영향이 없습니다.
