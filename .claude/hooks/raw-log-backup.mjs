// PreCompact/SessionEnd 시점에 대화 원본이 요약(compact)으로 바뀌어 디테일이 사라지기 전에,
// 프로젝트 안 Logs/RawLog 폴더에 이어붙여 백업한다. AI 호출 없이 파일 복사만 한다.
// 백업 위치는 프로젝트 내부(DefenceR의 raw-log-backup.mjs와 같은 관례) — 다른 프로젝트로 반출하지 않는다.
//
// 어디까지 복사했는지는 transcript_path별로 상태 파일에 **바이트 오프셋**으로 남긴다(줄 수 아님) —
// 그래야 "아직 다 안 써진 줄" 도중에 훅이 돌아도 그 줄을 반쪽만 잘라 확정하는 일이 없다. 바이트
// 단위라 다음 실행에서 그 줄의 나머지부터 이어서 읽힌다.
//
// 상태 파일엔 오프셋과 별개로 __lastRunAt·__lastResult(appended/nothing-new/failed 등)도 남긴다 —
// 성공 경로에서만 기록하면 "한 번도 안 돌았다"와 "돌았는데 새 내용이 없었다"가 구분이 안 된다.
//
// 대상 파일(raw.jsonl)에는 여러 transcript_path의 내용이 그대로 이어 붙는다 — 세션을 이어받으면
// Claude Code가 이전 대화를 새 트랜스크립트 파일에 그대로 복사해 넣을 수 있어, 그 경우 내용이
// 중복으로 쌓일 수 있다. 이 훅에서 중복 제거는 하지 않는다 — 읽는 쪽이 각 줄의 uuid 기준으로
// 걸러야 한다.
//
// 이 훅은 실패해도 세션 진행을 막으면 안 되므로 전부 try/catch로 감싼다.

import { existsSync, readFileSync, writeFileSync, mkdirSync, openSync, readSync, closeSync, fstatSync, appendFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'

const HOOKS_DIR = dirname(fileURLToPath(import.meta.url))
const STATE = join(HOOKS_DIR, '..', 'raw-log-state.json')
const DEST_DIR = join(HOOKS_DIR, '..', '..', 'Logs', 'RawLog')
const DEST_FILE = join(DEST_DIR, 'raw.jsonl')

const readState = () => { try { return JSON.parse(readFileSync(STATE, 'utf8')) } catch { return {} } }
const writeState = (next) => { try { writeFileSync(STATE, JSON.stringify(next, null, 2), 'utf8') } catch {} }

// 오프셋(진행 위치)과 별개로 "마지막으로 돈 시각·결과"를 남긴다. newOffset을 주면 그 transcript의
// 진행 위치도 같이 갱신한다 — 성공 경로와 조용히 빠지는 경로가 이 함수 하나로 합쳐진다.
function recordRun(transcriptPath, result, newOffset) {
  const state = readState()
  if (transcriptPath && newOffset !== undefined) state[transcriptPath] = newOffset
  state.__lastRunAt = new Date().toISOString()
  state.__lastResult = result
  writeState(state)
}

async function main() {
  let payload = {}
  try {
    let s = ''; for await (const chunk of process.stdin) s += chunk
    payload = JSON.parse(s)
  } catch {
    recordRun(null, 'bad-payload')
    return
  }

  const transcriptPath = payload.transcript_path
  if (!transcriptPath || !existsSync(transcriptPath)) {
    recordRun(transcriptPath ?? null, 'no-transcript')
    return
  }

  try {
    mkdirSync(DEST_DIR, { recursive: true })

    const state = readState()
    const prevOffset = typeof state[transcriptPath] === 'number' ? state[transcriptPath] : 0

    const fd = openSync(transcriptPath, 'r')
    const size = fstatSync(fd).size

    // 파일이 이전 기록보다 짧아졌으면(다른 세션이 같은 경로를 재사용하는 등) 처음부터 다시 읽는다.
    // 안 그러면 이 조건이 영원히 참이 돼 그 뒤로 아무 내용도 안 쌓인다.
    const lastOffset = size < prevOffset ? 0 : prevOffset

    if (size <= lastOffset) {
      closeSync(fd)
      recordRun(transcriptPath, 'nothing-new')
      return
    }

    const buf = Buffer.alloc(size - lastOffset)
    readSync(fd, buf, 0, buf.length, lastOffset)
    closeSync(fd)

    appendFileSync(DEST_FILE, buf)
    recordRun(transcriptPath, 'appended', size)
  } catch {
    // 실패해도 세션 진행은 막지 않는다 — 조용히 넘어가되 마지막 결과는 남긴다.
    recordRun(transcriptPath, 'failed')
  }
}

await main()
