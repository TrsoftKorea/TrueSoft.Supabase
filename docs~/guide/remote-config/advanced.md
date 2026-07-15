# 더 알아보기

더 복잡한 JSON 구조나 타입이 필요할 때 참고하세요.

## 설정 클래스 작성

DB에 저장된 JSON 구조에 맞게 클래스를 작성합니다. 지원하는 타입은 다음과 같습니다.

```csharp
using System.Collections.Generic;
using Newtonsoft.Json;
using TrueBase.Unity;

[RemoteConfigKey("gameplay_v1")]
public class GameplayConfig
{
    // ── 기본 타입 ────────────────────────────────────────────────────────────
    public bool   enabled;          // JSON: "enabled"
    public int    maxLevel;         // JSON: "maxLevel"
    public float  spawnInterval;    // JSON: "spawnInterval"
    public string announcement;     // JSON: "announcement"

    // ── JSON 키와 필드명이 다를 때 ────────────────────────────────────────────
    [JsonProperty("max_stamina")]
    public int MaxStamina;          // JSON: "max_stamina" → C#: MaxStamina

    // ── nullable (JSON에 키가 없을 때 null로 남겨두고 싶을 때) ────────────────
    public int?   bonusLevel;       // null이면 기본값 적용
    public float? eventMultiplier;

    // ── 컬렉션 ────────────────────────────────────────────────────────────────
    public List<string>            bannedWords;    // JSON: ["word1", "word2"]
    public int[]                   stageClearExp;  // JSON: [100, 200, 400]
    public Dictionary<string, int> itemDropRate;   // JSON: {"sword": 10, "shield": 5}

    // ── 중첩 클래스 ───────────────────────────────────────────────────────────
    public StaminaConfig stamina;   // JSON: "stamina": { ... }
    public BattleConfig  battle;    // JSON: "battle":  { ... }

    public class StaminaConfig
    {
        public int   max;
        public int   regenSec;
        public float regenAmount;
    }

    public class BattleConfig
    {
        public float playerDmg;
        public float enemyHpScale;

        // 중첩 안의 중첩도 가능
        public BossConfig boss;

        public class BossConfig
        {
            public float hpMultiplier;
            public int   spawnFloor;
        }
    }
}
```

설정 클래스(중첩 포함)는 매개변수 없는 생성자로 역직렬화됩니다. 생성자를 직접 정의했다면 기본 생성자도 함께 두면 됩니다. 생성기로 만들면 자동으로 처리됩니다.

중첩 객체는 `?.`로 접근합니다. JSON에 해당 키가 없으면 `null`이므로 null 체크를 함께 합니다.

```csharp
float dmg    = cfg?.battle?.playerDmg ?? 1f;
float bossHp = cfg?.battle?.boss?.hpMultiplier ?? 1f;
int   maxSt  = cfg?.stamina?.max ?? 100;
```

## 클래스 생성기

JSON 구조를 보고 C# 클래스를 직접 작성하는 대신 Inspector에서 자동으로 생성할 수 있습니다.

1. `SupabaseSettings` 에셋 선택 → Inspector 하단 **Remote Config 클래스 생성** foldout 열기
2. Secret 키 입력 후 **키 목록 가져오기** — `remote_config` 테이블의 키 목록이 드롭다운으로 표시됩니다
3. 키 선택 후 **필드 파싱** — DB의 JSON 구조가 필드 목록으로 표시됩니다
4. 타입을 바꾸려면 **CSV로 저장하기 → 편집 → CSV 불러오기**로 반영합니다. `int`를 `long`으로, `string`을 `List<string>`으로 바꾸는 식입니다
5. 클래스명 확인 (키 이름에서 자동 유도, 수정 가능)
6. **소스 생성** → **저장**으로 `.cs` 파일을 프로젝트에 저장합니다

CSV의 열은 `field` · `type` · `include`입니다. `field`는 필드 경로로, 매칭 키이므로 수정하면 안 됩니다. `include`는 `1`이 포함, `0`이 제외입니다. 중첩 객체 행의 `type`은 `(중첩 객체)`로 표시되며 수정할 수 없습니다. [유저 데이터 클래스의 CSV 편집](../user-data/class-gen#csv-edit)과 같은 방식입니다.

생성된 클래스는 `[RemoteConfigKey]` 어트리뷰트가 포함되어 있어 바로 사용할 수 있습니다.

```csharp
// 생성된 클래스는 세 패턴 모두 그대로 사용 가능
var reader  = RemoteConfig<GameplayV1Config>.CreateReader();
var binding = RemoteConfig<GameplayV1Config>.CreateBinding(pollInterval: 60f);
var listener = RemoteConfig<GameplayV1Config>.CreateListener(cfg => Apply(cfg), pollInterval: 60f);
```

::: tip
같은 JSON 키 이름이 중첩 객체에서 반복되면 기존 파일 타입 복원이 해당 키에 대해 스킵됩니다. JSON 키 이름이 겹치지 않도록 설계하세요.
:::
