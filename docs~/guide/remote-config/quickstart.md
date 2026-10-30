# 빠른 시작

원격 설정은 앱을 업데이트하지 않고도 서버에서 게임 수치를 바꿀 수 있는 기능입니다.  
예를 들어 스테미나 최대치, 몬스터 체력 배율, 이벤트 배너 텍스트 등을 DB에서 관리할 수 있습니다.

## 1단계 · 설정 클래스 작성

DB에 저장된 JSON 구조와 같은 모양의 C# 클래스를 만들고 `[RemoteConfigKey]`를 붙입니다.

```csharp
using Newtonsoft.Json;
using TrueBase.Unity;

[RemoteConfigKey("gameplay_v1")]   // DB의 키 이름
public class GameplayConfig
{
    public bool  enabled;
    public int   maxStamina;
    public float spawnInterval;
}
```

## 2단계 · DB에 값 입력

운영 콘솔 **원격 설정**에서 키를 추가하고, 클래스의 필드마다 필드명·타입·값을 넣습니다. 키는 예를 들어 `gameplay_v1`입니다. 추가한 값은 운영 콘솔 **변경 관리**에서 게시해야 게임에 내려갑니다.

## 3단계 · 코드에서 사용

```csharp
// 가장 간단한 사용법 — 값이 필요할 때 한 번 읽기
var cfg = await RemoteConfig<GameplayConfig>.GetAsync();
if (cfg.IsSuccess)
{
    maxStamina = cfg.Data.maxStamina;
}
```
