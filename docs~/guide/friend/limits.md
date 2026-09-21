# 제한값 읽기

```csharp
Task<SupabaseResult<FriendLimits>> Supabase.GetFriendLimitsAsync()
```

친구 기능의 상한과 간격을 읽습니다. 화면에 "친구 12 / 100"처럼 분모를 띄울 때 씁니다.

```csharp
var limits = await Supabase.GetFriendLimitsAsync();
var friends = await Supabase.GetFriendsAsync();
if (limits.IsSuccess && friends.IsSuccess)
    SetFriendCount(friends.Data.Count, limits.Data.MaxFriends);
```

**반환**

`.Data`는 `FriendLimits`입니다.

| 프로퍼티 | 타입 | 설명 |
|---------|------|------|
| `MaxFriends` | int | 친구 수 상한 |
| `MaxPendingSent` | int | 보낸 뒤 응답을 기다리는 요청의 개수 상한 |
| `RequestCooldownSeconds` | int | 연속 요청 최소 간격. 0이면 제한 없음 |

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.FriendSettingsMissing` | 제한값 설정이 DB에 없습니다. 설치가 깨진 상태입니다 |
| `SupabaseReason.NotSignedIn` | 로그인 상태가 아닙니다 |

::: warning 게임에 숫자를 박아 두지 마세요
이 값은 운영 콘솔에서 바뀝니다. 화면에 상수로 적어 두면 운영이 값을 바꾸는 순간 실제 동작과 표시가 어긋납니다.
:::

::: tip 자주 부르지 않아도 됩니다
바뀌는 일이 드문 값이라 친구 화면을 열 때 한 번 받아 두고 쓰면 충분합니다.
:::
