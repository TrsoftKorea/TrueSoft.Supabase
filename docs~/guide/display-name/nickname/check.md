# 닉네임 중복 확인

```csharp
Task<SupabaseResult> Supabase.IsNameAvailableAsync(string displayName)
```

닉네임 사용 가능 여부를 확인합니다. `result.IsSuccess`가 `true`면 사용 가능이고, 쓸 수 없으면 `.Reason`이 이유를 알려 줍니다. 현재 계정이 이미 사용 중인 닉네임은 사용 가능으로 처리합니다.

```csharp
var result = await Supabase.IsNameAvailableAsync(displayName);
if (result.IsSuccess)
    ShowMessage("사용 가능한 닉네임입니다.");
else if (result.Reason == SupabaseReason.NameForbidden)
    ShowMessage("사용할 수 없는 단어가 들어 있습니다.");
else
    ShowMessage("이미 사용 중인 닉네임입니다.");
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `displayName` | 확인할 닉네임. 최대 64자 |

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.NameTaken` | 이미 사용 중인 닉네임입니다 |
| `SupabaseReason.NameForbidden` | 금칙어가 들어 있거나 운영 콘솔에서 금지한 닉네임입니다 |

**실사용 예시**

닉네임 입력란에서 입력을 마칠 때마다 확인하고, 쓸 수 있을 때만 확정 버튼을 켭니다.

```csharp
public async void OnNicknameEndEdit(string text)
{
    confirmButton.interactable = false;
    var result = await Supabase.IsNameAvailableAsync(text.Trim());
    if (text != nicknameInput.text) return;   // 기다리는 사이 다시 고친 입력의 결과는 버립니다

    if (result.IsSuccess) { SetHint("사용할 수 있습니다"); confirmButton.interactable = true; return; }
    switch (result.Reason)
    {
        case SupabaseReason.NameTaken:     SetHint("이미 쓰고 있는 닉네임입니다"); break;
        case SupabaseReason.NameForbidden: SetHint("쓸 수 없는 단어가 들어 있습니다"); break;
        default:                           SetHint("확인하지 못했습니다. 잠시 뒤 다시 시도해 주세요"); break;
    }
}
```
