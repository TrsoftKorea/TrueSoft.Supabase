# 데이터 동기화

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

## 필드명 규칙

플레이나누 Storage JSON은 camelCase 키를 사용합니다. SDK의 기본 변환은 C# 필드명을 그대로 JSON 키로 사용하므로, **C# 필드명을 camelCase로 선언하면 별도 매핑 없이 자동으로 연결됩니다.**

DB 컬럼명은 `[DataColumn]`으로 별도 지정하므로 C# 필드명과 달라도 됩니다.

```csharp
[Serializable]
[JsonObject(MemberSerialization.Fields)]   // Newtonsoft가 internal 필드(필드명을 JSON 키로) 처리
public sealed class Row
{
    [DataColumn("player_level")] internal int       playerLevel;          // DB: player_level, 플레이나누: playerLevel
    [DataColumn("item_ids")]     internal List<int> itemIds = new();       // DB: item_ids,     플레이나누: itemIds
}
```

## 데이터 변환 커스터마이징

`int` · `string` 등 단순 필드와 정상적인 JSON 배열/객체는 자동으로 변환됩니다. **특정 필드를 플레이나누에서 다른 형태로 저장**해야 하면, 생성 클래스의 partial에서 `ConfigureNanoo`를 override해 그 필드 변환만 등록합니다. 예를 들어 `List<int>` `[2, 3]`을 플레이나누엔 `"2_3"`으로 저장할 때:

```csharp
using System.Linq;

public sealed partial class PlayerSave   // 생성기가 만든 클래스
{
    protected override void ConfigureNanoo(NanooFieldMap<Row> map) => map
        .Field(r => r.itemIds,                                  // 필드 선택식 — 키 하드코딩·타입 지정 불필요
            v => string.Join("_", v),                           // List<int> → "2_3"
            s => s.Split('_').Select(int.Parse).ToList())       // "2_3" → List<int>
        .Field(r => r.isVip,                                    // 필드 더 필요하면 한 줄씩
            v => v ? "Y" : "N",
            s => s == "Y");
}
```

- **필드 선택식**(`r => r.itemIds`)으로 등록하므로 키 문자열을 쓸 필요가 없고, 타입(`List<int>`·`bool`)도 자동 추론됩니다. 필드명을 바꾸면 컴파일러가 잡아줍니다.
- 등록한 **그 필드만** 가공되고, 나머지(및 `updated_at`)는 자동 처리 → 동기화 비교도 그대로 유지됩니다.
- 플레이나누 직렬화·역직렬화 양쪽에 적용되며, **REST/DB 저장·로드에는 영향이 없습니다.**
- 변환 방식이 더 복잡해 전체 JSON 모양을 직접 짜야 하면, `NanooSerializeJson` / `NanooDeserializeJson`을 직접 override하세요(등록 변환 대신 그게 쓰입니다).

단순한 키명 차이는 변환 등록이 아니라 [필드명 규칙](/guide/migration/sync#필드명-규칙)으로 해결합니다(C# 필드명을 플레이나누 키에 맞추고 DB 컬럼은 `[DataColumn]`로 지정).

---
