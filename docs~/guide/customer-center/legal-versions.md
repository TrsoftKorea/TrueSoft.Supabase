# 약관 버전 조회

```csharp
Task<SupabaseResult<SupabaseLegalVersions>> Supabase.GetLegalVersionsAsync()
```

지금 시행 중인 이용약관·개인정보처리방침 버전을 돌려줍니다. 로그인하지 않아도 됩니다. 운영 콘솔 **고객센터 › 약관·방침**에서 게시한 버전 가운데 시행일이 지난 마지막 버전입니다.

```csharp
var versions = await Supabase.GetLegalVersionsAsync();
if (versions.IsSuccess && versions.Data.TermsVersion > PlayerPrefs.GetInt("agreed_terms"))
    ShowConsentScreen(versions.Data);
```

**반환**

`.Data`는 `SupabaseLegalVersions`입니다.

| 프로퍼티 | 타입 | 설명 |
|---------|------|------|
| `TermsVersion` | `int` | 이용약관 버전. 게시한 버전이 없으면 0 |
| `TermsEffectiveDate` | `DateTime?` | 이용약관 시행일. 게시한 버전이 없으면 null |
| `PrivacyVersion` | `int` | 개인정보처리방침 버전. 게시한 버전이 없으면 0 |
| `PrivacyEffectiveDate` | `DateTime?` | 개인정보처리방침 시행일. 게시한 버전이 없으면 null |

**실사용 예시**

첫 실행 때 로그인 전에 동의 화면을 띄울지 정합니다. 기기에 남긴 동의 버전이 지금 버전보다 낮으면 띄우고, 로그인한 뒤 [약관 동의 남기기](/guide/customer-center/legal-consent-save)로 서버에 올립니다.

```csharp
public async void OnTitleReady()
{
    var versions = await Supabase.GetLegalVersionsAsync();
    if (!versions.IsSuccess) { ShowToast("약관 정보를 불러오지 못했습니다"); return; }

    var v = versions.Data;
    var agreedTerms = PlayerPrefs.GetInt("agreed_terms");
    var agreedPrivacy = PlayerPrefs.GetInt("agreed_privacy");
    if (v.TermsVersion > agreedTerms || v.PrivacyVersion > agreedPrivacy)
        ShowConsentScreen(v);   // 동의하면 두 버전과 만 14세 확인을 기기에 남깁니다
    else
        StartLogin();
}
```
