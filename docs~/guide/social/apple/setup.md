# 대시보드 설정

Apple 로그인([신규 로그인](/guide/social/apple/signin))을 쓰기 전 준비입니다. 각 설정의 이유·자주 나는 오류는 [Apple 설정](/guide/social/apple/setup-details)을 참고하세요.

## Apple Developer

- App ID에 **Sign in with Apple**을 활성화합니다.

## 운영 콘솔 {#console}

운영 콘솔 **설정 › 서비스 연결 › 스토어 › App Store**의 **번들 ID**에 앱 Bundle ID를 넣습니다. 저장하면 Supabase 로그인 설정에 Apple 로그인이 켜지고 Bundle ID가 들어갑니다. **Supabase** 탭의 관리 열쇠가 들어 있어야 합니다.

## Android 추가 {#android}

Android에서 Apple 로그인을 쓰면 위 설정에 더해 아래를 추가합니다.

**Apple Developer**

- **Identifiers > Services IDs**에서 Services ID를 만들고 Sign in with Apple을 구성합니다.
  - **Identifier**: `번들ID.Services` 형식 (예: `com.company.mygame.Services`)
- **Web Authentication Configuration** 구성 화면에서 Primary App ID를 선택하고 등록합니다.
  - **Domains and Subdomains**: `<프로젝트-ref>.supabase.co`
  - **Return URLs**: `https://<프로젝트-ref>.supabase.co/auth/v1/callback`
- 확장자가 `.p8`인 **Sign in with Apple Key**를 발급해 둡니다.

**운영 콘솔**

- **설정 › 서비스 연결 › 로그인 › Sign in with Apple**에 Services ID·Team ID·Key ID·Private key를 넣습니다. 서버가 이 값으로 Apple이 요구하는 Secret Key를 만들어 로그인 설정에 넣고, 매주 새로 만들어 만료되지 않게 합니다.
- **설정 › 서비스 연결 › 스토어 › Google Play**의 **패키지 이름**에 Android 패키지 이름을 넣습니다. 로그인을 마친 브라우저가 게임으로 돌아오는 주소가 이 값으로 등록됩니다.

::: info 탈퇴 처리에 필요한 값
계정을 삭제할 때 Apple 연결을 끊으려면 서비스 연결값도 필요합니다. [탈퇴 시 Apple 연결 해제](/guide/social/apple/#revoke)를 참고하세요.
:::
