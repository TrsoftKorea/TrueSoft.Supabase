# 우편 상세 조회

```csharp
Task<SupabaseResult<Mail>> Supabase.GetMailAsync(string mailId)
```

우편 한 건의 상세 내용을 조회합니다. 첨부 보상이 없는 우편은 조회하는 순간 읽음 처리됩니다.

```csharp
var result = await Supabase.GetMailAsync(mailId);
if (result.IsSuccess)
{
    Mail mail = result.Data;
    titleLabel.text = mail.Title;
    bodyLabel.text  = mail.Content;
}
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `mailId` | 조회할 우편 UUID |

**반환**

`.Data`에 `Mail` 한 건이 담깁니다.

**실패 원인**

| Reason | 설명 |
|--------|------|
| `mail_not_found` | 본인 소유가 아니거나 존재하지 않는 우편입니다 |
| `SupabaseFailReason.NotSignedIn` | 로그인 상태가 아닙니다 |
