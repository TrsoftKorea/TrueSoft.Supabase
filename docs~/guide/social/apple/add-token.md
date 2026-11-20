# Apple 추가 연동 · 커스텀

```csharp
Task<SupabaseResult> Supabase.LinkAppleWithIdTokenAsync(
    string idToken,
    string rawNonce          = null,
    string authorizationCode = null)
```

이미 가진 Apple ID 토큰으로, 로그인된 계정(익명 포함)에 Apple 계정을 추가로 연동합니다. 일반적으로는 [Apple 추가 연동](/guide/social/apple/add)을 쓰세요.

```csharp
var result = await Supabase.LinkAppleWithIdTokenAsync(idToken, rawNonce, authorizationCode);
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
| `authorizationCode` | 토큰과 함께 받은 일회용 인증 코드. 넘겨야 탈퇴할 때 Apple 연결이 끊깁니다. [탈퇴 시 Apple 연결 해제](/guide/social/apple/#revoke)를 참고하세요 (기본값: `null`) |

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.IdentityAlreadyLinked` | 이 Apple 계정은 이미 다른 게임 계정에 연결되어 있습니다 |
| `SupabaseReason.IdTokenInvalid` | 인증 서버가 ID 토큰을 받지 않았습니다. 토큰이 깨졌거나 만료됐거나 nonce가 맞지 않습니다 |

**실사용 예시**

외부 Apple 로그인 플러그인이 토큰을 돌려주면 지금 계정에 연결하고 설정 화면의 연결 표시를 갱신합니다.

```csharp
public async void OnAppleCredential(string idToken, string rawNonce, string authCode)
{
    var result = await Supabase.LinkAppleWithIdTokenAsync(idToken, rawNonce, authCode);
    if (result.IsSuccess) { appleLinkedMark.SetActive(Supabase.IsLinkedWithApple); return; }

    if (result.Reason == SupabaseReason.IdentityAlreadyLinked)
        ShowPopup("이 Apple 계정은 다른 게임 계정에서 쓰고 있습니다");
    else
        ShowToast("Apple 계정을 연결하지 못했습니다");
}
```
