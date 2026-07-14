# 로드

로그인과 데이터 로드는 별개 단계입니다. **로그인 완료 후 한 번** 데이터를 로드합니다.

```csharp
await Supabase.SignInAnonymouslyAsync();   // 1. 로그인
await PlayerSave.LoadAsync();              // 2. 데이터 로드
```

`LoadAsync`는 서버에서 유저 데이터를 가져와 `Current`에 채웁니다. 신규 유저는 이때 DB 행이 초기값(컬럼 DEFAULT)으로 자동 생성됩니다. 로드가 끝난 뒤 실행할 코드가 있으면 `PlayerSave.Instance.OnLoaded`를 구독하세요.

여러 세이브 클래스를 한 번에 로드하려면 `Supabase.LoadAllUserSavesAsync()`를 사용합니다.

`Supabase.TriggerAutoLoginAsync()`(자동 로그인)는 이 로드를 내부에서 함께 처리하므로, 수동 로그인에서만 위 2단계를 직접 호출하면 됩니다.
