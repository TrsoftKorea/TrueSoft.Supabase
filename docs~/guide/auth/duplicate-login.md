# 중복 로그인 감지

```csharp
event Action Supabase.OnDuplicateLoginDetected
```

다른 기기에서 같은 계정으로 로그인되면 발행됩니다. 앱 생명주기 전체를 관리하는 Manager에서 한 번만 등록하세요.

```csharp
void Awake()     => Supabase.OnDuplicateLoginDetected += OnDuplicateLogin;
void OnDestroy() => Supabase.OnDuplicateLoginDetected -= OnDuplicateLogin;

void OnDuplicateLogin()
{
    // 강제 로그아웃 후 로그인 화면으로 이동
    _ = Supabase.SignOutFullyAsync();
}
```

**실사용 예시**

다른 기기에서 로그인되면 안내 팝업을 띄워 타이틀로 돌려보내고, 그 사이 실패한 호출은 따로 알리지 않습니다.

```csharp
void OnDuplicateLogin()
{
    ShowPopup("다른 기기에서 로그인되어 접속을 종료합니다", onConfirm: async () =>
    {
        await Supabase.SignOutFullyAsync();
        SceneManager.LoadScene("Title");
    });
}

public async void OnOpenMailbox()
{
    var result = await Supabase.GetMailsAsync();
    if (result.Reason == SupabaseReason.DuplicateLogin) return;   // 안내는 위 이벤트가 띄웁니다
    if (!result.IsSuccess) { ShowToast("우편을 불러오지 못했습니다"); return; }
    RenderMailList(result.Data);
}
```

::: info 감지 시점
SDK 가 데이터를 읽거나 쓸 때마다 서버가 그 요청이 지금 로그인한 기기에서 왔는지 확인합니다. 따로 확인 요청을 보내지 않으므로 요청 수는 늘지 않습니다. 이와 별도로 60초마다 접속 확인을 보내고 앱으로 돌아올 때도 확인하므로, 아무것도 읽거나 쓰지 않는 동안에도 감지됩니다. 접속 확인은 운영 콘솔 대시보드의 동시접속자 집계에도 쓰이고, 세이브 저장이 성공하면 같은 일을 한 것이라 그 뒤 60초 동안은 보내지 않습니다. 로그인 직후 이 기기가 등록되기 전의 잠깐은 서버가 가를 수 없어 쓰기 전에 따로 확인합니다.
:::

::: info 감지된 뒤의 호출
다른 기기에서 로그인한 뒤에 데이터를 읽거나 쓰면 서버가 거절해 `SupabaseReason.DuplicateLogin`으로 실패하고 `OnDuplicateLoginDetected`가 발행됩니다. 세이브 저장·우편 받기 같은 쓰기뿐 아니라 우편 목록·순위 같은 조회도 같습니다. 자동 로그인으로 되살린 계정이 이미 다른 기기에서 쓰이고 있으면 로그인도 같은 사유로 실패합니다.
:::
