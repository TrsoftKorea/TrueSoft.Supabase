# 인증 (Auth)

## 로그인

```csharp
// 익명 로그인
await Supabase.TrySignInAnonymouslyAsync();

// Google 로그인 (Android 네이티브)
await Supabase.TrySignInWithGoogleAsync();

// Google 로그인 (ID 토큰 직접 전달 — iOS / 커스텀 OAuth)
await Supabase.TrySignInWithGoogleIdTokenAsync(idToken);
```

---

## 로그아웃

```csharp
await Supabase.TrySignOutFullyAsync();
```

> [!WARNING]
> `TrySignOutAsync()`만 쓰면 Android에서 Google 계정 선택기 상태가 남아 다음 로그인 시 자동으로 이전 계정이 선택될 수 있습니다.  
> 반드시 `TrySignOutFullyAsync()`를 사용하세요.

---

## 익명 → Google 연동

> [!IMPORTANT]
> 익명 세션에서 직접 `TrySignInWithGoogleAsync`를 호출하면 `anonymous_session_requires_explicit_link` 오류가 반환됩니다.  
> 반드시 아래 연동 전용 API를 사용하세요.

```csharp
// Google 연동 (Android 네이티브)
await Supabase.TryLinkGoogleToCurrentAnonymousAsync();

// Google 연동 (ID 토큰 직접 전달)
await Supabase.TryLinkGoogleToCurrentAnonymousWithIdTokenAsync(idToken);
```

- 연동 성공 시 동일 `auth.users.id`를 유지하면서 `is_anonymous`가 false가 됩니다.
- 이미 다른 사용자에 연결된 계정이면 연동이 실패하고 기존 익명 세션은 유지됩니다.
- Supabase 대시보드 Authentication 설정에서 **Manual linking (beta)** 를 활성화해야 합니다.

---

## 세션 복원

```csharp
// 앱 재시작 시 저장된 refresh_token으로 세션 복원
await Supabase.TryRestoreSessionAsync();
```

> [!NOTE]
> `SupabaseRuntime` 컴포넌트를 씬에 배치하면 `Awake()`에서 자동으로 세션 복원을 시도합니다.  
> 별도로 호출할 필요가 없습니다.

---

## 익명 계정 복구

기기 지문 기반으로 익명 계정을 복구합니다. SDK가 내부적으로 처리합니다.  
관련 SQL: `Sql/player/06_anonymous_recovery_tokens.sql`

---

## 주의사항

> [!WARNING]
> - Google이 이미 로그인된 상태에서 `TrySignInAnonymouslyAsync`를 호출하면 실패합니다. 먼저 `TrySignOutFullyAsync`로 로그아웃하세요.
