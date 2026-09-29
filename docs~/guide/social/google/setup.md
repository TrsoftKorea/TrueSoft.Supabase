# Google 대시보드 설정

## 기본 설정

1. **OAuth 동의 화면** — [Google Cloud Console](https://console.cloud.google.com/apis/dashboard)에서 프로젝트를 만들고 동의 화면을 설정합니다. 앱 이름·이메일을 입력하고 사용자 유형은 **외부**를 선택합니다.
2. **OAuth 클라이언트 ID 발급** — **사용자 인증 정보 > OAuth 클라이언트 ID**에서 유형을 **웹 애플리케이션**으로 생성합니다.
   - 승인된 리디렉션 URI에 `https://<project-id>.supabase.co/auth/v1/callback`을 추가합니다.
   - 생성 후 **클라이언트 ID**와 **클라이언트 보안 비밀번호**를 복사합니다.
3. **Supabase 연결** — 대시보드 **Authentication > Providers > Google**에 위 두 값을 입력합니다.

## Android 로그인을 쓴다면 {#android}

1. 같은 메뉴에서 유형을 **Android**로 OAuth 클라이언트를 추가 생성합니다. 패키지명과 SHA-1 지문을 입력합니다.
2. 위 **웹 애플리케이션** 클라이언트 ID를 `SupabaseSettings`의 `googleWebClientId` 필드에 입력합니다.

## iOS 로그인을 쓴다면 {#ios}

1. 같은 메뉴에서 유형을 **iOS**로 OAuth 클라이언트를 추가 생성합니다. 앱의 번들 ID를 입력합니다.
2. 생성된 **iOS** 클라이언트 ID를 `SupabaseSettings`의 `googleIosClientId` 필드에 입력합니다. `….apps.googleusercontent.com`으로 끝나는 값입니다.
3. Supabase 대시보드 **Authentication > Providers > Google**의 **Client IDs** 칸에 iOS 클라이언트 ID를 쉼표로 이어 붙입니다. 웹 클라이언트 ID가 맨 앞에 와야 합니다.

```
<웹 클라이언트 ID>,<iOS 클라이언트 ID>
```

::: warning Client IDs에 빠지면 iOS 로그인이 전부 거절됩니다
iOS 로그인 창이 받아 오는 토큰은 iOS 클라이언트 ID 앞으로 발급됩니다. Supabase는 목록에 있는 클라이언트 ID의 토큰만 받습니다.
:::

::: info 로그인 창을 열 때마다 확인 창이 먼저 뜹니다
iOS가 "앱 이름이(가) 로그인을 위해 google.com을(를) 사용하려고 합니다" 창을 먼저 띄웁니다. 시스템이 띄우는 창이라 끌 수 없고, **계속**을 누르면 Google 계정 선택 화면이 나옵니다. Info.plist에 URL 스킴을 따로 등록하지 않아도 됩니다.
:::
