## 쓸 때 볼 것

리뷰 27건(2026-10-30~11-16)에서 서로 다른 리뷰 다섯 건 이상에 되풀이된 빈틈만 모았다. 괄호는 나온 리뷰 수와 사례.

1. **이 문장이 약속하는 것을 코드가 실패 경로까지 지키는가** — 문서·화면 안내·주석(17건). 없는 갱신 장치를 "자동 갱신"이라 씀(10-30 `fail-reasons.md`), `signOut` 이 모든 게임을 끊는데 "그 게임만"(11-05 `Admin~/README.md`), 관리 열쇠가 필요한데 "연결값만 넣으면 됨"(11-16 `database-setup.md`).
2. **같은 일을 하는 다른 화면·함수에 있는 처리를 여기도 넣었는가**(8건). `allowEmptyBody` 가 다른 세 곳엔 있고 푸시에만 없음(10-30), `setError('')`·요청 번호가 이웃 화면엔 있고 여기 없음(11-02 `ServerStatus.tsx`, 11-11 `MailsTab.tsx`), 웹 로그인 경로만 차단 재확인 없음(11-12 `support-api`).
3. **기다리는 사이 값·세션·화면 조건이 바뀌거나 같은 작업이 겹치면** — 늦은 응답이 이기거나 보내는 중 바뀐 값을 잃는다(8건). 필터를 바꾸면 늦게 온 응답이 이김(11-05 `Purchases.tsx`), 보내는 중이던 PATCH 가 지운 세이브를 되살림(11-04 `SaveIfChangedAsync`), 잠금 확인과 실패 기록 사이를 동시 요청이 파고듦(11-11 `hub.ts`).
4. **불러오기·확인이 실패하면 "없음·0건·성공"과 다르게 보이는가**(7건). 목록 실패를 빈 배열로 채워 "더할 사람이 없습니다"(11-11 `Operators.tsx`), 목록 실패로 중복 막기가 통과해 기존 항목을 덮어씀(11-11 `GameItems.tsx`), 태그 조회 실패를 "빠진 것 없음"으로 읽고 게시 성공 보고(11-12 `mock-server.mjs`).
5. **서버·HTTP 실패마다 게임이 받는 `Reason` 을 끝까지 따라갔고, 원문은 로그에 남는가**(8건 + 원문 버림 6건). 오프라인·토큰 만료가 `Unknown`(10-30 `UnitySupabaseHttpClient`), 429 가 `NetworkError` 라 게임이 "다시 시도"를 띄움(10-30), 새 사유를 넣으며 상수·enum·문서·로그 등급 넷을 다 안 채움(11-12 `leaderboard_score_invalid`), 상태 코드 없이 `customer_center_failed` 만 남김(10-30).
6. **새 입구(콘솔 action·RPC·엣지 함수)마다 누가 부를 수 있는지, 보낸 값을 믿는지, 반복 횟수를 세는지**(시도 제한 8건 · 값 그대로 믿음 6건 · 호출자 미정 5건). 비밀번호를 맞춰 보기 전에 세지 않아 동시 요청으로 5회 제한 돌파(11-11 `hub.ts`), 이메일 하나로만 세서 남이 운영자를 잠금(11-12, 11-16), 게임이 보낸 결제 금액을 그대로 씀(11-12 `purchase-verify-google`), `project.revealSecretKey` 가 `MASTER_ONLY_ACTIONS` 에 없음(11-16).
7. **로그아웃·새 로그인·게임 전환·목록 복귀 모든 경로에서 이 기능이 든 상태를 비우는가**(5건). 알림 대기 줄을 `ClearSession` 에서 안 비움(10-30), `NewSignIn` 에서 로컬 세이브를 안 비워 앞 계정 변경분이 새 계정에 저장(11-04), 게임을 바꿔도 주소의 `account`·`page` 가 남음(11-05 `Layout.tsx`).
8. **검사를 만들거나 넓혔으면: 대상이 없거나 이름이 바뀌면 오류로 끝나는가, 자동 실행 감시 목록도 넓혔는가, 꼭 걸릴 대조군으로 돌려 봤는가**(9건). `fail-reasons.md` 가 없으면 R8 이 조용히 꺼짐(10-30), R16 을 넓히고 `ChangeGate.cs` 감시 목록은 그대로(11-03), 줄 바꾼 JSX 글자를 0건으로 놓침(11-05 `check-ui`), 코드 비교 없이 "기준과 같음" 통과(11-16 `prepare.mjs`).

## 반드시 확인하는 것

