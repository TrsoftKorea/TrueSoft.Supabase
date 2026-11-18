// 여러 세션이 같은 폴더에서 역할을 나눠 일할 때 서로 밟지 않게 한다(판정은 .claude/zones/zones.mjs).
//
// PreToolUse(Edit·Write·NotebookEdit) — 고치기 전에 가른다.
//   1. 게시 묶음 파일: 자기 역할의 묶음(Admin~/.publish/<역할>/)만 쓴다. 남의 묶음·옛 파일은 막는다.
//   2. 다른 세션이 방금(15분) 고친 파일이면 확인창.
//   3. 역할을 선언한 세션이 자기 구역·공용 구역 밖을 고치면 확인창.
// PostToolUse — 실제로 고쳐진 것만 기록한다(ledger.jsonl). 확인창에서 거절된 수정은 기록되지 않아야
// 게시 버튼이 "다른 역할이 고친 파일"을 잘못 가르지 않는다.
//
// 이 훅이 못 보는 것: Bash 로 쓴 파일, 한 세션 안의 서브에이전트끼리(세션 ID 가 같다).
import { appendLedger, claimOf, loadConfig, publishBatchOf, readLedger, roleName, sameKey, toRelative, zoneOf } from '../zones/zones.mjs'

const decide = (permissionDecision) => (permissionDecisionReason) =>
  console.log(JSON.stringify({ hookSpecificOutput: { hookEventName: 'PreToolUse', permissionDecision, permissionDecisionReason } }))
const ask = decide('ask')
const deny = decide('deny')

function pre(rel, sessionId, role, cfg) {
  const batch = publishBatchOf(rel)
  if (batch !== null) {
    if (role && batch !== role) {
      return deny(`게시 묶음은 역할마다 따로입니다. ${role} 세션은 Admin~/.publish/${role}/ 의 files · message · message-sdk 에 씁니다(${rel} 은 ${batch ? `${batch} 세션의 묶음` : '역할 없는 세션의 옛 묶음'}입니다).`)
    }
    if (!role && batch !== '') {
      return deny(`${rel} 은 ${batch} 세션의 게시 묶음입니다. 역할을 선언하지 않은 세션은 Admin~/.publish-files · .publish-message · .publish-message-sdk 에 씁니다.`)
    }
    return // 자기 묶음 — 아래 겹침·구역 판정은 하지 않는다
  }

  const windowMs = ((cfg?.collisionWindowMinutes) ?? 15) * 60_000
  const now = Date.now()
  const key = sameKey(rel)
  const clash = readLedger().filter((e) => sameKey(e.path) === key && e.session !== sessionId && now - e.at < windowMs).pop()
  if (clash) {
    return ask(
      `${rel} 은 ${Math.max(1, Math.round((now - clash.at) / 60000))}분 전에 다른 세션(${roleName(clash.role)})이 고친 파일입니다.\n` +
        `그 세션이 아직 일하는 중이면 두 변경이 한 파일에 섞여 게시 때 함께 올라갑니다. 그래도 고칠까요?`,
    )
  }

  if (!role) return
  if (!cfg) return ask(`구역 설정(.claude/zones/zones.json)을 읽지 못해 ${role} 세션의 구역을 가를 수 없습니다. 그대로 고칠까요?`)
  if (zoneOf(rel, role, cfg) === 'outside') {
    const own = cfg.roles?.[role]
    const where = own?.allow?.length ? own.allow.join(', ') : '자기 구역 없음'
    return ask(`${role}(${own?.label ?? '정의 안 된 역할'}) 세션이 맡은 곳 밖의 ${rel} 을 고치려 합니다.\n맡은 곳: ${where} · 함께 쓰는 곳: ${(cfg.shared ?? []).join(', ')}`)
  }
}

function main(payload) {
  const toolName = payload.tool_name ?? ''
  if (!/^(Write|Edit|NotebookEdit)$/.test(toolName)) return
  const input = payload.tool_input ?? {}
  const rel = toRelative(input.file_path ?? input.notebook_path)
  if (!rel) return
  const sessionId = payload.session_id ?? 'unknown'
  const role = claimOf(sessionId)?.role ?? null

  if (payload.hook_event_name === 'PostToolUse') {
    // 실패한 수정(바꿀 글을 못 찾음 등)은 파일이 그대로라 기록하지 않는다.
    const res = payload.tool_response
    if (res && typeof res === 'object' && (res.success === false || res.error)) return
    appendLedger({ path: rel, session: sessionId, role, tool: toolName, at: Date.now() })
    return
  }
  pre(rel, sessionId, role, loadConfig())
}

let raw = ''
process.stdin.on('data', (c) => (raw += c))
process.stdin.on('end', () => {
  let payload
  try { payload = JSON.parse(raw) } catch { return } // 입력을 못 읽으면 막지 않는다 — 훅 때문에 일이 멈추면 안 된다
  try { main(payload) } catch (e) {
    // 판정 중 오류는 막지 않되 숨기지도 않는다 — 조용히 꺼진 검사는 통과처럼 보인다.
    if (payload?.hook_event_name !== 'PostToolUse') ask(`구역 검사가 오류로 멈췄습니다(${String(e?.message ?? e).split('\n')[0]}). 검사 없이 고칠까요?`)
  }
})
