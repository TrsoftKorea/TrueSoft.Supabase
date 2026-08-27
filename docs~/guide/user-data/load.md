# 로드

## 로드 호출

```csharp
Task<SupabaseLoadResult> Supabase.LoadUserSaveAsync()
```

로그인과 데이터 로드는 별개 단계입니다. **로그인 완료 후 한 번** 데이터를 로드합니다.

```csharp
await Supabase.SignInAnonymouslyAsync();   // 1. 로그인
await Supabase.LoadUserSaveAsync();              // 2. 데이터 로드
```

`LoadAsync`는 서버에서 유저 데이터를 가져와 `Current`에 채웁니다. 신규 유저는 이때 DB 행이 초기값(컬럼 DEFAULT)으로 자동 생성됩니다. `await` 반환 시점에 적용이 끝나 있으므로, 로드 후 실행할 코드는 `await LoadAsync()` 다음 줄에 이어서 작성합니다.

자동 로그인(`Supabase.TriggerAutoLoginAsync()`)도 로그인만 수행하므로, 수동 로그인과 동일하게 성공 후 위 로드 단계를 직접 호출합니다.

**반환**

| 프로퍼티 | 타입 | 설명 |
|---------|------|------|
| `.IsNewUser` | `bool` | DB에 본인 행이 없던 신규 유저의 최초 로드에서만 `true`. 값은 호출에 묶여 불변이며, 활용은 아래 [신규 유저 후처리](#new-user) |

## 신규 유저 후처리 {#new-user}

`LoadAsync()`는 `SupabaseLoadResult`를 반환합니다. 그 `IsNewUser`로 신규 유저를 분기합니다. **DB에 본인 행이 없던 신규 유저의 최초 로드**에서만 `true`이고, 기존 유저 로드나 재로그인 시에는 `false`입니다. 값이 호출에 묶여 불변이므로, 이후 재로드해도 이 결과의 `IsNewUser`는 바뀌지 않습니다.

```csharp
var result = await Supabase.LoadUserSaveAsync();
if (result.IsNewUser)
{
    // 신규 유저 전용 로직: 튜토리얼 시작, 웰컴 보너스 지급 등
    PlayerSave.Coins = 100;   // 세팅한 값은 자동 저장에 반영됩니다
}
```

## 로드 전 초기값 {#preload-fallback}

컬럼 DEFAULT로 표현하기 어려운, 리스트 크기·특정 위치 값 같은 컬렉션 초기값은 **로그인 후·로드 전에** 미리 세팅해 둡니다. 신규 유저뿐 아니라 **복귀 유저·게임 업데이트로 컬럼이 추가된 경우**에도 자동으로 초기값이 채워집니다.

`AutoList`·`AutoDict` 등 Auto 컬렉션은 **요소 단위로 병합**됩니다 — 서버에 **비기본값**이 든 인덱스·키는 서버값을 유지하고, **서버에 없거나 값이 기본값인** 슬롯·키만 세팅해 둔 값으로 채웁니다. 게임 업데이트로 리스트 크기가 3→4로 커져도 기존에 실제 값이 든 슬롯은 그대로 두고, 비었거나 기본값인 자리만 초기값이 들어갑니다.

```csharp
// 로그인 후, 로드 전에 초기값을 세팅
PlayerSave.Stages.EnsureCount(4);       // 새 버전의 크기 확보
PlayerSave.Stages[3] = firstStageState; // 늘어난 슬롯의 초기값

await Supabase.LoadUserSaveAsync();            // 서버에 실제 값이 든 슬롯은 유지, 없거나 기본값인 슬롯만 채워짐
```

이중 리스트(`AutoList2D`)는 `PlayerSave.Grid.EnsureSize(rows, cols)`로 같은 방식의 크기를 확보합니다. 특정 행만 열을 확보하려면 `PlayerSave.Grid[i].EnsureCount(cols)`를 씁니다.

::: warning 컬렉션 전용
로드 전 초기값은 **컬렉션/참조 타입 컬럼(jsonb)에서만** 동작합니다. 스칼라(`int`·`bool` 등)는 서버의 "없음"과 "기본값"을 구분할 수 없어 항상 서버값이 우선되므로, 스칼라 초기값은 DB 컬럼 DEFAULT로 지정하세요. 반대로 **컬렉션(jsonb) 컬럼에는 DB DEFAULT를 걸지 마세요** — Retool로 컬럼을 만들면 자동으로 NULL 허용·기본값 없음이 됩니다. DB에 값이 채워져 있으면 fallback이 서버 우선으로 밀립니다.

"기본값" 판정은 필드의 `[AutoDefault]`를 기준으로 하며, 없으면 타입 기본값을 씁니다. 클래스 같은 **참조 타입 원소**는 값 비교가 어려워, 서버에 non-null 인스턴스가 있으면 기본값과 구조적으로 같아도 서버값이 유지됩니다. 다만 **`null` 원소는 항상 "데이터 없음"으로 보고 fallback으로 채워집니다.** 값 타입·구조체는 기본값이면 채워집니다.
:::

::: tip
로드 전 세팅은 첫 로드 시점에 한 번 스냅샷됩니다. 유지된 초기값은 서버 정본과의 **차이(diff)만** PATCH되며, 그 저장까지 끝난 뒤 `await LoadAsync()`가 반환됩니다.
:::
