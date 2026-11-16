#!/usr/bin/env node
// 대기 프로젝트 준비 — 새로 만든 빈 Supabase 프로젝트를 콘솔의 "새 프로젝트 만들기"로 꺼낼 수 있게 만든다.
//
//   node Tools~/StandbyProject/prepare.mjs <ref> --name <표에 적을 이름> --admin-emails <마스터 이메일,…>
//   node Tools~/StandbyProject/prepare.mjs --check <ref>      바꾸지 않고 점검만
//
// 설정은 손으로 적어 두지 않고 기준 게임(--reference, 기본 데빌 슬레이어)에서 읽는다 — 서버 함수마다
// 로그인 검사를 켤지, 콘솔 비밀값이 무엇인지. 비밀값은 값이 아니라 요약값(sha256)만 대조한다.
// 모든 단계는 다시 돌려도 안전하다. 실패하면 그 자리에서 멈추고, 고친 뒤 처음부터 다시 돌리면 된다.
import { spawnSync } from 'node:child_process'
import { createHash } from 'node:crypto'
import { cpSync, mkdirSync, mkdtempSync, readFileSync, readdirSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join, resolve, dirname } from 'node:path'
import { fileURLToPath } from 'node:url'

// fetch 직후 process.exit 를 부르면 윈도 Node 가 "Assertion failed … async.c" 로 죽는다 — 던져서 멈추고 종료 코드만 정한다.
class Stop extends Error {}
function die(msg) { console.error(`\n✘ ${msg}`); throw new Stop() }
const onStop = (e) => { if (!(e instanceof Stop)) console.error(e); process.exitCode = 1 }
process.on('uncaughtException', onStop)
process.on('unhandledRejection', onStop)

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..')
const FUNCTIONS_DIR = join(ROOT, 'Samples~', 'DatabaseSetup', 'EdgeFunctions')
const ADMIN_API = join(ROOT, 'Admin~', 'supabase', 'functions', 'admin-api', 'index.ts')
const INSTALL_SQL = join(ROOT, 'Samples~', 'DatabaseSetup', 'SQL', 'player', 'install.sql')
const VERIFY_SQL = join(ROOT, 'Samples~', 'DatabaseSetup', 'SQL', 'player', 'verify.sql')
const DEPLOY_YML = join(ROOT, '.github', 'workflows', 'admin-deploy.yml')
const CLAUDE_MD = join(ROOT, 'CLAUDE.md')
const BASE_GAMES = ['wxivrmvtpufeczltward', 'owumqjyctqhuyailqutd'] // 배포 설정에 따로 이름 붙은 두 게임

// ── 인자 ─────────────────────────────────────────────────────────────────────
const argv = process.argv.slice(2)
const opt = (name) => { const i = argv.indexOf(name); return i >= 0 ? argv[i + 1] : undefined }
const checkOnly = argv.includes('--check')
const ref = checkOnly ? opt('--check') : argv.find((a, i) => !a.startsWith('--') && !argv[i - 1]?.startsWith('--'))
const reference = opt('--reference') ?? 'owumqjyctqhuyailqutd'
const name = opt('--name')
const adminEmails = opt('--admin-emails')

if (!ref || !/^[a-z]{20}$/.test(ref)) die('프로젝트 ref(소문자 20자)를 주세요. 사용법은 파일 맨 위 주석.')
if (!checkOnly && (!name || !adminEmails)) die('--name 과 --admin-emails 가 필요합니다.')
if (ref === reference) die('기준 게임과 같은 프로젝트입니다. --reference 로 다른 게임을 고르세요.')

// ── Supabase CLI ─────────────────────────────────────────────────────────────
// 저장소 루트의 supabase/config.toml 을 읽으면 CLI 가 실패해 늘 빈 작업 폴더를 준다.
const EMPTY = mkdtempSync(join(tmpdir(), 'standby-'))
process.on('exit', () => rmSync(EMPTY, { recursive: true, force: true }))

