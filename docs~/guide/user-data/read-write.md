# 읽기 / 쓰기

## 값 읽기 / 쓰기

생성된 static 프로퍼티로 접근합니다.

```csharp
int lv = PlayerSave.Level;

PlayerSave.Level = 10;
PlayerSave.Coins += 100;
```

값을 쓰면 `MarkDirty()`가 자동으로 호출되고, `SupabaseRuntime`이 쿨타임 주기로 자동 저장합니다. 스칼라는 **이전과 같은 값을 쓰면 아무 일도 일어나지 않습니다** — 매 프레임 같은 값을 대입해도 저장이 발생하지 않습니다.

## 컬렉션

`List`, 배열, `Dictionary` 컬럼은 일반 컬렉션과 똑같이 다루면 됩니다. 항목을 추가하거나 바꾸면 다른 값과 마찬가지로 쿨타임 주기에 자동 저장됩니다.

```csharp
PlayerSave.Inventory.Add(5);
PlayerSave.Inventory[0] = 9;
PlayerSave.Stats["atk"] = 100;

PlayerSave.Inventory = new List<int>{1, 2}; // 통째 교체도 가능
```

::: tip 중첩 컬렉션
2차원 컬렉션(`List<List<T>>` 등)은 `AutoList2D` / `AutoDict2D`를 사용하세요. 자세한 내용은 [자동 확장 컬렉션](/guide/data-types/auto-collections)을 참고하세요.
:::

쓸 수 있는 타입과 직렬화 규칙은 [데이터 타입](/guide/data-types/supported)을 참고하세요.
