# 세이브 삭제

```csharp
Task<SupabaseResult> PlayerSave.DeleteAsync()
```

본인 세이브를 삭제합니다. 서버의 세이브 행을 지우고 로컬 상태를 기본값으로 되돌립니다. 다음 [로드](./load.md) 시 기본값 행이 자동 재생성되므로 실질적으로 **기본값으로 리셋**됩니다.

```csharp
var result = await PlayerSave.DeleteAsync();
if (result.IsSuccess)
{
    // 삭제 완료 — 로컬이 기본값으로 리셋됨
}
else
{
    // 실패 처리
}
```

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseErrorCode.UserSaveDeleteFailed` | 삭제 실패 (네트워크 오류 등) |

::: warning 탈퇴가 아닙니다
계정은 그대로 두고 세이브 데이터만 비웁니다. 계정 자체를 없애려면 [탈퇴 신청](/guide/withdrawal/submit)을 사용하세요.
:::

::: warning 저장이 활발할 때 호출하지 마세요
로컬을 먼저 기본값으로 리셋한 뒤 서버를 삭제하므로 삭제 도중 자동 저장이 옛 데이터를 되쓰지는 않습니다. 다만 이미 전송 중이던 저장이 삭제 직후 도착하면 행이 되살아날 수 있습니다. 전투 중처럼 저장이 잦은 순간이 아니라 설정 화면 등 조용한 시점에 호출하세요. 삭제에 실패하면 로컬은 기본값이지만 서버 데이터는 남아 있으니 `PlayerSave.LoadAsync()`로 다시 맞추세요.
:::
