---
name: project_sdk_audit_progress
description: "SDK 정기 점검 회차별 진행 상태 — 끝난 축, 남은 축, 사용자 결정 대기 항목. 세션이 끊겨도 여기서 이어간다."
metadata: 
  node_type: memory
  type: project
  originSessionId: 2d84c5d4-80fb-4e7d-be34-79931c54df32
  modified: 2026-08-05T03:29:12.799Z
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

**정의만 있고 실행되지 않는 것은 코드로 안 보인다** — 리더보드 회차 전환 크론과 `purchase-verify-apple-legacy` 가 그랬다. 호출자만 없을 뿐 정의는 멀쩡해 문법·타입·검사기 어디에도 안 걸렸다. 스킬의 세로 점검에 "정의만 있고 실행되지 않는 것을 찾는다" 절을 넣었고, `install.sql` 안쪽(주석은 cron 인데 `cron.schedule` 없음)은 **R13 으로 자동화**했다. 배포 여부·시크릿은 라이브 조회가 필요해 절차로 남겼다.

**상태 수명 표가 이 리포지토리에서 특히 잘 듣는다** — 로그아웃 버그(1회차)와 계정 전환 버그(2회차)가 모두 그 표의 빈칸에서 나왔다. 세로 점검을 할 때 이 표를 건너뛰지 말 것.

## 3회차 — 가로(축) 점검 (2026-07-31~)

2회차가 코드를 크게 흔든 뒤 무엇이 남았는지를 본다. 자동화 축은 기준선에서 전부 초록이라 수동 축만 돈다.

- **미사용 코드 — 완료.** 타입 195개·메서드 641개·프로퍼티 188개 전수 조사. 후보 25건 중 24건이 오탐(`[MenuItem]`·`[RuntimeInitializeOnLoadMethod]`·`[PostProcessBuild]`·`IPostGenerateGradleAndroidProject`·`IStoreListener` 구현·`UnitySendMessage` 네이티브 콜백·`ICollection<T>` 요구 멤버). 제거 1건:
  - `StaticUserSave<TRow>.SharedInstance` + `_sharedInstance` 필드·대입. 주석은 "PlayNanooRuntime 등 외부에서 참조할 때"라고 했지만 그 경로는 생성자의 `SupabaseSDK._nanooSaveBridge = this` 등록으로 대체돼 있었다. 필드는 대입만 되고 읽히지 않았다

  판단해 그대로 둔 것: `AutoList2D` 행의 `CopyTo`·`FindLast`·`GetRange`·`TrueForAll` 은 `List<T>` API 대칭을 위한 것이라 구멍을 내지 않는다. `SupabaseSession.ExpiresIn` 은 형제 래퍼 6개 중 하나뿐이라 혼자 빼면 `expires_in` 필드만 프로퍼티가 없어진다 — 게임에 노출되는 타입도 아니다.

  **전수 조사 셸 주의** — 문자 클래스 안에 `\[` `\]` 를 넣으면 POSIX ERE 에서 클래스가 일찍 닫혀 매칭이 0이 된다. 조용히 "발견 없음"으로 보이므로 **후보가 0이면 먼저 정규식을 의심한다.** 반환 타입 자리는 `[^;={}()]+` 처럼 제외 문자로 쓰는 편이 안전하다.

