# API

SDK가 제공하는 주요 API를 분류별로 모았습니다. 각 항목은 상세 가이드로 연결됩니다.

## 결과 다루기

게임 코드가 호출하는 API의 결과는 `SupabaseResult`(성공·실패만) / `SupabaseResult<T>`(값 포함) 하나로 통일됩니다. 필요한 만큼만 꺼내 쓰면 됩니다.

**성공만 확인** — `bool`처럼 씁니다.

```csharp
if (await Supabase.SignInAnonymouslyAsync())
    StartGame();
```

**에러 코드 분기** — 결과를 변수로 받아 enum 타입인 `Reason`으로 분기합니다.

```csharp
var result = await Supabase.SignInAnonymouslyAsync();
if (!result.IsSuccess)
{
    if (result.Reason == SupabaseReason.NetworkError) ShowRetry();
    else ShowError(result.ErrorCode);   // ErrorCode: 원문 문자열(동적 사유 포함)
}
```

각 함수 가이드의 **에러 코드** 표에 나오는 값은 `SupabaseReason` enum 멤버입니다. `result.Reason`으로 분기하세요. 카탈로그에 없는 동적 사유는 `Reason == SupabaseReason.Unknown`이며, 원문은 `ErrorCode` 문자열에서 확인합니다.

**부가 정보** — 일부 실패는 추가 데이터를 함께 줍니다.

```csharp
if (result.Reason == SupabaseReason.UserBanned)
    ShowBanScreen(result.BanInfo);   // 차단일 때만 채워짐
```

::: tip 왜 이렇게 설계했나
실패는 네트워크·차단·중복 등 게임에서 흔한 정상 결과라 예외를 던지지 않습니다. `SupabaseResult`는 `bool`로 암묵 변환되어 기존 `if (await ...())` 코드와 그대로 호환되면서, 필요할 때만 타입 안전 분기용 `Reason`, 원문 문자열인 `ErrorCode`, `BanInfo`로 원인을 꺼낼 수 있습니다.
:::

## 분류

<div class="tb-cards">

- [**인증**<br><small>게임에 들어오고 나가기</small>](/guide/api/auth)
- [**계정**<br><small>플레이어 이름·탈퇴·서버</small>](/guide/api/account)
- [**게임 데이터**<br><small>저장과 서버가 정하는 값</small>](/guide/api/game-data)
- [**커뮤니티**<br><small>플레이어끼리 대화하고 친구 맺기</small>](/guide/api/community)
- [**우편함**<br><small>아이템 받기</small>](/guide/api/mailbox)
- [**결제·보상**<br><small>상품을 팔고 보상 주기</small>](/guide/api/reward)
- [**매치·랭킹**<br><small>같이 할 사람 모으고 순위 남기기</small>](/guide/api/match-rank)
- [**운영**<br><small>플레이어를 돌보는 기능</small>](/guide/api/operation)
- [**에러 코드**<br><small>실패 사유 전체</small>](/guide/api/fail-reasons)

</div>
