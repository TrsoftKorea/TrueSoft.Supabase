# 로드

## 로드 호출

로그인과 데이터 로드는 별개 단계입니다. **로그인 완료 후 한 번** 데이터를 로드합니다.

```csharp
await Supabase.SignInAnonymouslyAsync();   // 1. 로그인
await PlayerSave.LoadAsync();              // 2. 데이터 로드
```

`LoadAsync`는 서버에서 유저 데이터를 가져와 `Current`에 채웁니다. 신규 유저는 이때 DB 행이 초기값(컬럼 DEFAULT)으로 자동 생성됩니다. `await` 반환 시점에 적용이 끝나 있으므로, 로드 후 실행할 코드는 `await LoadAsync()` 다음 줄에 이어서 작성합니다.

여러 세이브 클래스를 한 번에 로드하려면 `Supabase.LoadAllUserSavesAsync()`를 사용합니다.

자동 로그인(`Supabase.TriggerAutoLoginAsync()`)도 로그인만 수행하므로, 수동 로그인과 동일하게 성공 후 위 로드 단계를 직접 호출합니다.

## 신규 유저 초기값 {#new-user}

컬럼 DEFAULT로 표현하기 어려운 초기 데이터(예: JSON 컬렉션의 시작 아이템)는 `OnFirstLoad`에서 설정합니다. **DB에 본인 행이 없던 신규 유저의 최초 로드 시에만** 발행되며, 여기서 넣은 값은 그 자리에서 서버에 저장됩니다. 로그인 전에 한 번 구독하세요.

```csharp
PlayerSave.OnFirstLoad += () =>
{
    // 신규 유저에게 기본 영웅 A1 하나를 지급. 나머지 영웅은 0 그대로.
    PlayerSave.Heroes[HeroName.A1].Count = 1;
};
```

기존 유저 로드나 재로그인 시에는 발행되지 않으므로, 초기 지급이 중복되지 않습니다.

::: tip
`OnFirstLoad`에서 설정한 값은 기본값과의 **차이(diff)만** 서버에 PATCH됩니다. 이 초기 저장까지 끝난 뒤 `await LoadAsync()`가 반환됩니다.
:::
