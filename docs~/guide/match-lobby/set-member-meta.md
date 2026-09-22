# 참가자 칸 지정

```csharp
Task<SupabaseResult> Supabase.SetMatchLobbyMemberMetaAsync(
    string                              lobbyId,
    string                              accountId,
    IReadOnlyDictionary<string, object> metadata)
```

참가자마다 붙는 자유 칸을 통째로 바꿉니다. 호스트는 누구 것이든, 참가자는 자기 것만 바꿀 수 있고, 팀·진영·준비 상태 등 의미는 서버가 관여하지 않으며 게임이 정합니다.

```csharp
await Supabase.SetMatchLobbyMemberMetaAsync(
    _currentLobbyId,
    Supabase.UserId,
    new Dictionary<string, object> { ["team"] = "red", ["ready"] = true });
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `lobbyId` | 대상 로비의 `LobbyId` |
| `accountId` | 칸을 바꿀 멤버의 계정 ID. 내 것이면 `Supabase.UserId` |
| `metadata` | 넣을 값 전체. 기존 값에 더하지 않고 통째로 갈아끼웁니다. null이면 비웁니다 |

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.MatchLobbyNotFound` | 남의 칸을 바꾸려는데 호스트가 아니거나, 그런 로비가 없습니다 |
| `SupabaseReason.MatchLobbyMemberNotFound` | 지정하려는 멤버가 로비에 없습니다 |
| `SupabaseReason.NotSignedIn` | 로그인 상태가 아닙니다 |

::: warning 일부만 바꿀 수 없습니다
넘긴 값이 기존 값을 통째로 대체합니다. `ready`만 바꾸려면 [목록](/guide/match-lobby/list)에서 읽은 기존 칸에 그 키만 덮어써서 전체를 다시 넘기세요.
:::

::: tip 팀전·자유대결 모두 이 하나로 표현합니다
서버는 값의 의미를 전혀 해석하지 않습니다. 2팀 대전이면 `team`, 자유 난입이면 `slot`, 준비 확인이 필요하면 `ready`처럼 게임이 원하는 규칙으로 쓰면 됩니다.
:::
