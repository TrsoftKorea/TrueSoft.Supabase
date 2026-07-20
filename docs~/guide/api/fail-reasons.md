# 에러 코드 전체

`SupabaseResult.Reason`이 가질 수 있는 모든 `SupabaseFailCode` 값을 분류별로 모았습니다. 각 함수 가이드의 **에러 코드** 표는 그 호출이 반환할 수 있는 사유만 추린 것이고, 이 페이지는 카탈로그 전체 레퍼런스입니다.

## 분기 방법

`Reason`은 enum이라 타입 안전하게 분기하고, 원문 문자열이 필요하면 `ErrorCode`를 씁니다.

```csharp
var result = await Supabase.SignInAnonymouslyAsync();
if (!result.IsSuccess)
{
    if (result.Reason == SupabaseFailCode.NetworkError) ShowRetry();
    else ShowError(result.ErrorCode);
}
```

아래 표의 `Reason`은 `SupabaseErrorCode` 문자열 상수와도 이름이 1:1로 같습니다(`SupabaseErrorCode.UserBanned` ↔ `SupabaseFailCode.UserBanned`). `ErrorCode`는 `Reason`이 매핑되는 원문 문자열입니다.

::: info None과 Unknown
`None`은 성공했거나 실패 사유가 없는 경우(`ErrorCode`가 비어 있음)이고, `Unknown`은 사유는 있으나 카탈로그에 없는 동적·서버·네이티브 문자열입니다. 이때 원문은 `ErrorCode`에서 확인하세요.
:::

## 공통 · 세션

| Reason | ErrorCode | 설명 |
|--------|-----------|------|
| `NotInitialized` | `sdk_not_initialized` | SDK가 초기화되지 않았습니다 |
| `NotSignedIn` | `auth_not_signed_in` | 로그인 상태가 아닙니다 |
| `SessionRequired` | `session_required` | 로그인 세션이 필요합니다 |
| `AccessTokenEmpty` | `access_token_empty` | 액세스 토큰이 비어있습니다 |
| `NetworkError` | `http_response_null` | 네트워크 오류 또는 타임아웃 |

## 로그인 · 세션 복원

| Reason | ErrorCode | 설명 |
|--------|-----------|------|
| `UserBanned` | `user_banned` | 계정이 차단되었습니다. `BanInfo`에서 상세 확인 |
| `DuplicateLogin` | `duplicate_login_detected` | 다른 기기에서 동일 계정 로그인으로 현재 세션이 무효화됨 |
| `SignedInNonAnonymous` | `signed_in_non_anonymous_sign_out_first` | 비익명 계정으로 로그인 중. 먼저 `SignOutFullyAsync` 호출 |
| `AnonymousRequired` | `anonymous_session_required` | 익명 세션이 필요한 작업입니다 |
| `AnonymousRequiresLink` | `anonymous_session_requires_explicit_link` | 익명 세션에서는 `Link*` 메서드를 사용해야 합니다 |
| `AnonymousSessionTokenMissing` | `anonymous_session_token_missing` | 익명 세션 토큰이 없습니다. 재로그인 필요 |
| `RestoreSessionFailed` | `restore_session_failed` | 저장된 세션 복원에 실패했습니다 |
| `AutoLoginNoToken` | `auto_login_blocked_or_no_token` | 자동 로그인이 차단되었거나 저장된 토큰이 없습니다 |
| `AutoLoginFailed` | `auto_login_on_start_failed` | 앱 시작 자동 로그인에 실패했습니다 |
| `AfterAutoLoginFailed` | `after_auto_login_failed` | 자동 로그인 후처리 훅이 실패를 반환했습니다 |
| `InvalidSignInMethod` | `invalid_signin_method` | 지원하지 않는 로그인 방식입니다 |

## 연동 해제

| Reason | ErrorCode | 설명 |
|--------|-----------|------|
| `IdentityNotLinked` | `identity_not_linked` | 해제하려는 provider가 현재 계정에 연동되어 있지 않습니다 |
| `CannotUnlinkLastIdentity` | `cannot_unlink_last_identity` | 마지막 남은 연동은 해제할 수 없습니다 |
| `UnlinkFailed` | `unlink_failed` | 연동 해제에 실패했습니다 |

## Google 로그인

| Reason | ErrorCode | 설명 |
|--------|-----------|------|
| `GoogleSignInCancelled` | `google_signin_cancelled` | 사용자가 Google 로그인 화면을 직접 취소했습니다 |
| `GoogleWebClientIdEmpty` | `google_web_client_id_empty` | `SupabaseSettings.googleWebClientId`가 설정되지 않았습니다 |
| `GoogleSignInFailed` | `google_signin_failed` | Play Services 내부 오류입니다 |
| `GoogleIdTokenEmpty` | `google_id_token_empty` | Google ID 토큰을 획득하지 못했습니다 |
| `GoogleLinkFailed` | `google_link_failed` | Supabase Google identity 연동에 실패했습니다 |
| `GoogleLinkNotCleared` | `google_link_anonymous_not_cleared` | Google 연동 후 익명 플래그가 해제되지 않았습니다 |
| `GoogleProviderNull` | `google_provider_null` | Google 로그인 프로바이더가 null입니다 |
| `GoogleResultNull` | `google_result_null` | Google 로그인 결과가 null입니다 |

