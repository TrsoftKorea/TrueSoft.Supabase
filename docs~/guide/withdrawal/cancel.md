# 탈퇴 취소

```csharp
Task<SupabaseResult> Supabase.RedeemWithdrawalCancelAsync(string cancelToken = null)
```

탈퇴 예약을 취소합니다. 예약된 계정으로 로그인하면 `WithdrawalGateBlocked`로 막히며, 로그인 결과의 `WithdrawalCancelToken`을 넘겨 비로그인 상태로 취소합니다. 토큰을 비우면 SDK가 게이트에서 저장해둔 토큰을 사용합니다.

```csharp
var login = await Supabase.TriggerAutoLoginAsync();
if (login.Reason == SupabaseFailCode.WithdrawalGateBlocked)
{
    // 남은 유예 시간 등을 보여주고, 사용자가 취소를 선택하면
    var cancel = await Supabase.RedeemWithdrawalCancelAsync(login.WithdrawalCancelToken);
    if (cancel.IsSuccess)
        ShowMessage("탈퇴가 취소되었습니다. 다시 로그인해 주세요.");
    else
        ShowError(cancel.Reason);
}
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `cancelToken` | 취소 토큰. 로그인 결과의 `.WithdrawalCancelToken`. 비우면 게이트가 저장한 토큰 사용 (기본값: `null`) |

**에러 코드**

| ErrorCode | 설명 |
|--------|------|
| `SupabaseErrorCode.WithdrawalCancelTokenEmpty` | 저장된 취소 토큰이 없습니다 |
| `SupabaseErrorCode.WithdrawalCancelJwtVerifyMustBeOff` | 취소 Edge Function의 `verify_jwt`를 꺼야 합니다 |
| `SupabaseErrorCode.NetworkError` | 네트워크 오류 또는 타임아웃 |
