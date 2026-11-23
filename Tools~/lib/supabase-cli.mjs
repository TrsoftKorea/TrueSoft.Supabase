// Supabase CLI 를 부르는 공통 부분 — 대기 프로젝트 준비(StandbyProject)와 실제 서버 대조(LiveCheck)가 함께 쓴다.
// 인증은 이 PC 의 `npx supabase login` 을 CLI 가 알아서 쓴다. 열쇠 값은 이 코드가 만지지 않는다.
import { spawn, spawnSync } from 'node:child_process'
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join, resolve, dirname } from 'node:path'
import { fileURLToPath } from 'node:url'

export const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..')
export const FUNCTIONS_DIR = join(ROOT, 'Samples~', 'DatabaseSetup', 'EdgeFunctions')
export const ADMIN_API = join(ROOT, 'Admin~', 'supabase', 'functions', 'admin-api', 'index.ts')
export const INSTALL_SQL = join(ROOT, 'Samples~', 'DatabaseSetup', 'SQL', 'player', 'install.sql')
export const VERIFY_SQL = join(ROOT, 'Samples~', 'DatabaseSetup', 'SQL', 'player', 'verify.sql')
export const CLAUDE_MD = join(ROOT, 'CLAUDE.md')

// fetch 직후 process.exit 를 부르면 윈도 Node 가 "Assertion failed … async.c" 로 죽는다 — 던져서 멈추고 종료 코드만 정한다.
export class Stop extends Error {}
export function die(msg) { console.error(`\n✘ ${msg}`); throw new Stop() }
export function exitOnStop() {
  const onStop = (e) => { if (!(e instanceof Stop)) console.error(e); process.exitCode = 1 }
  process.on('uncaughtException', onStop)
  process.on('unhandledRejection', onStop)
}

// 저장소 루트의 supabase/config.toml 을 읽으면 CLI 가 실패해 늘 빈 작업 폴더를 준다.
export const EMPTY = mkdtempSync(join(tmpdir(), 'supabase-cli-'))
process.on('exit', () => rmSync(EMPTY, { recursive: true, force: true }))

// 윈도에서 npx 는 셸로만 뜬다. 인자는 따옴표가 필요한 것(경로·쉼표 값)을 부르는 쪽에서 감싼다.
// CLI 판을 못박는다 — 판마다 옵션·동의 방식이 바뀌었다(3.x 는 프로젝트를 바꾸는 명령에 --yes 가 없으면 거절한다).
const commandOf = (args, workdir) => ['npx', 'supabase@3.1.0', ...args, '--workdir', `"${workdir}"`].join(' ')

export function cli(args, { workdir = EMPTY } = {}) {
  const r = spawnSync(commandOf(args, workdir), { encoding: 'utf8', shell: true, maxBuffer: 64 << 20 })
  return { ok: r.status === 0, out: r.stdout ?? '', err: r.stderr ?? '' }
}

/** 여러 프로젝트를 한꺼번에 물을 때 쓴다. */
export function cliAsync(args, { workdir = EMPTY } = {}) {
  return new Promise((done) => {
    const p = spawn(commandOf(args, workdir), { shell: true })
    let out = '', err = ''
    p.stdout.on('data', (d) => { out += d })
    p.stderr.on('data', (d) => { err += d })
    p.on('close', (code) => done({ ok: code === 0, out, err }))
  })
}

// --project-ref 는 stderr 에 늘 "deprecated" 경고를 내 진짜 오류(stdout 의 JSON)를 가린다 — 둘 다 보인다.
export const failText = (r) => `${r.out.trim().slice(-600)}\n${r.err.trim().slice(-400)}`.trim()

export function parseJson(r, label) {
  const i = r.out.search(/[[{]/)
  if (!r.ok || i < 0) die(`CLI 실패: ${label}\n${failText(r)}`)
  return JSON.parse(r.out.slice(i))
}
export const cliJson = (args, opts) => parseJson(cli([...args, '-o', 'json'], opts), `supabase ${args.join(' ')}`)
export const cliJsonAsync = async (args, opts) => parseJson(await cliAsync([...args, '-o', 'json'], opts), `supabase ${args.join(' ')}`)

export const rowsOf = (j) => (Array.isArray(j) ? j : j.rows ?? [])
export const sqlFile = (target, file) => cliJson(['db', 'query', `--remote=${target}`, '-f', `"${file}"`, '--yes'])
export const sqlFileAsync = (target, file) => cliJsonAsync(['db', 'query', `--remote=${target}`, '-f', `"${file}"`, '--yes'])
export function sqlText(target, text) {
  const f = join(EMPTY, `q${Date.now()}.sql`)
  writeFileSync(f, text)
  return sqlFile(target, f)
}
// 갓 만든 프로젝트에서 --remote 로 비밀값을 넣으면 "Finished" 라 답하고 아무것도 안 넣은 적이 있다(2026-11-10) — --project-ref 를 쓴다.
export const functionsOf = (target) => cliJson(['functions', 'list', '--project-ref', target])
export const functionsOfAsync = (target) => cliJsonAsync(['functions', 'list', '--project-ref', target])

/** CLAUDE.md 「Supabase 프로젝트 규칙」 표의 프로젝트(SQL 변경을 함께 받는 곳) — [{ name, ref }]. */
export function liveProjects() {
  const md = readFileSync(CLAUDE_MD, 'utf8')
  return [...md.matchAll(/^\| ([^|\r\n]+?) \| `([a-z]{20})` \|(?=\r?$)/gm)].map((m) => ({ name: m[1], ref: m[2] }))
}
