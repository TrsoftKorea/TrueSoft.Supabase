# Supabase 프로젝트 생성

## 1. 프로젝트 만들기

1. [supabase.com](https://supabase.com)에서 로그인 후 **New project**를 클릭합니다.
2. Organization, 프로젝트 이름, DB 비밀번호, 리전을 입력합니다.

   ::: tip
   한국 중심 서비스라면 리전은 **Northeast Asia (Tokyo)** 를 추천합니다.  
   리전은 프로젝트 생성 후 변경할 수 없으므로 처음에 신중하게 선택하세요.
   :::

3. 고급 옵션에서 아래와 같이 설정합니다.

   | 옵션 | 설정 | 이유 |
   |------|------|------|
   | **Enable Data API** | ON | SDK가 REST API로 DB와 통신하므로 필수 |
   | **Automatically expose new tables** | OFF | 새 테이블이 의도치 않게 외부에 노출되는 것을 방지 |
   | **Enable automatic RLS** | ON | 새 테이블에 RLS가 자동 적용되어 데이터 보호 |

4. **Create new project**를 클릭하고 프로비저닝이 완료될 때까지 약 1~2분 대기합니다.

## 2. 인증 설정

**Authentication > Sign In / Providers** 에서 아래 항목을 설정합니다.

| 항목 | 설정 | 이유 |
|------|------|------|
| **Allow anonymous sign-ins** | ON | 로그인 없이 바로 게임을 시작하는 익명 플레이어를 지원 |
| **Confirm email** | OFF | 이메일 인증 없이 즉시 로그인. 게임에서 이메일 로그인을 사용하지 않으면 불필요 |
| **Manual linking** | ON | 비회원(익명)으로 플레이하다가 소셜 계정으로 전환할 때 필요. 이 옵션이 OFF면 연동 API가 오류를 반환합니다 |

소셜 로그인을 사용한다면 **Authentication > Sign In / Providers** 에서 추가로 활성화합니다.  
Google OAuth 설정 방법은 [Google 대시보드 설정](/guide/social/google/setup)을 참고하세요.

## 3. 데이터베이스 SSL 설정

**Database > Settings > SSL configuration** 에서 아래 항목을 확인합니다.

| 항목 | 설정 | 이유 |
|------|------|------|
| **Enforce SSL on incoming connections** | ON | Retool 등 외부 도구에서 DB에 직접 연결할 때 필요 |
