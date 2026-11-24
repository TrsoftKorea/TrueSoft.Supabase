# 본인에게 보내기

```csharp
Task<SupabaseResult<string>> Supabase.SendMailToSelfAsync(
    string                         title,
    string                         content     = "",
    IReadOnlyList<MailItemPayload> items       = null,
    int                            expiresDays = 30,
    string                         category    = null,
    string                         onceKey     = null)
```

게임이 로그인한 플레이어 자신에게 우편을 보냅니다. 플레이어는 우편함에서 내용을 확인하고 다른 우편과 똑같이 보상을 받습니다. `onceKey`를 주면 계정마다 그 키로 한 번만 보내집니다.

```csharp
var items = new List<MailItemPayload> { new MailItemPayload { Key = "gold", Count = 500 } };
await Supabase.SendMailToSelfAsync("점검 보상", "불편을 드려 죄송합니다.", items, onceKey: "maint_1124");
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `title` | 우편 제목. 1~100자 |
| `content` | 우편 본문. 2000자까지 (기본값: "") |
| `items` | 보상 아이템. 20개까지, 수량은 1 이상 (기본값: null) |
| `expiresDays` | 수령 기한 일수. 1~365 (기본값: 30) |
| `category` | 우편 분류. 비우면 `default` (기본값: null) |
| `onceKey` | 한 번만 보낼 때의 키. 100자까지, 비우면 제한 없음 (기본값: null) |

**반환**

`.Data`는 새 우편의 ID `string`입니다.

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.MailAlreadySent` | 같은 키로 이미 보낸 우편이 있습니다 |
| `SupabaseReason.MailItemProtected` | 보호한 데이터 필드에 더하는 아이템이 있습니다 |
| `SupabaseReason.MailSendLimited` | 24시간 동안 본인에게 보낸 우편이 50통을 넘었습니다 |
| `SupabaseReason.MailInvalid` | 제목·본문·아이템·기한 형식이 올바르지 않습니다 |

**실사용 예시**

로그인 직후 게임이 플레이어 데이터를 보고 소급 보상 대상이면 한 번만 우편을 보냅니다. 키를 주었으므로 같은 계정이면 기기를 바꾸거나 다시 설치해도 두 번 받지 않습니다.

```csharp
async Task GrantRetroactiveRewardAsync()
{
    if (PlayerSave.StageClearCount < 30) return;   // 소급 대상이 아니면 보내지 않습니다

    var items = new List<MailItemPayload> { new MailItemPayload { Key = "gem", Count = 100 } };
    var sent = await Supabase.SendMailToSelfAsync("보상 누락 지급", "30스테이지 달성 보상을 보내 드립니다.", items,
                                                  onceKey: "retro_stage30_2611");
    if (sent.IsSuccess) ShowToast("우편함에 보상이 도착했습니다");
    else if (sent.Reason != SupabaseReason.MailAlreadySent) Debug.LogWarning(sent.ErrorCode);
}
```

::: warning 조작한 앱도 부를 수 있습니다
게임이 대상인지 판단하므로, 조작한 앱은 대상이 아니어도 보낼 수 있습니다. 보호한 데이터 필드에 더하는 아이템은 서버가 거절합니다. 그 밖의 데이터 필드는 플레이어가 원래 직접 저장할 수 있는 값이라 새로 열리는 길이 아닙니다. 아이템 처리기로 지급하는 보상은 게임이 막아야 합니다.
:::
