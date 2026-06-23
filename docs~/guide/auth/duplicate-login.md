# 중복 로그인 감지

다른 기기에서 같은 계정으로 로그인되면 `OnDuplicateLoginDetected`가 발행됩니다.  
앱 생명주기 전체를 관리하는 Manager에서 한 번만 등록하세요.

60초 주기로 서버와 세션 토큰을 비교해 감지합니다. `SupabaseSettings > 중복 감지 폴링 주기`에서 조정할 수 있습니다.

```csharp
void Awake()     => Supabase.OnDuplicateLoginDetected += OnDuplicateLogin;
void OnDestroy() => Supabase.OnDuplicateLoginDetected -= OnDuplicateLogin;

void OnDuplicateLogin()
{
    // 강제 로그아웃 후 로그인 화면으로 이동
    _ = Supabase.TrySignOutFullyAsync();
}
```

---
