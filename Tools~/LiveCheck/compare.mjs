// 실제 서버 대조의 판정 — 입출력 없이 값만 받아 어긋남을 돌려준다(check.mjs 가 부르고 selftest.mjs 가 시험한다).
import { createHash } from 'node:crypto'

const md5 = (s) => createHash('md5').update(s).digest('hex')
// fingerprint.sql 의 함수 본문 요약과 같은 방법 — 주석(--)과 공백을 지운다.
export const bodyHash = (s) => md5(s.replace(/--[^\n]*/g, '').replace(/\s+/g, ''))

// 게임마다 운영자가 칸을 더하는 표 — 이 표의 칸·색인·제약은 install.sql 에 적힌 것만 대조한다.
export const CUSTOM_TABLES = ['user_data', 'leaderboard_scores']
// 콘솔 "서비스 연결"에서 게임마다 넣는 값 — 있고 없음이 게임마다 다른 게 정상이다.
const isConsoleSetting = (axis, name) => axis === 'vault_name' && name.startsWith('ts_cfg:')

/** install.sql 의 함수 정의 — [{ name, body, line }]. 본문은 첫 달러 따옴표($$·$tag$) 안. */
export function fileFunctions(sql) {
  const head = /create\s+(?:or\s+replace\s+)?function\s+(?:public\.)?"?(\w+)"?\s*\(/gi
  const out = []
  let m
  while ((m = head.exec(sql))) {
    const rest = sql.slice(m.index)
    const open = /\$(\w*)\$/.exec(rest)
    if (!open) continue
    const tag = open[0]
    const start = open.index + tag.length
    const end = rest.indexOf(tag, start)
    if (end < 0) continue
    out.push({ name: m[1].toLowerCase(), body: bodyHash(rest.slice(start, end)), line: sql.slice(0, m.index).split('\n').length })
    head.lastIndex = m.index + end + tag.length
  }
  return out
}

/** install.sql 의 크론 이름 — cron.schedule('이름', …). */
export const fileCronNames = (sql) => [...new Set([...sql.matchAll(/cron\.schedule\(\s*'([^']+)'/g)].map((m) => m[1]))]

/** install.sql 의 create table 안에 적힌 SDK 칸 이름. 「게임 데이터 컬럼」 표시 아래는 예시라 게임마다 다르다. */
export function fileTableColumns(sql, table) {
  const m = new RegExp(`create table if not exists public\\.${table}\\s*\\(([\\s\\S]*?)\\n\\);`, 'i').exec(sql)
  if (!m) return null
  return m[1].split(/^.*게임 데이터 컬럼.*$/m)[0].split('\n')
    .map((l) => l.replace(/--.*$/, '').trim())
    .filter((l) => l && !/^(constraint|primary|unique|check|foreign)\b/i.test(l))
    .map((l) => l.split(/\s+/)[0].replace(/"/g, ''))
}

const key = (r) => `${r.axis} ${r.name}`
const customTableOf = (r) => (['column', 'index', 'constraint'].includes(r.axis) ? CUSTOM_TABLES.find((t) => r.name.startsWith(`${t}.`)) : undefined)

/**
 * 프로젝트끼리 구조 대조. projects = [{ name, rows }] — 첫 프로젝트를 기준으로 본다.
 * 돌려주는 것: { problems: [문장], counts: { axis: 개수 }, skipped: 게임별이라 넘긴 개수 }
 */
export function compareProjects(projects, baseColumns, fileText = '') {
  const problems = []
  const counts = {}
  let skipped = 0
  const maps = projects.map((p) => new Map(p.rows.map((r) => [key(r), r])))
  const all = new Set(maps.flatMap((m) => [...m.keys()]))
  for (const k of [...all].sort()) {
    const sample = maps.find((m) => m.has(k)).get(k)
    if (isConsoleSetting(sample.axis, sample.name)) continue
    const have = maps.map((m) => m.get(k))
    const custom = customTableOf(sample)
    const missing = projects.filter((_, i) => !have[i]).map((p) => p.name)
    if (missing.length) {
      // 칸은 create table 에 적힌 것, 색인·제약은 install.sql 에 이름이 적힌 것이 SDK 몫이다.
      const own = sample.name.slice(custom?.length + 1)
      const base = custom && (sample.axis === 'column' ? baseColumns[custom]?.includes(own) : fileText.includes(own))
      if (custom && !base) { skipped++; continue }
      counts[sample.axis] = (counts[sample.axis] ?? 0) + 1
      problems.push(`${sample.axis} ${sample.name} — ${missing.join('·')} 에 없음`)
      continue
    }
    counts[sample.axis] = (counts[sample.axis] ?? 0) + 1
    const sig = (r) => `${r.body}|${r.meta}`
    const odd = projects.filter((_, i) => sig(have[i]) !== sig(have[0])).map((p) => p.name)
    if (odd.length) problems.push(`${sample.axis} ${sample.name} — ${odd.join('·')} 가 ${projects[0].name} 와 다름`)
  }
  for (const [table, cols] of Object.entries(baseColumns)) {
    for (const c of cols ?? []) {
      const lacking = projects.filter((_, i) => !maps[i].has(`column ${table}.${c}`)).map((p) => p.name)
      if (lacking.length === projects.length) problems.push(`column ${table}.${c} — install.sql 에 있는데 어느 게임에도 없음`)
    }
  }
  return { problems, counts, skipped }
}

/** 한 프로젝트의 DB 함수를 install.sql 과 대조. */
export function compareFunctionsToFile(rows, fileDefs) {
  const problems = []
  const file = new Map()
  for (const d of fileDefs) (file.get(d.name) ?? file.set(d.name, []).get(d.name)).push(d)
  const live = new Map()
  for (const r of rows.filter((r) => r.axis === 'function')) (live.get(r.owner_name) ?? live.set(r.owner_name, []).get(r.owner_name)).push(r)
  for (const [name, ls] of live) {
    const defs = file.get(name)
    if (!defs) { problems.push(`DB 함수 ${ls.map((l) => l.name).join(' · ')} — install.sql 에 없음(지운 함수가 남았거나 직접 만든 것)`); continue }
    for (const l of ls) if (!defs.some((d) => d.body === l.body)) problems.push(`DB 함수 ${l.name} — 본문이 install.sql ${defs.map((d) => `${d.line}줄`).join('·')} 과 다름`)
    if (defs.length > ls.length) problems.push(`DB 함수 ${name} — install.sql 에는 ${defs.length}개 정의, 서버에는 ${ls.length}개`)
  }
  for (const [name, defs] of file) if (!live.has(name)) problems.push(`DB 함수 ${name} — install.sql ${defs[0].line}줄에 있는데 서버에 없음`)
  return { problems, live: [...live.values()].flat().length, file: fileDefs.length }
}

/** 크론 이름 대조. */
export function compareCron(rows, names) {
  const live = new Set(rows.filter((r) => r.axis === 'cron').map((r) => r.name))
  return [
    ...names.filter((n) => !live.has(n)).map((n) => `자동 작업 ${n} — install.sql 에 있는데 서버에 없음`),
    ...[...live].filter((n) => !names.includes(n)).map((n) => `자동 작업 ${n} — 서버에만 있음`),
  ]
}

/**
 * 서버 함수(엣지 함수) 목록 대조. lists = [{ name, functions: [{ slug, ezbr_sha256, verify_jwt }] }], repoSlugs = 저장소의 이름들.
 */
export function compareEdgeLists(lists, repoSlugs, pick = null) {
  const problems = []
  const seen = (s) => !pick || pick.has(s)
  const repo = new Set(repoSlugs.filter(seen))
  for (const p of lists) {
    const slugs = new Set(p.functions.map((f) => f.slug).filter(seen))
    for (const s of repo) if (!slugs.has(s)) problems.push(`서버 함수 ${s} — ${p.name} 에 배포 안 됨`)
    for (const s of slugs) if (!repo.has(s)) problems.push(`서버 함수 ${s} — ${p.name} 에만 있음(저장소에 없음)`)
  }
  const first = new Map(lists[0].functions.map((f) => [f.slug, f]))
  for (const p of lists.slice(1)) {
    for (const f of p.functions) {
      const r = first.get(f.slug)
      if (!r || !seen(f.slug)) continue
      if (r.ezbr_sha256 !== f.ezbr_sha256) problems.push(`서버 함수 ${f.slug} — ${p.name} 코드가 ${lists[0].name} 와 다름`)
      if (r.verify_jwt !== f.verify_jwt) problems.push(`서버 함수 ${f.slug} — ${p.name} 로그인 검사 설정이 ${lists[0].name} 와 다름`)
    }
  }
  return problems
}

/** 내려받은 서버 함수 코드와 저장소 코드 비교 — 줄 끝(CRLF·공백)만 무시한다. 같으면 null, 다르면 첫 다른 줄 번호. */
export function firstDiffLine(a, b) {
  const la = a.replace(/\r/g, '').split('\n').map((l) => l.trimEnd())
  const lb = b.replace(/\r/g, '').split('\n').map((l) => l.trimEnd())
  while (la.length && la[la.length - 1] === '') la.pop()
  while (lb.length && lb[lb.length - 1] === '') lb.pop()
  const n = Math.max(la.length, lb.length)
  for (let i = 0; i < n; i++) if (la[i] !== lb[i]) return i + 1
  return null
}
