# 수령한 우편 일괄 삭제

```csharp
Task<SupabaseResult<int>> Supabase.DeleteClaimedMailsAsync(string category = null)
```

수령한 우편만 일괄로 숨깁니다. 미수령 우편은 기한이 지났어도 남습니다. 기한이 지난 우편은 보관 기간이 끝나면 서버가 지웁니다.

```csharp
var result = await Supabase.DeleteClaimedMailsAsync();
if (result.IsSuccess)
{
    int deleted = result.Data;   // 삭제된 우편 개수
    RefreshMailbox();
}
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `category` | 삭제 대상 분류. `null`이면 전체 분류 (기본값: `null`) |

**반환**

`.Data`는 삭제된 우편 개수 `int`입니다.

**실사용 예시**

우편함의 받은 우편 정리 버튼에서 확인을 받은 뒤 지우고, 몇 통을 지웠는지 알립니다.

```csharp
public async void OnClickDeleteClaimed()
{
    if (!await ConfirmPopup("받은 우편을 모두 지울까요?")) return;

    var result = await Supabase.DeleteClaimedMailsAsync(currentTab);
    if (!result.IsSuccess) { ShowToast("우편을 지우지 못했습니다"); return; }

    ShowToast(result.Data > 0 ? $"우편 {result.Data}통을 지웠습니다" : "지울 우편이 없습니다");
    RefreshMailbox();
}
```
