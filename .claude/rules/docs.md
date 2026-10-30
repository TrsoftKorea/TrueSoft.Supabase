---
paths:
  - "docs~/**/*.md"
  - "docs~/.vitepress/config.ts"
  - "package.json"
---

## Documentation Rules

All user-facing docs live in `docs~/guide/`. Apply these rules on every code change — do not wait to be asked.

**어느 규칙이 자동 검사되는지 알고 쓴다.** 검사되는 규칙은 어겨도 `SdkAudit`이 잡지만, 수동 규칙은 지키지 않으면 아무 일도 일어나지 않는다.

| 규칙 | 검사 |
|------|------|
| 2 죽은 링크 | `npx vitepress build` |
| 3 콜아웃 문법 | R5 |
| 7 헤딩 괄호 | R5 (헤딩만. 표 칸·산문은 수동) |
| 8 시그니처 형식·기본값 | R4·R8 |
| 9 파라미터 표·에러 코드 표기 | R8·R10 |
| 10 H1 아래 H2 | R5 |
| 11 코드 우선·페이지당 시그니처 1개 | R10·R5·R15 |
| 0 페이지 꼴 | 함수 페이지의 예제 위치·유무만 R22. 나머지 순서는 수동 |
| 13 페이지 꼴·표 꼴·용어 | R22 (아래 표) |
| 1·4·5·6·12·14 | **수동** |

수동 규칙은 정기 점검의 문서 축에서 사람이 훑는다.

### 0. 페이지는 네 가지 꼴 중 하나

어느 페이지를 열어도 같은 자리에 같은 것이 있어야 한다(2026-10-30 사용자 요청 "가이드 문서도 규칙집을 만들어서 통일성을 주고 싶어"). 새 페이지는 아래 넷 중 하나로 만든다.

| 꼴 | 순서 |
|---|---|
| 함수 페이지 | `#` 제목 → 시그니처 → 설명 1~3문장 → 호출 예제 → **파라미터** → **반환** → **에러 코드** → `:::` 상자(맨 끝) |
| 기능 개요(`index.md`) | `#` 제목 → 한 문장 소개 → 개념 절(`##`, 앵커) → 맨 끝 `## 메서드` 표 `\| 메서드 \| 설명 \|`(첫 칸은 메서드 이름 링크) |
| 모음 페이지(`index.md`) | `#` 제목 → 한 문장 소개 → `tb-cards` 카드 목록 |
| 안내 페이지 | `#` 제목 → 한 문장 소개 → 절들. 순서가 있는 절차는 `## 1. 제목` 꼴 |

### 13. 페이지 꼴·표 꼴·용어 — R22 가 검사

| 검사 | 막는 것 | 대신 |
|---|---|---|
| R22 제목 | 페이지 `#` 제목에 사이드바 이름이 안 들어 있음 | 사이드바 이름을 그대로 쓰거나 앞에 기능 이름만 붙인다(`Google 신규 로그인`, `인증 API`) |
| R22 예제순서 | 함수 페이지의 호출 예제가 파라미터·반환·에러 표 뒤에 옴 | 설명 바로 아래 예제 |
| R22 예제없음 | 함수 페이지에 호출 예제가 없음 | 최소 한 줄이라도 부르는 코드 |
| R22 에러표 | 에러 표 첫 칸이 `SupabaseReason.이름` 이 아님 · 설명이 "~니다"·"~세요" 문장이 아님 | `\| \`SupabaseReason.MailExpired\` \| 만료된 우편입니다 \|`. 실패 사유 카탈로그(`api/fail-reasons.md`)의 첫 칸만 예외 |
| R22 프로퍼티표 | 이름 칸에 점(`` `.Count` ``) · 타입 칸에 코드 표시 없음 | `` \| `Count` \| `int` \| 지급 수량 \| `` |
| R22 반환 | **반환** 다음 첫 줄 꼴이 다름 | 결과에서 꺼내 읽는 값이면 "`` `.Data`는 `MailSettings`입니다. ``"(`` `.이름`은/는 ``), 돌려받는 객체 자체를 설명하면 "`` `SupabaseLoadResult`를 돌려줍니다. ``"(`` `타입`을/를 돌려줍니다 ``). 검사는 둘 중 하나인지만 본다 |
| R22 표줄표 | 표 칸 안 줄표(`—`) | 마침표로 끊고 새 문장 |
| R22 링크 | 다른 페이지 링크가 `./`·`../`·파일 이름으로 시작 | `/guide/…` 로 시작. 같은 페이지 앵커(`#x`)·외부 주소·그림은 그대로 |
| R22 용어 | `Retool`·`어드민` · `유저`(`유저 데이터`·`유저 ID` 빼고) · `메일`(`이메일` 빼고) | 아래 14 용어표 |
| R22 라벨쌍점 | 줄 머리 굵은 라벨 뒤 쌍점 `**사전 준비:**` | `**사전 준비**` 또는 `###` 제목 |
| R22 설명길이(경고) | 함수 설명이 4문장 이상 | 나머지는 아래 본문이나 `:::` 상자로 |

