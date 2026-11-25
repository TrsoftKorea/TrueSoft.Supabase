# Apple 게스트 연동

```csharp
Task<SupabaseResult> Supabase.LinkAppleToGuestAsync()
```

익명 계정에 iOS에서 Apple 계정을 연동합니다. 기존 계정의 데이터는 그대로 이어집니다. [대시보드 설정](/guide/social/apple/setup)을 먼저 완료하세요.

```csharp
var result = await Supabase.LinkAppleToGuestAsync();
if (result.IsSuccess)
{
    // 연동 완료 — 기존 게스트 데이터 그대로 유지
    ShowLinkedBadge();
}
else
{
    ShowLinkError(result.Reason);
}
```

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.AppleSignInCancelled` | 플레이어가 로그인 창을 직접 닫았습니다 |
| `SupabaseReason.AppleSignInIosOnly` | iOS가 아닌 환경입니다. 에디터·Android도 여기에 해당합니다 |
| `SupabaseReason.AnonymousRequired` | 익명 세션이 아닙니다 |
| `SupabaseReason.AppleLinkFailed` | Apple 연동에 실패했습니다 |
| `SupabaseReason.IdentityAlreadyLinked` | 이 Apple 계정은 이미 다른 게임 계정에 연결되어 있습니다 |
| `SupabaseReason.UserBanned` | 차단된 계정입니다. 자세한 내용은 `result.BanInfo`에 있습니다 |

**실사용 예시**

게스트 플레이어가 설정 화면에서 Apple 연결을 누르면 진행 상황을 그 계정에 묶고 버튼을 숨깁니다.

```csharp
public async void OnClickSaveWithApple()
{
    var result = await Supabase.LinkAppleToGuestAsync();
    if (result.IsSuccess)
    {
        linkAppleButton.SetActive(Supabase.IsAnonymous);
        ShowToast("Apple 계정에 진행 상황을 저장했습니다");
        return;
    }

    switch (result.Reason)
    {
        case SupabaseReason.AppleSignInCancelled: break;
        case SupabaseReason.IdentityAlreadyLinked:
            ShowPopup("이 Apple 계정은 이미 다른 게임 계정에 연결되어 있습니다"); break;
        default:
            ShowToast("Apple 계정을 연결하지 못했습니다"); break;
    }
}
```

::: info 커스텀 토큰
직접 받은 Apple ID 토큰을 쓰려면 [Apple 게스트 연동 · 커스텀](/guide/social/apple/link-token)을 사용하세요.
:::

::: warning
익명 세션에서만 호출하세요.
:::
