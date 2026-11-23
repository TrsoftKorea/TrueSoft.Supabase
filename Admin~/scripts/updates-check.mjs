// 판을 올리는 게시는 그 판의 업데이트 내역(가이드 docs~/guide/updates.md)이 있어야 한다(2026-11-23 사용자 결정 — 판마다 패치노트).
// SDK 코드가 든 게시는 그 판의 절도 함께 고쳐져 있어야 하고, 날짜 줄은 게시 버튼이 게시하는 날로 적는다.
// 게시 서버(mock-server.mjs)가 부르고, 따로 시험할 수 있게 파일을 나눴다.

export const UPDATES_FILE = 'docs~/guide/updates.md'

const anchorOf = (version) => `v${version.replace(/\./g, '-')}`
const DATE_LINE = /^\d{4}-\d{2}-\d{2}[ \t]*$/

/** 판 절 머리 — `## 0.3.0 {#v0-3-0}`. 판 번호가 다르게 적힌 절(0.3.1 로 적고 0.4.0 을 고름)은 맞지 않는 것으로 본다. */
export function sectionPattern(version) {
  const v = version.replace(/\./g, '\\.')
  return new RegExp(`^## ${v} \\{#${anchorOf(version)}\\}[ \\t]*\\r?$`, 'm')
}

const isHeader = (line, version) => sectionPattern(version).test(line)

/** 그 판 절의 줄들(머리 줄부터 다음 `## ` 줄 전까지). 없으면 null. */
export function sectionLines(text, version) {
  const lines = text.split(/\r?\n/)
  const start = lines.findIndex((l) => isHeader(l, version))
  if (start < 0) return null
  let end = start + 1
  while (end < lines.length && !lines[end].startsWith('## ')) end++
  return lines.slice(start, end)
}

/** 절에서 비교할 내용 — 버튼이 적는 날짜 줄(머리 다음 첫 내용 줄)과 줄 끝 공백은 뺀다. */
function sectionBody(text, version) {
  const lines = sectionLines(text, version)
  if (!lines) return null
  const rest = lines.slice(1)
  const first = rest.findIndex((l) => l.trim() !== '')
  if (first >= 0 && DATE_LINE.test(rest[first])) rest.splice(first, 1)
  return rest.map((l) => l.trimEnd()).join('\n').trim()
}

const readOr = (read, fallback) => { try { return read() } catch { return fallback } }
const firstLine = (e) => String(e?.message ?? e).split('\n')[0]

/**
 * 판을 올리는 게시의 확인 — 멈출 사유(없으면 null).
 * - raised: 이번 게시가 판을 올리는지. 그대로면 보지 않는다.
 * - readWorking(): 작업 폴더의 updates.md 내용. 못 읽으면 던진다 — "절 없음"과 갈라 오류로 멈춘다.
 * - readHead(): 마지막 커밋의 내용(없으면 던지거나 '').
 * - inSelection: updates.md 가 고른 묶음에 들었는지.
 */
export function updatesProblem({ version, raised, readWorking, readHead, inSelection }) {
  if (!raised) return null
  let working
  try { working = readWorking() } catch (e) {
    return `업데이트 내역(${UPDATES_FILE})을 읽지 못해 게시하지 않았습니다: ${firstLine(e)}`
  }
  const pattern = sectionPattern(version)
  if (!pattern.test(working)) {
    return `업데이트 내역에 ${version} 절이 없어 게시하지 않았습니다. ${UPDATES_FILE} 맨 위에 「## ${version} {#${anchorOf(version)}}」 절을 적어 주세요.`
  }
  // 절이 이미 커밋돼 있으면 묶음과 상관없다. 아니면 이번에 함께 올라가야 한다.
  if (!pattern.test(readOr(readHead, '')) && !inSelection) {
    return `업데이트 내역의 ${version} 절이 고른 묶음에 없어 함께 올라가지 않습니다. 업데이트 내역 파일(${UPDATES_FILE})이 든 묶음을 함께 골라 주세요.`
  }
  return null
}

/**
 * SDK 코드가 든 게시의 확인 — 그 판의 절이 마지막 커밋보다 고쳐져 있고 함께 올라가야 한다.
 * 판을 올리지 않는 게시(올려 둔 판이 아직 게시 전)에서도 돈다. 절이 새로 생긴 것도 고친 것이다.
 */
export function sdkUpdatesProblem({ version, hasSdk, readWorking, readHead, inSelection }) {
  if (!hasSdk) return null
  let working
  try { working = readWorking() } catch (e) {
    return `업데이트 내역(${UPDATES_FILE})을 읽지 못해 게시하지 않았습니다: ${firstLine(e)}`
  }
  const now = sectionBody(working, version)
  if (now === null) {
    return `SDK 코드가 바뀌었는데 업데이트 내역에 ${version} 절이 없어 게시하지 않았습니다. ${UPDATES_FILE} 맨 위에 「## ${version} {#${anchorOf(version)}}」 절을 적어 주세요.`
  }
  const before = sectionBody(readOr(readHead, ''), version)
  if (before !== null && before === now) {
    return `SDK 코드가 바뀌었는데 업데이트 내역의 ${version} 절이 고쳐지지 않아 게시하지 않았습니다. 이번에 달라지는 점을 그 절에 적어 주세요.`
  }
  if (!inSelection) {
    return `SDK 코드가 바뀌었는데 업데이트 내역 파일(${UPDATES_FILE})이 고른 묶음에 없어 게시하지 않았습니다. 그 파일이 든 묶음을 함께 골라 주세요.`
  }
  return null
}

/** 게시하는 날 — 한국 날짜(YYYY-MM-DD). */
export const koreaDate = (now = new Date()) =>
  new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Seoul', year: 'numeric', month: '2-digit', day: '2-digit' }).format(now)

/**
 * 그 판 절의 날짜 줄(머리 다음 첫 내용 줄이 날짜 꼴이면)을 date 로 바꾼다. 날짜 줄이 없으면 머리 다음에 빈 줄과 함께 넣는다.
 * 절이 없으면 그대로 돌려준다. 줄바꿈(CRLF·LF)은 파일 것을 따른다.
 */
export function stampDate(text, version, date) {
  const eol = text.includes('\r\n') ? '\r\n' : '\n'
  const lines = text.split(/\r?\n/)
  const start = lines.findIndex((l) => isHeader(l, version))
  if (start < 0) return text
  let i = start + 1
  while (i < lines.length && lines[i].trim() === '') i++
  if (i < lines.length && DATE_LINE.test(lines[i])) {
    lines[i] = date
  } else {
    lines.splice(start + 1, 0, '', date)
  }
  return lines.join(eol)
}
