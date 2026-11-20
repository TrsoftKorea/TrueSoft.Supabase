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

**실사용 예시**

설정 화면의 탈퇴 버튼에서 확인을 받고 신청한 뒤 타이틀로 돌아갑니다.

```csharp
public void OnClickWithdraw()
{
    ShowConfirmPopup("탈퇴를 신청합니다. 유예 기간 안에 다시 로그인하면 취소할 수 있습니다", onConfirm: async () =>
    {
        var result = await Supabase.RequestWithdrawalAsync();
        if (!result.IsSuccess) { ShowToast("탈퇴를 신청하지 못했습니다. 다시 시도해 주세요"); return; }
        SceneManager.LoadScene("Title");   // 이미 로그아웃된 상태입니다
    });
}
```
