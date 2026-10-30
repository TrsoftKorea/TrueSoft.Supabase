# 고객센터 창 열기

```csharp
Task<SupabaseResult> Supabase.OpenCustomerCenterAsync(string language = null)
```

고객센터 창을 엽니다. Android·iOS는 게임 위에 창으로 열고 플레이어가 닫을 때까지 기다리며, 그 밖의 플랫폼은 기본 브라우저로 열고 바로 끝납니다. 차단돼 로그인할 수 없는 계정도 [본인 확인](/guide/customer-center/#banned)이 되면 열립니다.

```csharp
var result = await Supabase.OpenCustomerCenterAsync(GameSettings.LanguageCode);
if (result.Reason == SupabaseReason.CustomerCenterLoginRequired)
    ShowToast("로그인한 뒤 이용해 주세요.");
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `language` | 창 언어. 비우면 기기 언어 (기본값: null) |

게임 안에 언어 옵션이 있으면 그 값을 `language`로 넘깁니다. `ko`·`en`·`ja`·`zh-CN`·`zh-TW` 같은 언어 코드와 `Korean`·`ChineseTraditional` 같은 유니티 언어 이름을 받습니다. 창이 지원하지 않는 언어를 넘기면 경고 로그를 남기고 기기 언어로 엽니다. 기기 언어도 창이 지원하지 않으면 영어로 보입니다. 플레이어가 창 아래쪽 언어 목록에서 직접 고른 적이 있으면 그 언어가 먼저입니다.

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.CustomerCenterLoginRequired` | 로그인 상태가 아니고, 차단된 계정의 본인 증명도 없습니다 |
| `SupabaseReason.CustomerCenterNotShown` | 기기가 6초 안에 창을 띄우지 못했습니다. 다른 화면이 이미 떠 있을 때 주로 생깁니다 |
