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

- 자주 묻는 질문 보기와 검색. 첫 화면에 분류별로 보이고, 위쪽 검색창에 낱말을 넣으면 질문과 답에서 찾습니다
- 분류를 고르고 내용과 그림 3장까지 붙여 문의하기. 분류를 고르면 그 분류의 질문이 먼저 보입니다
- 쿠폰 번호를 넣어 보상 받기. 보상은 게임 우편함으로 들어갑니다
- 내 문의 목록과 답변 보기
- 답변을 받은 문의에 덧붙이기

창의 글자는 기기 언어를 따라 13개 언어로 나오고, 맨 아래에서 언어를 바꿀 수 있습니다. 운영 콘솔 우편·푸시 문구와 같은 언어입니다. 게임 이름·색·아이콘은 운영 콘솔 **설정 › 게임 정보** 값을 따릅니다. 자주 묻는 질문과 분류는 운영 콘솔 **고객센터**에서 한국어·영어로 쓰고, 다른 언어 화면에는 영어로 보입니다. 게임을 다시 빌드하지 않아도 바로 보입니다.

## 게임 밖에서 열기 {#web}

고객센터는 브라우저로도 열립니다. 스토어 설명이나 공식 카페에 아래 주소를 걸어 두면, 게임에 들어가지 못하는 플레이어도 자주 묻는 질문을 보고 쿠폰을 쓰고 문의를 보낼 수 있습니다.

```text
https://truebase-support.pages.dev/?p=<프로젝트 ref>
```

`<프로젝트 ref>`는 게임의 Supabase 주소 `https://<프로젝트 ref>.supabase.co`의 앞부분입니다.

게임 밖에서는 로그인이 없으므로 쿠폰과 문의에 유저 ID를 함께 적습니다. 쿠폰 보상은 그 ID의 우편함으로, 문의 답변은 그 계정의 게임 안 고객센터로 갑니다. 남의 ID를 적어도 남의 우편이나 문의를 읽을 수는 없습니다. 답을 기다리는 게임 밖 문의는 계정당 하나까지이고, 게임 안에서 여는 문의와 따로 셉니다. 같은 접속 주소나 계정에서 너무 자주 시도하면 잠시 막힙니다.

유저 ID는 `Supabase.UserId`입니다. 플레이어가 옮겨 적을 수 있게 게임 설정 화면 같은 곳에 보여 주고 복사 버튼을 두세요.

```csharp
userIdText.text = Supabase.UserId;
copyButton.onClick.AddListener(() => GUIUtility.systemCopyBuffer = Supabase.UserId);
```

운영 콘솔 **고객센터**에서 이렇게 들어온 문의에는 "게임 밖 접수"가 붙습니다. 적힌 유저 ID의 주인이 직접 보냈는지는 확인되지 않은 문의입니다.

::: warning 차단된 계정의 쿠폰
차단된 계정은 게임 안·밖 어디서도 쿠폰을 쓸 수 없습니다. 문의는 보낼 수 있습니다.
:::

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