제목 없는 `:::` 상자는 `config.ts` 의 `markdown.container` 이름(팁·참고·주의·위험)으로 뜬다 — 제목을 꼭 달 필요는 없다.

### 14. 용어와 글

| 쓴다 | 안 쓴다 | 비고 |
|---|---|---|
| 플레이어 | 유저·사용자 | 기능 이름 `유저 데이터`, 고객센터의 `유저 ID` 는 그대로. 외부 서비스 화면 글자("사용자 인증 정보")도 그대로 |
| 우편 | 메일 | |
| 운영 콘솔 | Retool·어드민·관리 도구 | |
| 에러 코드(표 이름) · 실패 사유(설명 글) | | |

- 어투는 "~합니다". 파라미터·프로퍼티 설명은 명사로 끝맺고("수령할 우편 ID"), 에러 설명은 문장으로 쓴다("만료된 우편입니다").
- 다른 페이지로 보낼 때는 "[무엇](/guide/…)을 참고하세요."로 끝맺는다. "~ 참고." 토막으로 끝내지 않는다.
- 본문 문장 속 줄표(`—`)는 허용한다. 표 칸 안·하위 유형 구분(Rule 7)에서만 쓰지 않는다.

### 1. Update docs alongside code

프로젝트 `CLAUDE.md`의 「문서」 절에 있다 — 코드를 고칠 때 보여야 하는데 이 파일은 `docs~`를 열 때만 실려서 옮겼다.

### 2. Dead link prevention

Whenever a doc file or section is removed or renamed:
1. Search all `docs~/guide/*.md` for links pointing to the old file/anchor.
2. Remove or update every match before finishing the task.

Korean heading anchors are unreliable in VitePress. Any heading that is **linked to from elsewhere** must have an explicit anchor ID:
```md
## 더 알아보기 {#more}   ← link target
[더 알아보기](#more)      ← link
```
Do NOT rely on auto-generated Korean slugs like `#더-알아보기`.

### 3. Callout box style — VitePress `:::` only

**Never use** GitHub-style alerts (`> [!NOTE]`, `> [!TIP]`, `> [!WARNING]`, `> [!IMPORTANT]`, `> [!CAUTION]`). VitePress does not render them correctly.

Always use VitePress custom containers:

| 용도 | 컨테이너 |
|------|---------|
| 팁 / 추천 사항 | `::: tip` |
| 중립적 참고 정보 | `::: info` |
| 주의 / 경고 / 중요 | `::: warning` |
| 위험 / 데이터 손실 가능성 | `::: danger` |
| 접을 수 있는 부가 설명 | `::: details 제목` |

```md
::: warning
`SupabaseSettings.asset`은 반드시 `Assets/Resources/` 하위에 있어야 합니다.
:::

::: tip iOS 배포 대상 자동 설정
SDK가 빌드 시 자동으로 15.0으로 설정합니다.
:::
```