**SQL(`install.sql`)·권한(grant)을 건드렸다면:**
- 함수·테이블 권한을 회수(revoke)할 때 `from public`만 쓰지 않았는가 — Supabase 기본 권한이 `anon`·`authenticated`에 **직접** 부여하는 EXECUTE/ALL은 `public` 회수로는 안 빠진다. 반드시 `from public, anon, authenticated` 세 개 다. (실제로 이 실수로 SECURITY DEFINER 관리 함수 여러 개가 anon에 뚫려 있었음 — 에러 없이 조용히 뚫림.)
- 새 테이블·함수를 추가했으면 **grant를 명시적으로 넣었는가.** 안 넣으면 클라이언트 접근 시 PostgREST 권한 오류(이건 눈에 보임) — 하지만 반대로 **grant를 과하게 주는 실수**는 에러 없이 조용히 새어나간다.
- `install.sql` 파일만 고치고 **라이브 프로젝트(ProjectR/DefenceR·DevilSlayer)에 MCP로 적용하는 걸 빠뜨리지 않았는가.** 파일과 라이브가 갈라지면 다음 신규 프로젝트 온보딩 때까지 아무도 모른다(`mails.localized`·`ts_coupon_redeem`에서 실제로 두 번 발생).
- `install.sql` 절 순서를 건드렸다면 **권한 최소화 절(지금 24절)이 여전히 맨 마지막인가** — 앞 절 함수를 이름으로 grant하므로 순서가 바뀌면 "함수 없음" 오류로 설치 자체가 깨진다.

**로그인 후처리·탈퇴·차단 확인을 건드렸다면:**
- 막아야 하는 확인(탈퇴 예약·차단·삭제 대상)이 **조회 실패를 "해당 없음"으로 읽고 통과시키지 않는가.** 예전에 상태 조회·가드 호출이 실패하면 통과시켰고, 그다음 프로필 보장이 탈퇴 예약을 지워 **이용자 모르게 탈퇴가 취소되는** 길이 있었다(2026-10-16 수정). 실패 시 로그인을 멈추는지, 서버 쪽 쓰기가 그 상태를 지우지 않는지 둘 다 본다.

**이미 있는 공개 API·RPC 의 동작 범위를 바꿨다면(지우는 대상·반환 의미 등):**
- 소비 게임(DefenceR·DevilSlayer)이 그 API 를 **언제 부르는지** 확인했는가. "수령한 우편 일괄 삭제"에 만료 우편을 끼워 넣었더니, DefenceR 이 우편함을 열 때마다 그걸 불러 보관 기간 동안 보여야 할 만료 우편이 열자마자 지워졌다(2026-10-19). 이름이 약속한 범위를 넓히는 변경은 호출 위치를 보기 전엔 하지 않는다.

**IAP(결제) 코드를 건드렸다면:**
- 지급 확인(`ConfirmPurchase`/grant 경로)이 **계정 전환·Dispose 같은 비동기 경합 중에도 컨트롤러 참조를 잃지 않는가.** 예전에 계정전환 자동 Dispose 중 참조를 놓쳐 `ConfirmPurchase`가 아무 오류 없이 조용히 스킵된 적이 있다 — 결제는 성공했는데 지급 확인만 안 되는 상태.
- `already_granted`/`alreadyGranted` 플래그가 **실제 지급 여부를 반영하는가** (지금은 없앤 v4 통합 경로에서 한때 이 값이 항상 `false`로 고정돼 있던 적이 있음).

**`SupabaseResult`/`Try` 반환 패턴을 건드렸다면:**
- 실패 사유(`Reason`)가 있는데 `IsSuccess = true`로 반환하지 않는가 — 호출자가 실패를 놓친다.
- bare value(`Task<string>`·리스트 원본 등)를 그대로 반환하거나 공개 메서드에 `Try` 접두어를 새로 쓰지 않았는가.

## 직접 돌려보는 검사

```bash
dotnet build Tools~/CoreCompileCheck/CoreCompileCheck.csproj
```
Core(`Runtime/Core/`) 컴파일 확인 — UnityEngine 의존 없이 즉시 오류를 잡는다. 경고 0이 정상.

```bash
dotnet run --project Tools~/SdkAudit
```
공개 API·파사드 노출 규칙·문서 정합성 등 정적 검사. 파사드·공개 API·문서·`install.sql`을 건드렸으면 실행.

```bash
dotnet run --project Tools~/FailReasonCheck
```
`SupabaseErrorCode`·`SupabaseReason`·`FromErrorCode` 3자 정합성 및 죽은 사유 검증. 실패 사유를 추가·수정했으면 실행.
