# 원격 설정

앱을 업데이트하지 않고 서버에서 게임 수치(스태미나 최대치, 몬스터 체력 배율, 이벤트 텍스트 등)를 바꾸는 기능입니다.

## 값을 가져오는 시점 {#fetch-timing}

앱을 시작할 때 Remote Config를 자동으로 가져오지 않습니다.  
`GetAsync()`, `CreateListener()` 중 하나를 처음 호출하는 순간 해당 키만 서버에서 가져옵니다.

이후에는 가져온 값을 메모리에 보관해두고 빠르게 반환합니다.  
유효 시간이 지난 뒤의 `GetAsync()`는 서버에서 새 값을 받은 뒤 반환합니다. 리스너는 `pollInterval` 주기로 서버를 확인해 값이 바뀌면 콜백을 부릅니다.

::: info
유효 시간 안의 읽기는 네트워크 없이 바로 끝나고, 유효 시간이 지난 첫 읽기만 서버 응답을 기다립니다.
:::

## 어떤 패턴을 써야 하나요? {#which-pattern}

| 상황 | 추천 패턴 |
|------|-----------|
| 필요한&nbsp;시점에&nbsp;한&nbsp;번&nbsp;읽을&nbsp;때 | **`GetAsync()`** |
| 값이&nbsp;바뀌는&nbsp;순간&nbsp;반응해야&nbsp;할&nbsp;때 | **`CreateListener()`** |

자주 읽어야 하면 리스너 콜백에서 게임 필드에 담아 두고 그 필드를 읽습니다.

```csharp
GameplayConfig _config = new();   // 첫 값이 오기 전에 쓸 기본값
_listener = RemoteConfig<GameplayConfig>.CreateListener(cfg => _config = cfg);
...
maxStamina = _config.maxStamina;   // 게임이 들고 있는 값
```

처음이라면 [빠른 시작](/guide/remote-config/quickstart)을 참고하세요. 설계와 역직렬화 주의점은 [더 알아보기](/guide/remote-config/advanced)에 있습니다.

## 메서드

| 메서드 | 설명 |
|--------|------|
| [`GetAsync`](/guide/remote-config/get) | 필요할 때 한 번 조회 |
| [`CreateListener`](/guide/remote-config/listener) | 값이 바뀔 때 콜백 호출 |
