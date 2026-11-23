# 아이템 핸들러 일괄 등록

```csharp
SupabaseResult Supabase.RegisterMailItemHandlers(IReadOnlyDictionary<string, Action<int>> handlers)
```

보상 키마다 지급 함수를 한 번에 등록합니다. 함수는 지급 수량을 받습니다. 클래스를 따로 만들지 않아도 되고, 등록한 키는 [아이템 핸들러 등록](/guide/mailbox/item-handler)과 똑같이 동작합니다.

```csharp
Supabase.RegisterMailItemHandlers(new Dictionary<string, Action<int>>
{
    ["energy"]  = count => Energy.Add(count),
    ["skin_01"] = count => Skins.Unlock("skin_01"),
});
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `handlers` | 보상 키와 지급 함수 |

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.MailItemHandlerInvalid` | 목록이 비었거나, 키가 빈 줄·함수가 null인 줄·앞뒤 공백을 빼면 키가 같은 줄이 있습니다. 이때는 아무것도 등록하지 않습니다 |

**실사용 예시**

앱 시작 때 한 번 등록하고, 이미 등록한 키를 다시 넣으면 새 함수로 바뀝니다.

```csharp
void Awake()
{
    var result = Supabase.RegisterMailItemHandlers(new Dictionary<string, Action<int>>
    {
        ["energy"]   = count => Energy.Add(count),
        ["revive"]   = count => Inventory.AddRevive(count),
        ["skin_01"]  = _ => Skins.Unlock("skin_01"),
    });
    if (!result.IsSuccess)
        Debug.LogError($"우편 보상 등록 실패: {result.Reason}");
}
```

::: info 서버가 지급하는 보상
숫자 데이터 필드나 리스트·딕셔너리 원소에 더하는 보상은 등록하지 않아도 서버가 지급합니다. [서버가 지급하는 보상](/guide/mailbox/#server-grant)을 참고하세요. 지급 함수에서 예외가 나면 수령 결과가 `SupabaseReason.MailItemHandlerFailed` 실패로 돌아옵니다. 우편은 이미 받은 것으로 처리되고, 그 뒤 순서의 보상은 지급 함수에 넘어가지 않습니다. 지급 함수는 기다리지 않고 바로 끝나야 합니다 — `async` 함수를 넘기면 그 안의 실패를 알 수 없으니, 기다려야 하는 지급은 [아이템 핸들러 등록](/guide/mailbox/item-handler)을 쓰세요.
:::
