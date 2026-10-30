# 고객센터

플레이어가 게임 안에서 문의를 보내고 운영자의 답변을 읽는 창입니다. 화면은 SDK가 띄우므로 게임은 버튼 하나만 만들면 됩니다. 운영자는 운영 콘솔 **고객센터**에서 문의를 보고 답합니다.

## 창 열기 {#open}

```csharp
public async void OnClickCustomerCenter()
{
    var result = await Supabase.OpenCustomerCenterAsync();
    if (result.Reason == SupabaseReason.CustomerCenterLoginRequired)
        ShowToast("로그인한 뒤 이용해 주세요.");

    await RefreshSupportBadgeAsync();
}
```

Android·iOS에서는 게임 위에 창이 뜨고, 플레이어가 창을 닫을 때 `await`가 끝납니다. iOS는 창을 아래로 쓸어내려도 닫힙니다. 에디터와 그 밖의 플랫폼에서는 기본 브라우저로 열립니다.

창에서 할 수 있는 일은 다음과 같습니다.

- 자주 묻는 질문 보기. 문의하기에서 분류를 고르면 그 분류의 질문이 먼저 보입니다
- 분류를 고르고 내용과 그림 3장까지 붙여 문의하기
- 내 문의 목록과 답변 보기
- 답변을 받은 문의에 덧붙이기

창의 글자는 기기 언어가 한국어면 한국어, 그 밖이면 영어로 나옵니다. 게임 이름·색·아이콘은 운영 콘솔 **설정 › 게임 정보** 값을 따릅니다. 자주 묻는 질문과 분류는 운영 콘솔 **고객센터**에서 쓰고, 게임을 다시 빌드하지 않아도 바로 보입니다.

## 새 답변 표시 {#unread}

운영자가 답하면 그 문의에 새 답변 표시가 켜지고, 플레이어가 창에서 그 문의를 열면 꺼집니다. 메뉴의 빨간 점 같은 표시는 [새 답변 수](/guide/customer-center/unread)로 만듭니다.

```csharp
async Task RefreshSupportBadgeAsync()
{
    var unread = await Supabase.GetCustomerCenterUnreadCountAsync();
    supportBadge.SetActive(unread.IsSuccess && unread.Data > 0);
}
```

## 차단된 계정 {#banned}

차단된 계정은 로그인이 막히지만 고객센터에는 문의할 수 있습니다. 로그인 결과가 `SupabaseReason.UserBanned`일 때 차단 안내와 함께 고객센터 버튼을 보여 주세요.

```csharp
var login = await Supabase.SignInWithGoogleAsync();
if (login.Reason == SupabaseReason.UserBanned)
    ShowBanDialog(login.BanInfo, onContact: () => _ = Supabase.OpenCustomerCenterAsync());
```

SDK는 아래 중 하나로 본인을 확인합니다.

| 상황 | 확인 방법 |
|------|-----------|
| 이&nbsp;기기에서&nbsp;로그인한&nbsp;적이&nbsp;있음 | 기기에 남은 로그인 기록 |
| 구글·애플&nbsp;로그인이&nbsp;방금&nbsp;차단으로&nbsp;거절됨 | 그 로그인에 쓴 구글·애플 본인 증명. 다른 기기에서도 됩니다 |

게스트 계정은 구글·애플 증명이 없어서, 앱을 지우고 다시 깔았거나 다른 기기라면 차단 상태로 문의할 수 없습니다. 로그아웃하면 기기의 로그인 기록도 지워집니다.

## 메서드

| 메서드 | 설명 |
|--------|------|
| [`OpenCustomerCenterAsync`](/guide/customer-center/open) | 고객센터 창 열기 |
| [`GetCustomerCenterUnreadCountAsync`](/guide/customer-center/unread) | 새 답변이 있는 문의 수 |

::: info 설치
DB는 `install.sql`의 고객센터 절이 만들고, 창은 엣지 함수 `support-api`를 부릅니다. [엣지 함수 배포](/guide/start/database-setup#edge-function-deploy)에서 이 함수의 **Verify JWT**를 꺼야 창이 열립니다.
:::

::: warning iOS 카메라 사용 문구
그림을 붙일 때 iOS가 "사진 찍기"도 보여 주므로, 빌드할 때 `Info.plist`에 카메라 사용 문구가 없으면 SDK가 기본 문구를 넣습니다. 게임이 이미 넣어 둔 문구는 그대로 둡니다.
:::
