# 에러 코드

`SupabaseResult.Reason`이 가질 수 있는 모든 `SupabaseReason` 값을 분류별로 모았습니다. 각 함수 가이드의 **에러 코드** 표는 그 호출이 반환할 수 있는 사유만 추린 것이고, 이 페이지는 카탈로그 전체 레퍼런스입니다.

## 분기 방법

`Reason`은 enum이라 타입 안전하게 분기하고, 원문 문자열이 필요하면 `ErrorCode`를 씁니다.

```csharp
var result = await Supabase.SignInAnonymouslyAsync();
if (!result.IsSuccess)
{
    if (result.Reason == SupabaseReason.NetworkError) ShowRetry();
    else ShowError(result.ErrorCode);
}
```

아래 `Reason` 열은 `SupabaseReason` enum 멤버입니다. `result.Reason`과 비교해 분기하세요(예: `result.Reason == SupabaseReason.UserBanned`). 실패 원문 문자열이 필요하면 `result.ErrorCode`를 읽습니다.

::: info None과 Unknown
`None`은 성공했거나 실패 사유가 없는 경우(`ErrorCode`가 비어 있음)이고, `Unknown`은 사유는 있으나 카탈로그에 없는 동적·서버·네이티브 문자열입니다. 이때 원문은 `ErrorCode`에서 확인하세요.
:::

::: info 오류가 아닌 사유도 실패로 옵니다
사유가 있는 결과는 항상 `IsSuccess`가 false입니다. 그래야 호출자가 `Reason`을 확인할 수 있기 때문입니다. 예를 들어 저장할 변경분이 없어 전송을 건너뛴 경우도 `UserSaveNoChanges` 사유의 실패로 돌아옵니다. 실패라고 해서 모두 오류인 것은 아니므로, 이런 사유는 `Reason`으로 걸러 정상 흐름으로 처리하세요.
:::

## 공통 · 세션

| Reason | 설명 |
|--------|------|
| `NotInitialized` | SDK가 초기화되지 않았습니다 |
| `NotSignedIn` | 로그인 상태가 아닙니다 |
| `SessionRequired` | 로그인 세션이 필요합니다 |
| `AccessTokenEmpty` | 액세스 토큰이 비어있습니다 |
| `NetworkError` | 네트워크 오류 또는 타임아웃 |

## 로그인 · 세션 복원

| Reason | 설명 |
|--------|------|
| `UserBanned` | 계정이 차단되었습니다. `BanInfo`에서 상세 확인 |
| `DuplicateLogin` | 다른 기기에서 동일 계정 로그인으로 현재 세션이 무효화됨 |
| `SignedInNonAnonymous` | 비익명 계정으로 로그인 중. 먼저 `SignOutFullyAsync` 호출 |
| `AnonymousRequired` | 익명 세션이 필요한 작업입니다 |
| `AnonymousRequiresLink` | 익명 세션에서는 `Link*` 메서드를 사용해야 합니다 |
| `AnonymousSessionTokenMissing` | 익명 세션 토큰이 없습니다. 재로그인 필요 |
| `RestoreSessionFailed` | 저장된 세션 복원에 실패했습니다 |
| `AutoLoginNoToken` | 자동 로그인이 차단되었거나 저장된 토큰이 없습니다 |
| `AutoLoginFailed` | 앱 시작 자동 로그인에 실패했습니다 |
| `AfterAutoLoginFailed` | 자동 로그인 후처리 훅이 실패를 반환했습니다 |
| `InvalidSignInMethod` | 지원하지 않는 로그인 방식입니다 |

## 연동 해제

| Reason | 설명 |
|--------|------|
| `IdentityNotLinked` | 해제하려는 provider가 현재 계정에 연동되어 있지 않습니다 |
| `CannotUnlinkLastIdentity` | 마지막 남은 연동은 해제할 수 없습니다 |
| `UnlinkFailed` | 연동 해제에 실패했습니다 |

## Google 로그인

| Reason | 설명 |
|--------|------|
| `GoogleSignInCancelled` | 사용자가 Google 로그인 화면을 직접 취소했습니다 |
| `GoogleWebClientIdEmpty` | `SupabaseSettings.googleWebClientId`가 설정되지 않았습니다 |
| `GoogleSignInFailed` | Play Services 내부 오류입니다 |
| `GoogleIdTokenEmpty` | Google ID 토큰을 획득하지 못했습니다 |
| `GoogleLinkFailed` | Supabase Google identity 연동에 실패했습니다 |
| `GoogleLinkNotCleared` | Google 연동 후 익명 플래그가 해제되지 않았습니다 |
| `GoogleProviderNull` | Google 로그인 프로바이더가 null입니다 |
| `GoogleResultNull` | Google 로그인 결과가 null입니다 |

