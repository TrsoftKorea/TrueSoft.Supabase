# 인증 (Auth)

---

## 로그인

```csharp
// 익명(게스트) 로그인
await Supabase.TrySignInAnonymouslyAsync();

// Google 로그인 (Android 네이티브 Play Services)
await Supabase.TrySignInWithGoogleAsync();

// Google 로그인 (ID 토큰 직접 전달 — iOS / 커스텀 OAuth)
await Supabase.TrySignInWithGoogleIdTokenAsync(idToken);

// Apple 로그인 (ID 토큰 직접 전달 — 외부 SDK 없이 사용 가능)
await Supabase.TrySignInWithAppleIdTokenAsync(idToken);

// Apple 로그인 (iOS 네이티브 — Apple Authentication Manager 모듈 필요, TRUESOFT_APPLE_AUTH_AVAILABLE 정의)
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

// Apple 연동 (ID 토큰 직접 전달)
await Supabase.TryLinkAppleToCurrentAnonymousWithIdTokenAsync(idToken);
```

- 연동 성공 시 동일 `auth.users.id`를 유지하면서 `is_anonymous`가 false가 됩니다.
- 이미 다른 사용자에 연결된 계정이면 연동이 실패하고 기존 익명 세션은 유지됩니다.
- Supabase 대시보드 Authentication 설정에서 **Manual linking (beta)**를 활성화해야 합니다.

## 세션 복원 (수동)

로그인은 자동으로 실행되지 않습니다. 원하는 타이밍에 직접 호출하세요.

```csharp
// SupabaseRuntime (또는 서브클래스)을 상속한 컴포넌트에서
await TriggerAutoLoginAsync();
```

`TriggerAutoLoginAsync()`는 저장된 refresh_token으로 세션 복원을 시도합니다.  
세션이 없거나 만료된 경우에는 로그인 화면으로 분기하세요.

## 익명 계정 복구

기기 지문 기반으로 익명 계정을 복구합니다. SDK가 내부적으로 처리합니다.  
관련 SQL: `Sql/player/03_anonymous_recovery.sql`

## 주의사항

- Google이 이미 로그인된 상태에서 `TrySignInAnonymouslyAsync`를 호출하면 실패합니다. 먼저 `TrySignOutFullyAsync`로 로그아웃하세요.
- `TrySignInWithAppleAsync`는 Unity Package Manager > Built-in 탭에서 `Apple Authentication Manager` 모듈을 활성화하고 `TRUESOFT_APPLE_AUTH_AVAILABLE` 스크립팅 심볼을 정의해야 합니다. `TrySignInWithAppleIdTokenAsync`는 이 심볼 없이도 사용할 수 있습니다.
