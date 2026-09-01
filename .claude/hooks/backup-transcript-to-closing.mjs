// Closing 프로젝트(D:\Project\Closing) 요청 — 압축(PreCompact) 전 대화 원본이 요약으로
// 바뀌기 전에 백업해 둔다. AI 호출 없이 순수 파일 이어붙이기만 한다(비용 없음).
// PreCompact·SessionEnd 둘 다 이 스크립트를 쓴다 — 로직은 "새 줄만 이어붙이기"로 동일하다.

import { readFileSync, existsSync, writeFileSync, appendFileSync, mkdirSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'

let payload = {}
try {
  let s = ''; for await (const chunk of process.stdin) s += chunk
  payload = JSON.parse(s)
} catch { process.exit(0) }

const transcriptPath = payload.transcript_path
if (!transcriptPath || !existsSync(transcriptPath)) process.exit(0)

const hooksDir = dirname(fileURLToPath(import.meta.url))  // .claude/hooks
const stateFile = join(hooksDir, '..', 'raw-log-state.json')  // .claude/raw-log-state.json
const destDir = 'D:\\Project\\Closing\\TrueSoft.Supabase\\raw-log'
const destFile = join(destDir, 'raw.jsonl')

let state = {}
try { state = JSON.parse(readFileSync(stateFile, 'utf8')) } catch { /* 첫 실행 — 빈 상태로 시작 */ }

let lines
try {
  lines = readFileSync(transcriptPath, 'utf8').split('\n')
} catch { process.exit(0) }
// 마지막 개행으로 생기는 빈 요소는 줄로 안 센다
const total = lines.length && lines[lines.length - 1] === '' ? lines.length - 1 : lines.length

const already = state[transcriptPath] ?? 0
if (already >= total) process.exit(0)  // 이 트랜스크립트에서 새로 생긴 줄 없음

const newLines = lines.slice(already, total)
if (newLines.length === 0) process.exit(0)

try {
  if (!existsSync(destDir)) mkdirSync(destDir, { recursive: true })
  appendFileSync(destFile, newLines.join('\n') + '\n')
  state[transcriptPath] = total
  writeFileSync(stateFile, JSON.stringify(state, null, 2))
} catch {
  // Closing 쪽 폴더에 못 쓰더라도(경로 없음·권한 등) 세션 진행을 막지 않는다 — 조용히 넘어간다.
  process.exit(0)
}
