// 판을 올리는 게시는 그 판의 업데이트 내역(가이드 docs~/guide/updates.md)이 있어야 한다(2026-11-23 사용자 결정 — 판마다 패치노트).
// 게시 서버(mock-server.mjs)가 부르고, 따로 시험할 수 있게 파일을 나눴다.

export const UPDATES_FILE = 'docs~/guide/updates.md'

/** 판 절 머리 — `## 0.3.0 {#v0-3-0}`. 판 번호가 다르게 적힌 절(0.3.1 로 적고 0.4.0 을 고름)은 맞지 않는 것으로 본다. */
export function sectionPattern(version) {
  const v = version.replace(/\./g, '\\.')
  const anchor = `v${version.replace(/\./g, '-')}`
  return new RegExp(`^## ${v} \\{#${anchor}\\}[ \\t]*\\r?$`, 'm')
}

/**
 * 게시를 멈출 사유(없으면 null).
 * - raised: 이번 게시가 판을 올리는지. 그대로면 보지 않는다.
 * - readWorking(): 작업 폴더의 updates.md 내용. 못 읽으면 던진다 — "절 없음"과 갈라 오류로 멈춘다.
 * - readHead(): 마지막 커밋의 내용(없으면 '').
 * - inSelection: updates.md 가 고른 묶음에 들었는지.
 */
export function updatesProblem({ version, raised, readWorking, readHead, inSelection }) {
  if (!raised) return null
  let working
  try { working = readWorking() } catch (e) {
    return `업데이트 내역(${UPDATES_FILE})을 읽지 못해 게시하지 않았습니다: ${String(e?.message ?? e).split('\n')[0]}`
  }
  const pattern = sectionPattern(version)
  if (!pattern.test(working)) {
    return `업데이트 내역에 ${version} 절이 없어 게시하지 않았습니다. ${UPDATES_FILE} 맨 위에 「## ${version} {#v${version.replace(/\./g, '-')}}」 절을 적어 주세요.`
  }
  // 절이 이미 커밋돼 있으면 묶음과 상관없다. 아니면 이번에 함께 올라가야 한다.
  let head = ''
  try { head = readHead() } catch { head = '' }
  if (!pattern.test(head) && !inSelection) {
    return `업데이트 내역의 ${version} 절이 고른 묶음에 없어 함께 올라가지 않습니다. 업데이트 내역 파일(${UPDATES_FILE})이 든 묶음을 함께 골라 주세요.`
  }
  return null
}
