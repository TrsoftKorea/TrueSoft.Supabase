# 인증

인증은 플레이어 계정을 만들고 관리하는 기능입니다.  
별도 회원가입 없이 바로 시작하는 익명 로그인과, 소셜 계정 연동을 통한 기기 간 이어하기를 지원합니다.

---

## 익명 로그인

저장된 세션이 있으면 기존 계정으로 복원하고, 없으면 새 익명 계정을 생성해 로그인합니다.  
로그인하면 세션이 기기에 자동으로 저장되어, 다음 실행 시 `TriggerAutoLoginAsync()`로 복원할 수 있습니다.

소셜 로그인은 [소셜 로그인](./social-login.md)을 참고하세요.

```csharp
Task<SupabaseCallResult> Supabase.TrySignInAnonymouslyAsync()
```

익명(게스트) 계정으로 로그인합니다. 이미 비익명 계정으로 로그인된 경우 실패합니다.

**실패 원인**

| Reason | 설명 |
|--------|------|
| `SupabaseFailReason.UserBanned` | 차단된 계정 — `result.BanInfo` 참고 |
| `SupabaseFailReason.WithdrawalDeleted` | 탈퇴 처리된 계정 — 새 계정으로 재가입됨 |
| `SupabaseFailReason.NetworkError` | 네트워크 오류 또는 타임아웃 |

로그인 후 유저 데이터를 쓰려면 [데이터 로드](./user-data#로드)를 호출합니다. 자동 로그인은 이 로드를 내부에서 함께 처리합니다.

---

## 자동 로그인

씬에 `SupabaseRuntime` 컴포넌트를 배치하면 SDK가 초기화됩니다.  
로그인은 자동 실행되지 않으므로 원하는 타이밍에 직접 호출합니다.

```csharp
var result = await SupabaseRuntime.TriggerAutoLoginAsync();
if (result.Success)
{
    // 자동 로그인 성공 → 유저 세이브 로드도 완료된 상태
    InitGame();
}
else
{
    // 저장된 세션 없음 (첫 실행 또는 로그아웃 후) → 로그인 화면으로 이동
    ShowLoginScreen();
}
```

### 로그인 후 사용 가능한 값 {#로그인-후-사용-가능한-값}

로그인이 성공하면 아래 프로퍼티를 바로 사용할 수 있습니다.

| 프로퍼티 | 설명 |
|---------|------|
| `Supabase.MyProfile.DisplayName` | 닉네임. 설정 전에는 빈 문자열 |
| `Supabase.MyProfile.ServerCode` | 서버 코드 (예: `"GLOBAL"`, `"KR1"`) |
| `Supabase.MyProfile.IsWithdrawn` | 탈퇴 예약 여부 |
| `Supabase.UserId` | 플레이어 고유 ID. 재로그인·계정 연동 후에도 변하지 않음 |
| `Supabase.IsAnonymous` | 익명 로그인 여부 |
| `Supabase.IsLinkedWithGoogle` | Google 연동 여부 |
| `Supabase.IsLinkedWithApple` | Apple 연동 여부 |

---

## 로그아웃

```csharp
Task<SupabaseCallResult> Supabase.TrySignOutFullyAsync()
```

로그아웃하고 세션을 정리합니다. 익명 계정이면 기기 지문 기반 복구 토큰을 서버에 저장해 동일 기기에서 재접속 시 같은 계정으로 이어집니다.  
Google 로그인을 사용 중이라면 Android Play Services 계정 선택기도 함께 초기화됩니다.

---

## 익명 계정 복구

앱을 삭제했다가 재설치하거나 로그아웃 후 다시 익명 로그인을 하면, 기기 고유 지문을 이용해 이전 익명 계정을 자동으로 복구합니다.  
`TrySignInAnonymouslyAsync()` 또는 `TriggerAutoLoginAsync()` 호출 시 내부적으로 수행됩니다. 별도로 호출할 필요가 없습니다.

**복구 조건:**
- 같은 기기에서 재설치한 경우 복구됩니다.
- 기기를 변경하거나 지문이 달라진 경우 복구되지 않고 새 익명 계정이 생성됩니다.
- 소셜 계정으로 연동한 이후에는 소셜 로그인으로 복원되므로 이 기능이 필요하지 않습니다.

**복구 실패 시:** 새 익명 계정으로 로그인이 진행됩니다.  
별도 오류 이벤트는 발행되지 않습니다.

---

## 중복 로그인 감지

다른 기기에서 같은 계정으로 로그인되면 `OnDuplicateLoginDetected`가 발행됩니다.  
앱 생명주기 전체를 관리하는 Manager에서 한 번만 등록하세요.

60초 주기로 서버와 세션 토큰을 비교해 감지합니다. `SupabaseSettings > 중복 감지 폴링 주기`에서 조정할 수 있습니다.

```csharp
void Awake()     => Supabase.OnDuplicateLoginDetected += OnDuplicateLogin;
void OnDestroy() => Supabase.OnDuplicateLoginDetected -= OnDuplicateLogin;

void OnDuplicateLogin()
{
    // 강제 로그아웃 후 로그인 화면으로 이동
    _ = Supabase.TrySignOutFullyAsync();
}
```

---

## 차단된 계정 처리 {#ban-handling}

Supabase 대시보드에서 계정을 차단(`banned_until` 설정)하면, 해당 계정으로 로그인 시 SDK가 자동으로 차단 정보를 가져와 `result.BanInfo`에 채웁니다.

```csharp
var result = await Supabase.TrySignInAnonymouslyAsync();

if (!result.Success && result.BanInfo != null)
{
    var info = result.BanInfo;

    if (info.IsPermanentBan)
        Debug.Log("영구 차단");
    else
        Debug.Log($"차단 해제: {info.BannedUntil:yyyy-MM-dd HH:mm}");

    if (!string.IsNullOrEmpty(info.BanMessage))
        Debug.Log($"사유: {info.BanMessage}");
}
```

`SupabaseCallResult.Reason == SupabaseFailReason.UserBanned`일 때만 `BanInfo`가 유효하며, 그 외에는 항상 `null`입니다.


### 수동 조회

```csharp
Task<SupabaseBanInfo?> Supabase.TryGetBanInfoAsync(string accountId)
```

특정 계정의 차단 정보를 조회합니다. 차단 상태가 아니거나 조회 실패 시 `null`을 반환합니다.

**파라미터**

| 파라미터 | 설명 | 타입 |
|----------|------|------|
| `accountId` | 조회할 계정 ID (`auth.users.id`) | `string` |

**반환**

| 프로퍼티 | 타입 | 설명 |
|---------|------|------|
| `.IsPermanentBan` | `bool` | 영구 차단 여부 |
| `.BannedUntil` | `DateTime` | 차단 해제 일시. 영구 차단이면 의미 없음 |
| `.BanMessage` | `string` | 어드민이 설정한 차단 사유 메시지. 없으면 빈 문자열 |
