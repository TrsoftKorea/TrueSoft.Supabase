# 고객센터 창 열기

```csharp
Task<SupabaseResult> Supabase.OpenCustomerCenterAsync()
```

고객센터 창을 엽니다. Android·iOS는 게임 위에 창으로 열고 플레이어가 닫을 때까지 기다리며, 그 밖의 플랫폼은 기본 브라우저로 열고 바로 끝납니다. 차단돼 로그인할 수 없는 계정도 [본인 확인](/guide/customer-center/#banned)이 되면 열립니다.

```csharp
var result = await Supabase.OpenCustomerCenterAsync();
if (result.Reason == SupabaseReason.CustomerCenterLoginRequired)
    ShowToast("로그인한 뒤 이용해 주세요.");
```

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.CustomerCenterLoginRequired` | 로그인 상태가 아니고, 차단된 계정의 본인 증명도 없습니다 |
| `SupabaseReason.NetworkError` | 네트워크 오류 또는 타임아웃 |
