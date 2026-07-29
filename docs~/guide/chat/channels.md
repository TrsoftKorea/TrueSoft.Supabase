# 채널 목록 조회

```csharp
Task<SupabaseResult<IReadOnlyList<ChatChannelInfo>>> Supabase.GetChatChannelsAsync(bool forceRefresh = false)
```

사용할 수 있는 채널을 가져옵니다. 운영이 중지한 채널은 빠집니다.

```csharp
var r = await Supabase.GetChatChannelsAsync();
if (!r.IsSuccess) return;

foreach (var ch in r.Data)
    AddChatTab(ch.Code, ch.DisplayName, ch.MaxLength);
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `forceRefresh` | 캐시를 무시하고 서버에서 다시 받습니다. 운영이 채널을 바꾼 뒤 반영할 때만 씁니다. (기본값: false) |

**반환**

| 프로퍼티 | 타입 | 설명 |
|---------|------|------|
| `Code` | string | 채널 코드. 발송·구독에 이 값을 넘깁니다 |
| `Kind` | string | `global`이면 전체, `server`면 같은 서버끼리 |
| `DisplayName` | string | 채팅 탭에 표시할 이름 |
| `MaxLength` | int | 보낼 수 있는 최대 글자 수. 입력창 제한에 그대로 쓰세요 |
| `SlowModeSeconds` | int | 같은 사람의 연속 채팅 최소 간격. 0이면 제한 없음 |

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.NotSignedIn` | 로그인 상태가 아닙니다 |

::: tip 한 번만 받으면 됩니다
채널 설정은 운영자가 가끔 바꾸는 값이라 SDK가 캐시합니다. 매번 호출해도 네트워크를 타지 않으므로 채팅창을 열 때마다 불러도 됩니다.
:::
