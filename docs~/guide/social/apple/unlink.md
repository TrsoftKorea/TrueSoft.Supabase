# 연동 해제

```csharp
Task<SupabaseResult> Supabase.UnlinkAppleAsync()
```

현재 계정에서 Apple 연동을 해제합니다. 해제 후 `Supabase.IsLinkedWithApple`이 `false`가 됩니다.

```csharp
var r = await Supabase.UnlinkAppleAsync();
if (!r && r.Reason == SupabaseReason.CannotUnlinkLastIdentity)
    ShowToast("최소 하나의 로그인 수단은 남겨야 합니다.");
```

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.IdentityNotLinked` | Apple이 현재 계정에 연동되어 있지 않습니다. |
| `SupabaseReason.CannotUnlinkLastIdentity` | 마지막 남은 연동이라 해제할 수 없습니다. |
| `SupabaseReason.UnlinkFailed` | 연동 해제에 실패했습니다. |

**실사용 예시**

설정 화면의 Apple 연결 해제 버튼에서 확인을 받고 해제한 뒤 연결 표시를 갱신합니다.

```csharp
public void OnClickUnlinkApple()
{
    ShowConfirmPopup("Apple 계정 연결을 해제합니다", onConfirm: async () =>
    {
        var result = await Supabase.UnlinkAppleAsync();
        if (result.Reason == SupabaseReason.CannotUnlinkLastIdentity)
            ShowPopup("다른 로그인 수단을 먼저 연결해 주세요");
        else if (!result.IsSuccess)
            ShowToast("연결을 해제하지 못했습니다");

        appleLinkedMark.SetActive(Supabase.IsLinkedWithApple);
    });
}
```

::: warning 마지막 연동은 해제할 수 없습니다
계정에 연동이 하나뿐이면 해제 시 로그인 수단이 사라지므로 거부됩니다. 둘 이상 연동했을 때 하나를 해제할 수 있습니다.
:::
