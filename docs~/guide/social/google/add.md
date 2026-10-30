# Google 추가 연동

```csharp
Task<SupabaseResult> Supabase.LinkGoogleNativeAsync()
```

이미 로그인된 계정에 Google 계정을 추가 연동합니다. [신규 로그인](/guide/social/google/signin)과 같은 계정 선택 창을 Android·iOS에서 띄웁니다. 익명 계정도 가능합니다.

```csharp
var result = await Supabase.LinkGoogleNativeAsync();
if (result.IsSuccess)
{
    // 연동 완료
}
```

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.GoogleSignInCancelled` | 플레이어가 계정 선택 창을 닫았습니다 |
| `SupabaseReason.GoogleSignInFailed` | Google 로그인 처리 중 오류가 났습니다 |
| `SupabaseReason.OAuthLoginInProgress` | iOS에서 Google 로그인이 이미 진행 중입니다. 이 호출만 실패하고 진행 중인 로그인은 그대로 이어집니다 |
| `SupabaseReason.GoogleWebClientIdEmpty` | Android에서 `googleWebClientId`가 비어 있습니다 |
| `SupabaseReason.GoogleIosClientIdInvalid` | iOS에서 `googleIosClientId`가 비었거나, 형식이 틀리거나, 웹 클라이언트 ID와 같습니다 |
| `SupabaseReason.GoogleLinkFailed` | Supabase 계정 연동에 실패했습니다 |
