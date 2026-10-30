# 새 답변 수

```csharp
Task<SupabaseResult<int>> Supabase.GetCustomerCenterUnreadCountAsync()
```

운영자 답변을 아직 읽지 않은 내 문의 수를 돌려줍니다. 플레이어가 고객센터 창에서 그 문의를 열면 줄어듭니다. 로그인 상태에서만 됩니다.

```csharp
var unread = await Supabase.GetCustomerCenterUnreadCountAsync();
supportBadge.SetActive(unread.IsSuccess && unread.Data > 0);
```

**반환**

`.Data`는 새 답변이 있는 문의 수입니다.
