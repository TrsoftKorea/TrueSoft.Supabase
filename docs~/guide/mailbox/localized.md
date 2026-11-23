# 다국어 메시지

```csharp
string mail.TitleFor(string lang)
string mail.ContentFor(string lang)
```

운영 콘솔에서 언어별로 발송한 우편은 `TitleFor`·`ContentFor`로 원하는 언어의 텍스트를 얻습니다. 언어는 게임이 직접 지정하며, 해당 언어가 없으면 기본 `Title`·`Content`로 fallback합니다. 우편 목록은 [목록 조회](/guide/mailbox/list)로 가져옵니다.

```csharp
var result = await Supabase.GetMailsAsync();
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
| `lang` | 언어코드. 예: `"ja"`, `"en"`. 해당 언어가 없으면 기본값 반환 |

**반환**

`string`을 돌려줍니다. 그 언어의 제목·본문이 없으면 기본 `Title`·`Content`입니다.

**실사용 예시**

우편 목록 줄을 게임 옵션의 언어로 채우고, 플레이어가 언어를 바꾸면 받아 둔 우편으로 다시 그립니다.

```csharp
void BindMailRow(MailRowView row, Mail mail)
{
    string lang = GameSettings.LanguageCode;   // 게임 옵션에서 고른 언어
    row.Title.text   = mail.TitleFor(lang);
    row.Preview.text = mail.ContentFor(lang);
}

// 서버에 다시 묻지 않습니다
void OnLanguageChanged()
{
    foreach (var (row, mail) in mailRows)
        BindMailRow(row, mail);
}
```
