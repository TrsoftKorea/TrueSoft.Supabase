// 게시 버튼의 실제 서버 대조 관문 시험(npm run check:live). scripts/live-check.mjs 를 고치면 돌린다.
// 판정 함수만이 아니라 가짜 대조 도구를 실제로 띄워 본다 — 멈춤·시간 초과는 프로세스를 돌려야 드러난다.
// 실패 경로가 "막힘"으로 나오는 것과 함께 통과 1건이 "통과"로 나와야 한다(전부 막는 고장도 실패 시험만으론 초록불이다).
import fs from 'node:fs'
import os from 'node:os'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { LIVE_TOOL, liveArgs, liveProblem, runLiveCheck } from './live-check.mjs'

const ADMIN = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const fail = []
let count = 0
const ok = (cond, name, got) => { count++; if (!cond) fail.push(`${name}${got !== undefined ? ` → ${JSON.stringify(got)}` : ''}`) }
const f = (...files) => files.map((file) => ({ file }))
const blocked = (p) => typeof p === 'string' && p.startsWith('실제 서버 대조 도구가 실패')

// ── 어느 대조를 돌릴지 ──
ok(liveArgs(f('Admin~/src/pages/Mails.tsx', 'Runtime/Unity/Supabase.cs', 'Samples~/DatabaseSetup/README.md')) === null, '서버 파일 없음 → 안 돌림')
ok(JSON.stringify(liveArgs(f('Samples~/DatabaseSetup/SQL/player/install.sql'))) === '["--only","db"]', 'SQL 만 → --only db')
ok(JSON.stringify(liveArgs(f('Samples~/DatabaseSetup/EdgeFunctions/push-send/index.ts'))) === '["--only","functions"]', '엣지 함수만 → --only functions')
ok(JSON.stringify(liveArgs(f('Admin~/supabase/functions/admin-api/index.ts'))) === '["--only","functions"]', '콘솔 서버 함수만 → --only functions')
ok(JSON.stringify(liveArgs(f('Samples~/DatabaseSetup/SQL/player/install.sql', 'Admin~/supabase/functions/admin-api/index.ts'))) === '[]', '둘 다 → 전부')
ok(JSON.stringify(liveArgs([{ file: 'Samples~/DatabaseSetup/old.sql', orig: 'Samples~/DatabaseSetup/SQL/x.sql' }])) === '["--only","db"]', '이름 바꾼 파일의 옛 경로도 봄')

// 화면(PublishBar 의 진행 글자)과 서버가 같은 파일을 서버 파일로 보는지
const bar = fs.readFileSync(path.join(ADMIN, 'src/mock/PublishBar.tsx'), 'utf8').match(/const SERVER_FILE = \/(.+)\/\r?\n/)
ok(!!bar, '화면의 서버 파일 판정을 찾음')
if (bar) {
  const re = new RegExp(bar[1])
  const samples = ['Samples~/DatabaseSetup/SQL/player/install.sql', 'Samples~/DatabaseSetup/EdgeFunctions/a/index.ts', 'Admin~/supabase/functions/admin-api/index.ts',
    'Admin~/supabase/config.toml', 'Samples~/DatabaseSetup/README.md', 'Admin~/src/App.tsx', 'Runtime/Unity/Supabase.cs']
  ok(samples.every((s) => re.test(s) === (liveArgs(f(s)) !== null)), '화면·서버의 서버 파일 범위 일치')
}

// ── 출력 판정 ──
const MISMATCH = '실제 서버 대조 — A · B\n\n✔ 게임끼리 DB 구조\n\n✘ DB 함수 ↔ install.sql — B\n   · ts_foo: 본문 다름\n   · ts_bar: 서버에 없음\n\n8개 중 7개 통과\n'
const mismatch = liveProblem({ status: 1, stdout: MISMATCH })
ok(mismatch?.startsWith('실제 서버가 저장소와 달라') && mismatch.includes('✘ DB 함수 ↔ install.sql — B\n   · ts_foo: 본문 다름\n   · ts_bar: 서버에 없음') && !mismatch.includes('✔'), '어긋남 → 막힘(✘ 줄·항목)', mismatch)
ok(blocked(liveProblem({ status: 1, stdout: '실제 서버 대조 — A · B\n', stderr: '✘ 로그인이 필요합니다\n' })), '도구가 스스로 멈춤 → 도구 실패')
ok(blocked(liveProblem({ status: 1, stderr: 'TypeError: fetch failed\n' })), '도구가 예외로 죽음 → 도구 실패')
ok(blocked(liveProblem({ status: null, error: new Error('spawn ENOENT') })), '도구를 못 띄움 → 도구 실패')
ok(blocked(liveProblem({ status: 0, stdout: '' })), '종료 0인데 끝 줄 없음 → 도구 실패')
ok(blocked(liveProblem({ status: 0, stdout: '9개 중 8개 통과\n' })), '종료 0인데 숫자 안 맞음 → 도구 실패')
ok(liveProblem({ status: 0, stdout: '✔ a\r\n\r\n9개 중 9개 통과\r\n' }) === null, '줄바꿈이 CRLF 여도 일치 → 통과')

// ── 가짜 도구를 실제로 띄워 본다 ──
// 저장소 대신 임시 폴더에 같은 경로로 가짜 도구를 둔다. 실제 도구는 두 게임 서버에 묻는다 — 여기서는 부르지 않는다.
async function runFake(body, timeoutMs = 20_000) {
  const repo = fs.mkdtempSync(path.join(os.tmpdir(), 'livecheck-'))
  try {
    fs.mkdirSync(path.join(repo, path.dirname(LIVE_TOOL)), { recursive: true })
    fs.writeFileSync(path.join(repo, LIVE_TOOL), body)
    return await runLiveCheck(repo, ['--only', 'db'], timeoutMs)
  } finally {
    // 시간 초과로 끈 프로세스가 폴더를 잡고 있으면 지우지 못한다 — 임시 폴더라 남겨도 된다.
    try { fs.rmSync(repo, { recursive: true, force: true }) } catch {}
  }
}
const started = Date.now()
const hang = await runFake("console.log('실제 서버 대조 — A · B'); setInterval(() => {}, 1000)\n", 1_500)
ok(hang.status === null && blocked(liveProblem(hang)) && Date.now() - started < 10_000, '도구가 멈춤 → 시간 초과로 끊고 막힘', { status: hang.status, error: hang.error?.message })
const zero = await runFake("console.log('0개 중 0개 통과')\n")
ok(zero.status === 0 && blocked(liveProblem(zero)), '0개 중 0개 → 막힘(본 것이 없음)', zero)
const passed = await runFake("console.log('✔ DB 구조\\n\\n' + process.argv.slice(2).join(' ') + '\\n9개 중 9개 통과')\n")
ok(passed.status === 0 && liveProblem(passed) === null && passed.stdout.includes('--only db'), '모두 일치 → 통과(인자도 넘어감)', passed)
const differ = await runFake("console.log('✘ 서버 함수 — A\\n   · push-send: 다름\\n\\n9개 중 8개 통과'); process.exit(1)\n")
ok(differ.status === 1 && liveProblem(differ)?.startsWith('실제 서버가 저장소와 달라'), '어긋남(종료 1) → 막힘', differ)

if (fail.length) {
  console.log(`실제 서버 대조 시험 ${count}개 중 ${fail.length}개 실패:\n${fail.map((m) => `  ✘ ${m}`).join('\n')}`)
  process.exit(1)
}
console.log(`실제 서버 대조 시험 ${count}개 통과`)
