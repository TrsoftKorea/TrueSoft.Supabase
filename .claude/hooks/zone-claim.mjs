// 역할 선언(UserPromptSubmit). 사용자가 메시지 첫머리에 "역할 콘솔"·"역할: SDK"·"역할 해제" 라고 쓰면 이 세션의 역할을 적는다.
// 규칙 문장으로 "역할을 선언하라"고 적어 두면 잊는다 — 사용자가 한 줄 쓰는 것으로 끝나게 했다.
// 지금 누가 어떤 역할인지: node .claude/hooks/zone-claim.mjs --list
// "역할 해제" 는 이 세션을, "역할 해제 SDK" 는 그 역할을 놓는다 — 이미 닫힌 세션이 남긴 기록이 다른 묶음의 게시를 막을 때 쓴다.
import { allClaims, claimOf, clearClaim, lastActivity, loadConfig, readLedger, releaseRole, releaseSession, setClaim } from '../zones/zones.mjs'

const PATTERN = /^\s*역할\s*[:：]?\s*([^\s,.!?]+)(?:[ \t]+([^\s,.!?]+))?/
const ACTIVE_MS = 3 * 3_600_000

const say = (text) => console.log(JSON.stringify({ hookSpecificOutput: { hookEventName: 'UserPromptSubmit', additionalContext: text } }))

function others(sessionId, role) {
  const entries = readLedger()
  const now = Date.now()
  return allClaims()
    .filter((c) => c.session !== sessionId && c.role === role)
    .map((c) => ({ ...c, last: Math.max(lastActivity(c.session, entries), c.at ?? 0) }))
    .filter((c) => now - c.last < ACTIVE_MS)
}

function claim(payload) {
  const m = PATTERN.exec(String(payload.prompt ?? ''))
  if (!m) return
  const sessionId = payload.session_id ?? 'unknown'
  const word = m[1]
  const cfg = loadConfig()
  if (/^(해제|없음|끝)$/.test(word)) {
    if (m[2]) {
      const target = cfg && Object.keys(cfg.roles).find((r) => r.toLowerCase() === m[2].toLowerCase())
      if (!target) return say(`"${m[2]}" 는 정해진 역할이 아니라 놓지 않았다. 사용자에게 그대로 알린다.`)
      releaseRole(target)
      return say(`「${target}」 역할 세션들이 지금까지 고친 기록을 놓았다 — 그 파일들이 다른 묶음의 게시를 더는 막지 않는다. 그 역할 세션이 아직 일하는 중이었다면 그 변경이 다른 묶음에 함께 올라갈 수 있다고 사용자에게 한 줄로 알린다.`)
    }
    const before = claimOf(sessionId)?.role
    clearClaim(sessionId)
    releaseSession(sessionId)
    return say(before ? `이 세션의 역할(${before})을 풀었다. 이제 구역 확인 없이 고치고, 게시 묶음은 옛 Admin~/.publish-files 쪽을 쓴다. 사용자에게 한 줄로 알린다.` : '이 세션은 선언한 역할이 없었다.')
  }
  if (!cfg) return say('구역 설정(.claude/zones/zones.json)을 읽지 못해 역할을 적지 않았다. 사용자에게 그대로 알린다.')
  const role = Object.keys(cfg.roles).find((r) => r.toLowerCase() === word.toLowerCase())
  if (!role) return say(`"${word}" 는 정해진 역할이 아니라 적지 않았다. 역할은 ${Object.keys(cfg.roles).join(' · ')} 중 하나다. 사용자에게 다시 골라 달라고 한다.`)

  setClaim(sessionId, role)
  const own = cfg.roles[role]
  const dup = others(sessionId, role)
  say([
    `이 세션은 「${role}」 담당이다(${own.label}). 같은 폴더에서 다른 역할의 세션이 동시에 일한다.`,
    `- 맡은 곳: ${own.allow.length ? own.allow.join(', ') : '없음(함께 쓰는 곳만)'} · 함께 쓰는 곳: ${cfg.shared.join(', ')}. 그 밖을 고치면 훅이 사용자에게 확인창을 띄운다.`,
    `- 다른 세션이 15분 안에 고친 파일을 고치려 해도 확인창이 뜬다. 함께 쓰는 파일(install.sql·CLAUDE.md 등)은 고치기 직전에 다시 읽는다.`,
    `- 게시 묶음은 Admin~/.publish/${role}/ 에 files(올릴 파일 목록) · message(콘솔 요약) · message-sdk(그 밖 요약)로 적는다. 옛 Admin~/.publish-files 는 쓰지 않는다. 게시 버튼은 고른 묶음만 올린다.`,
    `- 다른 역할도 고친 파일이 묶음에 있으면 버튼이 그 역할의 묶음과 함께 고를 때만 게시한다 — 그때는 사용자에게 어느 세션과 함께 올려야 하는지 알린다.`,
    dup.length ? `- 주의: 같은 「${role}」 역할의 다른 세션이 최근 3시간 안에 활동했다(${dup.map((c) => c.session.slice(0, 8)).join(', ')}). 같은 역할 둘은 같은 묶음을 함께 쓴다 — 사용자에게 알린다.` : '',
    '사용자에게 역할을 맡았다고 한 줄로 알리고, 이 메시지의 나머지 요청이 있으면 이어서 한다.',
  ].filter(Boolean).join('\n'))
}

if (process.argv.includes('--list')) {
  const entries = readLedger()
  const now = Date.now()
  const rows = allClaims().map((c) => ({ ...c, last: Math.max(lastActivity(c.session, entries), c.at ?? 0) })).sort((a, b) => b.last - a.last)
  if (!rows.length) console.log('역할을 선언한 세션이 없습니다.')
  for (const c of rows) console.log(`${c.role}\t${c.session}\t마지막 활동 ${Math.round((now - c.last) / 60000)}분 전`)
} else {
  let raw = ''
  process.stdin.on('data', (c) => (raw += c))
  process.stdin.on('end', () => {
    let payload
    try { payload = JSON.parse(raw) } catch { return }
    try { claim(payload) } catch (e) { say(`역할 선언이 오류로 멈췄다(${String(e?.message ?? e).split('\n')[0]}). 사용자에게 그대로 알린다.`) }
  })
}
