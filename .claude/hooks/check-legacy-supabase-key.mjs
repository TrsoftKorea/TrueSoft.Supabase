// Supabase legacy service_role 키를 새 코드에 쓰려 하면 확인을 구한다.
// service_role 은 2026년 말 폐지 예정, 공식 대체재는 secret key(sb_secret_...) — 기능은 같다
// (RLS 우회, 서버 전용). 둘 다 지금은 동시에 동작해서 legacy 를 써도 에러가 안 난다 — 그래서
// 조용히 반복해서 쓰게 된다(2026-08-19, 실제 지적받은 사례: project_admin_tool_retool_replacement.md).
//
// **SQL의 `service_role` 권한 이름은 건드리지 않는다** — grant/revoke·has_*_privilege 는
// 정당한 기존 패턴이다. 여기서 잡는 건 백엔드 코드가 연결에 쓰는 키 자료뿐이다.

import { readFileSync } from 'node:fs'

let payload = {}
try {
  let s = ''; for await (const chunk of process.stdin) s += chunk
  payload = JSON.parse(s)
} catch { process.exit(0) }

const path = payload.tool_input?.file_path ?? ''
if (!/\.(ts|tsx|js|mjs)$/.test(path)) process.exit(0)  // SQL·마크다운은 안 본다

const content = payload.tool_input?.content ?? payload.tool_input?.new_string ?? ''
// 키 자료로 쓰이는 형태만 잡는다: 환경변수명·SDK 초기화 인자. SQL 문자열 리터럴 형태
// ('service_role' 한 단어만 따옴표로 감싼 것)는 권한 이름일 확률이 높아 제외한다.
const KEY_USAGE = /SUPABASE_SERVICE_ROLE_KEY|service_role_key|SERVICE_ROLE_KEY/i

if (KEY_USAGE.test(content)) {
  console.log(JSON.stringify({
    decision: 'ask',
    reason: 'legacy service_role 키를 쓰려 한다. Supabase 공식 대체재는 secret key(sb_secret_...) — 기능은 동일하고 지금 발급 가능하다. 이 파일에서 legacy 키가 꼭 필요한 이유가 있는지 확인한다. (프로젝트 규칙)',
  }))
}
