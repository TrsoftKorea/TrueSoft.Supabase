# 값 읽기

```csharp
Task<SupabaseResult<T>> RemoteConfig<T>.GetAsync(int maxStale = 0)
```

값을 한 번 읽습니다. 캐시가 신선하면 즉시, 오래됐으면 서버에서 새 값을 받은 뒤 반환합니다.

```csharp
var cfg = await RemoteConfig<GameplayConfig>.GetAsync();
if (!cfg.IsSuccess) return;

maxStamina = cfg.Data.maxStamina;
```

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `maxStale` | 유효로 간주할 최대 캐시 경과 초. `0`이면 이 키에 앞서 지정한 값을, 없으면 300초를 씁니다 (기본값: `0`) |

**반환**

`.Data`는 역직렬화된 설정 객체 `T`입니다.

**실사용 예시**

상점 버튼을 누르면 최신 가격 설정을 읽고, 받지 못하면 상점을 열지 않습니다.

```csharp
public async void OnClickOpenShop()
{
    loadingSpinner.SetActive(true);
    var result = await RemoteConfig<ShopConfig>.GetAsync(maxStale: 60);   // 가격은 1분 넘은 값을 쓰지 않습니다
    loadingSpinner.SetActive(false);

    if (!result.IsSuccess) { ShowToast("상점 정보를 불러오지 못했습니다"); return; }

    shopPanel.Open(result.Data.products);
}
```
