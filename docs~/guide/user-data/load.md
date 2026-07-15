# 로드

## 로드 호출

로그인과 데이터 로드는 별개 단계입니다. **로그인 완료 후 한 번** 데이터를 로드합니다.

```csharp
await Supabase.SignInAnonymouslyAsync();   // 1. 로그인
await PlayerSave.LoadAsync();              // 2. 데이터 로드
```

`LoadAsync`는 서버에서 유저 데이터를 가져와 `Current`에 채웁니다. 신규 유저는 이때 DB 행이 초기값(컬럼 DEFAULT)으로 자동 생성됩니다. `await` 반환 시점에 적용이 끝나 있으므로, 로드 후 실행할 코드는 `await LoadAsync()` 다음 줄에 이어서 작성합니다.

자동 로그인(`Supabase.TriggerAutoLoginAsync()`)도 로그인만 수행하므로, 수동 로그인과 동일하게 성공 후 위 로드 단계를 직접 호출합니다.

## 신규 유저 후처리 {#new-user}

`LoadAsync()`는 `SupabaseLoadResult`를 반환합니다. 그 `IsNewUser`로 신규 유저를 분기합니다. **DB에 본인 행이 없던 신규 유저의 최초 로드**에서만 `true`이고, 기존 유저 로드나 재로그인 시에는 `false`입니다. 값이 호출에 묶여 불변이므로, 이후 재로드해도 이 결과의 `IsNewUser`는 바뀌지 않습니다.

```csharp
var result = await PlayerSave.LoadAsync();
if (result.IsNewUser)
{
    // 신규 유저 전용 로직: 튜토리얼 시작, 웰컴 보너스 지급 등
    PlayerSave.Coins = 100;   // 세팅한 값은 자동 저장에 반영됩니다
}
```

## 로드 전 초기값 {#preload-fallback}

컬럼 DEFAULT로 표현하기 어려운 컬렉션 초기값(리스트 크기, 특정 위치 값 등)은 **로그인 후·로드 전에** 미리 세팅해 둡니다. 로드 시 서버에 값이 있으면 서버값으로 덮고, 서버에 값이 없으면(SQL NULL) 세팅해 둔 초기값을 유지합니다. 신규 유저뿐 아니라 **복귀 유저·게임 업데이트로 컬럼이 추가된 경우**에도 자동으로 초기값이 채워집니다.

```csharp
// 로그인 후, 로드 전에 초기값을 세팅
PlayerSave.Items.EnsureCount(5);    // 리스트 크기 확보
PlayerSave.Items[2] = starterItem;  // 특정 위치 기본값

await PlayerSave.LoadAsync();        // 서버에 값이 있으면 덮고, 없으면 위 값을 유지
```

::: warning 컬렉션 전용 · DB DEFAULT는 NULL로
로드 전 초기값은 **컬렉션/참조 타입 컬럼(jsonb)에서만** 동작합니다. 스칼라(`int`·`bool` 등)는 서버의 "없음"과 "기본값"을 구분할 수 없어 항상 서버값이 우선되므로, 스칼라 초기값은 DB 컬럼 DEFAULT로 지정하세요.

또한 대상 jsonb 컬럼의 **DB DEFAULT를 NULL로** 둬야 합니다. `[]`·`{}` 같은 DEFAULT가 있으면 서버가 항상 그 값을 반환해 로드 전 초기값이 무시됩니다.
:::

::: tip
로드 전 세팅은 첫 로드 시점에 한 번 스냅샷됩니다. 유지된 초기값은 서버 정본과의 **차이(diff)만** PATCH되며, 그 저장까지 끝난 뒤 `await LoadAsync()`가 반환됩니다.
:::
