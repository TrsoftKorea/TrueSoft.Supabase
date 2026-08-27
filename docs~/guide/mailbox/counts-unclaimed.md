# 미수령 메일 수

```csharp
Task<SupabaseResult<int>> Supabase.GetUnclaimedMailCountAsync(
    string category = null)
```

아직 수령하지 않은 우편 개수를 조회합니다. 첨부 없는 텍스트 우편은 열람 전, 보상 우편은 수령 전 상태를 포함합니다. 우편함 배지 표시에 사용합니다.

```csharp
var result = await Supabase.GetUnclaimedMailCountAsync();
if (result.IsSuccess)
{
    int count = result.Data;
    mailBadge.SetCount(count);
}
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `category` | 조회할 분류. `null`이면 전체 분류 (기본값: `null`) |

**반환**

`.Data`에 미수령 우편 개수가 `int`로 담깁니다. 지정한 분류에 해당 우편이 없으면 `0`입니다.

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.NotSignedIn` | 로그인 상태가 아닙니다 |
