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

게임 안에 언어 옵션이 있으면 그 값을 `language`로 넘깁니다. `ko`·`en`·`ja`·`zh-CN`·`zh-TW` 같은 언어 코드와 `Korean`·`ChineseTraditional` 같은 유니티 언어 이름을 받습니다. 창이 지원하지 않는 언어를 넘기면 경고 로그를 남기고 기기 언어로 엽니다. 기기 언어도 창이 지원하지 않으면 영어로 보입니다. 플레이어가 창 아래쪽 언어 목록에서 고른 언어는 넘기는 값이 그대로인 동안 유지되고, 값이 바뀌면 새 값으로 열립니다.

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.CustomerCenterLoginRequired` | 로그인 상태가 아니고, 차단된 계정의 본인 증명도 없습니다 |
| `SupabaseReason.CustomerCenterNotShown` | 기기가 6초 안에 창을 띄우지 못했습니다. 다른 화면이 이미 떠 있을 때 주로 생깁니다 |

**실사용 예시**

설정 화면의 고객센터 버튼에서 창을 열고, 창이 닫히면 새 답변 배지를 다시 셉니다.

```csharp
public async void OnClickCustomerCenter()
{
    var result = await Supabase.OpenCustomerCenterAsync(GameSettings.LanguageCode);
    if (result.Reason == SupabaseReason.CustomerCenterNotShown)
    {
        ShowToast("잠시 뒤 다시 눌러 주세요");
        return;
    }
    if (!result.IsSuccess) { ShowToast("고객센터를 열지 못했습니다"); return; }

    // 창에서 읽은 답변만큼 배지가 줄어듭니다
    var unread = await Supabase.GetCustomerCenterUnreadCountAsync();
    supportBadge.SetActive(unread.IsSuccess && unread.Data > 0);
}
```
