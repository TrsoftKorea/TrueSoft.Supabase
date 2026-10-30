# 기기 등록

```csharp
Task<SupabaseResult> Supabase.RegisterPushAsync()
```

알림 권한을 묻고 이 기기를 푸시 알림 대상으로 등록합니다. 권한을 이미 허용한 기기는 묻지 않고 바로 등록합니다. 로그인 상태에서만 되며, 기기 주소가 바뀔 수 있으므로 실행할 때마다 로그인 뒤에 부릅니다.

```csharp
var push = await Supabase.RegisterPushAsync();
if (push.Reason == SupabaseReason.PushPermissionDenied)
    ShowNotice("알림을 받으려면 기기 설정에서 알림을 켜 주세요.");
```

에디터·PC에서는 푸시 알림 사용을 켰는지와 상관없이 `PushUnsupportedPlatform`으로 끝납니다. 이 사유와 `PushPermissionDenied`는 오류가 아니라 일반 로그로 남습니다. 로그아웃(`Supabase.SignOutFullyAsync`)하면 등록이 풀립니다.

**에러 코드**

| Reason | 설명 |
|--------|------|
| `SupabaseReason.PushUnsupportedPlatform` | 에디터·PC처럼 푸시를 쓸 수 없는 환경입니다 |
| `SupabaseReason.PushNotConfigured` | `SupabaseSettings`의 푸시 알림 사용이 꺼져 있거나 Firebase 값이 비어 있습니다 |
| `SupabaseReason.PushPermissionDenied` | 플레이어가 알림 권한을 허용하지 않았습니다 |
| `SupabaseReason.PushTokenUnavailable` | 기기에서 푸시 주소를 받지 못했습니다 |
| `SupabaseReason.PushTokenInvalid` | 서버가 기기 푸시 주소의 형식을 거절했습니다 |
| `SupabaseReason.ProfileNotFound` | 로그인한 계정의 플레이어 프로필이 없습니다. 로그인 후처리가 끝난 뒤 다시 부릅니다 |
