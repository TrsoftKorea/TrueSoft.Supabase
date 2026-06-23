# 빠른 시작

원격 설정은 앱을 업데이트하지 않고도 서버에서 게임 수치를 바꿀 수 있는 기능입니다.  
예를 들어 스테미나 최대치, 몬스터 체력 배율, 이벤트 배너 텍스트 등을 DB에서 관리할 수 있습니다.

---

## 1단계 — 설정 클래스 작성

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

## 2단계 — DB에 값 입력

Retool 등 관리 도구에서 `remote_config` 테이블에 행을 추가합니다 — `key`(예: `gameplay_v1`)와 클래스 구조와 같은 모양의 `value_json`을 입력합니다.

## 3단계 — 코드에서 사용

```csharp
// 가장 간단한 사용법 — 값이 필요할 때 한 번 읽기
var reader = RemoteConfig<GameplayConfig>.CreateReader();
var cfg = await reader();

if (cfg != null)
{
    maxStamina = cfg.maxStamina;
}
```
