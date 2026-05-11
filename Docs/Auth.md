# 인증 (Auth)

---

## 로그인

```csharp
// 익명 로그인
await Supabase.TrySignInAnonymouslyAsync();

// Google 로그인 (Android 네이티브)
await Supabase.TrySignInWithGoogleAsync();

// Google 로그인 (ID 토큰 직접 전달 — iOS / 커스텀 OAuth)
await Supabase.TrySignInWithGoogleIdTokenAsync(idToken);

// Apple 로그인 (iOS, com.unity.modules.appleauthenticationmanager 필요)
await Supabase.TrySignInWithAppleAsync();
```

## 로그아웃

```csharp
await Supabase.TrySignOutFullyAsync();
// Android에서는 Google 네이티브 로그아웃도 함께 처리됩니다.
// TrySignOutAsync()만 쓰면 Google 계정 선택기 상태가 남을 수 있습니다.
```

## 익명 → Google / Apple 연동

익명 세션에서 직접 `TrySignInWithGoogleAsync`를 호출하면 실패합니다.  
연동 전용 API를 사용하세요.

```csharp
// Google 연동 (Android 네이티브)
await Supabase.TryLinkGoogleToCurrentAnonymousAsync();

// Google 연동 (ID 토큰 직접 전달)
await Supabase.TryLinkGoogleToCurrentAnonymousWithIdTokenAsync(idToken);

// Apple 연동 (iOS)
await Supabase.TryLinkAppleToCurrentAnonymousAsync();
```

- 연동 성공 시 동일 `auth.users.id`를 유지하면서 `is_anonymous`가 false가 됩니다.
- 이미 다른 사용자에 연결된 계정이면 연동이 실패하고 기존 익명 세션은 유지됩니다.
- Supabase 대시보드 Authentication 설정에서 **Manual linking (beta)** 를 활성화해야 합니다.

## 세션 복원

```csharp
// 앱 재시작 시 저장된 refresh_token으로 세션 복원
await Supabase.TryRestoreSessionAsync();
```

## 익명 계정 복구

기기 지문 기반으로 익명 계정을 복구합니다. SDK가 내부적으로 처리합니다.  
관련 SQL: [`Sql/player/06_anonymous_recovery_tokens.sql`](../Sql/player/06_anonymous_recovery_tokens.sql)

## 주의사항

- Google이 이미 로그인된 상태에서 `TrySignInAnonymouslyAsync`를 호출하면 실패합니다. 먼저 `TrySignOutFullyAsync`로 로그아웃하세요.
- Apple 로그인은 Unity Package Manager > Built-in 탭에서 `Apple Authentication Manager` 모듈을 활성화해야 합니다.
