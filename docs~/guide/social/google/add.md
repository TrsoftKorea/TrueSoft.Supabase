# Google 추가 연동

```csharp
Task<SupabaseResult> Supabase.LinkGoogleNativeAsync()
```

이미 로그인된 계정에 Google 계정을 추가 연동합니다. [신규 로그인](./signin)과 같은 계정 선택 창을 Android·iOS에서 띄웁니다. 익명 계정도 가능합니다.

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
| `SupabaseReason.GoogleSignInCancelled` | 사용자가 계정 선택 창을 닫음 |
| `SupabaseReason.GoogleSignInFailed` | Google 로그인 처리 오류 |
| `SupabaseReason.OAuthLoginInProgress` | iOS · Google 로그인이 이미 진행 중. 이 호출만 실패하고 진행 중인 로그인은 그대로 이어짐 |
| `SupabaseReason.GoogleWebClientIdEmpty` | Android · `googleWebClientId`가 비어 있음 |
| `SupabaseReason.GoogleIosClientIdInvalid` | iOS · `googleIosClientId`가 비었거나, 형식이 틀리거나, 웹 클라이언트 ID와 같음 |
| `SupabaseReason.GoogleLinkFailed` | Supabase identity 연동 실패 |
| `SupabaseReason.NetworkError` | 네트워크 오류 또는 타임아웃 |
