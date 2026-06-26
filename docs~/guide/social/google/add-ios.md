# Google 추가 연동 · iOS

```csharp
Task<SupabaseCallResult> Supabase.TryLinkGoogleWithIdTokenAsync(string idToken, string googleAccessToken = null)
```

이미 로그인된 계정(익명 포함)에 외부 SDK로 발급받은 ID 토큰으로 Google 계정을 추가 연동합니다.

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `idToken` | Google OAuth에서 발급받은 ID 토큰 |
| `googleAccessToken` | Google Access Token (기본값: `null`) |
