# Google 대시보드 설정

## 기본 설정

1. **OAuth 동의 화면** — [Google Cloud Console](https://console.cloud.google.com/apis/dashboard)에서 프로젝트를 만들고 동의 화면을 설정합니다. 앱 이름·이메일을 입력하고 사용자 유형은 **외부**를 선택합니다.
2. **OAuth 클라이언트 ID 발급** — **사용자 인증 정보 > OAuth 클라이언트 ID**에서 유형을 **웹 애플리케이션**으로 생성합니다.
   - 승인된 리디렉션 URI에 `https://<project-id>.supabase.co/auth/v1/callback`을 추가합니다.
   - 생성 후 **클라이언트 ID**와 **클라이언트 보안 비밀번호**를 복사합니다.
3. **운영 콘솔에 입력** — 운영 콘솔 **설정 › 서비스 연결 › 로그인 › Google 로그인**에 위 두 값을 넣습니다. 저장하면 Supabase 로그인 설정에 바로 들어갑니다. **Supabase** 탭의 관리 열쇠가 들어 있어야 합니다.

## Android 로그인을 쓴다면 {#android}

1. 같은 메뉴에서 유형을 **Android**로 OAuth 클라이언트를 추가 생성합니다. 패키지명과 SHA-1 지문을 입력합니다.
2. 위 **웹 애플리케이션** 클라이언트 ID를 `SupabaseSettings`의 `googleWebClientId` 필드에 입력합니다.

## iOS 로그인을 쓴다면 {#ios}

1. 같은 메뉴에서 유형을 **iOS**로 OAuth 클라이언트를 추가 생성합니다. 앱의 번들 ID를 입력합니다.
2. 생성된 **iOS** 클라이언트 ID를 `SupabaseSettings`의 `googleIosClientId` 필드에 입력합니다. `….apps.googleusercontent.com`으로 끝나는 값입니다.
3. 같은 값을 운영 콘솔 **설정 › 서비스 연결 › 로그인 › Google 로그인**의 **iOS 클라이언트 ID**에도 넣습니다.

::: warning 콘솔에 빠지면 iOS 로그인이 전부 거절됩니다
iOS 로그인 창이 받아 오는 토큰은 iOS 클라이언트 ID 앞으로 발급됩니다. Supabase는 로그인 설정에 등록된 클라이언트 ID의 토큰만 받습니다.
:::

::: info 로그인 창을 열 때마다 확인 창이 먼저 뜹니다
iOS가 "앱 이름이(가) 로그인을 위해 google.com을(를) 사용하려고 합니다" 창을 먼저 띄웁니다. 시스템이 띄우는 창이라 끌 수 없고, **계속**을 누르면 Google 계정 선택 화면이 나옵니다. Info.plist에 URL 스킴을 따로 등록하지 않아도 됩니다.
:::
