# 연동 해제

```csharp
Task<SupabaseCallResult> Supabase.TryUnlinkGoogleAsync()
```

현재 계정에서 Google 연동을 해제합니다. 성공하면 세션이 갱신되어 `Supabase.IsLinkedWithGoogle`이 `false`가 되고, 네이티브 Google credential 상태도 함께 정리되어 **다음 Google 연동 때 계정 선택창이 다시 나타납니다**. 플랫폼 구분 없이 동일하게 호출합니다.

**실패 원인**

| Reason | 설명 |
|--------|------|
| `SupabaseFailReason.NotSignedIn` | 로그인 상태가 아닙니다. |
| `SupabaseFailReason.IdentityNotLinked` | Google이 현재 계정에 연동되어 있지 않습니다. |
| `SupabaseFailReason.CannotUnlinkLastIdentity` | 마지막 남은 연동이라 해제할 수 없습니다. |
| `SupabaseFailReason.UnlinkFailed` | 연동 해제에 실패했습니다. |

```csharp
var r = await Supabase.TryUnlinkGoogleAsync();
if (!r && r.Reason == SupabaseFailReason.CannotUnlinkLastIdentity)
    ShowToast("최소 하나의 로그인 수단은 남겨야 합니다.");
```

::: warning 마지막 연동은 해제할 수 없습니다
계정에 연동이 하나뿐이면 해제 시 로그인 수단이 사라지므로 거부됩니다. 예를 들어 게스트 계정에 Google만 연동한 상태에서는 Google을 해제할 수 없습니다. Google·Apple처럼 둘 이상 연동했을 때 하나를 해제할 수 있습니다.
:::

::: info 계정 선택창·동의 화면
연동 해제 시 네이티브 credential 상태가 정리되므로, 다음 연동 때 **계정 선택창**은 자동으로 다시 뜹니다. 권한 **동의 화면**까지 다시 띄우려면(OAuth grant 회수) `Supabase.TryRevokeGoogleAccessAsync()`를 별도로 호출하세요.
:::
