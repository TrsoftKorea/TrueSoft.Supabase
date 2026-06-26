# Google 대시보드 설정

## 기본 설정

1. **OAuth 동의 화면** — [Google Cloud Console](https://console.cloud.google.com/apis/dashboard)에서 프로젝트를 만들고 동의 화면을 설정합니다. 앱 이름·이메일을 입력하고 사용자 유형은 **외부**를 선택합니다.
2. **OAuth 클라이언트 ID 발급** — **사용자 인증 정보 > OAuth 클라이언트 ID**에서 유형을 **웹 애플리케이션**으로 생성합니다.
   - 승인된 리디렉션 URI에 `https://<project-id>.supabase.co/auth/v1/callback`을 추가합니다.
   - 생성 후 **클라이언트 ID**와 **클라이언트 보안 비밀번호**를 복사합니다.
3. **Supabase 연결** — 대시보드 **Authentication > Providers > Google**에 위 두 값을 입력합니다.

## Android 네이티브 로그인을 쓴다면

1. 같은 메뉴에서 유형을 **Android**로 OAuth 클라이언트를 추가 생성합니다. 패키지명과 SHA-1 지문을 입력합니다.
2. 위 **웹 애플리케이션** 클라이언트 ID를 `SupabaseSettings`의 `googleWebClientId` 필드에 입력합니다.
