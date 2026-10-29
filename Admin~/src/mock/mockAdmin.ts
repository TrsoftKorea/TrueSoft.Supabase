// 가짜 데이터 모드 — 서버 대신 콘솔 요청에 답한다. 화면 모양을 확인하는 용도라 숫자는 그럴듯하게만 만든다.
// 일반 빌드에는 들어가지 않는다(lib/api 가 __MOCK__ 일 때만 이 파일을 불러온다).
// 새 화면을 가짜 데이터로 보려면 HANDLERS 에 그 화면의 action 을 더한다. 없는 action 은 빈 목록으로 답한다.
import type { ProjectTarget } from '../lib/projectTarget'

const MOCK_EMAIL = 'mock@local'

/** 로그인 표 모양만 갖춘 가짜 토큰. 서버로 나가지 않는다 — 만료 시각·이메일을 읽는 코드가 그대로 돌게 하려는 것. */
export function mockToken(): string {
  const payload = btoa(JSON.stringify({ email: MOCK_EMAIL, exp: Math.floor(Date.now() / 1000) + 24 * 3600 }))
  return `mock.${payload.replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '')}.mock`
}

// ── 가짜 데이터의 규칙 ──────────────────────────────────────────────────────
// 1. 같은 날짜·시각은 어느 기간으로 봐도 같은 값 — 값은 기간이 아니라 날짜·시각에서만 나온다(hash).
// 2. 나눈 값의 합 = 전체(신규+재방문 = 전체, 서버별·플랫폼별 합 = 지금 접속자).
// 3. 복귀 ≤ 재방문, 리텐션 가입 인원 = 그날 신규, 다시 온 사람 ≤ 가입 인원이고 날이 갈수록 줄어든다.
// 4. 같은 숫자는 화면끼리 같다(대시보드 오늘 신규·활동 = 통계의 오늘 값). 매출은 긴 기간 합 ≥ 짧은 기간 합.
// 5. 동시접속자 긴 기간의 점 = 그 구간 5분 값의 최고치, 기간 최고치 = 그래프의 최고 점.

/** 문자열 → [0,1) 고정 난수. 같은 입력이면 언제나 같은 값. */
function hash(key: string): number {
  let h = 2166136261
  for (let i = 0; i < key.length; i++) h = Math.imul(h ^ key.charCodeAt(i), 16777619)
  return ((h >>> 0) % 100000) / 100000
}

const kstToday = () => new Date(Date.now() + 9 * 3600_000).toISOString().slice(0, 10)
const addDays = (iso: string, n: number) => new Date(Date.parse(iso) + n * 86400_000).toISOString().slice(0, 10)

// ── 일별 접속 ──
function dayStats(day: string) {
  const isNew = Math.round(20 + hash(`new:${day}`) * 25)
  const returning = Math.round(140 + hash(`ret:${day}`) * 60)
  const comeback = Math.min(returning, Math.round(hash(`cb:${day}`) * 9))
  return { day, total: isNew + returning, new: isNew, returning, comeback }
}

/** 가입일 day 의 신규 중 n 일째에 다시 온 사람. n 이 커질수록 줄어든다(앞 날 값을 넘지 않게 누적 최소). */
function keptOf(day: string, size: number, n: number, offsets: number[]): number {
  let kept = size
  for (const o of offsets) {
    if (o > n) break
    const rate = Math.max(0.05, 0.55 * Math.pow(o, -0.35) + (hash(`kept:${day}:${o}`) - 0.5) * 0.06)
    kept = Math.min(kept, Math.round(size * rate))
  }
  return kept
}

// 가입 플랫폼별 몫. 같은 숫자를 같은 몫으로 나누므로 플랫폼별 값을 다 더하면 전체와 같다(규칙 2).
// Windows 가입자는 두지 않는다 — 기록이 없는 플랫폼 버튼이 빠지는 모습을 보이려는 것.
const SIGNUP_SHARE: [string, number][] = [['android', 0.64], ['ios', 0.36]]

function activityStats(days: number, signupPlatform = '') {
  const today = kstToday()
  const daily = Array.from({ length: days }, (_, i) => dayStats(addDays(today, i - days + 1)))
  const offsets = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 21, 30, 45, 60]
  const retention = [...daily].reverse().map((d, age) => ({
    day: d.day,
    size: d.new,
    kept: offsets.map((o) => (o >= age ? null : keptOf(d.day, d.new, o, offsets))),
  }))
  const first_day = daily[0]?.day ?? null
  // 실제 서버처럼 이 기간에 기록이 있는 가입 플랫폼만 — 거르기와 상관없이 같다.
  const signup_platforms = SIGNUP_SHARE.map(([k]) => k).filter((k) => daily.some((d) => (split(d.total, SIGNUP_SHARE)[k] ?? 0) > 0))
  if (!signupPlatform) return { today, daily, offsets, retention, first_day, signup_platforms }
  const part = (n: number) => split(n, SIGNUP_SHARE)[signupPlatform] ?? 0
  return {
    today, offsets, first_day, signup_platforms,
    daily: daily.map((d) => {
      const isNew = part(d.new), returning = part(d.returning)
      return { day: d.day, total: isNew + returning, new: isNew, returning, comeback: Math.min(part(d.comeback), returning) }
    }),
    retention: retention.map((c) => {
      const size = part(c.size)
      return { day: c.day, size, kept: c.kept.map((k) => (k === null ? null : Math.min(part(k), size))) }
    }),
  }
}

