# 실제 서버 대조

CLAUDE.md 「Supabase 프로젝트 규칙」 표의 게임들이 저장소와 같은지 본다. 읽기만 하고 고치지 않는다.

```bash
node "Tools~/LiveCheck/check.mjs"                  # DB·서버 함수 전부
node "Tools~/LiveCheck/check.mjs" --only db        # DB 만
node "Tools~/LiveCheck/check.mjs" --only functions # 서버 함수만
node "Tools~/LiveCheck/selftest.mjs"               # 판정 시험(서버 안 부름)
```

이 PC 에 `npx supabase login` 이 되어 있어야 한다. 어긋남이 있으면 종료 코드 1. 게시 버튼이 `install.sql`·서버 함수가 든 게시 전에 돌린다.

## 보는 것

| 대조 | 방법 |
|------|------|
| 게임끼리 DB 구조 | 표·칸·제약·색인·정책·트리거·뷰·함수·함수 권한·표 권한·자동 작업·확장·파일 저장소·Vault 이름의 요약값(`fingerprint.sql`)을 첫 게임과 비교 |
| DB 함수 ↔ `install.sql` | 함수마다 본문을 주석·공백을 빼고 비교. 서버에만 있거나 파일에만 있는 함수도 |
| 설치 검증 | `verify.sql` 을 돌려 전부 `applied` 인지(클라이언트 권한 포함) |
| 자동 작업 ↔ `install.sql` | `cron.schedule` 이름 |
| 서버 함수 | 저장소의 함수가 모두 배포됐는지, 게임끼리 코드 요약값·로그인 검사 설정이 같은지, 첫 게임의 배포 코드가 저장소와 같은지(내려받아 비교) |

배포 코드를 내려받는 데 함수마다 몇 초 걸려, 저장소 코드와 서버 요약값이 지난번 확인과 같으면 건너뛴다(`.cache.json`, 깃 제외).

## 대조하지 않는 것

- **게임마다 다른 것이 정상인 것**: `user_data`·`leaderboard_scores` 에 운영자가 더한 칸과 그 색인·제약(SDK 칸은 `create table` 의 「게임 데이터 컬럼」 표시 위), 콘솔 "서비스 연결" 값(Vault `ts_cfg:*`), 비밀값.
- 행 데이터, 비밀값의 내용, 로그인(Auth) 설정.
- 표·정책 등은 `install.sql` 문장과 직접 맞추지 않는다 — 게임끼리 같은지와 `verify.sql` 로 본다. 두 게임이 똑같이 파일과 어긋나면 함수·자동 작업·`verify.sql` 이 보는 범위 밖에서는 못 잡는다.
