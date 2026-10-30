# 즉시 탈퇴

```csharp
Task<SupabaseResult> Supabase.DeleteAccountNowAsync()
```

로그인한 계정을 그 자리에서 삭제합니다. 유예와 취소가 없어 되돌릴 수 없습니다. 성공하면 로그아웃 상태가 됩니다.

```csharp
var result = await Supabase.DeleteAccountNowAsync();
if (result.IsSuccess)
{
    ReturnToTitle();   // 삭제 완료 — 이미 로그아웃됨
}
else
{
    ShowError(result.Reason);   // 계정은 그대로 — 다시 시도할 수 있음
}
```

Apple로 로그인한 계정은 삭제 전에 Apple 연결도 끊습니다. 같은 소셜 계정으로 다시 로그인하면 새 계정이 만들어집니다.

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.AccountDeleteFailed` | 삭제하지 못했습니다. 계정은 그대로이며 다시 시도할 수 있습니다 |

::: warning 되돌릴 수 없습니다
호출 전에 플레이어에게 한 번 더 확인을 받으세요. 유예를 두고 취소할 수 있게 하려면 [탈퇴 신청](/guide/withdrawal/submit)을 쓰세요.
:::

::: info 서버 준비
[Edge Function 배포](/guide/start/database-setup#edge-function-deploy)에서 `withdrawal-now`를 배포해야 합니다.
:::
