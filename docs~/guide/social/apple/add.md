# Apple 추가 연동

```csharp
Task<SupabaseCallResult> Supabase.TryLinkAppleWithIdTokenAsync(string idToken, string rawNonce = null)
```

이미 로그인된 계정(익명 포함)에 외부 SDK로 발급받은 ID 토큰으로 Apple 계정을 추가 연동합니다.

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `idToken` | Sign in with Apple에서 발급받은 ID 토큰 |
| `rawNonce` | 토큰과 함께 전달된 nonce (기본값: `null`) |
