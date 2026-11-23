// 작업 끝(Stop) 검사 감싸기: `node stop-check.mjs <이름> <명령> [인자...]`
// 검사가 실패하면 출력을 Claude 에게 넘기고 2(막기)로 끝낸다 — Stop 훅은 2일 때만 막는다.
// 같은 응답을 다시 쓰는 동안에는 검사마다 한 번만 막는다. 다른 세션이 고치던 파일이나 dotnet 이 못 도는 PC 처럼
// 이 세션이 고칠 수 없는 실패면 끝없이 막혀 세션이 끝나지 못하기 때문이다(전역 scripts/lib/stop-once.mjs).
import { spawnSync } from 'node:child_process'
import { existsSync, readFileSync } from 'node:fs'
import { homedir } from 'node:os'
import { join } from 'node:path'
import { pathToFileURL } from 'node:url'

const [name, cmd, ...args] = process.argv.slice(2)
if (!name || !cmd) { console.error('stop-check: 이름과 명령이 필요합니다'); process.exit(0) }

let payload = {}
try { payload = JSON.parse(readFileSync(0, 'utf8') || '{}') } catch {}

const hook = `truesoft:${name}`
let once = null
const lib = join(homedir(), '.claude', 'scripts', 'lib', 'stop-once.mjs')
if (existsSync(lib)) { try { once = await import(pathToFileURL(lib).href) } catch {} }
const mayBlock = () => once ? once.mayBlock(payload, hook) : !payload.stop_hook_active
const markBlocked = () => { if (once) once.markBlocked(payload, hook) }

if (!mayBlock()) process.exit(0)

const r = spawnSync(cmd, args, { encoding: 'utf8', maxBuffer: 32 * 1024 * 1024 })
if (r.status === 0) process.exit(0)

const out = [r.stdout, r.stderr].filter(Boolean).join('\n').trim()
process.stderr.write(`${out || r.error?.message || `${name} 가 종료 코드 ${r.status} 로 끝났습니다`}\n`)
markBlocked()
process.exit(2)
