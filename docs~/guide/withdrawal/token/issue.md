# 취소 토큰 발급

```csharp
Task<SupabaseResult<string>> Supabase.RequestWithdrawalCancelTokenAsync()
```

탈퇴 취소 토큰을 발급합니다.

```csharp
var result = await Supabase.RequestWithdrawalCancelTokenAsync();
if (result.IsSuccess)
{
    var cancelToken = result.Data;   // 발급된 취소 토큰
    ShowCancelToken(cancelToken);
}
```

**반환**

`.Data` — 발급된 탈퇴 취소 토큰 문자열.

**실패 원인**

| Reason | 설명 |
|--------|------|
| `SupabaseFailReason.WithdrawalCancelIssueFailed` | 토큰 발급 실패 |
| `SupabaseFailReason.NotSignedIn` | 로그인 상태가 아닙니다 |
