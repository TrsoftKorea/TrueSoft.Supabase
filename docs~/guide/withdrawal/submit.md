# 탈퇴 신청

```csharp
Task<SupabaseResult> Supabase.RequestWithdrawalAsync()
```

탈퇴를 예약합니다. 요청이 성공하면 즉시 로그아웃 처리됩니다.

```csharp
var result = await Supabase.RequestWithdrawalAsync();
if (result.IsSuccess)
{
    ReturnToTitle();   // 신청 성공 — 이미 로그아웃됨
}
else
{
    ShowError(result.Reason);
}
```

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.NotSignedIn` | 로그인 상태가 아닙니다 |
| `SupabaseReason.NetworkError` | 네트워크 오류 또는 타임아웃 |