### 4. What goes in a callout box

Use callout boxes for **supplementary content** — content the reader can skip on first read but needs for edge cases:
- 주의사항 (warnings, "반드시 ~하세요") → `::: warning`
- 팁 / 자동 처리 안내 → `::: tip`
- 참고 / 동작 방식 보충 → `::: info`
- 긴 선택적 내용 → `::: details`

Core usage (the happy path) must remain as **plain prose + code blocks**, not buried in callout boxes.

### 5. Link directly to the target section

When referencing a specific section in another doc, link directly to the section anchor — never link to the page and name the section in surrounding text.

```md
❌ [시작하기](/guide/start/)의 **데이터베이스 설정** 절차를 먼저 완료하세요.
✅ [데이터베이스 설정](/guide/start/database-setup) 절차를 먼저 완료하세요.

❌ [데이터베이스 설정](/guide/start/database-setup)의 Edge Function 배포가 완료되어 있어야 합니다.
✅ [Edge Function 배포](/guide/start/database-setup#edge-function-deploy)가 완료되어 있어야 합니다.
```

If the target heading contains Korean, add an explicit anchor ID to the heading first (see Rule 2).

### 6. Sample display names — English only

`package.json`의 `samples[].displayName`은 영어만 사용한다. 한글 단독 또는 한영 혼용 이름은 금지. 예: `"PlayNANOO Migration"` (O), `"PlayNANOO 이관"` (X).

### 7. No parenthetical asides in headings, tables, steps, or prose

Do **not** append parenthetical clarifications to headings, table cells, or numbered steps.

- ❌ `# 인증 (Auth)`, `### 클래스 생성기 (선택)`, `| (자동 생성됨) |`
- ✅ `# 인증`, `### 클래스 생성기`, or move the aside to prose/callout

If the information matters, state it as a separate sentence or callout box. If it doesn't, omit it.

**산문 본문에서도 쓸데없는 부연 괄호를 쓰지 않는다.** 값·타입·이유를 괄호로 덧붙이지 말고, 인라인으로 풀거나 중요하면 별도 문장으로 쓴다.

- ❌ `삭제 예정 시각(DateTimeOffset)`, `탈퇴가 완료(계정 삭제)되면`, `로그인 없이 호출됩니다(publishable 키)`, `삭제 예정 시각(WithdrawnAt)`
- ✅ `삭제 예정 시각`, `탈퇴가 완료되어 계정이 삭제되면`, `로그인 없이 호출됩니다`, `삭제 예정 시각 WithdrawnAt`

예외: 코드 식·마크다운 링크(`(/guide/withdrawal/cancel)`)·파라미터 표의 `(기본값: x)`처럼 **문법·표준 표기**는 아사이드가 아니므로 허용한다(Rule 8·9).

하위 유형을 구분할 땐 **괄호도 em-dash(`—`)도 쓰지 않고 가운뎃점(`·`)**을 쓴다. 헤딩·사이드바 항목·표 라벨 모두 동일.

- ❌ `### 탈퇴 취소 — 토큰 방식`, `### 신규 로그인 — Android`, 사이드바 `탈퇴 취소 (토큰)`
- ✅ `### 탈퇴 취소 · 토큰 방식`, `### 신규 로그인 · Android`, 사이드바 `탈퇴 취소 · 토큰`

**헤딩은 짧은 한글 서술형으로 유지한다.** 긴 메서드명·한영 혼용을 헤딩에 넣으면 우측 책갈피(outline)에서 이름이 잘리고 가독성이 떨어진다.

- 메서드명을 헤딩에 직접 쓰지 않는다(Rule 9). 메서드명은 코드 시그니처에서 보여준다.
- 불필요한 영어 병기를 빼고 한글 서술형으로 쓴다. 짧은 플랫폼·고유명사 토큰(`Android`·`iOS`·`Google`·`Apple` 등)은 허용하되, 한글 설명과 영어 식별자를 한 헤딩에 뒤섞지 않는다.
- ❌ `### iOS · 커스텀 — TrySignInWithGoogleIdTokenAsync`
- ✅ `### iOS 로그인 · 커스텀`

