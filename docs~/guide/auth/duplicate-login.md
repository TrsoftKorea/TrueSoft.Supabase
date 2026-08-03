# 중복 로그인 감지

```csharp
event Action Supabase.OnDuplicateLoginDetected
```

다른 기기에서 같은 계정으로 로그인되면 발행됩니다. 앱 생명주기 전체를 관리하는 Manager에서 한 번만 등록하세요. 60초 주기로 서버와 세션 토큰을 비교해 감지하며, 주기는 `SupabaseSettings`의 **Duplicate Session Poll Seconds**에서 조정합니다.

```csharp
void Awake()     => Supabase.OnDuplicateLoginDetected += OnDuplicateLogin;
void OnDestroy() => Supabase.OnDuplicateLoginDetected -= OnDuplicateLogin;

void OnDuplicateLogin()
{
    // 강제 로그아웃 후 로그인 화면으로 이동
    _ = Supabase.SignOutFullyAsync();
}
```
