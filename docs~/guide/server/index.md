# 서버

여러 서버로 나뉜 게임에서 이 기기가 접속할 서버를 정합니다.

## 두 가지 서버 값

| 값 | 어디에 있나 | 무엇인가 |
|----|------------|----------|
| `Supabase.ServerCode` | 기기 로컬 | 이 기기가 **접속하려는** 서버 |
| [서버 정보 조회](./info) | DB | 계정에 **배정된** 서버 |

로그인할 때 SDK가 둘을 비교해, 다르면 계정을 로컬에서 고른 서버로 옮깁니다. 그래서 평소에는 두 값이 같습니다.

## 현재 선택된 서버

```csharp
string Supabase.ServerCode
```

설정한 적이 없으면 `SupabaseSettings`의 기본 서버 코드를 돌려줍니다. 값이 비는 일은 없습니다.

```csharp
label.text = Supabase.ServerCode;   // "GLOBAL"
```

바꾸려면 [서버 선택](./select)을 호출합니다.
