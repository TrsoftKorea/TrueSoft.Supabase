# 우편 상세 조회

```csharp
Task<SupabaseResult<Mail>> Supabase.GetMailDetailAsync(string mailId)
```

우편 한 건의 상세 내용을 조회합니다. 첨부 보상이 없는 우편은 조회하는 순간 읽음 처리됩니다.

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
| `auth_not_signed_in` | 로그인 상태가 아닙니다 |