// ── 국가별 매출 ── 하루·나라별 결제는 날짜에서만 나온다(규칙 1) → 긴 기간 합은 짧은 기간 합보다 작아질 수 없다.
const COUNTRIES: [string, number][] = [['KR', 1], ['JP', 0.35], ['US', 0.25], ['TW', 0.15], ['DE', 0.06], ['unknown', 0.03]]
const PRICES = [1100, 3300, 5500, 9900, 33000, 119000]

function revenueStats(days: number) {
  const today = kstToday()
  const rows = COUNTRIES.map(([country, weight]) => {
    let purchases = 0
    let krw = 0
    for (let i = 0; i < days; i++) {
      const day = addDays(today, -i)
      const count = Math.floor(hash(`pay:${country}:${day}`) * 6 * weight)
      for (let k = 0; k < count; k++) krw += PRICES[Math.floor(hash(`price:${country}:${day}:${k}`) * PRICES.length)]!
      purchases += count
    }
    // 결제자는 건수보다 적다(한 사람이 여러 번 산다).
    return { country, revenue_krw: krw, purchases, payers: Math.ceil(purchases * 0.6) }
  }).filter((r) => r.purchases > 0).sort((a, b) => b.revenue_krw - a.revenue_krw)
  // 금액 모름 — 구매 내역 표본의 40일 전 옛 방식 iOS 결제 한 건. 실제 SQL 처럼 건수에는 들어가고 금액은 0원이다.
  const unknownAmount = days > 40 ? 1 : 0
  const kr = rows.find((r) => r.country === 'KR')
  if (kr) kr.purchases += unknownAmount
  return {
    days,
    total_krw: rows.reduce((s, r) => s + r.revenue_krw, 0),
    purchases: rows.reduce((s, r) => s + r.purchases, 0),
    payers: rows.reduce((s, r) => s + r.payers, 0),
    unknown_amount: kr ? unknownAmount : 0,
    countries: rows,
  }
}

// ── 테스트 결제 ── 하루 건수·금액은 날짜에서만 나오고, 합계는 날짜별 합이다(규칙 1·2).
const TEST_PRODUCTS = ['gem_pack_small', 'gem_pack_large', 'starter_bundle']
function testDay(day: string) {
  const count = Math.floor(hash(`test:${day}`) * 4)
  const items = Array.from({ length: count }, (_, k) => {
    const apple = hash(`test-store:${day}:${k}`) < 0.5
    return {
      verified_at: `${day}T${String(10 + k).padStart(2, '0')}:00:00+09:00`,
      store: apple ? 'apple_app_store' : 'google_play',
      product_id: TEST_PRODUCTS[Math.floor(hash(`test-prod:${day}:${k}`) * TEST_PRODUCTS.length)]!,
      price_amount_krw: PRICES[Math.floor(hash(`test-price:${day}:${k}`) * 4)]!,
      display_name: `QA_${(k + 1).toString().padStart(2, '0')}`,
      account_id: null,
    }
  })
  return { day, count, krw: items.reduce((s, x) => s + x.price_amount_krw, 0), items }
}

function testPurchaseStats(days: number) {
  const today = kstToday()
  const list = Array.from({ length: days }, (_, i) => testDay(addDays(today, i - days + 1)))
  const all = list.flatMap((d) => d.items)
  const by_store: Record<string, number> = {}
  for (const x of all) by_store[x.store] = (by_store[x.store] ?? 0) + 1
  return {
    daily: list.map(({ day, count, krw }) => ({ day, count, krw })),
    by_store,
    total_count: all.length,
    total_krw: all.reduce((s, x) => s + x.price_amount_krw, 0),
    recent: [...all].reverse().slice(0, 50),
    unknown_count: 4,
  }
}

// ── 일일 채팅 ── 종류별 개수는 날짜에서만 나오고 total 은 그 합이다.
const CHAT_KINDS: [string, number][] = [['global', 900], ['server', 500], ['group', 160], ['direct', 220]]

function chatStats(days: number) {
  const today = kstToday()
  const daily = Array.from({ length: days }, (_, i) => {
    const day = addDays(today, i - days + 1)
    const by_kind = Object.fromEntries(CHAT_KINDS.map(([k, base]) => [k, Math.round(base * (0.6 + hash(`chat:${k}:${day}`) * 0.8))]))
    return { day, total: Object.values(by_kind).reduce((s, v) => s + v, 0), by_kind }
  })
  return { daily, first_day: daily[0]?.day ?? null }
}

// ── 서비스 연결 켜기/끄기 ── Sign in with Apple 은 켜 두고 값을 비워, 필수 값이 빠진 모습(빨간 없음)이 보이게 한다.
let mockServices: Record<string, boolean> = { signin: true }