### 8. Show all parameters in code examples

함수 페이지는 **항상 시그니처로 시작**한다(Rule 11, 코드 우선). 시그니처는 `반환타입 Supabase.메서드(...)` 형태로 쓰고 `public`/`static`/`async` 등 수식어는 뺀다. 파라미터가 여러 줄이면 정렬해 가독성을 높인다.

```csharp
Task<SupabaseResult<IAPFacade>> SupabaseIAP.CreateIAPAsync(
    string[]                       productIds,
    Func<string, bool, Task<bool>> onGrant,
    Action<IAPPurchaseFailedInfo>  onFailed  = null,
    int                            timeoutMs = 10_000)
```
`onGrant`는 `(productId, alreadyGranted)`.

파라미터 표는 타입 열 없이 `| 파라미터 | 설명 |` 2열로. 모든 파라미터를 표기하되 이름만으로 의미가 명확하면 생략 가능, optional은 `(기본값: x)` 표기.

When a signature changes, update **all** matching examples in `docs~/guide/`.

### 9. 가이드 함수 블록 형식

함수 블록의 **본문 구성**이다. 헤딩 레벨은 Rule 11이 정한다(단일 함수/메서드 페이지의 `#` 제목, 메서드명이 아니라 **서술형 기능 제목**). 해당 항목이 없으면 섹션 자체를 생략한다.

```md
# 기능명   ← 페이지 H1(서술형, Rule 11)

```csharp
반환타입 Supabase.메서드명(타입 파라미터, ...)
```

설명 1~3문장.

```csharp
호출 예제
```

**파라미터**  ← 파라미터가 없으면 섹션 전체 생략

| 파라미터 | 설명 |
|----------|------|
| `name` | 설명. optional이면 `(기본값: x)` 표기 |

**반환**  ← `SupabaseResult`(성공·실패만)이면 생략. 값을 돌려주면(`Task<string>`·상태 객체·`T` 등) **반드시 기술** — 프로퍼티가 여럿이면 표로, 단일 값이면 한 줄로

| 프로퍼티 | 타입 | 설명 |
|---------|------|------|

**에러 코드**  ← 의미 있는 에러 코드가 없으면 생략

| Reason | 설명 |
|--------|------|
| `SupabaseReason.멤버명` | 설명 |
```

| 항목 | 규칙 |
|------|------|
| 헤딩 | 페이지 H1(서술형 기능 제목). 메서드명을 그대로 쓰지 않는다. 오버로드는 별도 페이지 |
| 시그니처 | `Supabase.`(또는 `SupabaseIAP.`) 접두어 사용. 항상 포함, 수식어(`public`/`static`/`async`) 제외 |
| 파라미터 표 | 타입 열 없이 2열. `(기본값: x)` 표기 포함 |
| 반환 표 | `isSuccess` / `Success` 생략. 직접 반환 타입이나 `.Data` 프로퍼티만 기술 |
| 에러 코드 | 표에 `SupabaseReason` enum 멤버를 나열(게임은 `.Reason`으로 분기). 문자열 카탈로그 `SupabaseErrorCode`는 internal이라 게임 문서에 노출하지 않는다 |

### 10. H1 아래 본문에는 반드시 H2를 붙인다

VitePress 우측 책갈피(outline)는 `H2`(`## `)부터 표시하고 `H1`(페이지 제목)은 제외한다. 따라서 **H1 바로 아래에 헤딩 없이 실질 본문이 떠 있으면 그 내용은 책갈피에 안 잡혀 최상단 항목이 누락된다.**

H1과 첫 `## ` 사이에 **코드블록 / 표 / 2단락 이상**이 있으면, 그 상단 본문에도 `## ` 제목을 붙인다.

- ❌ `# 자동 로그인` 바로 아래 코드 예제 → 그 뒤 첫 H2만 책갈피에 뜸
- ✅ `# 자동 로그인` → `## 자동 로그인 호출`(코드 포함) → `## 로그인 후 사용 가능한 값`

