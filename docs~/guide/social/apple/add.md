# Apple 추가 연동

```csharp
Task<SupabaseResult> Supabase.LinkAppleNativeAsync()
```

이미 로그인된 계정에 iOS에서 Apple 계정을 추가로 연동합니다. 익명 계정도 가능합니다. [대시보드 설정](/guide/social/apple/setup)을 먼저 완료하세요.

```csharp
var result = await Supabase.LinkAppleNativeAsync();
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

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.AppleSignInCancelled` | 플레이어가 로그인 창을 직접 닫았습니다 |
| `SupabaseReason.AppleSignInIosOnly` | iOS가 아닌 환경입니다. 에디터·Android도 여기에 해당합니다 |
| `SupabaseReason.AppleLinkFailed` | Apple 연동에 실패했습니다 |
| `SupabaseReason.IdentityAlreadyLinked` | 이 Apple 계정은 이미 다른 게임 계정에 연결되어 있습니다 |

**실사용 예시**

설정 화면의 Apple 연결 버튼에서 연결하고, 플레이어가 창을 닫았으면 아무것도 띄우지 않습니다.

```csharp
public async void OnClickLinkApple()
{
    var result = await Supabase.LinkAppleNativeAsync();
    if (result.IsSuccess) { appleLinkedMark.SetActive(Supabase.IsLinkedWithApple); return; }

    switch (result.Reason)
    {
        case SupabaseReason.AppleSignInCancelled: break;
        case SupabaseReason.IdentityAlreadyLinked:
            ShowPopup("이 Apple 계정은 다른 게임 계정에서 쓰고 있습니다"); break;
        default:
            ShowToast("Apple 계정을 연결하지 못했습니다"); break;
    }
}
```

::: info 커스텀 토큰
직접 받은 Apple ID 토큰을 쓰려면 [Apple 추가 연동 · 커스텀](/guide/social/apple/add-token)을 사용하세요.
:::
