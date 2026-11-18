// 여러 세션이 같은 폴더에서 역할을 나눠 일할 때 쓰는 공용 판정. 훅(zone-guard·zone-claim)과 게시 버튼(Admin~/scripts/mock-server.mjs)이
// 모두 이 파일 하나를 읽는다 — 구역·묶음 판정을 두 곳에 따로 적으면 한쪽만 고쳐져 갈라진다.
//
// - zones.json  : 역할별 구역(커밋됨)
// - claims/<세션>.json : 어느 세션이 어떤 역할인지(커밋 안 함). 세션 첫 메시지 "역할 콘솔" 을 zone-claim 훅이 적는다
// - ledger.json : 누가 언제 어느 파일을 고쳤는지(커밋 안 함). zone-guard 훅이 Edit·Write 마다 적는다
//
// 훅에 오는 세션 ID 는 서브에이전트도 부모와 같다 — 한 세션 안의 에이전트끼리는 여기서 가를 수 없다.
// Bash 로 쓴 파일(sed·리다이렉트)도 기록되지 않는다.
import { appendFileSync, existsSync, mkdirSync, readFileSync, readdirSync, rmSync, writeFileSync } from 'node:fs'
import { dirname, isAbsolute, join, relative, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

export const ZONES_DIR = dirname(fileURLToPath(import.meta.url))
export const ROOT = resolve(ZONES_DIR, '..', '..')
const CONFIG = join(ZONES_DIR, 'zones.json')
const LEDGER = join(ZONES_DIR, 'ledger.jsonl')
const CLAIMS = join(ZONES_DIR, 'claims')
// 게시 묶음 폴더. 역할마다 <역할>/files · message · message-sdk 를 둔다.
export const PUBLISH_DIR = 'Admin~/.publish/'
// 역할 없이 쓰던 옛 게시 파일 — 역할 없는 세션 하나만 일할 때의 묶음이다.
export const LEGACY_PUBLISH = ['Admin~/.publish-files', 'Admin~/.publish-message', 'Admin~/.publish-message-sdk']

const LEDGER_MAX = 5000
const LEDGER_DAYS = 14

const readJson = (p, fallback) => {
  try { return JSON.parse(readFileSync(p, 'utf8')) } catch { return fallback }
}

/** 구역 설정. 못 읽으면 null — 부르는 쪽이 "검사 못 함"을 알려야 한다(빈 설정으로 두면 조용히 꺼진다). */
export function loadConfig() {
  const cfg = readJson(CONFIG, null)
  return cfg && typeof cfg.roles === 'object' && Object.keys(cfg.roles).length ? cfg : null
}

/** 저장소 기준 상대 경로(슬래시). 저장소 밖이면 null. */
export function toRelative(filePath) {
  if (!filePath) return null
  const rel = relative(ROOT, resolve(ROOT, String(filePath))).replace(/\\/g, '/')
  // 드라이브가 다르면 relative 가 절대 경로를 돌려준다 — 저장소 밖이다.
  if (!rel || rel.startsWith('..') || isAbsolute(rel)) return null
  return rel
}

// 윈도우 경로는 대소문자를 안 가린다 — 훅에는 d:\project\… 처럼 소문자로 오기도 한다.
export const sameKey = (p) => p.toLowerCase()
const startsWithI = (p, prefix) => sameKey(p).startsWith(sameKey(prefix))

/** 이 경로를 그 역할이 고쳐도 되는가. 'own' 자기 구역 · 'shared' 함께 쓰는 곳 · 'outside' 밖. */
export function zoneOf(rel, role, cfg) {
  if ((cfg.roles?.[role]?.allow ?? []).some((p) => startsWithI(rel, p))) return 'own'
  if ((cfg.shared ?? []).some((p) => startsWithI(rel, p))) return 'shared'
  return 'outside'
}

/** 게시 묶음 파일이면 그 묶음의 역할 이름(옛 파일은 ''), 아니면 null. */
export function publishBatchOf(rel) {
  if (LEGACY_PUBLISH.some((p) => sameKey(p) === sameKey(rel))) return ''
  if (!startsWithI(rel, PUBLISH_DIR)) return null
  return rel.slice(PUBLISH_DIR.length).split('/')[0] ?? null
}

// ── 역할 선언 ──

export function claimOf(sessionId) {
  return readJson(join(CLAIMS, `${sessionId}.json`), null)
}

export function setClaim(sessionId, role) {
  mkdirSync(CLAIMS, { recursive: true })
  writeFileSync(join(CLAIMS, `${sessionId}.json`), JSON.stringify({ role, at: Date.now() }, null, 2), 'utf8')
}

export function clearClaim(sessionId) {
  rmSync(join(CLAIMS, `${sessionId}.json`), { force: true })
}

export function allClaims() {
  if (!existsSync(CLAIMS)) return []
  return readdirSync(CLAIMS).filter((f) => f.endsWith('.json'))
    .map((f) => ({ session: f.slice(0, -5), ...readJson(join(CLAIMS, f), {}) }))
    .filter((c) => c.role)
}

// ── 수정 기록 ──

export function readLedger() {
  let text = ''
  try { text = readFileSync(LEDGER, 'utf8') } catch { return [] }
  const out = []
  for (const line of text.split('\n')) {
    if (!line.trim()) continue
    try { out.push(JSON.parse(line)) } catch { /* 쓰다 만 줄은 건너뛴다 */ }
  }
  return out
}

// 한 줄씩 덧붙인다 — 두 세션의 훅이 동시에 읽고 통째로 다시 쓰면 한쪽 기록이 사라진다.
// 정리(오래된 줄 지우기)는 줄이 두 배로 쌓였을 때만 한다. 정리하는 그 순간 다른 세션이 붙인 줄은 잃을 수 있다.
export function appendLedger(entry) {
  appendFileSync(LEDGER, JSON.stringify(entry) + '\n', 'utf8')
  const entries = readLedger()
  if (entries.length <= LEDGER_MAX * 2) return
  const cutoff = Date.now() - LEDGER_DAYS * 86_400_000
  const kept = entries.filter((e) => e.at >= cutoff).slice(-LEDGER_MAX)
  writeFileSync(LEDGER, kept.map((e) => JSON.stringify(e)).join('\n') + '\n', 'utf8')
}

/** 세션의 마지막 수정 시각. 기록이 없으면 0. */
export function lastActivity(sessionId, entries = readLedger()) {
  let t = 0
  for (const e of entries) if (e.session === sessionId && e.at > t) t = e.at
  return t
}

// ── 손 놓음 ──
// 어떤 역할의 묶음이 게시됐거나 세션이 "역할 해제"를 하면, 그 전의 수정 기록은 다른 묶음을 더는 막지 않는다.
// 안 그러면 그 세션이 목록에 안 넣고 끝낸 파일(함께 쓰는 install.sql 등)을 다른 묶음이 영영 못 올린다.
const RELEASED = join(ZONES_DIR, 'released.json')
const readReleased = () => {
  const r = readJson(RELEASED, {})
  return { roles: r.roles ?? {}, sessions: r.sessions ?? {} }
}
function writeReleased(update) {
  const r = readReleased()
  update(r)
  writeFileSync(RELEASED, JSON.stringify(r, null, 2), 'utf8')
}
/** 역할 이름(역할 없음은 '')의 지금까지 기록을 놓는다. 게시 버튼이 그 묶음을 올린 뒤, 또는 "역할 해제 <역할>" 이 부른다. */
export const releaseRole = (role, at = Date.now()) => writeReleased((r) => { r.roles[role] = at })
export const releaseSession = (sessionId, at = Date.now()) => writeReleased((r) => { r.sessions[sessionId] = at })

/**
 * since 이후 이 파일을 고친, 그 역할이 아닌 쪽들. 역할 없는 세션은 ''.
 * 게시 버튼이 "이 묶음에 다른 작업이 섞였는가"를 가를 때 쓴다 — since 는 그 파일의 마지막 커밋 시각이다.
 */
export function foreignRoles(rel, role, since, entries = readLedger()) {
  const key = sameKey(rel)
  const released = readReleased()
  const out = new Set()
  for (const e of entries) {
    if (e.at <= since || sameKey(e.path) !== key) continue
    const r = e.role ?? ''
    if (r === role) continue
    if (e.at <= (released.roles[r] ?? 0) || e.at <= (released.sessions[e.session] ?? 0)) continue
    out.add(r)
  }
  return [...out]
}

export const roleName = (r) => r || '역할 없음'
