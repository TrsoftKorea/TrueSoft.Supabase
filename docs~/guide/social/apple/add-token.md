# Apple 추가 연동 · 커스텀

```csharp
Task<SupabaseResult> Supabase.LinkAppleWithIdTokenAsync(string idToken, string rawNonce = null)
```

이미 가진 Apple ID 토큰으로, 로그인된 계정(익명 포함)에 Apple 계정을 추가로 연동합니다. 일반적으로는 [Apple 추가 연동](./add)을 쓰세요.

```csharp
var result = await Supabase.LinkAppleWithIdTokenAsync(idToken, rawNonce);
if (result.IsSuccess)
{
    // 연동 완료 — 현재 계정에 Apple 계정 추가
    ShowLinkedBadge();
}
else
{
    ShowLinkError(result.Reason);
}
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `idToken` | Sign in with Apple에서 발급받은 ID 토큰 |
| `rawNonce` | 토큰과 함께 전달된 nonce (기본값: `null`) |
