# 서버 시간

```csharp
Task<SupabaseResult<DateTimeOffset>> Supabase.GetServerNowAsync()
```

서버 기준 UTC 시각을 가져옵니다. 로그인 없이 호출할 수 있습니다.

```csharp
var t = await Supabase.GetServerNowAsync();
if (!t.IsSuccess) return;

if (t.Data < eventEndUtc)
    GrantEventReward();
```

**반환**

`.Data`에 서버 기준 UTC 시각이 `DateTimeOffset`으로 담깁니다.

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.NetworkError` | 한 번도 시각을 받지 못했고 지금도 서버에 닿지 못했습니다 |

## 자주 불러도 됩니다 {#cache}

한 번 받아 두면 SDK가 기준점을 들고 있어서, 이후 호출은 **네트워크를 타지 않고 즉시 돌아옵니다.** 30분이 지나거나 앱이 백그라운드에서 돌아오면 다음 호출에서 알아서 다시 맞춥니다.

그래서 게임이 따로 캐시를 만들 필요가 없습니다. 필요한 곳에서 그때그때 부르면 됩니다.

::: info 기기 시계를 바꿔도 흔들리지 않습니다
기준점을 기기 시계가 아니라 앱이 켜진 뒤 흐른 시간으로 재기 때문입니다. 시각을 한 번이라도 받아 둔 뒤에는 네트워크가 끊겨도, 사용자가 시계를 돌려도 계속 정확합니다.
:::

::: warning 클라이언트가 계산한 값입니다
기준점만 서버에서 받고 그 뒤로는 로컬에서 흘립니다. 조작을 막아야 하는 판정은 서버에서 하세요.
:::

## 기다리지 않고 읽기 {#sync}

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
로딩 화면에서 [`GetServerNowAsync()`](#cache)를 한 번 기다리면 그 뒤로는 계속 성공합니다.
:::