// ── 게임 시간대 ── 화면에서 바꿔 볼 수 있게 메모리에만 둔다(새로고침하면 처음 값).
let gameSettings = {
  time_zone: 'Asia/Seoul', updated_at: null as string | null, updated_by: null as string | null,
  example_deadline: addDays(kstToday(), 8) + 'T00:00:00+09:00',
}

// ── 동시접속자 ──
const SLOT = 5 * 60_000

/** 5분 칸 하나의 접속자. 칸 시작 시각에서만 나온다. */
function ccuAt(slotStart: number): number {
  const h = new Date(slotStart + 9 * 3600_000).getUTCHours() + new Date(slotStart).getUTCMinutes() / 60
  return Math.max(0, Math.round(45 + 35 * Math.cos(((h - 21) * Math.PI) / 12) + (hash(`ccu:${slotStart}`) - 0.5) * 12))
}

/** 합이 total 이 되게 비율대로 나눈다(남는 1명은 첫 칸으로). */
function split(total: number, parts: [string, number][]): Record<string, number> {
  const out: Record<string, number> = {}
  let left = total
  parts.forEach(([k, ratio], i) => {
    const v = i === parts.length - 1 ? left : Math.floor(total * ratio)
    out[k] = v
    left -= v
  })
  return out
}

function ccuStats(hours: number) {
  const bucketMin = hours <= 24 ? 5 : hours <= 24 * 7 ? 60 : hours <= 24 * 90 ? 240 : 1440
  const bucket = bucketMin * 60_000
  const nowSlot = Math.floor(Date.now() / SLOT) * SLOT
  const from = nowSlot - hours * 3600_000
  const series: { at: string; total: number }[] = []
  let peak = { at: new Date(nowSlot).toISOString(), total: -1 }
  for (let b = Math.floor(from / bucket) * bucket; b <= nowSlot; b += bucket) {
    let best = -1
    for (let s = Math.max(b, from); s < b + bucket && s <= nowSlot; s += SLOT) {
      const v = ccuAt(s)
      if (v > best) best = v
      if (v > peak.total) peak = { at: new Date(s).toISOString(), total: v }
    }
    if (best >= 0) series.push({ at: new Date(b).toISOString(), total: best })
  }
  const total = ccuAt(nowSlot)
  return {
    now: {
      total,
      by_server: split(total, [['GLOBAL', 0.66], ['KR', 0.34]]),
      by_platform: split(total, [['android', 0.64], ['ios', 0.34], ['windows', 0.02]]),
    },
    bucket_minutes: bucketMin,
    series,
    peak,
    first_at: series[0]?.at ?? null,
  }
}

// ── 구매 내역 ── 표시 종류(원화·외화·시험·무료·환불·가격 없음)가 한 화면에 다 보이게 고른 표본. 통계 숫자와는 따로 논다.
function purchaseRows() {
  const at = (daysAgo: number, hm: string) => `${addDays(kstToday(), -daysAgo)}T${hm}:00+09:00`
  const row = (id: number, o: Partial<Record<string, unknown>>) => ({
    id: String(id), account_id: `a${id}`, user_id: `10${id}482913`, product_id: 'gem_pack_1', order_id: `GPA.33${id}-1234-5678`,
    package_name: 'com.truesoft.mock', store: 'google_play', price_amount: 5500_000000, price_currency: 'KRW', price_amount_krw: 5500,
    verified_at: at(0, '10:12'), refunded_at: null, is_test: false, is_free: false, ...o,
  })
  return [
    row(9, {}),
    row(8, { store: 'apple_app_store', product_id: 'gem_pack_3', order_id: '2000000812345678', price_amount: 9_990000, price_currency: 'USD', price_amount_krw: 14280, verified_at: at(0, '09:41') }),
    row(7, { is_test: true, verified_at: at(1, '18:05') }),
    row(6, { product_id: 'starter_pack', is_free: true, verified_at: at(1, '13:20') }),
    row(5, { product_id: 'gem_pack_2', price_amount: 11000_000000, price_amount_krw: 11000, refunded_at: at(0, '08:30'), verified_at: at(2, '21:47') }),
    row(4, { store: 'apple_app_store', order_id: '2000000798765432', price_amount: null, price_currency: null, price_amount_krw: null, is_test: null, verified_at: at(40, '11:02') }),
  ]
}

