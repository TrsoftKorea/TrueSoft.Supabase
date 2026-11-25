# 약관 동의 남기기

```csharp
Task<SupabaseResult<SupabaseLegalConsent>> Supabase.SaveLegalConsentAsync(
    int  termsVersion,
    int  privacyVersion,
    bool ageConfirmed)
```

플레이어가 동의한 약관 버전과 만 14세 이상 확인을 서버에 남깁니다. 로그인 상태에서만 됩니다. 같은 내용을 다시 보내면 기록을 더 쌓지 않으므로, 로그인할 때마다 보내도 됩니다.

```csharp
var saved = await Supabase.SaveLegalConsentAsync(v.TermsVersion, v.PrivacyVersion, ageConfirmed: true);
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `termsVersion` | 동의한 이용약관 버전. [약관 버전 조회](/guide/customer-center/legal-versions)로 받은 값 |
| `privacyVersion` | 동의한 개인정보처리방침 버전 |
| `ageConfirmed` | 만 14세 이상이라고 확인했는지 |

**반환**

`.Data`는 `SupabaseLegalConsent`입니다. 남긴 뒤의 값으로, [약관 동의 조회](/guide/customer-center/legal-consent)의 표와 같습니다.

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.LegalVersionNotPublished` | 넘긴 버전이 운영 콘솔에서 게시한 버전이 아닙니다 |

**실사용 예시**

로그인 전에 기기에 남겨 둔 동의를 로그인한 뒤 서버로 올립니다.

```csharp
async Task UploadConsent()
{
    if (!PlayerPrefs.HasKey("agreed_terms")) return;
    var saved = await Supabase.SaveLegalConsentAsync(
        PlayerPrefs.GetInt("agreed_terms"), PlayerPrefs.GetInt("agreed_privacy"),
        PlayerPrefs.GetInt("age_confirmed") == 1);

    if (saved.IsSuccess && saved.Data.NeedsConsent)
        ShowConsentScreen(saved.Data.Current);   // 그사이 약관이 개정됐습니다
}
```

::: info 동의 기록은 지우지 않습니다
동의 기록은 동의 증거라 탈퇴하거나 운영 콘솔에서 플레이어 전체 초기화를 해도 남습니다. 광고성·야간 알림 동의는 [수신 설정 저장](/guide/push/set-preferences)에서 따로 받습니다.
:::
