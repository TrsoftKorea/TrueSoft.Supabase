# 설치

## 런타임 배치

게임에 PlayNANOO가 있으면 SDK가 병행 런타임을 저절로 켭니다. 따로 가져올 것은 없습니다. 씬에서 `SupabaseRuntime` 대신 SDK 버전에 맞는 컴포넌트를 하나 배치합니다.

| 구현체 | 사용 API |
|--------|---------|
| `PlayNanooRuntime` | 신버전 `AccountManagerV20240401.*` |
| `PlayNanooLegacyRuntime` | 구버전 `AccountGuestSignIn` / `AccountManager.*` |

씬에는 `PlayNanooRuntime` / `PlayNanooLegacyRuntime` 중 하나만 둡니다. `SupabaseRuntime`을 따로 배치하지 않습니다.

::: warning 0.8.0 전에 샘플로 가져온 게임
0.8.0부터 병행 런타임은 샘플이 아니라 SDK 안에 들어 있습니다. 판을 올리기 전에 `Assets/Samples/TrueBase/<판>/PlayNANOO Migration` 폴더를 먼저 지우세요. 남겨 두면 같은 이름의 클래스가 둘이 돼 컴파일되지 않습니다. 0.5.1 샘플을 쓰던 씬은 컴포넌트와 설정값이 그대로 이어집니다. 그보다 옛 판 샘플을 쓰던 씬은 컴포넌트가 `Missing`으로 보이니 한 번 다시 넣으세요.
:::

## 게임의 플레이나누 계정 코드 정리 {#remove-game-token-code}

플레이나누 로그인·로그아웃·탈퇴와 토큰 갱신은 이제 런타임이 맡습니다. 게임 코드에 남은 아래 호출은 지웁니다.

- 플레이나누 토큰 갱신·토큰 로그인·토큰 로그아웃 호출. `AccountTokenRefresh`·`AccountTokenSignIn`·`AccountTokenSignOut`, 신버전은 `AccountManagerV20240401.TokenRefresh` 등입니다.
- 플레이나누 호출이 토큰 만료로 실패했을 때 토큰을 갱신하는 처리
- 주기적으로 토큰을 갱신하는 코루틴

::: danger 남겨 두면 플레이나누 기능이 통째로 멈춥니다
게임이 들고 있던 토큰 변수는 이제 채워지지 않아 비어 있습니다. 플레이나누는 갱신에 넘긴 값을 먼저 자기 토큰으로 저장하므로, 빈 값으로 갱신을 한 번 부르는 순간 토큰이 지워집니다. 그 뒤 저장·결제 등 모든 플레이나누 호출이 `30005`로 실패합니다. 런타임의 주기 갱신이 다시 채워도 게임 코드가 남아 있는 한 반복해서 끊깁니다.
:::

게임 폴더에서 아래 명령을 실행하면 남은 호출을 찾을 수 있습니다.

```powershell
Get-ChildItem -Recurse -Filter *.cs Assets | Select-String -Pattern "AccountTokenRefresh|AccountTokenSignIn|AccountTokenSignOut|AccountManagerV20240401\.Token" | Where-Object { $_.Path -notmatch "Samples" }
```
