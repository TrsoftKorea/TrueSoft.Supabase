## 반드시 확인하는 것

**SQL(`install.sql`)·권한(grant)을 건드렸다면:**
- 함수·테이블 권한을 회수(revoke)할 때 `from public`만 쓰지 않았는가 — Supabase 기본 권한이 `anon`·`authenticated`에 **직접** 부여하는 EXECUTE/ALL은 `public` 회수로는 안 빠진다. 반드시 `from public, anon, authenticated` 세 개 다. (실제로 이 실수로 SECURITY DEFINER 관리 함수 여러 개가 anon에 뚫려 있었음 — 에러 없이 조용히 뚫림.)
- 새 테이블·함수를 추가했으면 **grant를 명시적으로 넣었는가.** 안 넣으면 클라이언트 접근 시 PostgREST 권한 오류(이건 눈에 보임) — 하지만 반대로 **grant를 과하게 주는 실수**는 에러 없이 조용히 새어나간다.
- `install.sql` 파일만 고치고 **라이브 프로젝트(ProjectR/DefenceR·DevilSlayer)에 MCP로 적용하는 걸 빠뜨리지 않았는가.** 파일과 라이브가 갈라지면 다음 신규 프로젝트 온보딩 때까지 아무도 모른다(`mails.localized`·`ts_coupon_redeem`에서 실제로 두 번 발생).
- `install.sql` 절 순서를 건드렸다면 **권한 최소화 절(지금 23절)이 여전히 맨 마지막인가** — 앞 절 함수를 이름으로 grant하므로 순서가 바뀌면 "함수 없음" 오류로 설치 자체가 깨진다.

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
