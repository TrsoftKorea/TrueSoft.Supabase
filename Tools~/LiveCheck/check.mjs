#!/usr/bin/env node
// 실제 서버 대조 — CLAUDE.md 표의 게임들이 저장소와 같은지 본다. 고치지 않고 읽기만 한다.
//
//   node "Tools~/LiveCheck/check.mjs"                 DB·서버 함수 전부
//   node "Tools~/LiveCheck/check.mjs" --only db       DB 만
//   node "Tools~/LiveCheck/check.mjs" --only functions 서버 함수만
//
// 무엇을 보는지·무엇을 못 보는지는 README.md. 어긋남이 하나라도 있으면 종료 코드 1.
import { createHash } from 'node:crypto'
import { existsSync, readFileSync, readdirSync, writeFileSync, mkdirSync } from 'node:fs'
import { join, dirname } from 'node:path'
import { fileURLToPath } from 'node:url'
import {
  FUNCTIONS_DIR, ADMIN_API, INSTALL_SQL, VERIFY_SQL, EMPTY,
  die, exitOnStop, cliAsync, failText, rowsOf, sqlFileAsync, functionsOfAsync, liveProjects,
} from '../lib/supabase-cli.mjs'
import {
  CUSTOM_TABLES, fileFunctions, fileCronNames, fileTableColumns,
  compareProjects, compareFunctionsToFile, compareCron, compareEdgeLists, firstDiffLine,
} from './compare.mjs'

exitOnStop()

const HERE = dirname(fileURLToPath(import.meta.url))
const FINGERPRINT = join(HERE, 'fingerprint.sql')
// 저장소 코드 요약값 → 그때 서버 코드 요약값. 둘 다 같으면 내려받지 않는다(깃 제외 파일).
const CACHE = join(HERE, '.cache.json')
// 파일에서 뽑은 함수가 이보다 적으면 뽑는 방법이 고장 난 것이다 — 빠진 함수를 못 본 채 통과하지 않게.
const MIN_FILE_FUNCTIONS = 200

const argv = process.argv.slice(2)
const only = argv.includes('--only') ? argv[argv.indexOf('--only') + 1] : null
if (only && !['db', 'functions'].includes(only)) die('--only 는 db 또는 functions 입니다.')

const projects = liveProjects()
if (projects.length === 0) die('CLAUDE.md 「Supabase 프로젝트 규칙」 표에서 프로젝트를 못 찾았습니다.')
console.log(`실제 서버 대조 — ${projects.map((p) => p.name).join(' · ')}`)

const results = []
function report(label, problems, detail) {
  results.push(problems.length === 0)
  console.log(`\n${problems.length ? '✘' : '✔'} ${label}${detail ? ` — ${detail}` : ''}`)
  for (const p of problems.slice(0, 15)) console.log(`   · ${p}`)
  if (problems.length > 15) console.log(`   · 외 ${problems.length - 15}건`)
}

