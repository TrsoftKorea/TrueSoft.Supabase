// 작업 끝(Stop) 검사 감싸기: `node stop-check.mjs <이름> <명령> [인자...]`
// 검사가 실패하면 출력을 Claude 에게 넘기고 2(막기)로 끝낸다 — Stop 훅은 2일 때만 막는다.
// 이 세션이 고칠 수 없는 실패로는 막지 않는다. 막으면 끝날 때마다 다시 막혀 세션이 끝나지 못한다.
//   · 같은 응답을 다시 쓰는 동안에는 검사마다 한 번만 막는다(전역 scripts/lib/stop-once.mjs).
//   · 커밋 안 된 변경이 전부 다른 세션이 고친 것(구역 기록 .claude/zones/ledger.jsonl)이면 막지 않고 알리기만 한다 —
//     두 세션이 한 폴더를 쓰고 검사는 폴더 전체의 변경을 보기 때문이다. 기록에 없는 파일(셸로 고친 것 등)은 이 세션 몫으로 본다.
//   · 검사 도구를 띄우지 못하면(dotnet 없음 등) 막지 않고 알리기만 한다.
// 종료 코드 1 은 막지 않고 stderr 를 사용자에게만 보인다.
import { spawnSync } from 'node:child_process'
import { existsSync, readFileSync } from 'node:fs'
import { homedir } from 'node:os'
import { join } from 'node:path'
import { pathToFileURL } from 'node:url'

const [name, cmd, ...args] = process.argv.slice(2)
if (!name || !cmd) { console.error('stop-check: 이름과 명령이 필요합니다'); process.exit(1) }

let payload = {}
try { payload = JSON.parse(readFileSync(0, 'utf8') || '{}') } catch {}
const root = process.env.CLAUDE_PROJECT_DIR || payload.cwd || process.cwd()

const hook = `truesoft:${name}`
let once = null
const lib = join(homedir(), '.claude', 'scripts', 'lib', 'stop-once.mjs')
if (existsSync(lib)) { try { once = await import(pathToFileURL(lib).href) } catch {} }
const mayBlock = () => { try { if (once) return once.mayBlock(payload, hook) } catch {} return !payload.stop_hook_active }
const markBlocked = () => { try { once?.markBlocked(payload, hook) } catch {} }

if (!mayBlock()) process.exit(0)

const r = spawnSync(cmd, args, { encoding: 'utf8', maxBuffer: 32 * 1024 * 1024 })
if (r.status === 0) process.exit(0)

const out = [r.stdout, r.stderr].filter(Boolean).join('\n').trim()
if (r.error && r.status === null) {
  process.stderr.write(`[${name}] 검사 도구를 띄우지 못해 검사하지 않았습니다: ${r.error.message}\n`)
  process.exit(1)
}

const foreign = othersOnly()
if (foreign) {
  process.stderr.write(`[${name}] 검사가 실패했지만 커밋 안 된 변경이 모두 다른 세션이 고친 것(${foreign})이라 막지 않았습니다. 그 세션에서 고칩니다.\n${out}\n`)
  process.exit(1)
}

process.stderr.write(`${out || `${name} 가 종료 코드 ${r.status} 로 끝났습니다`}\n`)
markBlocked()
process.exit(2)

/** 커밋 안 된 변경이 하나 이상이고 전부 다른 세션의 마지막 수정이면 그 세션 수 설명, 아니면 null. */
function othersOnly() {
  const sid = payload.session_id
  const ledger = join(root, '.claude', 'zones', 'ledger.jsonl')
  if (!sid || !existsSync(ledger)) return null
  const st = spawnSync('git', ['-C', root, 'status', '--porcelain', '-uall'], { encoding: 'utf8' })
  if (st.status !== 0) return null
  const changed = st.stdout.split('\n').filter(Boolean).map((l) => l.slice(3).replace(/^.* -> /, '').replace(/^"|"$/g, ''))
  if (changed.length === 0) return null
  const last = new Map()
  for (const line of readFileSync(ledger, 'utf8').split('\n')) {
    try { const e = JSON.parse(line); if (e.path) last.set(e.path.toLowerCase(), e.session) } catch {}
  }
  const owners = new Set()
  for (const f of changed) {
    const who = last.get(f.toLowerCase())
    if (!who || who === sid) return null
    owners.add(who)
  }
  return `${changed.length}개 파일 · 세션 ${owners.size}개`
}
