# TrueBase 운영 도구

Retool을 대체하는 운영 도구입니다. 화면은 로그인부터 DB까지 끝에서 끝까지 통과합니다.

이식된 화면: 아이템 카탈로그, 운영자 관리, 플레이어 관리(목록·상세·닉네임 변경·차단·차단 해제, 유저 데이터 조회·수정), 우편함(발송·내역·분류·예약), 리더보드(정의·필드·기록), 변경 관리(스테이징·게시·롤백), 구매 내역, 원격 설정, 쿠폰, 채팅 관리(메시지·차단), 대시보드(플레이어·매출), 데이터 로그.

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

`supabase/functions/admin-api/index.ts` 를 게임 프로젝트마다 배포하고, 시크릿을 설정합니다.

```bash
supabase secrets set --project-ref <ref> \
  GOOGLE_CLIENT_ID=<1번에서 만든 것> \
  ADMIN_EMAILS=a@example.com,b@example.com \
  ADMIN_SESSION_SECRET=<32바이트 이상 무작위 문자열, 프로젝트마다 다르게>
```

`GOOGLE_CLIENT_ID`·`ADMIN_EMAILS` 둘 중 하나라도 비면 **모든 요청이 막힙니다**(설정 누락이 개방이 되지 않도록). `SUPABASE_URL`·`SUPABASE_SECRET_KEYS` 는 플랫폼이 자동 주입합니다.

**구글 계정이 없는 운영자를 위한 비밀번호 로그인**을 쓰려면 `ADMIN_SESSION_SECRET`도 필요합니다 — 비밀번호 로그인 세션 토큰 서명용이며, 비어 있으면 비밀번호 로그인 자체가 안 됩니다(구글 로그인은 영향 없음).

**운영자 비밀번호 설정**: 이메일 발송 없이, "운영자 관리" 화면에서 마스터가 직접 새 비밀번호를 정해서 운영자에게 알려줍니다. 최초 설정과 재설정 모두 같은 "비밀번호 설정" 버튼을 씁니다.

## 화면을 추가할 때

1. 필요한 DB 접근이 RPC 로 열려 있는지 확인합니다. `game_items` 처럼 service_role 에 테이블 권한이 없으면 SECURITY DEFINER RPC 를 먼저 만들어야 합니다(`install.sql` 에 함께 반영).
2. `admin-api` 의 switch 에 action 을 추가합니다.
3. `src/pages/` 에 화면을 추가하고 `callAdmin(target, 'action', params)` 로 호출합니다.

## 배포 (Cloudflare Pages)

브라우저는 Supabase 비밀 키를 갖지 않으므로 `npm run build` 결과물(정적 파일)을 아무 곳에나 올려도 안전하다. 여러 PC에서 접속하려면 이 결과물을 인터넷에 올려두면 된다.

1. **Cloudflare 대시보드 → Workers & Pages → Pages → GitHub 프로젝트 연결**, 이 저장소(`TrueBase.Admin`) 선택.
2. 빌드 설정:
   - 프레임워크 프리셋: **Vite**
   - 빌드 명령: `npm run build`
   - 빌드 출력 디렉터리: `dist`
3. **환경 변수**에 `.env`와 같은 값 3개를 등록(`Settings → Environment variables`):
   - `VITE_GOOGLE_CLIENT_ID`
   - `VITE_DEFENCER_URL`
   - `VITE_DEVILSLAYER_URL`
4. 배포되면 `https://<프로젝트명>.pages.dev` 주소가 생긴다. **Google Cloud Console → OAuth 클라이언트 ID → 승인된 자바스크립트 원본**에 이 주소를 추가해야 로그인이 된다(1번 "설정" 섹션 참고).
5. 이후 `master`에 push할 때마다 자동으로 새 버전이 배포된다.

SPA 라우팅(주소를 새로고침해도 화면이 유지되도록)은 `public/_redirects`가 처리한다 — Cloudflare Pages가 이 파일을 자동으로 인식한다.

## 아직 안 된 것

- 아이템 카탈로그·운영자 관리·플레이어 관리(+ 유저 데이터 탭)·우편함 4종·리더보드·변경 관리·구매 내역·원격 설정·쿠폰·채팅 관리·대시보드·데이터 로그까지 이식됐습니다. Retool 원본(`/frontend`)과 화면 단위로 직접 대조하지는 않았습니다 — 원본 저장소가 이 프로젝트에 없습니다.
- 감사 로그를 남기지 않습니다. 누가 무엇을 했는지 DB 에 기록되지 않습니다.
- 구글 ID 토큰은 1시간이면 만료되고, 만료되면 로그인 화면으로 돌아갑니다. 자동 갱신은 없습니다.