function cli(args, { workdir = EMPTY } = {}) {
  // 윈도에서 npx 는 셸로만 뜬다. 인자는 따옴표가 필요한 것(경로·쉼표 값)을 부르는 쪽에서 감싼다.
  // CLI 판을 못박는다 — 판마다 옵션·동의 방식이 바뀌었다(3.x 는 프로젝트를 바꾸는 명령에 --yes 가 없으면 거절한다).
  const command = ['npx', 'supabase@3.1.0', ...args, '--workdir', `"${workdir}"`].join(' ')
  const r = spawnSync(command, { encoding: 'utf8', shell: true, maxBuffer: 64 << 20 })
  return { ok: r.status === 0, out: r.stdout ?? '', err: r.stderr ?? '' }
}
// --project-ref 는 stderr 에 늘 "deprecated" 경고를 내 진짜 오류(stdout 의 JSON)를 가린다 — 둘 다 보인다.
const failText = (r) => `${r.out.trim().slice(-600)}\n${r.err.trim().slice(-400)}`.trim()
function cliJson(args, opts) {
  const r = cli([...args, '-o', 'json'], opts)
  const i = r.out.search(/[[{]/)
  if (!r.ok || i < 0) die(`CLI 실패: supabase ${args.join(' ')}\n${failText(r)}`)
  return JSON.parse(r.out.slice(i))
}
async function consoleLook(target) {
  try {
    const res = await fetch(`https://${target}.supabase.co/functions/v1/admin-api`, {
      method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ action: 'auth.consoleLook', params: {} }),
    })
    return (await res.json())?.data ?? null
  } catch {
    return null
  }
}
const sql = (target, file) => cliJson(['db', 'query', `--remote=${target}`, '-f', `"${file}"`, '--yes'])
function sqlText(target, text) {
  const f = join(EMPTY, `q${Date.now()}.sql`)
  writeFileSync(f, text)
  return sql(target, f)
}
const rowsOf = (j) => (Array.isArray(j) ? j : j.rows ?? [])
// 갓 만든 프로젝트에서 --remote 로 비밀값을 넣으면 "Finished" 라 답하고 아무것도 안 넣은 적이 있다(2026-11-10) — --project-ref 를 쓴다.
const functionsOf = (target) => cliJson(['functions', 'list', '--project-ref', target])
const secretsOf = (target) => Object.fromEntries(cliJson(['secrets', 'list', '--project-ref', target]).map((s) => [s.name, String(s.value)]))
const sha = (v) => createHash('sha256').update(v).digest('hex')

// ── 기준 게임에서 읽는 것 ─────────────────────────────────────────────────────
const sourceFunctions = [
  ...readdirSync(FUNCTIONS_DIR, { withFileTypes: true }).filter((d) => d.isDirectory()).map((d) => d.name),
  'admin-api',
].sort()
const googleClientId = readFileSync(DEPLOY_YML, 'utf8').match(/VITE_GOOGLE_CLIENT_ID:\s*(\S+)/)?.[1]
if (!googleClientId) die('admin-deploy.yml 에서 VITE_GOOGLE_CLIENT_ID 를 못 찾았습니다.')

console.log(`기준 게임 ${reference} 을 읽습니다…`)
// 로그인 검사 설정과 코드 요약값(ezbr_sha256). 요약값이 같으면 같은 코드다 — 두 게임에서 21개 모두 같았다(2026-11-16).
const refList = functionsOf(reference)
const refFunctions = new Map(refList.map((f) => [f.slug, f.verify_jwt]))
const refCode = new Map(refList.map((f) => [f.slug, f.ezbr_sha256]))
const missingInRef = sourceFunctions.filter((f) => !refFunctions.has(f))
if (missingInRef.length) die(`기준 게임에 없는 서버 함수가 있어 로그인 검사 설정을 정할 수 없습니다: ${missingInRef.join(', ')}`)
const refSecrets = secretsOf(reference)

