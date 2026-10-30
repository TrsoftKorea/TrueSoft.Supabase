# 우편 설정 조회

```csharp
Task<SupabaseResult<MailSettings>> Supabase.GetMailSettingsAsync()
```

운영 콘솔의 우편 설정을 읽습니다. 기한이 지난 우편을 목록에 며칠 남겨 두는지 안내할 때 씁니다.

```csharp
var result = await Supabase.GetMailSettingsAsync();
if (result.IsSuccess)
{
    int days = result.Data.ExpiredRetentionDays;
    footerText.text = $"기간이 만료된 우편은 {days}일 후 자동 삭제됩니다.";
}
```

**반환**

`.Data`는 `MailSettings`입니다.

| 프로퍼티 | 타입 | 설명 |
|---------|------|------|
| `ExpiredRetentionDays` | `int` | 기한이 지난 우편을 목록에 남겨 두는 일수. 지나면 목록에서 빠지고 서버가 지웁니다. `0`이면 만료 즉시 빠집니다 |

::: info 값은 운영 콘솔에서 바꿉니다
운영 콘솔 **설정**의 우편 항목에서 0~30일 사이로 바꿀 수 있습니다. 게임에 일수를 적어 두지 말고 이 값을 읽어 쓰세요. 콘솔에서 바꾸는 순간 안내 문구가 틀려집니다.
:::
