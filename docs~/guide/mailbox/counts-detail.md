# 분류별 전체 현황

```csharp
Task<SupabaseResult<MailInboxCounts>> Supabase.GetMailInboxCountsAsync()
```

미읽음·미수령 개수를 전체 집계와 분류별 세부 내역으로 한 번에 조회합니다. 분류 탭마다 배지를 붙일 때 호출을 여러 번 나누지 않고 이 한 번으로 끝납니다.

**반환**

`.Data`는 `MailInboxCounts`입니다.

| 프로퍼티 | 타입 | 설명 |
|---------|------|------|
| `.Unread` | `int` | 전체 미읽음 수 |
| `.UnclaimedMails` | `int` | 전체 미수령 보상 우편 수 |
| `.ByCategory` | `Dictionary<string, MailCategoryCounts>` | 분류별 세부 내역. 활성 우편이 있는 분류만 키로 존재 |

`MailCategoryCounts`는 `.Unread`·`.UnclaimedMails` 두 값을 가집니다.

**실패 원인**

| Reason | 설명 |
|--------|------|
| `auth_not_signed_in` | 로그인 상태가 아닙니다 |
