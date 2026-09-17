# 멤버 역할 태그 지정

```csharp
Task<SupabaseResult> Supabase.SetMatchLobbyMemberRoleAsync(string lobbyId, string accountId, string roleTag)
```

멤버에게 자유 문자열 태그를 지정합니다. 호스트만 부를 수 있고, 팀 이름·진영 등 의미는 서버가 관여하지 않으며 게임이 정합니다.

```csharp
await Supabase.SetMatchLobbyMemberRoleAsync(_currentLobbyId, member.AccountId, "team_red");
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `lobbyId` | 대상 로비의 `LobbyId` |
| `accountId` | 태그를 지정할 멤버의 계정 ID |
| `roleTag` | 자유 문자열. null이면 태그를 지웁니다 |

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.MatchLobbyNotFound` | 해당 로비가 없거나 호스트가 아닙니다 |
| `SupabaseReason.MatchLobbyMemberNotFound` | 지정하려는 멤버가 로비에 없습니다 |
| `SupabaseReason.NotSignedIn` | 로그인 상태가 아닙니다 |

::: tip 팀전·자유대결 모두 이 하나로 표현합니다
서버는 `roleTag` 값의 의미를 전혀 해석하지 않습니다. 2팀 대전이면 `"team_a"`/`"team_b"`, 자유 난입이면 `"slot_1"`처럼 게임이 원하는 규칙으로 쓰면 됩니다.
:::