- **중복 코드 — 완료.** 10줄 창 해시로 전수 스캔(스크립트는 세션 스크래치패드). 65줄 순감, 3건 통합:
  - **RPC 호출 보일러플레이트** — Chat·Coupon·Leaderboard 가 같은 `CallRpcAsync` 를 따로 갖고 빈 본문 처리만 달랐다(Coupon=성공, Chat=실패, Leaderboard=파라미터). `SupabaseRestHelpers.CallRpcAsync(..., allowEmptyBody)` 한 벌로. **통합 후 세 서비스의 `ExtractErrorCode`·`CreateAuthHeaders` 포워더가 죽어서 함께 제거** — 중복을 걷으면 죽은 코드가 새로 생기므로 되짚어 확인할 것
  - **탈퇴 게이트 8곳 중 6곳** — 로그인 경로마다 `if (!_isRecreating…) { 가드; 예약게이트; }` 를 손으로 적고 있었다. 게이트가 8벌이면 한 곳을 빠뜨렸을 때 그 경로만 조용히 통과한다. `RunWithdrawalGatesAfterSignInAsync` 로. 남긴 2곳은 진짜 다르다 — 익명 신규 생성은 "항상 no-op이라 생략"이 주석으로 근거가 있고, 익명 복구는 가드·게이트 실패를 서로 다른 `AnonymousRecoveryKind` 로 매핑한다
  - **네이티브 Google 로그인 준비 3곳** — `EnsureGoogleNativeReadyAsync` + `RequestNativeGoogleLoginAsync` 로 분리. **한 덩어리로 뽑지 않은 이유**: `SignInWithGoogleAsync` 만 중간에 익명 세션 가드가 끼는데, 계정 선택 창을 띄운 뒤로 밀면 유저가 고른 계정이 버려진다

  판단해 그대로 둔 것: `SupabaseBridge` ↔ `SupabaseSDK` 시그니처 중복(브리지의 존재 이유), `LeaderboardEntry`·`LeaderboardPlayerEntry` 의 공통 프로퍼티 7개(문서가 각각 표로 싣고 있어 기반 클래스를 빼면 공개 표면에 설명할 타입이 하나 는다).

- **비효율 코드 — 완료.** 발견 1건, 나머지는 이미 최적이라 손대지 않았다.
  - **로그인마다 탈퇴 상태를 두 번 물었다.** 가드(`withdrawal-guard` Edge Function)와 예약 게이트(`ts_my_withdrawal_status` RPC)가 둘 다 `user_profiles.withdrawn_at` 을 읽는데 순서가 가드 먼저였다. 예약이 없는 정상 로그인에서도 Edge Function 콜드 스타트를 매번 물었다. 상태를 먼저 읽어 `withdrawn_at` 이 null 이면 둘 다 건너뛰도록 해 **정상 로그인의 왕복을 2 → 1** 로 줄였다(로그인 5경로 + 자동 로그인 전부에 적용 — 중복 축에서 게이트를 한 곳으로 모아둔 덕).
  - **`is_scheduled` 로 가르면 안 된다.** 그 값은 `withdrawn_at > now` 라 **유예가 지난 삭제 대상은 false** 다. 그것으로 건너뛰면 삭제가 영영 실행되지 않는다. 반드시 `withdrawn_at` 의 null 여부로 가른다. 조회 실패 시에는 false 를 돌려 가드를 그대로 태운다

  손대지 않은 것(이미 최적): 매 프레임 tick 4개 중 3개는 게이트가 있고, 남은 `UserSaveStaticSyncRegistry.Tick` 은 dirty 검사가 5초 스로틀인 데다 **정상 상태에서 `NextAllowedAtRealtime` 이 0이라 타이머를 앞으로 옮겨도 이득이 없다**(마킹 없는 제자리 수정을 잡으려면 폴링이 불가피). `DataSchema` 는 리플렉션 결과가 전부 캐시. 로그인 후 프로필·서버코드 조회는 이미 `Task.WhenAll`. 기존 유저 로드는 왕복 1회(신규 유저만 3회 — 행 생성 후 재로드가 필요).