// ── 준비 ─────────────────────────────────────────────────────────────────────
if (!checkOnly) {
  // 운영 중인 게임에 돌리면 운영 DB 에 설치를 다시 하고 작업 폴더의 함수를 배포한다 — 막는다.
  // 콘솔 서버 함수가 아직 없는 빈 프로젝트는 응답이 없어 지나간다.
  const before = await consoleLook(ref)
  if (before?.active) die(`운영 중인 프로젝트입니다(${before.display_name ?? '이름 없음'}). 점검만 하려면 --check 를 쓰세요.`)
  if (sha(adminEmails) !== refSecrets.ADMIN_EMAILS) {
    die('--admin-emails 가 기준 게임의 마스터 이메일과 다릅니다(쉼표·순서·공백까지 같아야 합니다).')
  }

  step('1/6 DB 설치(install.sql)')
  sql(ref, INSTALL_SQL)

  step('2/6 프로젝트 주소 등록')
  const url = `https://${ref}.supabase.co`
  sqlText(ref, `select vault.create_secret('${url}', 'project_url') where not exists (select 1 from vault.secrets where name = 'project_url');`)

  step(`3/6 서버 함수 ${sourceFunctions.length}개 배포`)
  const work = join(EMPTY, 'deploy')
  for (const fn of sourceFunctions) {
    const dir = join(work, 'supabase', 'functions', fn)
    mkdirSync(dir, { recursive: true })
    cpSync(fn === 'admin-api' ? ADMIN_API : join(FUNCTIONS_DIR, fn, 'index.ts'), join(dir, 'index.ts'))
  }
  for (const fn of sourceFunctions) {
    const noVerify = refFunctions.get(fn) === false
    const r = cli(['functions', 'deploy', fn, '--project-ref', ref, '--use-api', '--yes', ...(noVerify ? ['--no-verify-jwt'] : [])], { workdir: work })
    if (!r.ok || !/Deployed Functions/.test(r.out + r.err)) die(`${fn} 배포 실패\n${failText(r)}`)
    console.log(`   ${fn}${noVerify ? ' · 로그인 검사 끔' : ''}`)
  }

  step('4/6 콘솔 서버 함수 비밀값')
  const set = cli(['secrets', 'set', '--project-ref', ref, '--yes', `GOOGLE_CLIENT_ID=${googleClientId}`, `"ADMIN_EMAILS=${adminEmails}"`])
  if (!set.ok) die(`비밀값 넣기 실패\n${failText(set)}`)

  step('5/6 콘솔 배포 설정에 주소 더하기')
  // 배포에 몇 분 걸리는 사이 다른 작업이 이 파일을 고쳤을 수 있어 쓰기 직전에 읽는다.
  const deployYml = readFileSync(DEPLOY_YML, 'utf8')
  if (!deployYml.includes(url)) {
    const line = deployYml.match(/^(\s*)VITE_MORE_PROJECT_URLS:\s*(.*)$/m)
    const next = line
      ? deployYml.replace(line[0], `${line[1]}VITE_MORE_PROJECT_URLS: ${[line[2].trim(), url].filter(Boolean).join(',')}`)
      : deployYml.replace(/^(\s*)VITE_DEVILSLAYER_URL:.*$/m, (m, sp) => `${m}\n${sp}# 그 밖의 프로젝트(대기 프로젝트 포함). 쉼표로 나눈다 — Tools~/StandbyProject 가 더한다.\n${sp}VITE_MORE_PROJECT_URLS: ${url}`)
    if (next === deployYml) die('admin-deploy.yml 에 주소를 넣을 자리를 못 찾았습니다.')
    writeFileSync(DEPLOY_YML, next)
  }

  step('6/6 SQL 적용 대상 표에 더하기')
  const md = readFileSync(CLAUDE_MD, 'utf8')
  if (!md.includes(`\`${ref}\``)) {
    const last = [...md.matchAll(/^\| [^|\r\n]+ \| `[a-z]{20}` \|(?=\r?$)/gm)].pop()
    if (!last) die('CLAUDE.md 에서 프로젝트 표를 못 찾았습니다.')
    const at = last.index + last[0].length
    const eol = md.includes('\r\n') ? '\r\n' : '\n'
    writeFileSync(CLAUDE_MD, `${md.slice(0, at)}${eol}| ${name} | \`${ref}\` |${md.slice(at)}`)
  }
}

// ── 점검 ─────────────────────────────────────────────────────────────────────
// 「전부」 개수를 함께 내 못 본 것이 0 으로 숨지 않게 한다.
step('점검')
const results = []
const check = (label, ok, detail = '') => { results.push(ok); console.log(`   ${ok ? '✔' : '✘'} ${label}${detail ? ` — ${detail}` : ''}`) }

const verifyRows = rowsOf(sql(ref, VERIFY_SQL))
const bad = verifyRows.filter((r) => r.status !== 'applied')
check('DB 설치 검증', verifyRows.length > 0 && bad.length === 0,
  `${verifyRows.length}개 중 ${verifyRows.length - bad.length}개 정상${bad.length ? ` · 어긋남: ${bad.slice(0, 5).map((r) => `${r.object_name}(${r.status})`).join(', ')}` : ''}`)

