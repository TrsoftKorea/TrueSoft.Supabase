# 기기 등록

```csharp
Task<SupabaseResult> Supabase.RegisterPushAsync()
```

알림 권한을 묻고 이 기기를 푸시 알림 대상으로 등록합니다. 권한을 이미 허용한 기기는 묻지 않고 바로 등록합니다. 로그인 상태에서만 되며, 기기 주소가 바뀔 수 있으므로 실행할 때마다 로그인 뒤에 부릅니다.

```csharp
var push = await Supabase.RegisterPushAsync();
if (!push.IsSuccess && push.Reason != SupabaseReason.PushUnsupportedPlatform)
    Debug.Log($"푸시 등록 실패: {push.Reason}");
```

로그아웃(`Supabase.SignOutFullyAsync`)하면 등록이 풀립니다.

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.NotSignedIn` | 로그인 상태가 아닙니다 |
| `SupabaseReason.PushUnsupportedPlatform` | 에디터·PC처럼 푸시를 쓸 수 없는 환경입니다 |
| `SupabaseReason.PushNotConfigured` | `SupabaseSettings`의 푸시 알림 사용이나 Firebase 값이 비어 있습니다 |
| `SupabaseReason.PushPermissionDenied` | 플레이어가 알림 권한을 허용하지 않았습니다 |
| `SupabaseReason.PushTokenUnavailable` | 기기에서 푸시 주소를 받지 못했습니다 |
