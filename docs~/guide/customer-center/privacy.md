# 개인정보처리방침 열기

```csharp
Task<SupabaseResult> Supabase.OpenPrivacyPolicyAsync(string language = null)
```

개인정보처리방침 창을 엽니다. 로그인하지 않아도 열립니다. Android·iOS는 게임 위에 창으로 열고 플레이어가 닫을 때까지 기다리며, 그 밖의 플랫폼은 기본 브라우저로 엽니다.

```csharp
await Supabase.OpenPrivacyPolicyAsync(GameSettings.LanguageCode);
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `language` | 창 언어. 비우면 기기 언어 (기본값: null) |

본문은 운영 콘솔 **고객센터 › 약관·방침**에서 게시한 버전입니다. 게시한 버전이 없으면 "준비 중"이 보입니다. 언어는 [고객센터 창 열기](/guide/customer-center/open)와 같은 방식으로 고릅니다.

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.CustomerCenterNotShown` | 기기가 6초 안에 창을 띄우지 못했습니다. 다른 화면이 이미 떠 있을 때 주로 생깁니다 |

**실사용 예시**

설정 화면의 개인정보처리방침 버튼에서 창을 엽니다.

```csharp
public async void OnClickPrivacyPolicy()
{
    var result = await Supabase.OpenPrivacyPolicyAsync(GameSettings.LanguageCode);
    if (result.Reason == SupabaseReason.CustomerCenterNotShown)
        ShowToast("잠시 뒤 다시 눌러 주세요");
}
```

::: info 스토어에 적는 주소
Google Play·App Store에 적는 개인정보처리방침 주소는 `https://truebase-support.pages.dev/?p=<프로젝트 ref>&view=privacy`입니다. 운영 콘솔 **고객센터 › 약관·방침**의 주소 복사로 복사합니다.
:::