- **규칙 모순 — 완료.** `CLAUDE.md`·`.claude/rules`·스킬의 검증 가능한 주장을 뽑아 전수 대조(파사드 7종 접근자·서비스 10종·Edge Function 10개·`RemoteConfig<T>` 표면·유저세이브 API 5개·상속 관계·`docs.md` 12개 규칙 등은 전부 일치). 어긋난 것 3건:
  - **PlayNanoo 문단이 `SupabaseSDK` 를 부르라고 적고 있었다.** 실제 샘플은 `SupabaseBridge.*` 를 부른다 — `SupabaseSDK` 는 `internal` 이라 `Samples~` 에서 보이지도 않는다. **같은 파일의 호출자 표(어셈블리 밖 → `SupabaseBridge`)와 정면으로 모순**이었고, 1회차에 컴파일을 깨뜨린 서술과 같은 형태다. 같은 문단의 `Supabase.Try*`·`TryLinkGoogle/AppleToGuestWithIdTokenAsync` 도 파사드에 `Try` 접두어가 없어진 뒤로 틀린 이름이었다
  - 어셈블리 목록에 `TrueBase.Unity.IAP.asmdef` 누락(4개 중 3개만). `defineConstraints` 로 패키지가 없으면 어셈블리째 빠진다는 근거까지 적었다
  - `.claude/rules/iap.md` 의 LogTag 표가 4개만 적는데 실제 파사드는 8개 — `*V4` 짝이 빠져 한쪽만 고치는 사고가 날 자리였다

  **검사기로 옮긴 것 — R14 구조 열거.** 위 어셈블리 누락처럼 `CLAUDE.md` 가 나열하는 목록이 실제와 갈라지는 건 R3 로는 안 잡힌다(R3 는 "적힌 이름이 실존하는가"만 보고 "빠진 것"은 못 본다). 어셈블리·Edge Function·Core 서비스 3종을 파일 시스템과 대조한다.

  **자체 테스트가 파이프라인 목록을 따로 갖고 있었다.** R14 를 Program 에만 등록했더니 selftest 에서 미발동으로 잡혔다 — 규칙 추가 시 두 곳을 고쳐야 하는 구조였다. `AuditPipeline.Run(ctx)` 하나로 합쳐 실행과 테스트가 같은 목록을 쓰게 했다.

- **문서 서술·톤 — 완료.** 세로 점검이 닿지 않은 기능(user-data·auth)의 주장을 대조하고, 자동화 못 하는 톤 규칙(6·8·12)을 눈으로 확인했다. 발견 2건:
  - **`CLAUDE.md` 의 Editor 절이 없는 파일을 가리켰다** — `SupabaseUserSaveClassGeneratorWindow.cs` 는 실존하지 않고 실제 이름은 `UserSaveClassGeneratorWindow.cs`. 메뉴도 5개 중 2개만 적혀 있었고 그중 하나는 경로가 틀렸다(실제는 `클래스 생성/유저 데이터` 하위 메뉴). 5개 전부로 고침
  - `read-write.md` 가 "값을 쓰면 `MarkDirty()` 가 자동 호출"이라고만 적었는데, 생성된 스칼라 setter 는 **같은 값이면 아무 일도 하지 않는다**(`EqualityComparer` 로 조기 반환). 그 조건을 덧붙임

  **R14 에 에디터 메뉴를 추가**했다 — `[MenuItem("TrueSoft/Supabase/…")]` 경로가 `CLAUDE.md` 에 다 있는지 본다. 메뉴는 개발자가 실제로 눌러야 하는 진입점이라 목록에서 빠지면 기능이 있는지도 모른다.

  대조해서 일치한 것: 클래스 생성기 주장 4개(기본값 없으면 `⚠ 필요` + 생성 버튼 비활성 · `[JsonProperty]` 자동 부착 · 함수·jsonb 기본값 제외 · 시스템 컬럼 `SkipColumns` 제외), `null` 원소 fallback, 중복 로그인 60초, 세션 상태 프로퍼티 5종, 톤 규칙 6·12. **탈퇴 문서(유예 중 재로그인 → `WithdrawalGateBlocked`, 유예 만료 → 다음 로그인 시 삭제)가 이번 회차 게이트 최적화 뒤에도 그대로 참임을 확인**했다 — `withdrawn_at` 이 있으면 두 경로 다 정상 진입한다.

  `Supabase.ServerCode` 가 `session-state.md` 표에 없는 건 드리프트가 아니다 — 세션 파생값이 아니라 기기 로컬값이라 `server/index.md` 가 따로 설명한다.

**3회차 완료 (2026-07-31).** 가로 5개 축을 다 돌았다.

