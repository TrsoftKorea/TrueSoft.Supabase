# 세이브 삭제

```csharp
Task<SupabaseResult> Supabase.DeleteUserSaveAsync()
```

본인 세이브를 삭제합니다. 서버의 세이브 행을 지우고 로컬 상태를 **기본값으로 리셋**합니다. 로드한 뒤에 지웠으면 기본값 행을 새로 만들어 바로 읽어 오므로, 다시 로드하지 않아도 이어서 저장할 수 있습니다.

```csharp
var result = await Supabase.DeleteUserSaveAsync();
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
| `SupabaseReason.UserSaveDeleteFailed` | 네트워크 오류 등으로 삭제하지 못했습니다 |
| `SupabaseReason.UserSaveLoadFailed` | 지웠지만 기본값 행을 다시 읽지 못했습니다. `Supabase.LoadUserSaveAsync()`로 다시 로드하세요 |

저장이 진행 중이면 끝날 때까지 최대 5초 기다렸다가 지웁니다. 삭제 뒤 게임이 처음 부르는 로드의 `IsNewUser`는 `true`입니다.

::: info 로드 전 초기값도 함께 지워집니다
[로드 전에 지정한 초기값](/guide/user-data/load#preload-fallback)은 삭제 시 함께 버려집니다. 남겨두면 다음 로드에서 재생성된 기본값 행 위에 옛 초기값이 다시 병합돼 "삭제했는데 기본값이 아닌 값으로 되살아나는" 문제가 생기기 때문입니다. 로드하기 전에 지웠으면 다음 로드 전에 다시 세팅하고, 로드한 뒤에 지웠으면 삭제가 끝난 다음 값을 넣으세요.
:::

::: info 플레이나누 병행 운영 중이라면
[플레이나누 이관](/guide/migration/) 런타임이 씬에 있으면 플레이나누 스토리지도 함께 비워집니다. 비우지 않으면 다음 로그인의 데이터 동기화가 옛 데이터를 그대로 다시 올려 삭제가 무효가 되기 때문입니다.
:::

::: warning 탈퇴가 아닙니다
계정은 그대로 두고 세이브 데이터만 비웁니다. 계정 자체를 없애려면 [탈퇴 신청](/guide/withdrawal/submit)을 사용하세요.
:::

::: warning 저장이 활발할 때 호출하지 마세요
로컬을 먼저 기본값으로 리셋한 뒤 서버를 삭제하므로 삭제 도중 자동 저장이 옛 데이터를 되쓰지는 않습니다. 다만 5초 넘게 응답이 없던 저장이 삭제 뒤에 도착하면 그 값이 새 기본값 행에 섞일 수 있습니다. 전투 중처럼 저장이 잦은 순간이 아니라 설정 화면 등 조용한 시점에 호출하세요. 삭제에 실패하면 로컬은 기본값이지만 서버 데이터는 남아 있으니 `Supabase.LoadUserSaveAsync()`로 다시 맞추세요.
:::
