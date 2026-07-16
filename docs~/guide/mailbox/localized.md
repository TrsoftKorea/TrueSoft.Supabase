# 다국어 메시지

```csharp
string mail.TitleFor(string lang)
string mail.ContentFor(string lang)
```

어드민이 언어별로 발송한 우편은 `TitleFor`·`ContentFor`로 원하는 언어의 텍스트를 얻습니다. 언어는 게임이 직접 지정하며, 해당 언어가 없으면 기본 `Title`·`Content`로 fallback합니다. 우편 목록은 [목록 조회](./list)로 가져옵니다.

```csharp
var result = await Supabase.GetMyMailsAsync();
if (result.IsSuccess)
{
    var lang = "ja"; // 게임의 현재 언어 설정값
    foreach (var mail in result.Data)
    {
        titleLabel.text = mail.TitleFor(lang);   // ja 없으면 기본 제목
        bodyLabel.text  = mail.ContentFor(lang);
    }
}
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `lang` | 언어코드(예: `"ja"`, `"en"`). 해당 언어가 없으면 기본값 반환 |
