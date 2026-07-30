# 기다리지 않고 읽기

```csharp
SupabaseResult<DateTimeOffset> Supabase.GetServerNow()
```

캐시된 기준점으로 즉시 계산해 돌려줍니다. `await`가 없어 `Update`처럼 매 프레임 실행되는 곳에서도 쓸 수 있습니다.

```csharp
void Update()
{
    var t = Supabase.GetServerNow();
    if (!t.IsSuccess) { label.text = ""; return; }

    var left = deadlineUtc - t.Data;
    label.text = $"{left.Minutes}분 {left.Seconds}초";
}
```

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.ServerTimeNotSynced` | 아직 기준점이 없습니다. 이 호출이 동기화를 시작하므로 잠시 뒤부터 성공합니다 |

아직 맞추지 못한 상태에서는 **기기 시계를 대신 주지 않고 실패로 돌려줍니다.** 잘못된 시각이 조용히 흘러드는 것보다 값이 없는 편이 안전하기 때문입니다.

실패한 그 호출이 백그라운드 동기화를 시작합니다. 게임이 따로 동기화를 부를 필요는 없습니다.

::: tip 처음부터 값이 있게 하려면
로딩 화면에서 [서버 시각 조회](./now)를 한 번 기다리면 그 뒤로는 계속 성공합니다.
:::