## Apple 로그인

| Reason | ErrorCode | 설명 |
|--------|-----------|------|
| `AppleIdTokenEmpty` | `apple_id_token_empty` | 전달된 Apple ID 토큰이 비어있습니다 |
| `AppleSignInCancelled` | `apple_signin_cancelled` | 사용자가 Apple 로그인 화면을 직접 취소했습니다 |
| `AppleSignInIosOnly` | `apple_login_ios_only` | 네이티브 Apple 로그인은 iOS에서만 지원됩니다 |
| `AppleSignInUnsupportedPlatform` | `apple_signin_unsupported_platform` | 현재 플랫폼에서는 Apple 로그인을 지원하지 않습니다 |
| `AppleLinkFailed` | `apple_link_failed` | Supabase Apple identity 연동에 실패했습니다 |
| `AppleLinkNotCleared` | `apple_link_anonymous_not_cleared` | Apple 연동 후 익명 플래그가 해제되지 않았습니다 |
| `PlayNanooBrowserAppleUnsupported` | `playnanoo_active_browser_apple_unsupported` | PlayNANOO 연동 중에는 브라우저 기반 Apple 로그인을 쓸 수 없습니다 |

## 웹 OAuth

| Reason | ErrorCode | 설명 |
|--------|-----------|------|
| `OAuthRefreshTokenMissing` | `oauth_refresh_token_missing` | 웹 OAuth 리다이렉트에 refresh_token이 없습니다 |
| `OAuthRedirectSchemeEmpty` | `oauth_redirect_scheme_empty` | OAuth 리다이렉트 스킴이 비어있습니다 |
| `OAuthLoginInProgress` | `oauth_login_already_in_progress` | 이미 진행 중인 웹 OAuth 로그인이 있습니다 |

## 닉네임

| Reason | ErrorCode | 설명 |
|--------|-----------|------|
| `NameTaken` | `display_name_taken` | 이미 사용 중인 닉네임입니다 |
| `NameTooLong` | `display_name_too_long` | 닉네임이 허용 길이를 초과합니다 |

## 탈퇴

| Reason | ErrorCode | 설명 |
|--------|-----------|------|
| `WithdrawalDeleted` | `withdrawal_deleted_manual_login_required` | 계정이 탈퇴 처리되어 재로그인이 필요합니다 |
| `WithdrawalGateBlocked` | `withdrawal_scheduled_gate_blocked` | 탈퇴 예약 게이트에 의해 로그인이 차단되었습니다 |
| `WithdrawalCancelTokenEmpty` | `withdrawal_cancel_token_empty` | 탈퇴 취소 토큰이 비어있습니다 |
| `WithdrawalCancelJwtVerifyMustBeOff` | `withdrawal_cancel_redeem_verify_jwt_must_be_off` | 재사용 검증을 위해 Supabase JWT 검증 설정이 꺼져 있어야 합니다 |
| `WithdrawalCancelIssueFailed` | `withdrawal_scheduled_cancel_token_issue_failed` | 탈퇴 취소 토큰 발급에 실패했습니다 |
| `WithdrawalDeletedRecreateFailed` | `withdrawal_deleted_recreate_failed` | 탈퇴 계정 재가입에 실패했습니다 |

## 유저 데이터

| Reason | ErrorCode | 설명 |
|--------|-----------|------|
| `SelectColumnsEmpty` | `select_columns_empty` | SELECT 컬럼이 지정되지 않았습니다 |
| `UserSaveFlushFailed` | `user_save_flush_failed` | 유저 세이브 저장에 실패했습니다 |
| `UserSaveLoadFailed` | `user_save_load_failed` | 유저 세이브 로드에 실패했습니다 |
| `UserSaveDeleteFailed` | `user_save_delete_failed` | 유저 세이브 삭제에 실패했습니다 |

## 우편함

| Reason | ErrorCode | 설명 |
|--------|-----------|------|
| `MailItemHandlerInvalid` | `mail_item_handler_invalid` | 우편 아이템 핸들러가 null이거나 `ItemKey`가 비어 있습니다 |

## IAP

| Reason | ErrorCode | 설명 |
|--------|-----------|------|
| `IapProductIdsEmpty` | `iap_product_ids_empty` | IAP 초기화에 전달된 상품 ID 목록이 비어 있습니다 |
| `IapDisposed` | `iap_disposed` | 이미 Dispose된 IAP 파사드입니다 |
| `IapServicesInitFailed` | `iap_services_init_failed` | Unity Services 초기화에 실패했습니다 |
| `IapInitTimeout` | `iap_init_timeout` | IAP 초기화가 제한 시간 내에 완료되지 않았습니다 |
| `IapInitFailed` | `iap_init_failed` | IAP 초기화에 실패했습니다 |