// ── 플레이어 ── 구매 내역 표본(a4~a9)과 같은 사람이다 — 누적 결제액은 그 표본에서 매출로 세는 결제만 더한 값(규칙 4).
// 화면 종류가 다 보이게 고른다: 게스트, 차단(기한·영구), 탈퇴 예약, 가입 플랫폼과 최근 플랫폼이 다른 계정.
// 탈퇴 예약 계정은 실제 서버처럼 목록에서 빠진다 — 구매 내역에서 눌러 들어간다.
const PLAYERS = (() => {
  const at = (daysAgo: number, hm: string) => `${addDays(kstToday(), -daysAgo)}T${hm}:00+09:00`
  const p = (account_id: string, display_name: string, o: Partial<{
    is_guest: boolean; platform: string; signup_platform: string; country_code: string; total_paid_krw: number
    refund_count: number; refund_krw: number; created: number; active: string; server_code: string
    withdrawn_at: string | null; banned_until: string | null; ban_message: string | null
  }>) => ({
    account_id, display_name, is_guest: false, platform: 'android', signup_platform: 'android', country_code: 'KR',
    total_paid_krw: 0, refund_count: 0, refund_krw: 0, created: 30, active: at(0, '09:00'), server_code: 'GLOBAL',
    withdrawn_at: null as string | null, banned_until: null as string | null, ban_message: null as string | null, ...o,
  })
  return [
    p('a9', '용사김', { total_paid_krw: 5500, created: 62, active: at(0, '10:12') }),
    p('a8', '마법사이', { platform: 'ios', signup_platform: 'ios', country_code: 'US', total_paid_krw: 14280, created: 41, active: at(1, '21:40') }),
    // 아이폰으로 가입했다가 안드로이드로 옮긴 계정 — 가입 플랫폼과 최근 플랫폼이 다르게 보인다.
    p('a7', '궁수박', { signup_platform: 'ios', created: 120, active: at(3, '08:05'), server_code: 'KR' }),
    p('a6', '도적최', { created: 15, active: at(2, '13:20'),
      banned_until: at(-7, '00:00'), ban_message: '욕설 반복으로 7일 동안 이용이 제한되었습니다.' }),
    p('a5', '성기사정', { country_code: 'JP', refund_count: 1, refund_krw: 11000, created: 9, active: at(2, '22:10') }),
    p('a4', '사냥꾼윤', { platform: 'ios', signup_platform: 'ios', created: 200, active: at(5, '11:02'), withdrawn_at: at(-5, '00:00') }),
    p('a3', 'Guest_7712', { is_guest: true, platform: 'windows', signup_platform: 'windows', country_code: 'KR', created: 2, active: at(0, '07:31') }),
    p('a2', '암살자한', { created: 70, active: at(20, '19:45'),
      banned_until: `${new Date().getFullYear() + 100}-01-01T00:00:00+09:00`, ban_message: '부정 결제가 확인되어 영구 이용 정지되었습니다.' }),
  ].map(({ created, active, ...r }) => ({ ...r, created_at: at(created, '12:00'), last_activity_at: active, last_sign_in_at: active }))
})()

const isBannedNow = (pl: (typeof PLAYERS)[number]) => !!pl.banned_until && Date.parse(pl.banned_until) > Date.now()

function playerList(p: Record<string, unknown>) {
  const search = String(p['search'] ?? '').trim()
  const rows = PLAYERS
    .filter((pl) => !pl.withdrawn_at && (!search || pl.display_name.includes(search)) && (!p['bannedOnly'] || isBannedNow(pl)))
    .sort((a, b) => b.last_activity_at.localeCompare(a.last_activity_at))
    .map(({ account_id, display_name, is_guest, platform, country_code, total_paid_krw, created_at, last_activity_at }) =>
      ({ account_id, display_name, is_guest, platform, country_code, total_paid_krw, created_at, last_activity_at }))
  return { rows, total: rows.length, pageSize: 20 }
}

// ── 대전 기록 ── 플레이어마다 고정된 판 기록. 요약은 이 기록을 게임 코드·상태별로 묶은 값이다(규칙 2).
// 상태가 다 보이게 섞는다: 확인됨·상대 미신고·상대 없음(혼자 하는 판)·무효, 지워진 상대 하나.
// pvp_1v1 은 매치 보상 화면의 게임 코드와 같다.
type MockMatch = {
  session_id: string; game_code: string; is_win: boolean; score: number; status: 'verified' | 'pending' | 'solo' | 'mismatch'
  rewarded: boolean; opponent_account_id: string | null; opponent_exists: boolean; opponent_name: string
  reported_at: string; resolved_at: string | null
}

function matchesOf(accountId: string): MockMatch[] {
  const others = PLAYERS.filter((x) => x.account_id !== accountId && !x.withdrawn_at)
  const count = 6 + Math.floor(hash(`mr:${accountId}`) * 20)
  return Array.from({ length: count }, (_, i) => {
    const h = hash(`mr:${accountId}:${i}`)
    const solo = i % 5 === 4
    const status: MockMatch['status'] = solo ? 'solo' : i === 0 ? 'pending' : i === 3 ? 'mismatch' : 'verified'
    const is_win = h > 0.45
    const opp = solo ? null : i === 2 ? null : others[i % others.length]!
    const reported_at = `${addDays(kstToday(), -i)}T${String(9 + (i % 12)).padStart(2, '0')}:${String(Math.floor(h * 60)).padStart(2, '0')}:00+09:00`
    return {
      session_id: `${accountId}-m${i}`, game_code: solo ? 'stage_clear' : 'pvp_1v1', is_win, score: Math.round(800 + h * 1200), status,
      rewarded: status === 'verified' && is_win,
      // i === 2 는 탈퇴로 지워진 상대 — 실제 서버도 계정은 없고 번호만 남는다.
      opponent_account_id: solo ? null : opp ? opp.account_id : 'a1',
      opponent_exists: !!opp, opponent_name: opp ? opp.display_name : '',
      reported_at, resolved_at: status === 'pending' ? null : reported_at,
    }
  })
}

