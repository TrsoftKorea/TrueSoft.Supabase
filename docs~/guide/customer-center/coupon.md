# 쿠폰 등록 창 열기

```csharp
Task<SupabaseResult> Supabase.OpenCouponAsync(string language = null)
```

고객센터 창을 첫 화면 대신 쿠폰 등록 화면으로 엽니다. 창 모양과 본인 확인은 [고객센터 창 열기](/guide/customer-center/open)와 같습니다. 플레이어가 창에서 쿠폰 번호를 넣으면 보상이 우편함으로 들어갑니다.

```csharp
var result = await Supabase.OpenCouponAsync(GameSettings.LanguageCode);
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `language` | 창 언어. 비우면 기기 언어 (기본값: null) |

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.CustomerCenterLoginRequired` | 로그인 상태가 아니고, 차단된 계정의 본인 증명도 없습니다 |
| `SupabaseReason.CustomerCenterNotShown` | 기기가 6초 안에 창을 띄우지 못했습니다. 다른 화면이 이미 떠 있을 때 주로 생깁니다 |

**실사용 예시**

설정 화면의 쿠폰 등록 버튼에서 창을 열고, 창이 닫히면 우편함 배지를 다시 셉니다.

```csharp
public async void OnClickCoupon()
{
    var result = await Supabase.OpenCouponAsync(GameSettings.LanguageCode);
    if (result.Reason == SupabaseReason.CustomerCenterNotShown)
    {
        ShowToast("잠시 뒤 다시 눌러 주세요");
        return;
    }
    if (!result.IsSuccess) { ShowToast("쿠폰 등록 창을 열지 못했습니다"); return; }

    // 창에서 등록한 쿠폰 보상은 우편으로 들어옵니다
    await RefreshMailBadge();
}
```

::: tip 게임 안에 입력 칸을 직접 두려면
쿠폰 번호 입력 화면을 게임이 직접 그리려면 [쿠폰 사용](/guide/coupon/)을 참고하세요.
:::
