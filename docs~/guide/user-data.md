# 유저 데이터

유저 데이터는 게임 진행 상황(레벨, 골드, 아이템 등)을 서버에 저장하고 기기 간 동기화하는 기능입니다.  
변경된 필드만 서버에 전송하며, 쿨타임 배치로 불필요한 요청을 줄입니다.

---

## 동작 방식

`StaticUserSave<TRow>`는 유저 세이브 데이터를 관리하는 기반 클래스입니다.

| 동작 | 설명 |
|------|------|
| Diff 패치 | 이전 상태와 비교해 변경된 필드만 서버에 전송합니다. 변경이 없으면 네트워크 요청을 생략합니다. |
| 쿨타임 자동 저장 | 프로퍼티 값을 바꾸거나 컬렉션을 수정하면 `SupabaseRuntime`이 쿨타임 주기로 자동 업로드합니다. |
| 즉시 저장 | 씬 전환·결제 완료처럼 지금 바로 저장해야 할 때는 `TrySaveAllAsync()`로 강제 플러시합니다. |

---

## 클래스 생성

`SupabaseSettings` 에셋 Inspector 하단에서 Secret 키를 입력하고 **스키마 가져오기 → 소스 생성 → 저장**으로 `PlayerSave.cs`를 생성합니다.

::: info
타입을 자동으로 결정하지 못한 컬럼은 ⚠ 표시되며, 드롭다운에서 직접 지정할 수 있습니다.
:::

생성된 파일은 다음과 같은 구조입니다.

```csharp
// PlayerSave.cs — 생성기로 자동 생성, 직접 수정하지 않습니다
using Newtonsoft.Json;
using TrueBase.Core.Data;
using TrueBase.Unity;

public sealed partial class PlayerSave : StaticUserSave<PlayerSave.Row>
{
    public static readonly PlayerSave Instance = new();
    private PlayerSave() : base() { }

    public static Task<bool> TryLoadAsync() => Instance.TryLoadAsync();

    // 필드는 internal — 데이터는 아래 정적 프로퍼티로 접근합니다.
    [Serializable]
    [JsonObject(MemberSerialization.Fields)]   // Newtonsoft가 internal 필드를 저장/로드
    public sealed class Row
    {
        [DataColumn("level")]     internal int            level;
        [DataColumn("coins")]     internal int            coins;
        [DataColumn("inventory")] internal List<int>      inventory = new List<int>();   // 컬렉션은 빈 인스턴스로 초기화
    }

    // 스칼라: get/set — 쓰면 MarkDirty 자동
    public static int Level
    {
        get => Instance.Current.level;
        set { Instance.Current.level = value; Instance.MarkDirty(); }
    }

    public static int Coins
    {
        get => Instance.Current.coins;
        set { Instance.Current.coins = value; Instance.MarkDirty(); }
    }

    // 컬렉션: 일반 컬렉션처럼 사용 — 제자리 수정도 자동 동기화에 반영
    public static List<int> Inventory
    {
        get => Instance.Current.inventory;
        set { Instance.Current.inventory = value ?? new List<int>(); Instance.MarkDirty(); }
    }
}
```

새 컬럼이 생기면 생성기를 다시 실행해 덮어씁니다.

---

## 로드

로그인과 데이터 로드는 별개 단계입니다. **로그인 완료 후 한 번** 데이터를 로드합니다.

```csharp
await Supabase.TrySignInAnonymouslyAsync();   // 1. 로그인
await PlayerSave.TryLoadAsync();              // 2. 데이터 로드
```

`TryLoadAsync`는 서버에서 유저 데이터를 가져와 `Current`에 채웁니다. 신규 유저는 이때 DB 행이 초기값(컬럼 DEFAULT)으로 자동 생성됩니다. 로드가 끝난 뒤 실행할 코드가 있으면 `PlayerSave.Instance.OnLoaded`를 구독하세요.

여러 세이브 클래스를 한 번에 로드하려면 `Supabase.TryLoadAllUserSavesAsync()`를 사용합니다.

`SupabaseRuntime.TriggerAutoLoginAsync()`(자동 로그인)는 이 로드를 내부에서 함께 처리하므로, 수동 로그인에서만 위 2단계를 직접 호출하면 됩니다.

---

## 읽기 / 쓰기

생성된 static 프로퍼티로 접근합니다.

```csharp
int lv = PlayerSave.Level;

PlayerSave.Level = 10;
PlayerSave.Coins += 100;
```

값을 쓰면 `MarkDirty()`가 자동으로 호출되고, `SupabaseRuntime`이 쿨타임 주기로 자동 저장합니다.

### 컬렉션

`List`, 배열, `Dictionary` 컬럼은 일반 컬렉션과 똑같이 다루면 됩니다. 항목을 추가하거나 바꾸면 다른 값과 마찬가지로 쿨타임 주기에 자동 저장됩니다.

```csharp
PlayerSave.Inventory.Add(5);
PlayerSave.Inventory[0] = 9;
PlayerSave.Stats["atk"] = 100;
PlayerSave.Matrix[0].Add(3);               // List<List<int>> 같은 중첩도 동일

PlayerSave.Inventory = new List<int>{1, 2}; // 통째 교체도 가능
```

쓸 수 있는 타입과 직렬화 규칙은 [데이터 타입](./data-types)을 참고하세요.

---

## 즉시 저장

씬 전환·결제 완료·앱 종료처럼 지금 당장 저장해야 할 때 사용합니다.

```csharp
Task<bool> Supabase.TrySaveAllAsync(int timeoutMs = 5000)
```

변경된 모든 세이브 데이터를 즉시 서버에 업로드합니다. 성공 시 `true`, 타임아웃 또는 실패 시 `false`를 반환합니다.

**파라미터**

| 파라미터 | 설명 | 타입 |
|----------|------|------|
| `timeoutMs` | 최대 대기 시간 ms (기본값: `5000`) | `int` |

::: info
`SupabaseRuntime`을 씬에 배치하면 `OnApplicationPause` / `OnApplicationQuit` 시 자동으로 플러시합니다.
:::

---

## 컬럼 추가

Retool에서 `user_saves` 테이블에 컬럼을 추가합니다. 추가 후 생성기를 다시 실행하면 `PlayerSave.cs`에 해당 프로퍼티가 자동으로 추가됩니다.

지원하는 필드 타입과 직렬화 규칙은 [데이터 타입](./data-types)을 참고하세요.

