// Supabase legacy service_role 키를 코드에 쓰려 하면 막는다.
// service_role 은 2026년 말 폐지 예정, 공식 대체재는 secret key(sb_secret_...) — 기능은 같다
// (RLS 우회, 서버 전용). 둘 다 지금은 동시에 동작해서 legacy 를 써도 에러가 안 난다 — 그래서
// 조용히 반복해서 쓰게 된다(2026-08-19, 실제 지적받은 사례: project_admin_tool_retool_replacement.md).
// 쓰지 않기로 정한 것이라 확인창 대신 막는다(2026-11-02 사용자 결정).
//
// **SQL의 `service_role` 권한 이름은 건드리지 않는다** — grant/revoke·has_*_privilege 는
// 정당한 기존 패턴이다. 여기서 잡는 건 백엔드 코드가 연결에 쓰는 키 자료뿐이다.
// 옛 키를 지우는 수정은 새로 쓰는 글에 이름이 없어 걸리지 않는다. 이 파일 자신만 검사 줄 때문에 뺀다.
// 같은 파일이 Admin~/.claude/hooks 에도 있다(콘솔 폴더로 연 세션용) — 둘을 같게 둔다.

let payload = {}
try {
  let s = ''; for await (const chunk of process.stdin) s += chunk
  payload = JSON.parse(s)
} catch { process.exit(0) }

const path = payload.tool_input?.file_path ?? ''
if (!/\.(ts|tsx|js|mjs)$/.test(path)) process.exit(0)  // SQL·마크다운은 안 본다
if (/check-legacy-supabase-key\.mjs$/.test(path)) process.exit(0)

const content = payload.tool_input?.content ?? payload.tool_input?.new_string ?? ''
// 키 자료로 쓰이는 형태만 잡는다: 환경변수명·SDK 초기화 인자. SQL 문자열 리터럴 형태
// ('service_role' 한 단어만 따옴표로 감싼 것)는 권한 이름일 확률이 높아 제외한다.
const KEY_USAGE = /SUPABASE_SERVICE_ROLE_KEY|service_role_key|SERVICE_ROLE_KEY/i

if (KEY_USAGE.test(content)) {
  // PreToolUse 는 hookSpecificOutput 꼴만 받는다 — 옛 최상위 decision 은 'approve'·'block' 만 옮겨 준다
  // (공식 문서 Hooks › PreToolUse, 2026-11-02 확인).
  console.log(JSON.stringify({
    hookSpecificOutput: {
      hookEventName: 'PreToolUse',
      permissionDecision: 'deny',
      permissionDecisionReason: 'legacy service_role 키는 쓰지 않는다(2026년 말 폐지 예정). secret key 로 고친다 — 엣지 함수는 SUPABASE_SECRET_KEYS(JSON, .default), 그 밖은 sb_secret_... 키. 기능은 같다. (프로젝트 규칙)',
    },
  }))
}
