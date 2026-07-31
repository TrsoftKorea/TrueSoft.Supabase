---
name: project_sdk_audit_progress
description: "SDK 정기 점검 회차별 진행 상태 — 끝난 축, 남은 축, 사용자 결정 대기 항목. 세션이 끊겨도 여기서 이어간다."
metadata: 
  node_type: memory
  type: project
  originSessionId: 2d84c5d4-80fb-4e7d-be34-79931c54df32
  modified: 2026-07-31T04:25:05.080Z
---

SDK 전체를 축 단위로 점검·개선하는 작업이 2026-07-30 시작됐다. 축 목록과 자동화 여부는 `Tools~/SdkAudit/README.md`의 "정기 점검 체크리스트"에 있다. **이 파일은 회차 진행 상태만 기록한다.**

## 1회차 (2026-07-30~)

끝난 축:

- **검사기 구축** — `Tools~/SdkAudit` 신규. R1 공개표면 · R2 리셋대칭 · R3 문서커버리지 · R4 시그니처 · R5 문서형식 · R6 샘플 · R7 미참조 · R8 문서값 · R9 SQL 설치순서
- **공개 표면** — 공개 타입 38 → 19. 파사드 7종·인프라 9종을 `internal`로. `SupabaseBridge` 신설(어셈블리 밖 배선 전용)
- **규칙 모순·모호** — CLAUDE.md 코드 불일치 8건 수정. `RemoteConfig<T>` 적용 범위 확정
- **미사용 코드** — 전수 조사 후 3건 제거. `SetCurrentServerCode`는 기능 누락으로 판명돼 `Supabase.SetServerCode`/`ServerCode`로 노출

- **규칙 위반 — 로그 레벨 분류** — `IsExpectedFailureReason` 12 → 28개. 유저 취소·첫 실행·닉네임 거절 등이 빨간 로그로 나가던 것을 고침. 하드코딩된 `errorOnFail: false` 2곳 제거
- **문서 서술 정확성** — 수치 주장(60초·64자·100건·8자·300초·30분) 전수 대조, 전부 일치
- **문서 구조·톤** — R10 신설(헤딩→시그니처 인접, 파라미터 표 2열)로 자동화
- **중복 코드** — Core 서비스 7개의 `ExtractErrorCode`·`CreateAuthHeaders` 중복을 `SupabaseRestHelpers` 한 벌로
- **비효율 코드** — 매 프레임 Task·List 할당 제거(`HasDuePoll`·`HasDueSubscription` 게이트 + 버퍼 재사용)

- **DB 정합성** — ProjectR 라이브와 `install.sql` 대조. 테이블 29개·함수 94개 일치, 컬럼 이름 전수 확인. 드리프트 1건(`user_data_logs.source` + 트리거의 `app.log_source` 기록)을 `install.sql`에 반영

**1회차 완료 (2026-07-30).** 미결 없음. 판단해 그대로 두기로 한 항목:

- `NotAuthenticated` 로그 레벨 — 세션 만료(정상)와 키 오류(비정상)가 섞이지만 **Error 유지로 확정**(사용자 결정). SDK가 토큰을 자동 갱신하므로 그 뒤에도 거부당하면 비정상 쪽에 가깝다

**문서 톤**도 정리했다 — 용도 열거 문장 2건, 반복되던 타입·동격 병기 괄호(`내 프로필(닉네임·서버 코드 등)` 등 12곳), API 색인 표의 부연 괄호를 가운뎃점으로. 인덱스 42개·RLS 정책 22개·RLS 활성화도 라이브와 대조해 일치 확인.

**표 칸 부연 괄호는 자동 검사를 넣었다가 뺐다** — 수식 표기 `(i)`·`(r, c)`, 버전 `(4.x)`, 파라미터 목록과 구분할 수 없어 오탐이 20건 넘게 나왔다. 노이즈 있는 검사는 없느니만 못하다.

## 2회차 — 세로(기능) 점검 (2026-07-30~)

기능 하나를 `install.sql → Core → Facade → 파사드 → 문서 → 샘플 → Retool`로 관통해 본다. 절차는 `/sdk-audit` 스킬의 "세로 — 기능 점검".

- **채팅 — 완료.** 이음매·문서 주장 15개·Retool 백엔드 8개 전부 일치. 발견 2건:
  - `SetSession(NewSignIn)` 이 계정 종속 상태를 안 끊어, 로그아웃 없이 다른 계정으로 로그인하면 이전 채팅 커서·구독과 원격 설정 캐시를 물려받았다. **고침**
  - Retool 목록 백엔드가 `pageSize`·`offset` 만 문자열 보간 → 정수 보정. **반영·게시 완료**
- **쿠폰 — 완료.** 이음매·문서 주장 8개·Retool 백엔드 6개 일치. 어드민 RPC 인자 15개·10개까지 순서 일치. 발견은 위 문자열 보간 2건(`listCouponCodes`·`listCouponRedemptions`)뿐이고 채팅 건과 함께 고쳤다.

