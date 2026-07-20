# 탈퇴 취소

```csharp
Task<SupabaseResult> Supabase.RedeemWithdrawalCancelAsync(string cancelToken = null)
```

탈퇴 예약을 취소합니다. 예약된 계정으로 로그인하면 게이트가 로그인을 `WithdrawalGateBlocked`로 막고 취소 토큰을 로컬에 저장하므로, 그 실패를 감지한 뒤 이 메서드를 **인자 없이** 호출하면 저장된 토큰으로 취소됩니다.

```csharp
var login = await Supabase.TriggerAutoLoginAsync();
if (!login.IsSuccess && login.Reason == SupabaseFailCode.WithdrawalGateBlocked)
{
    // 남은 유예 시간 등을 보여주고, 사용자가 취소를 선택하면
    var cancel = await Supabase.RedeemWithdrawalCancelAsync();
    if (cancel.IsSuccess)
        ShowMessage("탈퇴가 취소되었습니다. 다시 로그인해 주세요.");
    else
        ShowError(cancel.Reason);
}
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `cancelToken` | 취소 토큰. 비우면 게이트가 저장한 토큰을 사용 (기본값: `null`) |

**실패 원인**

| Reason | 설명 |
|--------|------|
| `SupabaseFailReason.WithdrawalCancelTokenEmpty` | 저장된 취소 토큰이 없습니다 |
| `SupabaseFailReason.WithdrawalCancelJwtVerifyMustBeOff` | 취소 Edge Function의 `verify_jwt`가 켜져 있습니다(꺼야 함) |
| `SupabaseFailReason.NetworkError` | 네트워크 오류 또는 타임아웃 |
