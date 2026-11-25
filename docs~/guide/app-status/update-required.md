# 업데이트 필요

```csharp
event Action Supabase.OnUpdateRequired
```

게임 중에 운영 콘솔에서 최소 버전을 올려 이 앱의 버전이 낮아지면 발행됩니다. SDK가 세션을 끊은 뒤에 불리고, 기기에 저장된 로그인은 남아 있습니다. 앱 생명주기 전체를 관리하는 Manager에서 한 번만 등록하세요.

```csharp
void Awake()     => Supabase.OnUpdateRequired += OnUpdateRequired;
void OnDestroy() => Supabase.OnUpdateRequired -= OnUpdateRequired;
```

**실사용 예시**

업데이트 안내를 띄우고 타이틀로 돌려보냅니다. 타이틀은 [상태 조회](/guide/app-status/status)로 스토어 주소를 받아 업데이트 안내를 다시 띄웁니다.

```csharp
void OnUpdateRequired()
{
    ShowPopup("새 버전으로 업데이트해야 합니다", onConfirm: () => SceneManager.LoadScene("Title"));
}

public async void OnOpenMailbox()
{
    var result = await Supabase.GetMailsAsync();
    if (result.Reason == SupabaseReason.UpdateRequired) return;   // 안내는 위 이벤트가 띄웁니다
    if (!result.IsSuccess) { ShowToast("우편을 불러오지 못했습니다"); return; }
    RenderMailList(result.Data);
}
```

::: info 감지 시점
최소 버전을 올린 뒤 데이터를 읽거나 쓰면 서버가 거절해 `SupabaseReason.UpdateRequired`로 실패하고 이 이벤트가 발행됩니다. 아무것도 읽거나 쓰지 않아도 60초마다 보내는 접속 확인에서 잡힙니다. 버전 통과 운영 계정으로 로그인했으면 발행되지 않습니다.
:::

::: info 로그인·계정 연결 중에는
로그인이나 계정 연결을 하는 도중에 걸리면 이벤트 없이 그 결과가 `SupabaseReason.UpdateRequired`로 실패합니다. 안내는 결과를 받은 곳에서 띄웁니다.
:::

::: warning 저장하지 않은 변경은 사라집니다
세션이 끊기면 아직 서버에 보내지 않은 유저 데이터 변경은 버려집니다.
:::
