# 닉네임 · 프로필

닉네임 설정·변경과 공개 프로필 조회 기능입니다.  
닉네임은 서버에서 중복 여부를 검증하므로 전체 고유합니다.

---

## 닉네임

```csharp
// 내 닉네임 — 로그인 후 자동 캐시된 프로필에서 조회
string myName = Supabase.MyProfile.DisplayName;
```

---

#### 중복 확인

```csharp
Task<SupabaseCallResult> Supabase.TryIsDisplayNameAvailableAsync(string displayName)
```

닉네임 사용 가능 여부를 확인합니다. `result.Success`가 `true`면 사용 가능, `false`면 이미 사용 중입니다.  
현재 계정이 이미 사용 중인 닉네임은 사용 가능으로 처리합니다.

**파라미터**

| 파라미터 | 설명 | 타입 |
|----------|------|------|
| `displayName` | 확인할 닉네임. 최대 64자 | `string` |

**실패 원인**

| Reason | 설명 |
|--------|------|
| `SupabaseFailReason.DisplayNameTaken` | 이미 사용 중인 닉네임 |
| `SupabaseFailReason.NotSignedIn` | 로그인 상태가 아닙니다 |
| `SupabaseFailReason.NetworkError` | 네트워크 오류 또는 타임아웃 |

---

#### 닉네임 설정

```csharp
Task<SupabaseCallResult> Supabase.TrySetMyDisplayNameAsync(string displayName)
```

내 닉네임을 설정합니다. 현재 닉네임과 동일하면 네트워크 요청 없이 성공 처리됩니다.

**파라미터**

| 파라미터 | 설명 | 타입 |
|----------|------|------|
| `displayName` | 설정할 닉네임. 최대 64자 | `string` |

**실패 원인**

| Reason | 설명 |
|--------|------|
| `SupabaseFailReason.DisplayNameTaken` | 이미 사용 중인 닉네임 |
| `SupabaseFailReason.DisplayNameTooLong` | 허용 길이 초과 |
| `SupabaseFailReason.NotSignedIn` | 로그인 상태가 아닙니다 |
| `SupabaseFailReason.NetworkError` | 네트워크 오류 또는 타임아웃 |

```csharp
// 중복 확인 후 설정
bool available = await Supabase.TryIsDisplayNameAvailableAsync("Player123");
if (available)
    await Supabase.TrySetMyDisplayNameAsync("Player123");
```

---

#### 다른 플레이어 조회

```csharp
Task<string> Supabase.TryGetPublicDisplayNameAsync(string userId, string defaultValue = "")
```

다른 플레이어의 닉네임을 조회합니다. 조회 실패 시 `defaultValue`를 반환합니다.

**파라미터**

| 파라미터 | 설명 | 타입 |
|----------|------|------|
| `userId` | 조회할 플레이어 ID (`profiles.user_id`) | `string` |
| `defaultValue` | 조회 실패 시 반환할 기본값 (기본값: `""`) | `string` |

---

## 프로필 조회

다른 플레이어의 공개 프로필(닉네임, 서버 코드 등)을 조회합니다.  
**내 프로필은 로그인 완료 시 자동으로 조회·캐시**됩니다. 별도 API 호출 없이 바로 사용할 수 있습니다.  
사용 가능한 프로퍼티 목록은 [인증 > 로그인 후 사용 가능한 값](./auth.md#로그인-후-사용-가능한-값)을 참고하세요.

```csharp
Task<PublicProfileSnapshot?> Supabase.TryGetPublicProfileAsync(string userId)
```

다른 플레이어의 공개 프로필을 조회합니다. 조회 실패 시 `null`을 반환합니다.

**파라미터**

| 파라미터 | 설명 | 타입 |
|----------|------|------|
| `userId` | 조회할 플레이어 ID (`profiles.user_id`) | `string` |

**반환**

| 프로퍼티 | 타입 | 설명 |
|---------|------|------|
| `.DisplayName` | `string` | 닉네임 |
| `.ServerCode` | `string` | 서버 코드 (예: `"GLOBAL"`, `"KR1"`) |
| `.IsWithdrawn` | `bool` | 탈퇴 예약 여부 |

```csharp
var profile = await Supabase.TryGetPublicProfileAsync(userId);
```