function matchRecords(p: Record<string, unknown>) {
  const all = matchesOf(String(p['accountId'] ?? ''))
  const code = String(p['gameCode'] ?? '').trim()
  const groups = new Map<string, { game_code: string; status: string; wins: number; losses: number; max_score: number; min_score: number }>()
  for (const m of all) {
    const key = `${m.game_code}|${m.status}`
    const g = groups.get(key) ?? { game_code: m.game_code, status: m.status, wins: 0, losses: 0, max_score: m.score, min_score: m.score }
    if (m.is_win) g.wins++; else g.losses++
    g.max_score = Math.max(g.max_score, m.score); g.min_score = Math.min(g.min_score, m.score)
    groups.set(key, g)
  }
  const summary = [...groups.values()].sort((a, b) => a.game_code.localeCompare(b.game_code) || a.status.localeCompare(b.status))
  const filtered = code ? all.filter((m) => m.game_code === code) : all
  const pageSize = 20
  const page = Math.max(1, Number(p['page'] ?? 1))
  return { summary, rows: filtered.slice((page - 1) * pageSize, page * pageSize), total: filtered.length, pageSize }
}

function playerOf(p: Record<string, unknown>) {
  const pl = PLAYERS.find((x) => x.account_id === p['accountId'])
  if (!pl) throw new Error('player_not_found')
  return pl
}

// ── 자동 작업 ── 설치 SQL 에 등록된 이름·주기 그대로(시각은 UTC). 하나는 실패 상태를 보여 준다.
const CRON_SCHEDULES: [string, string][] = [
  ['run-mail-schedules', '* * * * *'], ['deliver-open-mail-batches', '* * * * *'], ['ts_leaderboard_rotate_due', '* * * * *'],
  ['ccu-snapshot', '*/5 * * * *'], ['ts_match_lobby_cleanup', '*/5 * * * *'], ['withdrawal-cleanup', '0,30 * * * *'],
  ['withdrawal-overdue-check', '15 * * * *'], ['ts_chat_cleanup', '17 * * * *'], ['cleanup-expired-mails', '0 3 * * *'],
  ['cleanup-user-data-logs', '0 3 * * *'], ['cleanup-cron-run-details', '40 3 * * *'], ['purchase-refund-sync-google', '0 4 * * *'],
  ['ts_friend_request_cleanup', '20 4 * * *'], ['purchase-price-sync', '30 4 * * *'],
]

/** 분·시 칸만 읽어 마지막 실행 시각과 하루 실행 횟수를 낸다(여기 쓰는 꼴만). */
function cronTimes(schedule: string, now: number): { last: number; prev: number; perDay: number } {
  const [min = '*', hour = '*'] = schedule.split(' ')
  const step = min === '*' ? 1 : min.startsWith('*/') ? Number(min.slice(2)) : 0
  if (step) {
    const last = Math.floor(now / (step * 60_000)) * step * 60_000
    return { last, prev: last - step * 60_000, perDay: 1440 / step }
  }
  const mins = min.split(',').map(Number)
  const hits: number[] = []
  const base = Math.floor(now / 60_000) * 60_000
  for (let back = 0; hits.length < 2 && back < 3 * 1440; back++) {
    const t = new Date(base - back * 60_000)
    if (mins.includes(t.getUTCMinutes()) && (hour === '*' || Number(hour) === t.getUTCHours())) hits.push(t.getTime())
  }
  return { last: hits[0] ?? now, prev: hits[1] ?? now, perDay:hour === '*' ? mins.length * 24 : 1 }
}

function cronJobs() {
  const now = Date.now()
  const iso = (ms: number) => new Date(ms).toISOString()
  return CRON_SCHEDULES.map(([jobname, schedule]) => {
    const { last, prev, perDay } = cronTimes(schedule, now)
    const failed = jobname === 'withdrawal-overdue-check'
    const message = 'ERROR:  withdrawal_overdue: 탈퇴 유예가 2시간 넘게 지났는데 지워지지 않은 계정 1개 — withdrawal-sweep 배포·로그를 확인하세요'
    return {
      jobname, schedule, active: true,
      last_start: iso(last), last_end: iso(last + 400),
      last_status: failed ? 'failed' : 'succeeded', last_message: failed ? message : '1 row',
      last_success: iso(failed ? prev : last),
      runs_24h: perDay, failures_24h: failed ? 1 : 0, last_failure_message: failed ? message : null,
    }
  })
}

function purchaseList(p: Record<string, unknown>) {
  let rows = purchaseRows()
  if (p['refundedOnly']) rows = rows.filter((r) => r.refunded_at)
  if (p['test'] === 'only') rows = rows.filter((r) => r.is_test === true)
  else if (p['test'] === 'exclude') rows = rows.filter((r) => r.is_test !== true)
  if (p['all']) return { rows, total: rows.length, pageSize: rows.length, truncated: false }
  // 쪽을 실제로 나눠 돌려준다 — 표 바닥 줄의 쪽 넘김을 가짜 데이터로도 눌러 볼 수 있게.
  const pageSize = 4
  const page = Math.max(1, Number(p['page'] ?? 1))
  return { rows: rows.slice((page - 1) * pageSize, page * pageSize), total: rows.length, pageSize, truncated: false }
}

