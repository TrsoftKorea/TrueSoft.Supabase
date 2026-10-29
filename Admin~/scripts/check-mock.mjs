// 가짜 데이터 규칙 검사(npm run check:mock). 규칙은 src/mock/mockAdmin.ts 맨 위에 적혀 있다.
// 가짜 데이터를 고치면 돌린다 — 화면만 봐서는 "기간을 바꾸면 같은 날짜 값이 바뀜" 같은 어긋남을 놓친다.
import fs from 'node:fs'
import os from 'node:os'
import path from 'node:path'
import { createRequire } from 'node:module'
import { fileURLToPath, pathToFileURL } from 'node:url'

const ADMIN = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const ts = createRequire(path.join(ADMIN, 'package.json'))('typescript')
const src = fs.readFileSync(path.join(ADMIN, 'src/mock/mockAdmin.ts'), 'utf8')
const js = ts.transpileModule(src, { compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 } }).outputText
const tmp = path.join(fs.mkdtempSync(path.join(os.tmpdir(), 'mockcheck-')), 'mockAdmin.mjs')
fs.writeFileSync(tmp, js)
const { mockAdmin } = await import(pathToFileURL(tmp).href)

const fail = []
const ok = (c, m) => { if (!c) fail.push(m) }
const call = (action, params = {}) => mockAdmin('defencer', action, params)
const byDay = (r) => Object.fromEntries(r.daily.map((d) => [d.day, d]))

// 1·2·3 일별 접속과 리텐션
const a30 = await call('activity.stats', { days: 30 })
const a90 = await call('activity.stats', { days: 90 })
const a365 = await call('activity.stats', { days: 365 })
const m90 = byDay(a90), m365 = byDay(a365)
for (const d of a30.daily) {
  ok(JSON.stringify(d) === JSON.stringify(m90[d.day]) && JSON.stringify(d) === JSON.stringify(m365[d.day]), `[1] 기간에 따라 값이 다름 ${d.day}`)
  ok(d.new + d.returning === d.total, `[2] 신규+재방문 != 전체 ${d.day}`)
  ok(d.comeback <= d.returning, `[3] 복귀 > 재방문 ${d.day}`)
}
for (const c of a365.retention) {
  ok(c.size === m365[c.day].new, `[3] 리텐션 가입 인원 != 그날 신규 ${c.day}`)
  let prev = c.size
  for (const k of c.kept) { if (k === null) continue; ok(k <= prev, `[3] 리텐션이 날이 갈수록 늘어남 ${c.day}`); prev = k }
}
for (const c of a30.retention) {
  ok(JSON.stringify(c) === JSON.stringify(a365.retention.find((x) => x.day === c.day)), `[1] 리텐션이 기간에 따라 다름 ${c.day}`)
}

// 2 가입 플랫폼별로 거른 값의 합 = 전체(일별 신규·재방문, 리텐션 가입 인원)
ok(Array.isArray(a30.signup_platforms) && a30.signup_platforms.length > 0, '[2] 가입 플랫폼 목록이 없음')
const parts = await Promise.all((a30.signup_platforms ?? []).map((p) => call('activity.stats', { days: 30, signupPlatform: p })))
a30.daily.forEach((d, i) => {
  ok(parts.reduce((s, r) => s + r.daily[i].new, 0) === d.new, `[2] 가입 플랫폼별 신규 합 != 전체 ${d.day}`)
  ok(parts.reduce((s, r) => s + r.daily[i].returning, 0) === d.returning, `[2] 가입 플랫폼별 재방문 합 != 전체 ${d.day}`)
})
a30.retention.forEach((c, i) => ok(parts.reduce((s, r) => s + r.retention[i].size, 0) === c.size, `[2] 가입 플랫폼별 리텐션 가입 인원 합 != 전체 ${c.day}`))

// 4 화면끼리 같은 숫자
const dash = await call('dashboard.stats')
const today = a30.daily[a30.daily.length - 1]
ok(dash.new_today === today.new && dash.active_today === today.total, '[4] 대시보드 오늘 값 != 통계 오늘 값')

// 4 플레이어 — 누적 결제액 = 구매 내역에서 매출로 세는 결제의 합, 환불 칸 = 환불된 결제, 대시보드 차단 수 = 차단 목록 수
const buys = (await call('purchases.list', { all: true })).rows
const listed = (await call('players.list', {})).rows
ok(listed.length > 0, '[4] 플레이어 목록이 비어 있음')
for (const id of [...new Set(buys.map((b) => b.account_id))]) {
  const pr = await call('players.profile', { accountId: id })
  const mine = buys.filter((b) => b.account_id === id)
  const paid = mine.filter((b) => !b.refunded_at && b.is_test !== true && !b.is_free).reduce((s, b) => s + (b.price_amount_krw ?? 0), 0)
  const refunded = mine.filter((b) => b.refunded_at)
  ok(pr.total_paid_krw === paid, `[4] 플레이어 누적 결제액 != 구매 내역 합 ${id}`)
  ok(pr.refund_count === refunded.length && pr.refund_krw === refunded.reduce((s, b) => s + (b.price_amount_krw ?? 0), 0), `[4] 플레이어 환불 != 구매 내역 ${id}`)
}
ok(dash.banned_now === (await call('players.list', { bannedOnly: true })).rows.length, '[4] 대시보드 차단 수 != 차단 목록 수')