**문서 축을 "완료"로 닫은 뒤에도 2건이 더 나왔다** (2026-07-31, 사용자가 화면을 보고 지적). 단일 함수 페이지 55개 중 `auth/auto-login.md`(도입문 + 불필요한 H2)와 `auth/duplicate-login.md`(시그니처 없이 설명으로 시작)가 캐노니컬(규칙 11.2 — H1 직후 시그니처)에서 벗어나 있었다. 자동 로그인에는 에러 코드 표도 빠져 있어 함께 채웠다.

**R15 코드우선으로 자동화했다.** 판정식은 "csharp 시그니처 1개 · `##` 1개 이하인 페이지는 H1 다음 비어있지 않은 줄이 코드펜스여야 한다". `##` 2개 이상(다중 절·색인)을 제외하는 것이 오탐 방지의 핵심이다. **R5·R10 은 이 형태를 못 본다** — R5 는 "H1 아래 본문에 H2 가 없다"만 보므로, 도입문을 넣고 `## X 호출` 로 감싸면 오히려 통과한다. 그래서 축을 완료로 닫은 뒤에도 남아 있었다.

**검사기 현재 상태: R15까지, 자체 테스트 20/20.** 규칙을 추가하면 `Program.cs` 의 통과 문구·`SelfTest.ExpectedTags`·`Tools~/SdkAudit/README.md`(규칙 표·검사 대상 표·예외 표·체크리스트)·`CLAUDE.md` 검사 항목 나열·`.claude/rules/docs.md` 의 규칙↔검사 매핑까지 **여섯 곳**을 함께 갱신해야 한다. R13·R14 때 뒤 두 곳을 빠뜨려 R15에서 한꺼번에 보완했다.

## 4회차부터 — 한 번에 돈다

사용자 지시(2026-07-31): **축·기능마다 멈춰 확인받지 말고 작업 전체를 한 번에 한다.** 절차는 `/sdk-audit` 스킬의 "한 번에 도는 방식이 기본이다"에 있다 — 조사 → 합의 1회 → 적용(검증 불가한 `Runtime/Unity`·`Editor`를 맨 뒤로) → 검증 1회. 축마다 커밋을 권해 마지막 재컴파일이 실패해도 범인을 좁힌다.

**다음 대상은 인증 세로 점검.** 세로를 한 번도 안 돌았는데 3회차에서 로그인 경로를 세 번 고쳤다(탈퇴 게이트 6곳 통합 · 구글 준비 단계 분리 · 게이트 순서 최적화). 그다음은 유저 세이브 → 원격 설정 → 계정·탈퇴 → 서버 시간·서버 선택.

가로 축 전체 재점검은 다음 릴리스 즈음이면 충분하다 — 3회차 발견의 상당수가 2회차가 만든 잔해였고, 자동화 가능한 것은 R13·R14로 옮겼다.

## 규칙 — 실수는 그 자리에서 반영한다

사용자 지시(2026-07-30): 점검 중 실수하거나 실패하면 **다시 하지 않도록 그때그때 규칙으로 남긴다.** 검사로 막을 수 있으면 규칙 대신 검사를 만들고, 재발 범위에 따라 위치를 정한다 — 이 리포지토리 특유면 `/sdk-audit` 스킬의 함정 목록, 어디서든 나는 도구 실수면 전역 `~/.claude/CLAUDE.md`의 "도구 사용" 절.

## 이번 회차에서 배운 것

- **CLAUDE.md를 믿고 코드를 고치면 안 된다.** "SupabaseSDK는 MonoBehaviour singleton"이라는 잘못된 서술 때문에 샘플이 `internal` 클래스를 부르도록 바꿔 컴파일이 깨졌다. 코드로 먼저 확인할 것
- **Samples~는 import 전까지 컴파일되지 않아** Unity 컴파일 통과가 샘플 검증이 아니다. R6이 이 공백을 메운다
- 검사기가 오탐을 내면 규칙의 예외를 검사기에 반영한다. 예외 목록은 SdkAudit README의 표에 있다
