# 닉네임

닉네임 설정·변경과 공개 프로필 조회 기능입니다.  
닉네임은 서버에서 중복 여부를 검증하므로 전체 고유합니다.

## 내 닉네임 조회

```csharp
// 내 닉네임 — 로그인 후 자동 캐시된 프로필에서 조회
string myName = Supabase.MyProfile.DisplayName;
```

## 중복 확인 {#check}

```csharp
Task<SupabaseCallResult> Supabase.TryIsDisplayNameAvailableAsync(string displayName)
```

닉네임 사용 가능 여부를 확인합니다. `result.Success`가 `true`면 사용 가능, `false`면 이미 사용 중입니다.  
현재 계정이 이미 사용 중인 닉네임은 사용 가능으로 처리합니다.

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `displayName` | 확인할 닉네임. 최대 64자 |

**실패 원인**

| Reason | 설명 |
|--------|------|
| `SupabaseFailReason.DisplayNameTaken` | 이미 사용 중인 닉네임 |
| `SupabaseFailReason.NotSignedIn` | 로그인 상태가 아닙니다 |
| `SupabaseFailReason.NetworkError` | 네트워크 오류 또는 타임아웃 |

## 닉네임 설정 {#set}

```csharp
Task<SupabaseCallResult> Supabase.TrySetMyDisplayNameAsync(string displayName)
```

내 닉네임을 설정합니다. 현재 닉네임과 동일하면 네트워크 요청 없이 성공 처리됩니다.

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `displayName` | 설정할 닉네임. 최대 64자 |

**실패 원인**

| Reason | 설명 |
|--------|------|
| `SupabaseFailReason.DisplayNameTaken` | 이미 사용 중인 닉네임 |
| `SupabaseFailReason.DisplayNameTooLong` | 허용 길이 초과 |
| `SupabaseFailReason.NotSignedIn` | 로그인 상태가 아닙니다 |
| `SupabaseFailReason.NetworkError` | 네트워크 오류 또는 타임아웃 |

## 다른 플레이어 조회 {#get-other}

```csharp
Task<string> Supabase.TryGetPublicDisplayNameAsync(string userId, string defaultValue = "")
```

다른 플레이어의 닉네임을 조회합니다. 조회 실패 시 `defaultValue`를 반환합니다.

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `userId` | 조회할 플레이어 ID (`profiles.user_id`) |
| `defaultValue` | 조회 실패 시 반환할 기본값 (기본값: `""`) |

**반환**

조회한 플레이어의 닉네임 문자열. 조회 실패 시 `defaultValue`.
