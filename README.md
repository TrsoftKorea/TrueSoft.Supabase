# TrueBase 운영 도구

Retool을 대체하는 운영 도구입니다. 화면은 로그인부터 DB까지 끝에서 끝까지 통과합니다.

이식된 화면: 아이템 카탈로그, 운영자 관리, 플레이어 관리(목록·상세·닉네임 변경·차단·차단 해제), 우편함(발송·내역·분류·예약), 리더보드(정의·필드·기록), 변경 관리(스테이징·게시·롤백).

## 구조

```
브라우저 (구글 ID 토큰만)
   ↓  POST {게임프로젝트}/functions/v1/admin-api
Edge Function admin-api
   ├ 1. 구글 JWKS 로 ID 토큰 검증 → 실패 시 401
   ├ 2. ADMIN_EMAILS 허용목록 인가 → 실패 시 403
   └ 3. 통과한 뒤에만 secret 키로 RPC 호출
   ↓
Postgres (SECURITY DEFINER RPC)
```

**운영자는 게임의 `auth.users` 에 존재하지 않습니다.** 구글이 신원만 증명하고, 운영자인지는 Edge Function 의 허용목록이 판단합니다. 그래서

- 플레이어 계정 풀과 겹치지 않습니다.
- 게임의 탈퇴·차단 흐름이 운영자 접근을 건드리지 않습니다(`withdrawal-guard` 가 계정을 지우는 일 등).
- **운영자 계정 하나로 모든 게임 프로젝트를 다룹니다.**

브라우저는 Supabase 키를 아예 갖지 않습니다. secret 키는 Edge Function 안에만 있습니다.

`admin-api` 는 **게임 프로젝트마다 각각 배포**합니다. 프런트는 선택된 프로젝트의 주소로 호출합니다.

## 설정

### 1. 구글 OAuth 클라이언트 ID

Google Cloud Console → **API 및 서비스 → 사용자 인증 정보 → OAuth 클라이언트 ID 만들기 → 웹 애플리케이션**

- **승인된 자바스크립트 원본**에 앱 주소를 넣습니다. 개발용은 `http://localhost:5273`, 배포하면 그 도메인도 추가합니다.
- 리디렉션 URI 는 필요 없습니다(ID 토큰만 받습니다).

게임이 쓰는 클라이언트 ID 를 재사용해도 되지만, 원본 목록이 섞이므로 운영 도구용으로 따로 만드는 편이 깔끔합니다.

### 2. 프런트엔드

```bash
npm install
cp .env.example .env
```

`.env` 에 클라이언트 ID 와 각 게임 프로젝트 주소를 넣습니다. 여기 들어가는 값은 전부 공개돼도 되는 것들입니다 — `VITE_` 값은 번들에 그대로 박힙니다.

```bash
npm run dev
```

### 3. Edge Function

`supabase/functions/admin-api/index.ts` 를 게임 프로젝트마다 배포하고, 시크릿 둘을 설정합니다.

```bash
supabase secrets set --project-ref <ref> GOOGLE_CLIENT_ID=<1번에서 만든 것> ADMIN_EMAILS=a@example.com,b@example.com
```

둘 중 하나라도 비면 **모든 요청이 막힙니다**(설정 누락이 개방이 되지 않도록). `SUPABASE_URL`·`SUPABASE_SECRET_KEYS` 는 플랫폼이 자동 주입합니다.

## 화면을 추가할 때

1. 필요한 DB 접근이 RPC 로 열려 있는지 확인합니다. `game_items` 처럼 service_role 에 테이블 권한이 없으면 SECURITY DEFINER RPC 를 먼저 만들어야 합니다(`install.sql` 에 함께 반영).
2. `admin-api` 의 switch 에 action 을 추가합니다.
3. `src/pages/` 에 화면을 추가하고 `callAdmin(target, 'action', params)` 로 호출합니다.

## 아직 안 된 것

- 아이템 카탈로그·운영자 관리·플레이어 관리(+ 유저 데이터 탭)·우편함 4종·리더보드·변경 관리·구매 내역·원격 설정·쿠폰·채팅 관리·대시보드까지 이식됐습니다. 계정 전체를 가로지르는 데이터 변경 로그 화면(계정별 이력은 플레이어 상세의 유저 데이터 탭에서 볼 수 있습니다)은 아직 없습니다.
- 감사 로그를 남기지 않습니다. 누가 무엇을 했는지 DB 에 기록되지 않습니다.
- 구글 ID 토큰은 1시간이면 만료되고, 만료되면 로그인 화면으로 돌아갑니다. 자동 갱신은 없습니다.
- 정적 호스팅 배포 파이프라인이 없습니다. `npm run build` 결과물은 어디에든 올릴 수 있습니다.
