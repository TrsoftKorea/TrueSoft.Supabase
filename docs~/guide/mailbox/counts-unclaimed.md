# 미수령 우편 수

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

`.Data`는 미수령 우편 개수 `int`입니다. 지정한 분류에 해당 우편이 없으면 `0`입니다.

**실사용 예시**

로비 화면이 열릴 때와 우편 알림이 왔을 때 우편함 버튼의 배지를 새로 고칩니다.

```csharp
void OnEnable()
{
    Supabase.OnPushReceived += OnPush;
    RefreshMailBadge();
}
void OnDisable() => Supabase.OnPushReceived -= OnPush;

void OnPush(SupabasePushMessage push)
{
    if (push.Kind == SupabasePushKind.Mail) RefreshMailBadge();
}

async void RefreshMailBadge()
{
    var result = await Supabase.GetUnclaimedMailCountAsync();
    if (result.IsSuccess) mailBadge.SetCount(result.Data);   // 실패하면 이전 숫자를 둡니다
}
```