const urlRows = rowsOf(sqlText(ref, `select count(*)::int as n, bool_or(decrypted_secret = 'https://${ref}.supabase.co') as same from vault.decrypted_secrets where name = 'project_url';`))
check('프로젝트 주소 등록', urlRows[0]?.n === 1 && urlRows[0]?.same === true, `등록 ${urlRows[0]?.n ?? 0}개`)

const liveList = functionsOf(ref)
const live = new Map(liveList.map((f) => [f.slug, f]))
const fnBad = sourceFunctions.filter((f) => !live.has(f) || live.get(f).verify_jwt !== refFunctions.get(f))
const codeBad = sourceFunctions.filter((f) => live.has(f) && live.get(f).ezbr_sha256 !== refCode.get(f))
check('서버 함수와 로그인 검사 설정', fnBad.length === 0,
  `${sourceFunctions.length}개 중 ${sourceFunctions.length - fnBad.length}개 기준 게임과 같음${fnBad.length ? ` · 다름: ${fnBad.join(', ')}` : ''}`)
check('서버 함수 코드', codeBad.length === 0,
  `${sourceFunctions.length}개 중 ${sourceFunctions.length - codeBad.length}개 기준 게임과 같음${codeBad.length ? ` · 다름: ${codeBad.join(', ')}` : ''}`)

const sec = secretsOf(ref)
check('콘솔 로그인 클라이언트 값', sec.GOOGLE_CLIENT_ID === sha(googleClientId))
check('마스터 이메일', !!sec.ADMIN_EMAILS && sec.ADMIN_EMAILS === refSecrets.ADMIN_EMAILS)

const look = await consoleLook(ref)
check('콘솔 서버 함수 응답', look !== null, look ? (look.active ? `사용 중 · ${look.display_name ?? '이름 없음'}` : '대기 중') : '응답 없음')

// 익명 로그인 같은 로그인 기본 설정은 마스터가 관리 열쇠를 넣어야 들어간다(auth-config-sync). 열쇠가 없으면
// 기록이 last_ok = null·'no_token' 으로 남는다. 대기 프로젝트는 꺼내기 전에 반드시 반영돼 있어야 하므로
// --check 에서 대기 중이면 센다. 운영 중인 두 게임은 대시보드에서 손으로 맞춰 열쇠가 없어 세지 않는다.
const authRows = rowsOf(sqlText(ref, `select last_ok, last_message, to_char(last_finished_at, 'YYYY-MM-DD HH24:MI') as at from public.purchase_refund_sync where store = 'auth_sync';`))
const auth = authRows[0]
const authText = auth ? `${auth.last_ok === true ? '반영됨' : auth.last_message === 'no_token' ? '관리 열쇠 없음' : `실패 · ${auth.last_message ?? ''}`}${auth.at ? ` · ${auth.at} UTC` : ''}` : '기록 없음'
if (checkOnly && look && !look.active) check('로그인 기본 설정 반영', auth?.last_ok === true, authText)
else console.log(`   · 로그인 기본 설정 반영 — ${authText}${look?.active ? ' (운영 중이라 세지 않음)' : ' (관리 열쇠를 넣은 뒤 --check 로 확인)'}`)

const url = `https://${ref}.supabase.co`
check('콘솔 배포 설정에 주소', BASE_GAMES.includes(ref) || readFileSync(DEPLOY_YML, 'utf8').includes(url))
check('SQL 적용 대상 표', readFileSync(CLAUDE_MD, 'utf8').includes(`\`${ref}\``))

const passed = results.filter(Boolean).length
console.log(`\n${results.length}개 중 ${passed}개 통과`)
if (passed !== results.length) process.exitCode = 1
else if (!checkOnly) {
  console.log('\n남은 일: ① 바뀐 admin-deploy.yml·CLAUDE.md 를 게시 ② 마스터가 콘솔 "트루베이스 설정"에서 이 프로젝트의 관리 열쇠를 넣는다 ③ --check 로 로그인 기본 설정까지 반영됐는지 본다.')
}

function step(t) { console.log(`\n▶ ${t}`) }
