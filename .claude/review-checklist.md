## 반드시 확인하는 것

**SQL(`install.sql`)·권한(grant)을 건드렸다면:**
- 함수·테이블 권한을 회수(revoke)할 때 `from public`만 쓰지 않았는가 — Supabase 기본 권한이 `anon`·`authenticated`에 **직접** 부여하는 EXECUTE/ALL은 `public` 회수로는 안 빠진다. 반드시 `from public, anon, authenticated` 세 개 다. (실제로 이 실수로 SECURITY DEFINER 관리 함수 여러 개가 anon에 뚫려 있었음 — 에러 없이 조용히 뚫림.)
- 새 테이블·함수를 추가했으면 **grant를 명시적으로 넣었는가.** 안 넣으면 클라이언트 접근 시 PostgREST 권한 오류(이건 눈에 보임) — 하지만 반대로 **grant를 과하게 주는 실수**는 에러 없이 조용히 새어나간다.
- `install.sql` 파일만 고치고 **라이브 프로젝트(ProjectR/DefenceR·DevilSlayer)에 MCP로 적용하는 걸 빠뜨리지 않았는가.** 파일과 라이브가 갈라지면 다음 신규 프로젝트 온보딩 때까지 아무도 모른다(`mails.localized`·`ts_coupon_redeem`에서 실제로 두 번 발생).
- `install.sql` 절 순서를 건드렸다면 **20절(권한 최소화)이 여전히 맨 마지막인가** — 앞 절 함수를 이름으로 grant하므로 순서가 바뀌면 "함수 없음" 오류로 설치 자체가 깨진다.

**IAP(결제) 코드를 건드렸다면:**
- 지급 확인(`ConfirmPurchase`/grant 경로)이 **계정 전환·Dispose 같은 비동기 경합 중에도 컨트롤러 참조를 잃지 않는가.** 예전에 계정전환 자동 Dispose 중 참조를 놓쳐 `ConfirmPurchase`가 아무 오류 없이 조용히 스킵된 적이 있다 — 결제는 성공했는데 지급 확인만 안 되는 상태.
- `already_granted`/`alreadyGranted` 플래그가 **실제 지급 여부를 반영하는가** — v4/v5 두 엔진 모두 따로 확인(과거 v4 통합 경로에서 이 값이 항상 `false`로 고정돼 있던 적이 있음).

**Retool 백엔드(pg 래퍼) SQL을 건드렸다면:**
- `$N` placeholder 번호가 **SQL에 등장하는 순서와 일치하는가.** 이 프로젝트의 `getDb(target).query(sql, valuesArray)`는 `$N` 번호가 아니라 **등장 순서대로** 값을 바인딩한다 — 번호가 등장 순서와 어긋나면(예: `$2`가 `$1`보다 먼저 나오는 SQL) 값이 뒤바뀐 채로 들어가고, 각 값 자체는 유효해서 타입 오류로만 드러나거나 최악의 경우 조용히 잘못된 값이 저장된다.

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
