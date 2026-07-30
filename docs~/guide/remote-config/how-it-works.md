# 개요

원격 설정은 앱을 업데이트하지 않고 서버에서 게임 수치(스테미나 최대치, 몬스터 체력 배율, 이벤트 텍스트 등)를 바꾸는 기능입니다.

## 값을 가져오는 시점

앱을 시작할 때 Remote Config를 자동으로 가져오지 않습니다.  
`GetAsync()`, `CreateListener()` 중 하나를 처음 호출하는 순간 해당 키만 서버에서 가져옵니다.

이후에는 가져온 값을 메모리에 보관해두고 빠르게 반환합니다.  
설정된 유효 시간이 지나면 낡은 값을 즉시 반환하면서 **동시에** 백그라운드에서 서버 갱신을 시작합니다. 갱신이 완료되면 다음 호출부터 새 값이 반환됩니다.

::: info
값을 읽는 속도는 항상 빠르고, 서버 갱신은 뒤에서 알아서 처리됩니다.
:::

## 어떤 패턴을 써야 하나요?

| 상황 | 추천 패턴 |
|------|-----------|
| 필요한&nbsp;시점에&nbsp;한&nbsp;번&nbsp;읽을&nbsp;때 | **`GetAsync()`** |
| 값이&nbsp;바뀌는&nbsp;순간&nbsp;반응해야&nbsp;할&nbsp;때 | **`CreateListener()`** |

자주 읽어야 하면 리스너 콜백에서 게임 필드에 담아 두고 그 필드를 읽습니다.

```csharp
_listener = RemoteConfig<GameplayConfig>.CreateListener(cfg => _config = cfg);
...
maxStamina = _config.maxStamina;   // 게임이 들고 있는 값
```