- **우편함 — 완료.** 문서 주장 4개·이음매·Retool 백엔드 15개 대조. 발견 4건:
  - 만료 우편이 목록에만 남았다. 배지·일괄수령 RPC는 `expires_at > now()`인데 목록 조회 RLS에만 그 조건이 없어, 정리 크론(하루 1회 03시)이 돌기 전까지 배지 0인데 목록에 뜨고 수령은 `mail_expired`로 실패했다. **`mails_select_own` 정책에 조건 추가 — `install.sql`·라이브 둘 다 반영**
  - `GetUnclaimedMailCountAsync(userId, ...)`의 `userId`가 4개 계층을 그대로 통과하는데 아무도 쓰지 않고 문서가 스스로 "무시됩니다"라고 적고 있었다. **제거**
  - `MailItemHandlerRegistry`가 `public`이라 파사드 `RegisterMailItemHandler`와 등록 경로가 둘로 갈렸다. 레지스트리 직행은 파사드의 null·빈 key 검증을 건너뛴다. **`internal`로 내리고 샘플도 파사드 경유로**. 호출자 없던 `Clear()`도 제거
  - Retool `getMailRecords.ts`의 `pageSize`·`offset` 문자열 보간에 정수 보정 누락 — 아래 공통 형태 미적용. **반영·게시 완료**

- **리더보드 — 완료.** 문서 주장 12개·이음매·Retool 백엔드 8개 대조. 샘플이 공개 API 10개를 전부 시연한다. 발견 3건, 전부 서버 쪽:
  - **회차 전환 크론이 없었다.** `ts_leaderboard_rotate_due()`는 주석에 "cron 전용"이라 적혀 있고 전용 인덱스까지 있는데 `cron.schedule` 이 어디에도 없었다. 라이브의 daily 리더보드가 3일째 1회차에 멈춰 있었다. **`* * * * *` 로 등록 — install.sql·라이브 둘 다.** 등록 즉시 1→2회차로 넘어가고 `next_rotation_at` 은 `now()` 기준 재계산이라 밀린 회차를 몰아서 넘기지 않는다
  - `ts_leaderboard_submit_score` 의 on-conflict 가 `server_id = excluded.server_id` 로 덮어써, 회차 도중 서버를 옮기고 한 번만 더 제출하면 그 회차 누적 점수가 통째로 새 서버 순위로 따라갔다. `leaderboard_scores.server_id` 의 스키마 주석이 정반대를 적고 있어 SQL이 자기 주석과 모순이었다. **`coalesce(기존, 신규)` 로 고정**(최초 기록 때 프로필에 서버가 없었던 경우만 뒤늦게 채움)
  - `ts_leaderboard_set_player_data`·`ts_leaderboard_delete_my_score` 가 `p_rotation_count` 를 그대로 받아 지난 회차도 고치고 지울 수 있었다. SDK는 현재 회차만 보내지만 RPC는 열려 있어, 문서의 "지난 회차 기록은 확정" 보증이 서버에서 지켜지지 않았다. **현재 회차가 아니면 `leaderboard_rotation_closed`** — 새 사유를 카탈로그에 추가(95개)

  판단해 그대로 둔 것: Retool 점수 화면은 서버 미지정 시 `scope='server'` 리더보드도 전체 순위로 보여준다(게임에는 없는 뷰지만 운영자용으로 의도된 것, 코드에 주석 있음). `Supabase.ToRow` 로 남의 순위 항목을 행으로 만들어 `SetRowAsync` 에 넘기면 남의 데이터가 본인 항목에 들어가지만, 쓰기는 본인 행에만 닿고 게임 코드 오용이라 막지 않았다.