// 2 대전 기록 — 요약의 승+패 합 = 전체 판 수, 게임 코드로 거른 판 수 = 그 코드 요약의 합
for (const pl of listed) {
  const mr = await call('matchRecords.player', { accountId: pl.account_id })
  ok(Array.isArray(mr.summary) && mr.summary.length > 0, `[2] 대전 기록 요약이 없음 ${pl.account_id}`)
  ok(mr.summary.reduce((s, g) => s + g.wins + g.losses, 0) === mr.total, `[2] 대전 기록 요약 합 != 전체 판 수 ${pl.account_id}`)
  for (const code of new Set(mr.summary.map((g) => g.game_code))) {
    const byCode = await call('matchRecords.player', { accountId: pl.account_id, gameCode: code })
    ok(byCode.total === mr.summary.filter((g) => g.game_code === code).reduce((s, g) => s + g.wins + g.losses, 0), `[2] 대전 기록 ${code} 판 수 != 요약 ${pl.account_id}`)
  }
}

// 1·2 국가별 매출 — 긴 기간이 짧은 기간보다 작을 수 없고, 나라별 합 = 합계, 결제자 ≤ 건수
const r30 = await call('revenue.stats', { days: 30 })
const r90 = await call('revenue.stats', { days: 90 })
const sumOf = (rows, k) => rows.reduce((s, r) => s + r[k], 0)
for (const r of [r30, r90]) {
  ok(sumOf(r.countries, 'revenue_krw') === r.total_krw && sumOf(r.countries, 'purchases') === r.purchases, `[2] 나라별 합 != 합계 (${r.days}일)`)
  for (const c of r.countries) ok(c.payers <= c.purchases, `[2] 결제자 > 건수 ${c.country}`)
  ok((r.unknown_amount ?? 0) <= r.purchases, `[2] 금액 모름 > 건수 (${r.days}일)`)
}
ok((r90.unknown_amount ?? 0) >= (r30.unknown_amount ?? 0), '[1] 90일 금액 모름이 30일보다 작음')
for (const c of r30.countries) {
  const long = r90.countries.find((x) => x.country === c.country)
  ok(long && long.revenue_krw >= c.revenue_krw && long.purchases >= c.purchases, `[1] 90일 매출이 30일보다 작음 ${c.country}`)
}

// 1·2 일일 채팅 — 같은 날짜는 기간이 달라도 같고, 종류별 합 = total
const ch30 = await call('chat.stats', { days: 30 })
const ch90 = await call('chat.stats', { days: 90 })
for (const d of ch30.daily) {
  ok(Object.values(d.by_kind).reduce((s, v) => s + v, 0) === d.total, `[2] 채팅 종류별 합 != total ${d.day}`)
  const long = ch90.daily.find((x) => x.day === d.day)
  ok(long && long.total === d.total, `[1] 채팅 30일·90일 값이 다름 ${d.day}`)
}

// 2·5 동시접속자
const c24 = await call('ccu.stats', { hours: 24 })
const c7 = await call('ccu.stats', { hours: 168 })
const sum = (o) => Object.values(o).reduce((s, v) => s + v, 0)
ok(sum(c24.now.by_server) === c24.now.total && sum(c24.now.by_platform) === c24.now.total, '[2] 서버별·플랫폼별 합 != 지금 접속자')
for (const c of [c24, c7]) ok(c.peak.total === Math.max(...c.series.map((p) => p.total)), `[5] 기간 최고치 != 그래프 최고 (${c.bucket_minutes}분 묶음)`)
const hourMax = Object.fromEntries(c7.series.map((p) => [p.at, p.total]))
for (const p of c24.series) {
  const hour = new Date(Math.floor(Date.parse(p.at) / 3600000) * 3600000).toISOString()
  if (hour in hourMax) ok(p.total <= hourMax[hour], `[5] 5분 값이 그 시간 최고치보다 큼 ${p.at}`)
}

fs.rmSync(path.dirname(tmp), { recursive: true, force: true })
if (fail.length) {
  console.log(`가짜 데이터 규칙 위반 ${fail.length}건`)
  for (const f of fail.slice(0, 20)) console.log('  ' + f)
  process.exit(1)
}
console.log(`가짜 데이터 규칙 통과 — 일별 ${a365.daily.length}일·리텐션 ${a365.retention.length}개·동시접속 ${c24.series.length + c7.series.length}점`)
