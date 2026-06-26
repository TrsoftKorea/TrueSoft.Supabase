# 인증 API

로그인 상태·프로필 등 값은 [로그인 후 사용 가능한 값](/guide/auth/auto-login#after-login-values)을 참고하세요.

## 로그인

| 메서드 | 설명 |
|--------|------|
| [`TrySignInAnonymouslyAsync`](/guide/auth/anonymous) | 게스트(익명) 로그인 |
| [`TrySignInWithGoogleAsync`](/guide/social/google/signin-android) | Google 로그인 (Android 네이티브) |
| [`TrySignInWithGoogleIdTokenAsync`](/guide/social/google/signin-ios) | Google 로그인 (iOS·커스텀 ID 토큰) |
| [`TrySignInWithAppleIdTokenAsync`](/guide/social/apple/signin) | Apple 로그인 (ID 토큰) |
| [`SupabaseRuntime.TriggerAutoLoginAsync`](/guide/auth/auto-login) | 저장된 세션으로 자동 로그인 |

## 계정 연동

| 메서드 | 설명 |
|--------|------|
| [`TryLinkGoogleToCurrentAnonymousAsync`](/guide/social/google/link-android) | 익명 → Google 연동 (Android) |
| [`TryLinkGoogleToCurrentAnonymousWithIdTokenAsync`](/guide/social/google/link-ios) | 익명 → Google 연동 (ID 토큰) |
| [`TryLinkAppleToCurrentAnonymousWithIdTokenAsync`](/guide/social/apple/link) | 익명 → Apple 연동 (ID 토큰) |
| [`TryLinkGoogleNativeAsync`](/guide/social/google/add-android) | 로그인된 계정에 Google 추가 연동 (Android) |
| [`TryLinkGoogleWithIdTokenAsync`](/guide/social/google/add-ios) | 로그인된 계정에 Google 추가 연동 (ID 토큰) |
| [`TryLinkAppleWithIdTokenAsync`](/guide/social/apple/add) | 로그인된 계정에 Apple 추가 연동 (ID 토큰) |
| [`TryUnlinkGoogleAsync`](/guide/social/google/unlink) | 계정에서 Google 연동 해제 |
| [`TryUnlinkAppleAsync`](/guide/social/apple/unlink) | 계정에서 Apple 연동 해제 |

## 로그아웃 · 보안

| 멤버 | 설명 |
|------|------|
| [`TrySignOutFullyAsync`](/guide/auth/logout) | 전체 로그아웃 |
| [`TryGetBanInfoAsync`](/guide/auth/ban#manual-lookup) | 계정 차단 정보 조회 |
| [`OnDuplicateLoginDetected`](/guide/auth/duplicate-login) | 중복 로그인 감지 이벤트 |