- **IAP — 완료.** LogTag·로그 형식 규칙(`.claude/rules/iap.md`)은 파사드 6종 전부 지킨다. `purchases` 컬럼 12개 라이브 일치. 발견 3건:
  - **`already_verified` 에 소유자 확인이 없었다.** Edge Function 3개 모두 `purchase_token` UNIQUE 충돌(23505)만 보고 `ok:true, already_verified:true` 를 돌려줘, 다른 계정의 토큰을 보내도 같은 답이 왔다. 문서가 안내하는 크래시 복구 패턴(alreadyVerified면 인벤토리 재확인 후 지급)을 그대로 구현한 게임은 남의 결제로 지급한다. **기존 행의 `account_id` 를 확인해 다르면 `purchase_owned_by_other_account`(409).** ProjectR·DevilSlayer 양쪽에 배포 완료(2026-07-31, 두 프로젝트 sha256 동일)
  - **`purchase-verify-apple-legacy` 가 현재 스키마로는 동작할 수 없는 상태였다.** 어느 프로젝트에도 배포된 적이 없었는데, `purchases` 에 없는 `transaction_id` 컬럼에 넣고 `not null` 인 `package_name` 은 빠뜨려 첫 호출부터 `db_error` 500 이었다. 헤더 주석의 "transaction_id UNIQUE 필요"·"유저가 자기 account_id 로 INSERT 가능"도 스키마와 반대. 스키마에 맞춰 고치고(`purchase_token`·`order_id`·`package_name`·`user_id`, `store` 는 형제 함수와 같은 `apple_app_store`), 인증 키를 `SUPABASE_ANON_KEY` 에서 형제와 같은 `SUPABASE_PUBLISHABLE_KEYS` 로 맞춘 뒤 **양쪽 프로젝트에 v1 로 신규 배포**. 실제 동작에는 `APPLE_SHARED_SECRET` 시크릿이 필요하다(미설정 시 `server_config_error`)
  - `purchase_state` 가 죽은 필드였다. Edge Function 3개 어디에도 응답에 없고 DB 컬럼도 `drop column` 으로 걷어냈는데 Core DTO 3개와 복사 5곳에 남아 있었다(주석의 `0=purchased, -1=검증 실패` 도 사실 아님). **제거**
  - Retool `getPurchases.ts` 의 `pageSize`·`offset` 정수 보정 누락 — 우편함과 같은 건. **반영·게시 완료**

  판단해 그대로 둔 것: 미처리 주문은 스토어 계정에 남아 다음에 로그인한 게임 계정으로 검증·지급된다(계정 전환 시 A의 결제가 B에게). 주문을 계정에 묶으려면 로컬 저장이 필요하고 스토어 계정이 기기 단위라 구조적 한계다 — `notes.md` 에 회피법(로그아웃 전 `Dispose`)을 적는 선에서 멈췄다.

**2회차 완료 (2026-07-31).** 세로 점검 5개 기능을 다 돌았다. Retool 수정 2건도 반영·게시됐다.

**보류 중(사용자 판단)** — `purchase-verify-apple-legacy` 는 두 프로젝트에 배포됐지만 `APPLE_SHARED_SECRET` 시크릿이 없어 호출하면 `server_config_error` 를 낸다. iOS SK1 폴백(Unity IAP v4, 또는 v5 `forceStoreKit1`)을 실제로 쓸 때 App Store Connect 의 공유 암호를 넣어야 한다.

**Retool 목록 조회 백엔드의 공통 형태** — `pageSize`·`offset` 은 선언할 때 `Math.min(200, Math.max(1, Math.trunc(Number(x)) || 기본값))` 로 보정하고, `$N` 은 등장 순서대로 개별 바인딩한다. 새 목록 화면을 만들 때 이 형태를 따른다.

**전 기능 공통 — 해결.** 함수 페이지의 에러 코드 표가 인증 실패를 `NotSignedIn` 으로만 적어 `NotAuthenticated` 가 어디에도 없었다. 20여 페이지에 한 줄씩 넣는 대신 `fail-reasons.md` 의 **공통 · 세션** 절에 "이 사유는 함수별 표에 다시 적지 않는다"를 명시하고, `### 세션 만료` 에 두 사유의 차이와 한 곳 처리 예시를 넣었다. 함수 페이지는 손대지 않았다.

**R5 책갈피누락 주의** — `fail-reasons.md` 처럼 H1 아래 도입부가 있는 문서는 첫 `##` 전에 문단이 2개 이상이거나 표가 오면 검사기가 막는다. 교차 참조 문장은 도입부가 아니라 해당 절 안에 넣는다.

**상태 수명 표가 이 리포지토리에서 특히 잘 듣는다** — 로그아웃 버그(1회차)와 계정 전환 버그(2회차)가 모두 그 표의 빈칸에서 나왔다. 세로 점검을 할 때 이 표를 건너뛰지 말 것.

## 규칙 — 실수는 그 자리에서 반영한다

사용자 지시(2026-07-30): 점검 중 실수하거나 실패하면 **다시 하지 않도록 그때그때 규칙으로 남긴다.** 검사로 막을 수 있으면 규칙 대신 검사를 만들고, 재발 범위에 따라 위치를 정한다 — 이 리포지토리 특유면 `/sdk-audit` 스킬의 함정 목록, 어디서든 나는 도구 실수면 전역 `~/.claude/CLAUDE.md`의 "도구 사용" 절.

## 이번 회차에서 배운 것

- **CLAUDE.md를 믿고 코드를 고치면 안 된다.** "SupabaseSDK는 MonoBehaviour singleton"이라는 잘못된 서술 때문에 샘플이 `internal` 클래스를 부르도록 바꿔 컴파일이 깨졌다. 코드로 먼저 확인할 것
- **Samples~는 import 전까지 컴파일되지 않아** Unity 컴파일 통과가 샘플 검증이 아니다. R6이 이 공백을 메운다
- 검사기가 오탐을 내면 규칙의 예외를 검사기에 반영한다. 예외 목록은 SdkAudit README의 표에 있다