// ── DB ───────────────────────────────────────────────────────────────────────
if (only !== 'functions') {
  const sql = readFileSync(INSTALL_SQL, 'utf8')
  const fileDefs = fileFunctions(sql)
  if (fileDefs.length < MIN_FILE_FUNCTIONS) die(`install.sql 에서 찾은 DB 함수가 ${fileDefs.length}개뿐입니다. 정의 모양이 바뀌었으면 compare.mjs 의 fileFunctions 를 고치세요.`)
  const cronNames = fileCronNames(sql)
  const baseColumns = Object.fromEntries(CUSTOM_TABLES.map((t) => [t, fileTableColumns(sql, t)]))
  for (const [t, cols] of Object.entries(baseColumns)) if (!cols?.length) die(`install.sql 에서 ${t} 의 create table 을 못 찾았습니다.`)

  const db = await Promise.all(projects.map(async (p) => ({
    ...p,
    rows: rowsOf(await sqlFileAsync(p.ref, FINGERPRINT)),
    verify: rowsOf(await sqlFileAsync(p.ref, VERIFY_SQL)),
  })))

  const cross = compareProjects(db, baseColumns, sql)
  const total = Object.values(cross.counts).reduce((a, b) => a + b, 0)
  report(`게임끼리 DB 구조`, cross.problems,
    `${total}개 대조(${Object.entries(cross.counts).map(([a, n]) => `${a} ${n}`).join(' · ')}) · 게임별 칸 ${cross.skipped}개 제외`)

  for (const p of db) {
    const fn = compareFunctionsToFile(p.rows, fileDefs)
    report(`${p.name} DB 함수 ↔ install.sql`, fn.problems, `서버 ${fn.live}개 · 파일 ${fn.file}개`)
    const bad = p.verify.filter((r) => r.status !== 'applied')
    report(`${p.name} 설치 검증(verify.sql)`, bad.map((r) => `${r.category ?? ''} ${r.object_name} — ${r.status}${r.detail ? ` ${String(r.detail).slice(0, 120)}` : ''}`),
      `${p.verify.length}개 중 ${p.verify.length - bad.length}개 정상`)
    if (p.verify.length === 0) results.push(false)
    report(`${p.name} 자동 작업 ↔ install.sql`, compareCron(p.rows, cronNames), `파일 ${cronNames.length}개`)
  }
}

// ── 서버 함수 ─────────────────────────────────────────────────────────────────
if (only !== 'db') {
  const repo = new Map([
    ...readdirSync(FUNCTIONS_DIR, { withFileTypes: true }).filter((d) => d.isDirectory())
      .map((d) => [d.name, join(FUNCTIONS_DIR, d.name, 'index.ts')]),
    ['admin-api', ADMIN_API],
  ])
  const lists = await Promise.all(projects.map(async (p) => ({ ...p, functions: await functionsOfAsync(p.ref) })))
  report('서버 함수 목록·게임끼리 코드', compareEdgeLists(lists, [...repo.keys()]), `저장소 ${repo.size}개`)

  // 첫 게임의 배포 코드를 저장소와 본다. 다른 게임은 위에서 요약값으로 첫 게임과 맞췄다.
  const cache = existsSync(CACHE) ? JSON.parse(readFileSync(CACHE, 'utf8')) : {}
  const live = new Map(lists[0].functions.map((f) => [f.slug, f]))
  const sha = (t) => createHash('sha256').update(t.replace(/\r/g, '')).digest('hex')
  const problems = []
  let downloaded = 0
  const work = join(EMPTY, 'download')
  mkdirSync(work, { recursive: true })
  for (const [slug, path] of repo) {
    const deployed = live.get(slug)
    if (!deployed) continue
    const source = readFileSync(path, 'utf8')
    const c = cache[slug]
    if (c && c.repo === sha(source) && c.live === deployed.ezbr_sha256) continue
    downloaded++
    const r = await cliAsync(['functions', 'download', slug, '--project-ref', lists[0].ref, '--use-api'], { workdir: work })
    const file = join(work, 'supabase', 'functions', slug, 'index.ts')
    if (!r.ok || !existsSync(file)) { problems.push(`서버 함수 ${slug} — 내려받기 실패: ${failText(r).split('\n')[0]}`); continue }
    const line = firstDiffLine(readFileSync(file, 'utf8'), source)
    if (line) problems.push(`서버 함수 ${slug} — 배포된 코드가 저장소와 다름(${line}줄부터). 배포하지 않은 수정이 있거나 서버에서 직접 고쳤습니다`)
    else cache[slug] = { repo: sha(source), live: deployed.ezbr_sha256 }
  }
  writeFileSync(CACHE, JSON.stringify(cache, null, 2))
  report(`서버 함수 코드 ↔ 저장소(${lists[0].name})`, problems, `${repo.size}개 중 ${downloaded}개 내려받아 비교, 나머지는 지난번 확인과 같음`)
}

const passed = results.filter(Boolean).length
console.log(`\n${results.length}개 중 ${passed}개 통과`)
if (passed !== results.length) process.exitCode = 1