예외 — **한 줄짜리 도입 문장**이나 `:::` 콜아웃만 있는 경우는 책갈피가 불필요하므로 H2를 붙이지 않는다. 페이지에 H2가 하나도 없는 단일 주제 문서(예: 단일 함수 페이지)도 그대로 둔다(책갈피 자체가 숨겨짐).

### 11. 기능 페이지 캐노니컬 구조 — 코드 우선

API 색인이 링크하는 기능 페이지들은 **어느 페이지를 열어도 같은 구조**여야 한다. 핵심: 헤딩 바로 다음에 코드 시그니처가 와서 **코드가 눈에 띄어야** 한다 — **헤딩과 코드 사이에 설명 문장을 넣지 않는다**(설명이 길면 코드가 묻힘).

1. **함수 블록 순서** (Rule 9): 헤딩 → ```csharp 시그니처(`반환타입 메서드(...)`, `public`/`static`/`async` 등 수식어 제외) → **설명 1~3문장(코드 아래)** → 호출 예제 → 파라미터 표 → 반환 → 에러 코드. 코드 앞에 도입문/설명을 두지 않는다.
2. **단일 함수 페이지**: `# 기능명` 직후 바로 시그니처. 부가 맥락(왜 쓰는지·주의사항)은 코드 아래 설명에 합치거나 `:::` 콜아웃으로 페이지 끝에 둔다.
3. **다중 함수 페이지**: 함수가 여러 개라 코드가 많아지면 **폴더로 쪼갠다** — `<기능>/index.md`(개요 + 메서드 나열 표·결정 표로 각 페이지에 링크) + **메서드마다 별도 페이지(시그니처 코드 블록 1개)**. 한 페이지에 시그니처를 2개 이상 두지 않는다 — 호출 예제 코드 블록은 시그니처가 아니라 별개다. 예: `social/google/{index,setup,signin,signin-token,link,...}`. 사이드바는 `기능`을 접이식 그룹으로 만들고 하위에 각 메서드 페이지를 둔다.
4. 한 페이지 안에서 함수마다 같은 요소(파라미터/반환/에러 코드)는 **있으면 모두, 없으면 모두** 일관되게.
5. **본문에 장식용 수평선(`---`)을 쓰지 않는다** — H1 도입문과 본문 사이, `##` 섹션들 사이 모두. `##`와 여백이 구분 역할을 한다. (예외: 이미지가 많은 단계별 절차 페이지의 단계 구분 `---`은 허용.)

### 12. 표 칸이 세로로 줄바꿈되지 않게 한다

마크다운 표는 열 너비를 지정할 수 없어 브라우저가 칸을 최대한 좁히려고 **공백마다 줄바꿈**한다. 결정 표(`| 상황 | ... |`)처럼 한 열에 짧은 헤더 + 긴 한글 문구가 들어가면 그 문구가 글자 단위로 세로로 쌓여 가독성이 떨어진다.

**한 줄로 유지해야 하는 셀의 공백은 `&nbsp;`로 바꾼다.** 그러면 그 열이 문구의 자연 너비만큼 넓어진다.

```md
❌ | 로그인된 계정에 추가 연동 | ... |
✅ | 로그인된&nbsp;계정에&nbsp;추가&nbsp;연동 | ... |
```

- 적용 대상: `상황`·`용도`처럼 **짧은 헤더 + 긴 설명 문구**가 한 셀에 들어가는 결정·분기 표의 해당 열.
- 적용 제외: 첫 열이 공백 없는 코드 식별자(`TrySignInAnonymouslyAsync` 등)인 API 색인 표는 애초에 쌓이지 않으므로 손대지 않는다.
- 마지막(가장 넓은) 설명 열은 줄바꿈돼도 되므로 `&nbsp;` 처리하지 않는다 — 좁아서 세로로 쌓이는 **앞쪽 좁은 열**에만 적용한다.
