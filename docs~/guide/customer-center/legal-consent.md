# 약관 동의 조회

```csharp
Task<SupabaseResult<SupabaseLegalConsent>> Supabase.GetLegalConsentAsync()
```

이 플레이어가 마지막으로 동의한 약관 버전과, 동의 화면을 다시 띄워야 하는지를 돌려줍니다. 로그인 상태에서만 됩니다. 동의 기록은 유저 ID로 묶여 다시 가입해도 이어집니다.

```csharp
var consent = await Supabase.GetLegalConsentAsync();
if (consent.IsSuccess && consent.Data.NeedsConsent) ShowConsentScreen(consent.Data.Current);
```

**반환**

`.Data`는 `SupabaseLegalConsent`입니다.

| 프로퍼티 | 타입 | 설명 |
|---------|------|------|
| `NeedsConsent` | `bool` | 동의 화면을 다시 띄워야 하면 true. 동의한 적이 없거나, 동의한 버전이 지금 버전과 다르거나, 만 14세 이상 확인이 없을 때 |
| `HasConsent` | `bool` | 동의한 적이 있으면 true |
| `TermsVersion` | `int` | 동의한 이용약관 버전. 동의한 적이 없으면 0 |
| `PrivacyVersion` | `int` | 동의한 개인정보처리방침 버전. 동의한 적이 없으면 0 |
| `AgeConfirmed` | `bool` | 만 14세 이상이라고 확인했으면 true |
| `AgreedAt` | `DateTimeOffset?` | 동의한 시각. 동의한 적이 없으면 null |
| `Current` | `SupabaseLegalVersions` | 지금 시행 중인 버전. [약관 버전 조회](/guide/customer-center/legal-versions)의 표와 같습니다 |

**실사용 예시**

로그인한 뒤 약관이 개정됐는지 확인해, 바뀌었으면 동의 화면을 다시 띄웁니다.

```csharp
async Task CheckConsentAfterLogin()
{
    var consent = await Supabase.GetLegalConsentAsync();
    if (!consent.IsSuccess) return;   // 확인하지 못하면 이번에는 묻지 않습니다

    if (consent.Data.NeedsConsent)
        ShowConsentScreen(consent.Data.Current);
}
```