type Handler = (target: ProjectTarget, params: Record<string, unknown>) => unknown

const HANDLERS: Record<string, Handler> = {
  'auth.consoleLook': () => ({ color: null, display_name: null, icon: null }),
  'session.me': () => ({ email: MOCK_EMAIL, isMaster: true }),
  'schema.getDraft': () => ({ rows: [] }),
  'dashboard.stats': () => {
    const today = kstToday()
    const week = Array.from({ length: 7 }, (_, i) => dayStats(addDays(today, -i)))
    return {
      total_players: 12840, new_today: week[0]!.new, new_7d: week.reduce((s, d) => s + d.new, 0),
      active_today: week[0]!.total, banned_now: PLAYERS.filter(isBannedNow).length,
      purchases_today: 23, revenue_today_krw: 412000, revenue_7d_krw: 3289000, revenue_total_krw: 48120000,
    }
  },
  'ccu.stats': (_t, p) => ccuStats(Number(p['hours'] ?? 24)),
  'activity.stats': (_t, p) => activityStats(Number(p['days'] ?? 30), String(p['signupPlatform'] ?? '')),
  'revenue.stats': (_t, p) => revenueStats(Number(p['days'] ?? 30)),
  'testPurchases.stats': (_t, p) => testPurchaseStats(Number(p['days'] ?? 30)),
  'chat.stats': (_t, p) => chatStats(Number(p['days'] ?? 30)),
  // 서비스 연결 — 몇 개는 등록됨·몇 개는 없음으로 보여 입력·바꾸기 버튼이 둘 다 보이게 한다.
  'settings.getStoreStatus': () => ({
    rows: [
      { key: 'APPLE_BUNDLE_ID', source: 'console', status: 'ok', updatedAt: addDays(kstToday(), -3) + 'T10:00:00+09:00', updatedBy: 'mock@local' },
      { key: 'APPLE_SHARED_SECRET', source: 'supabase', status: 'ok', updatedAt: null, updatedBy: null },
      { key: 'GOOGLE_PACKAGE_NAME', source: 'console', status: 'ok', updatedAt: addDays(kstToday(), 0) + 'T09:00:00+09:00', updatedBy: 'mock@local' },
      { key: 'GOOGLE_SERVICE_ACCOUNT_JSON', source: 'console', status: 'ok', updatedAt: addDays(kstToday(), -10) + 'T15:30:00+09:00', updatedBy: 'mock@local' },
      { key: 'APPLE_TEAM_ID', source: null, status: 'missing', updatedAt: null, updatedBy: null },
      { key: 'APPLE_KEY_ID', source: null, status: 'missing', updatedAt: null, updatedBy: null },
      { key: 'APPLE_PRIVATE_KEY', source: null, status: 'missing', updatedAt: null, updatedBy: null },
      { key: 'APPLE_IAP_ISSUER_ID', source: null, status: 'missing', updatedAt: null, updatedBy: null },
      { key: 'APPLE_IAP_KEY_ID', source: null, status: 'missing', updatedAt: null, updatedBy: null },
      { key: 'APPLE_IAP_PRIVATE_KEY', source: null, status: 'missing', updatedAt: null, updatedBy: null },
    ],
    services: mockServices,
  }),
  'settings.setServiceEnabled': (_t, p) => {
    mockServices = { ...mockServices, [String(p['service'])]: p['enabled'] === true }
    return { services: mockServices }
  },
  // 데이터 관리 — 목록(배열)을 받는 화면이라 기본 응답(묶음)으로는 화면이 멈춘다.
  'players.list': (_t, p) => playerList(p),
  'players.profile': (_t, p) => {
    const { banned_until: _b, ban_message: _m, ...profile } = playerOf(p)
    return profile
  },
  'players.banInfo': (_t, p) => {
    const pl = playerOf(p)
    const banned = isBannedNow(pl)
    return { isBanned: banned, bannedUntil: banned ? pl.banned_until : null, banMessage: banned ? pl.ban_message : null }
  },
  'matchRecords.player': (_t, p) => matchRecords(p),
  'dataManagement.searchPlayers': () => [
    { account_id: 'a9', display_name: '용사김', updated_at: addDays(kstToday(), 0) + 'T10:12:00+09:00' },
    { account_id: 'a8', display_name: '마법사이', updated_at: addDays(kstToday(), -1) + 'T21:40:00+09:00' },
    { account_id: 'a7', display_name: '궁수박', updated_at: addDays(kstToday(), -3) + 'T08:05:00+09:00' },
  ],
  'userData.columns': () => [
    { column_name: 'gold', data_type: 'bigint', is_nullable: 'NO', column_default: '0' },
    { column_name: 'level', data_type: 'integer', is_nullable: 'NO', column_default: '1' },
    { column_name: 'inventory', data_type: 'jsonb', is_nullable: 'YES', column_default: null },
  ],
  // 쿠폰·매치 보상 — 이것도 목록(배열)을 받는 화면이다. 아이템 이름은 두 화면이 같이 쓴다.
  'gameItems.list': () => [
    { key: 'gem', display_name: '보석' },
    { key: 'gold', display_name: '골드' },
    { key: 'ticket', display_name: '입장권' },
  ],
  'coupons.list': () => [
    {
      id: 'c2', kind: 'keyword', title: '오픈 기념', content: '오픈을 축하합니다.', category: 'default',
      items: [{ key: 'gem', count: 100 }], expires_at: addDays(kstToday(), 14) + 'T23:59:00+09:00', mail_expires_days: 7,
      max_uses: null, used_count: 312, is_active: true, created_at: addDays(kstToday(), -6) + 'T10:00:00+09:00',
      code: 'WELCOME2026', issued_count: 1,
    },
    {
      id: 'c1', kind: 'normal', title: '사전 예약 보상', content: '사전 예약 감사 선물입니다.', category: 'default',
      items: [{ key: 'gold', count: 5000 }, { key: 'ticket', count: 3 }], expires_at: null, mail_expires_days: 30,
      max_uses: 1, used_count: 87, is_active: true, created_at: addDays(kstToday(), -20) + 'T09:00:00+09:00',
      code: null, issued_count: 200,
    },
  ],
  'coupons.codes': () => [],
  'matchRewards.list': () => [
    {
      game_code: 'pvp_1v1', record_count: 1240, player_count: 380, pending_count: 4,
      last_reported_at: addDays(kstToday(), 0) + 'T12:40:00+09:00',
      config: {
        mode: 'competitive', win_items: [{ key: 'gem', count: 10 }], mail_title: '승리 보상', mail_content: '승리를 축하합니다.',
        mail_expires_days: 7, updated_at: addDays(kstToday(), -2) + 'T15:00:00+09:00', updated_by: 'mock@local',
      },
    },
  ],
  // 우편 › 예약 목록 — 켜진 반복 발송 하나, 꺼진 예약 발송 하나. 아이템 키는 아이템 카탈로그와 같다.
  'mails.getSchedules': () => ({
    rows: [
      {
        id: 'ms1', schedule_type: 'repeat', target_mode: 'all', title: '매일 출석 보상', items: [{ key: 'gold', count: 1000 }], category: 'default',
        scheduled_at: null, repeat_time: '09:00:00', repeat_tz: 'Asia/Seoul', repeat_unit: 'day', repeat_dow: null, repeat_dom: null,
        next_run_at: addDays(kstToday(), 1) + 'T09:00:00+09:00', is_active: true, run_count: 12,
        created_by: MOCK_EMAIL, server_code: null, platform: null, open_for_new: false,
      },
      {
        id: 'ms2', schedule_type: 'scheduled', target_mode: 'server', title: '점검 보상', items: [{ key: 'gem', count: 50 }], category: 'default',
        scheduled_at: addDays(kstToday(), 3) + 'T12:00:00+09:00', repeat_time: null, repeat_tz: null, repeat_unit: null, repeat_dow: null, repeat_dom: null,
        next_run_at: addDays(kstToday(), 3) + 'T12:00:00+09:00', is_active: false, run_count: 0,
        created_by: MOCK_EMAIL, server_code: 'KR', platform: null, open_for_new: true,
      },
    ],
  }),
  'remoteConfig.list': () => ({
    rows: [
      { key: 'game', value_json: { stamina: { max: 120, regen_seconds: 300 }, notice_url: 'https://example.com/notice' }, enabled: true, requires_auth: false, description: null, version: 3 },
    ],
  }),
  // 서버 코드는 동시접속자 서버별 나눔(GLOBAL·KR)과 같다.
  'servers.list': () => [
    { id: 's1', server_code: 'GLOBAL', display_name: '글로벌', allow_transfers: true, is_default: true, player_count: 8421, created_at: addDays(kstToday(), -90) + 'T10:00:00+09:00' },
    { id: 's2', server_code: 'KR', display_name: '한국', allow_transfers: false, is_default: false, player_count: 4310, created_at: addDays(kstToday(), -60) + 'T10:00:00+09:00' },
  ],
  'bannedWords.list': () => [
    { kind: 'word', word: '바보', created_at: addDays(kstToday(), -10) + 'T11:00:00+09:00', created_by: MOCK_EMAIL },
    { kind: 'word', word: '광고', created_at: addDays(kstToday(), -3) + 'T15:20:00+09:00', created_by: MOCK_EMAIL },
    { kind: 'name', word: '운영자', created_at: addDays(kstToday(), -30) + 'T09:00:00+09:00', created_by: null },
  ],
  'jobs.list': () => cronJobs(),
  // 마스터(MOCK_EMAIL)는 목록에 나오지 않는다 — 화면 안내와 같게.
  'operators.list': () => [
    { email: 'ops1@local', display_name: '운영자1', disabled_at: null, created_at: addDays(kstToday(), -40) + 'T10:00:00+09:00', created_by: MOCK_EMAIL, hasPassword: true },
    { email: 'ops2@local', display_name: '운영자2', disabled_at: addDays(kstToday(), -5) + 'T10:00:00+09:00', created_at: addDays(kstToday(), -20) + 'T10:00:00+09:00', created_by: MOCK_EMAIL, hasPassword: false },
  ],
  // 쿠폰 코드는 쿠폰 목록의 '오픈 기념'과 같다.
  'players.coupons': () => [
    { code: 'WELCOME2026', kind: 'keyword', title: '오픈 기념', mail_id: 'm1', redeemed_at: addDays(kstToday(), -2) + 'T20:15:00+09:00' },
  ],
  // 로그는 바뀌기 전 값만 남는다 — 칸 이름은 유저 데이터 칸 목록과 같다.
  'userData.logs': () => [
    { id: 2, diff: { level: 11 }, source: 'client', created_at: addDays(kstToday(), 0) + 'T09:12:00+09:00' },
    { id: 1, diff: { inventory: { gem: 40 } }, source: 'admin', created_at: addDays(kstToday(), -1) + 'T18:40:00+09:00' },
  ],
  'userData.getRestorePreview': () => [
    { column: 'level', data_type: 'integer', current: 12, restored: 11 },
  ],
  'purchases.list': (_t, p) => purchaseList(p),
  // 구글은 옛 결제 재확인 결과가 붙은 정상 완료, 애플은 받은 알림 있음.
  'purchases.getRefundStatus': () => ({
    rows: [
      { store: 'google_play', last_started_at: addDays(kstToday(), 0) + 'T13:00:02+09:00', last_finished_at: addDays(kstToday(), 0) + 'T13:00:19+09:00',
        last_ok: true, last_message: '시험 결제 확인 12건(그중 시험 3건), 구글이 답하지 않은 2건', last_marked: 1 },
      { store: 'apple_app_store', last_started_at: addDays(kstToday(), -2) + 'T04:11:40+09:00', last_finished_at: addDays(kstToday(), -2) + 'T04:11:40+09:00',
        last_ok: true, last_message: null, last_marked: 1 },
      { store: 'price_sync', last_started_at: addDays(kstToday(), 0) + 'T13:30:01+09:00', last_finished_at: addDays(kstToday(), 0) + 'T13:30:06+09:00',
        last_ok: true, last_message: '환율 170개 통화 / 애플 결제 금액 확인 1건 / 원화 환산 1건', last_marked: 1 },
    ],
  }),
  'purchases.syncRefunds': () => ({ skipped: 'recently_run' }),
  // 설정 › 우편·친구·매치 로비 — 저장하면 받은 값을 그대로 돌려준다(새로 고치면 처음 값으로 돌아간다).
  'mails.settingsGet': () => ({ expired_retention_days: 3, updated_at: null, updated_by: null }),
  'mails.settingsUpdate': (_t, p) => ({ expired_retention_days: p['expiredRetentionDays'], updated_at: new Date().toISOString(), updated_by: 'mock@local' }),
  'friends.settingsGet': () => ({ max_friends: 100, max_pending_sent: 50, request_cooldown_seconds: 3, updated_at: null, updated_by: null }),
  'friends.settingsUpdate': (_t, p) => ({
    max_friends: p['maxFriends'], max_pending_sent: p['maxPendingSent'], request_cooldown_seconds: p['requestCooldownSeconds'],
    updated_at: new Date().toISOString(), updated_by: 'mock@local',
  }),
  'matchLobby.settingsGet': () => ({ max_pending_invites_received: 20, invite_cooldown_seconds: 3, lobby_expire_minutes: 30, updated_at: null, updated_by: null }),
  'matchLobby.settingsUpdate': (_t, p) => ({
    max_pending_invites_received: p['maxPendingInvitesReceived'], invite_cooldown_seconds: p['inviteCooldownSeconds'],
    lobby_expire_minutes: p['lobbyExpireMinutes'], updated_at: new Date().toISOString(), updated_by: 'mock@local',
  }),
  'game.settingsGet': () => gameSettings,
  'game.settingsUpdate': (_t, p) => {
    gameSettings = { ...gameSettings, time_zone: String(p['timeZone'] ?? gameSettings.time_zone), updated_at: new Date().toISOString(), updated_by: 'mock@local' }
    return gameSettings
  },
}

export async function mockAdmin<T>(target: ProjectTarget, action: string, params: Record<string, unknown>): Promise<T> {
  // 실제 요청처럼 아주 잠깐 기다린다 — 불러오는 중 표시가 깜빡이는지도 볼 수 있게.
  await new Promise((r) => setTimeout(r, 150))
  const handler = HANDLERS[action]
  if (handler) return handler(target, params) as T
  // 모르는 조회는 빈 목록, 변경 요청은 성공으로 답한다. 화면이 깨지지 않고 "비어 있음"으로 보이게 하려는 것.
  return { rows: [], total: 0, pageSize: 50 } as T
}
