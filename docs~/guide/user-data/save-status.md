# 저장 실패 알림

```csharp
event Action<SupabaseResult> Supabase.OnSaveStatusChanged
```

세이브 전송이 실패 상태로 들어가거나 벗어날 때 발행됩니다. 네트워크가 나쁜 곳에서 저장이 계속 실패하면 플레이어는 저장된 줄 알고 앱을 끌 수 있으므로, 저장 대기 표시를 띄우는 데 씁니다. 앱 생명주기 전체를 관리하는 Manager에서 한 번만 등록하세요.

```csharp
void Awake()     => Supabase.OnSaveStatusChanged += OnSaveStatusChanged;
void OnDestroy() => Supabase.OnSaveStatusChanged -= OnSaveStatusChanged;

void OnSaveStatusChanged(SupabaseResult status)
{
    saveWaitingIcon.SetActive(!status.IsSuccess);
}
```

실패한 변경은 사라지지 않고 쉬는 시간을 늘려 가며 자동으로 다시 보냅니다. 재시도마다 다시 알리지 않고, 자동 저장과 즉시 저장의 실패를 함께 알립니다. 다시 저장되거나, 다시 로드·로그아웃으로 보낼 변경이 사라지면 성공 결과로 한 번 알립니다. 실패로 알릴 때는 `Reason`이 그 사유입니다.

::: warning
실패 상태에서 앱이 종료되면 보내지 못한 변경은 사라집니다. 기기에 따로 보관하지 않습니다.
:::
