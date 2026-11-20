# 서버 정보 조회

```csharp
Task<SupabaseResult<ServerInfo>> Supabase.GetServerInfoAsync()
```

DB 컬럼 `user_profiles.server_id`에 기록된 내 서버에 대응하는 서버 코드를 조회합니다. 서버 배정은 로그인 시 자동으로 동기화됩니다.

```csharp
var info = await Supabase.GetServerInfoAsync();
if (info.IsSuccess)
    serverLabel.text = info.Data.ServerCode;
```

**반환**

`.Data`는 `ServerInfo`입니다.

| 프로퍼티 | 타입 | 설명 |
|---------|------|------|
| `ServerId` | `string` | 서버의 내부 ID |
| `ServerCode` | `string` | 서버 코드. 예: `"GLOBAL"`, `"KR1"` |

**실사용 예시**

계정 정보 창을 열 때 내 서버를 플레이어가 읽는 이름으로 보여 줍니다.

```csharp
async void OnOpenAccountInfo()
{
    serverLabel.text = "-";
    var info = await Supabase.GetServerInfoAsync();
    if (!info.IsSuccess) return;   // 로그인 전이거나 네트워크 오류면 비워 둡니다

    serverLabel.text = ServerNames.DisplayName(info.Data.ServerCode);   // "KR1" → "한국 1서버"
}
```
