# 연동 해제

```csharp
Task<SupabaseCallResult> Supabase.TryUnlinkAppleAsync()
```

현재 계정에서 Apple 연동을 해제합니다. 성공하면 세션이 갱신되어 `Supabase.IsLinkedWithApple`이 `false`가 됩니다.

**실패 원인**

| Reason | 설명 |
|--------|------|
| `SupabaseFailReason.NotSignedIn` | 로그인 상태가 아닙니다. |
| `SupabaseFailReason.IdentityNotLinked` | Apple이 현재 계정에 연동되어 있지 않습니다. |
| `SupabaseFailReason.CannotUnlinkLastIdentity` | 마지막 남은 연동이라 해제할 수 없습니다. |
| `SupabaseFailReason.UnlinkFailed` | 연동 해제에 실패했습니다. |

```csharp
var r = await Supabase.TryUnlinkAppleAsync();
if (!r && r.Reason == SupabaseFailReason.CannotUnlinkLastIdentity)
    ShowToast("최소 하나의 로그인 수단은 남겨야 합니다.");
```

::: warning 마지막 연동은 해제할 수 없습니다
계정에 연동이 하나뿐이면 해제 시 로그인 수단이 사라지므로 거부됩니다. Google·Apple처럼 둘 이상 연동했을 때 하나를 해제할 수 있습니다.
:::