## Apple 로그인

| Reason | 설명 |
|--------|------|
| `AppleIdTokenEmpty` | 전달된 Apple ID 토큰이 비어있습니다 |
| `AppleSignInCancelled` | 사용자가 Apple 로그인 화면을 직접 취소했습니다 |
| `AppleSignInIosOnly` | 네이티브 Apple 로그인은 iOS에서만 지원됩니다 |
| `AppleSignInUnsupportedPlatform` | 현재 플랫폼에서는 Apple 로그인을 지원하지 않습니다 |
| `AppleLinkFailed` | Supabase Apple identity 연동에 실패했습니다 |
| `AppleLinkNotCleared` | Apple 연동 후 익명 플래그가 해제되지 않았습니다 |
| `PlayNanooBrowserAppleUnsupported` | PlayNANOO 연동 중에는 브라우저 기반 Apple 로그인을 쓸 수 없습니다 |

## 웹 OAuth

| Reason | 설명 |
|--------|------|
| `OAuthRefreshTokenMissing` | 웹 OAuth 리다이렉트에 refresh_token이 없습니다 |
| `OAuthRedirectSchemeEmpty` | OAuth 리다이렉트 스킴이 비어있습니다 |
| `OAuthLoginInProgress` | 이미 진행 중인 웹 OAuth 로그인이 있습니다 |

## 닉네임

| Reason | 설명 |
|--------|------|
| `NameTaken` | 이미 사용 중인 닉네임입니다 |
| `NameTooLong` | 닉네임이 허용 길이를 초과합니다 |

## 탈퇴

| Reason | 설명 |
|--------|------|
| `WithdrawalDeleted` | 계정이 탈퇴 처리되어 재로그인이 필요합니다 |
| `WithdrawalGateBlocked` | 탈퇴 예약 게이트에 의해 로그인이 차단되었습니다 |
| `WithdrawalCancelTokenEmpty` | 탈퇴 취소 토큰이 비어있습니다 |
| `WithdrawalCancelJwtVerifyMustBeOff` | 재사용 검증을 위해 Supabase JWT 검증 설정이 꺼져 있어야 합니다 |
| `WithdrawalCancelIssueFailed` | 탈퇴 취소 토큰 발급에 실패했습니다 |
| `WithdrawalDeletedRecreateFailed` | 탈퇴 계정 재가입에 실패했습니다 |

## 유저 데이터

| Reason | 설명 |
|--------|------|
| `SelectColumnsEmpty` | SELECT 컬럼이 지정되지 않았습니다 |
| `UserSaveFlushFailed` | 유저 세이브 저장에 실패했습니다 |
| `UserSaveLoadFailed` | 유저 세이브 로드에 실패했습니다 |
| `UserSaveDeleteFailed` | 유저 세이브 삭제에 실패했습니다 |
| `UserSaveNoChanges` | 변경된 값이 없어 전송을 건너뛰었습니다. 오류가 아니라 보낼 것이 없었다는 뜻입니다 |

## 우편함

| Reason | 설명 |
|--------|------|
| `MailItemHandlerInvalid` | 우편 아이템 핸들러가 null이거나 `ItemKey`가 비어 있습니다 |

## IAP

| Reason | 설명 |
|--------|------|
| `IapProductIdsEmpty` | IAP 초기화에 전달된 상품 ID 목록이 비어 있습니다 |
| `IapDisposed` | 이미 Dispose된 IAP 파사드입니다 |
| `IapServicesInitFailed` | Unity Services 초기화에 실패했습니다 |
| `IapInitTimeout` | IAP 초기화가 제한 시간 내에 완료되지 않았습니다 |
| `IapInitFailed` | IAP 초기화에 실패했습니다 |

## 리더보드

| Reason | 설명 |
|--------|------|
| `LeaderboardTableNotFound` | 해당 코드의 리더보드가 없습니다 |
| `LeaderboardEnded` | 종료·비활성 리더보드라 기록할 수 없습니다. 순위 조회는 계속 가능합니다 |
| `LeaderboardRotationNotFound` | 존재하지 않는 회차입니다 |
| `LeaderboardScoreNotFound` | 그 회차에 해당 플레이어의 기록이 없습니다 |
| `LeaderboardColumnNotAllowed` | 이 리더보드에 등록되지 않은 플레이어 데이터 컬럼입니다 |
