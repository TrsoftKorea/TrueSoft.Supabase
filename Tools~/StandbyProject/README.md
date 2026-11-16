# 대기 프로젝트 준비

운영 콘솔의 "새 프로젝트 만들기"는 미리 준비해 둔 대기 프로젝트를 하나 꺼내 쓴다. 이 도구는 빈 Supabase 프로젝트를 대기 프로젝트로 만든다.

## 순서

1. Supabase 대시보드에서 프로젝트를 만든다. 지역은 Northeast Asia (Tokyo). Pro 요금제에서는 대기 중에도 월 약 $10 이 붙고 일시정지가 안 된다.
2. 도구를 돌린다. 이 PC 에 `npx supabase login` 이 되어 있어야 한다.

   ```bash
   node "Tools~/StandbyProject/prepare.mjs" <ref> --name "대기 1" --admin-emails "<마스터 이메일,…>"
   ```

   - `<ref>` 는 대시보드 주소의 `/project/` 뒤 20자.
   - `--admin-emails` 는 지금 게임들의 콘솔 서버 함수에 넣은 마스터 이메일과 글자까지 같아야 한다. 다르면 아무것도 바꾸지 않고 멈춘다.
3. 바뀐 `.github/workflows/admin-deploy.yml`·`CLAUDE.md` 를 게시한다. 게시해야 콘솔이 이 프로젝트를 안다.
4. 마스터가 콘솔 **트루베이스 설정**에서 이 프로젝트의 Supabase 관리 열쇠를 넣는다. 열쇠가 있어야 익명 로그인 같은 로그인 기본 설정이 들어간다.

## 도구가 하는 일

| 단계 | 내용 |
|------|------|
| DB 설치 | `install.sql` 실행 |
| 프로젝트 주소 | Vault `project_url` 등록. 없으면 탈퇴 계정 정리 같은 자동 작업이 안 돈다 |
| 서버 함수 | `Samples~/DatabaseSetup/EdgeFunctions` 전부와 `admin-api` 배포. 로그인 검사(Verify JWT)를 켤지는 기준 게임에서 읽는다 |
| 콘솔 비밀값 | `GOOGLE_CLIENT_ID`(배포 설정의 값)·`ADMIN_EMAILS` |
| 콘솔 배포 설정 | `admin-deploy.yml` 의 `VITE_MORE_PROJECT_URLS` 에 주소 추가 |
| SQL 적용 대상 표 | `CLAUDE.md` 표에 추가. 대기 중에도 SQL 변경을 같이 받아야 꺼낼 때 낡지 않는다 |
| 점검 | `verify.sql`, 주소 등록, 서버 함수·로그인 검사 설정이 기준 게임과 같은지, 비밀값 요약값, 콘솔 서버 함수 응답 |

다시 돌려도 안전하다. 중간에 멈추면 원인을 고치고 같은 명령을 다시 돌린다.

## 점검만

```bash
node "Tools~/StandbyProject/prepare.mjs" --check <ref>
```

아무것도 바꾸지 않는다. 운영 중인 게임에 돌려도 된다 — 기준 게임과 어긋난 곳을 찾을 때 쓴다. 기준 게임은 `--reference <ref>` 로 바꾼다(기본 데빌 슬레이어).

## 꺼낸 뒤

누군가 "새 프로젝트 만들기"로 꺼내면 `CLAUDE.md` 표의 이름을 그 게임 이름으로 바꾼다.
