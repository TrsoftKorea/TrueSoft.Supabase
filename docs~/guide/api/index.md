# API

SDK가 제공하는 주요 API를 분류별로 모았습니다. 각 항목은 상세 가이드로 연결됩니다.

## 결과 다루기

게임 코드가 호출하는 API는 대부분 `Try*` 메서드이고, 결과는 `SupabaseCallResult` 하나로 통일됩니다. 필요한 만큼만 꺼내 쓰면 됩니다.

**성공만 확인** — `bool`처럼 씁니다.

```csharp
if (await Supabase.TrySignInAnonymouslyAsync())
    StartGame();
```

**실패 원인 분기** — 결과를 변수로 받아 `Reason`을 봅니다.

```csharp
var result = await Supabase.TrySignInAnonymouslyAsync();
if (!result.Success)
{
    if (result.Reason == SupabaseFailReason.NetworkError) ShowRetry();
    else ShowError(result.Reason);
}
```

`result.Reason` 값은 각 함수 가이드의 **실패 원인** 표에 나오는 값입니다.

**부가 정보** — 일부 실패는 추가 데이터를 함께 줍니다.

```csharp
if (result.Reason == SupabaseFailReason.UserBanned)
    ShowBanScreen(result.BanInfo);   // 차단일 때만 채워짐
```

::: tip 왜 이렇게 설계했나
실패(네트워크·차단·중복 등)는 게임에서 흔한 정상 결과라 예외를 던지지 않습니다. `SupabaseCallResult`는 `bool`로 암묵 변환되어 기존 `if (await Try*())` 코드와 그대로 호환되면서, 필요할 때만 `Reason`·`BanInfo`로 원인을 꺼낼 수 있습니다.
:::

::: info Try / 비-Try
게임 코드에서는 `Try*`를 사용하세요. `SupabaseResult<T>`를 직접 뜯어봐야 하는 경우에만 비-`Try` 버전을 씁니다.
:::

## 분류

<div class="tb-cards">

- [**인증**<br><small>로그인 · 연동 · 세션</small>](/guide/api/auth)
- [**계정**<br><small>닉네임 · 프로필 · 탈퇴 · 서버 이주</small>](/guide/api/account)
- [**게임 데이터**<br><small>유저 데이터 · 원격 설정</small>](/guide/api/game-data)
- [**인앱 결제**<br><small>IAP 생성 · 검증</small>](/guide/api/iap)
- [**기타**<br><small>서버 시간 · 우편함</small>](/guide/api/etc)

</div>
