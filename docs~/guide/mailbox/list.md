# 우편함 목록 조회

```csharp
Task<SupabaseResult<IReadOnlyList<Mail>>> Supabase.GetMyMailsAsync(
    int    limit    = 50,
    int    offset   = 0,
    string category = null)
```

내 우편함 목록을 최신순으로 조회합니다. 삭제됐거나 만료된 우편, 다른 서버의 우편은 제외됩니다.

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `limit` | 반환할 최대 개수. 1~200 (기본값: 50) |
| `offset` | 건너뛸 개수. 페이지네이션에 사용 (기본값: 0) |
| `category` | 조회할 분류. `null`이면 전체 분류 (기본값: `null`) |

**반환**

`.Data`에 `Mail` 목록이 담깁니다.

| 프로퍼티 | 타입 | 설명 |
|---------|------|------|
| `.Category` | `string` | 분류 |
| `.Title` | `string` | 제목 |
| `.Content` | `string` | 본문 |
| `.IsRead` | `bool` | 읽음 여부 |
| `.Items` | `IReadOnlyList<MailItemPayload>` | 첨부 보상 |
| `.HasUnclaimedItems` | `bool` | 미수령 보상 여부 |

**실패 원인**

| Reason | 설명 |
|--------|------|
| `auth_not_signed_in` | 로그인 상태가 아닙니다 |
