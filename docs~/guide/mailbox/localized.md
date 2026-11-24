# 다국어 메시지

```csharp
string mail.TitleFor(string lang)
string mail.ContentFor(string lang)
```

지정한 언어의 제목·본문을 얻습니다. 그 언어로 쓴 글이 없으면 기본 언어 글입니다. 언어를 고르지 않아도 되면 `Title`·`Content`가 기기 언어로 같은 일을 합니다.

```csharp
titleLabel.text = mail.TitleFor("ja");   // 일본어 글이 없으면 기본 언어 제목
bodyLabel.text  = mail.ContentFor("ja");
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `lang` | 언어 코드. 예: `"ja"`, `"zh-TW"`. 비우면 기기 언어 |

**반환**

`string`을 돌려줍니다. 그 언어의 제목·본문이 없으면 기본 언어 제목·본문입니다.

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

::: info 기본 언어와 언어 찾는 순서
운영 콘솔에서 우편을 보낼 때 언어 표시가 있는 위 칸이 기본 언어 글입니다. 기본 언어는 운영 콘솔 설정에서 정합니다. 언어는 같은 코드 → 앞 두 글자(`pt-BR` → `pt`) → 앞 두 글자가 같은 코드(`zh` → `zh-CN`) 순으로 찾습니다. `"ko"`·`"zh_Hant"` 같은 코드와 `"Korean"` 같은 유니티 언어 이름을 모두 읽습니다.
:::
