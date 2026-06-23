# 커스텀 클래스 요소

`List<MyItem>` · `Dictionary<string, MyItem>`처럼 요소를 직접 만든 클래스로 둘 수 있습니다. `MyItem`에 파라미터 없는 생성자가 있으면 그대로 저장·로드됩니다.

```csharp
[Serializable]
public sealed class MyItem
{
    public int  id;
    public int  count;
}
```

`MyItem`의 private 필드까지 저장하려면 `[JsonObject(MemberSerialization.Fields)]`를 붙입니다.
