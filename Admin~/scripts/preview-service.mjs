// 가짜 데이터 미리보기(게시 버튼 포함)를 앱과 상관없이 늘 띄워 두는 실행기. PC 에 로그인하면 시작 프로그램이 이걸 부른다.
//  - 화면 코드를 고치면 vite 가 dist-mock 을 다시 빌드한다(미리보기 서버는 요청마다 파일을 읽어 새로고침하면 바로 보인다).
//  - 둘 중 하나가 죽으면 다시 띄운다. 이미 다른 미리보기가 4174 에서 돌고 있으면 아무것도 하지 않고 끝낸다.
//  - 깨어난 시각과 재시작은 저장소 맨 위 debug-preview-service.log 에 남는다(커밋 안 됨).
import { spawn } from 'node:child_process'
import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const ADMIN_DIR = path.resolve(HERE, '..')
const LOG = path.resolve(ADMIN_DIR, '..', 'debug-preview-service.log')
const URL_ = 'http://127.0.0.1:4174/'

const log = (msg) => {
  const line = `${new Date().toLocaleString('sv-SE')} ${msg}\n`
  try {
    // 너무 커지지 않게 1MB 를 넘으면 비우고 다시 쓴다.
    if (fs.existsSync(LOG) && fs.statSync(LOG).size > 1 << 20) fs.writeFileSync(LOG, '')
    fs.appendFileSync(LOG, line)
  } catch { /* 기록 실패로 미리보기를 멈추지 않는다 */ }
}

async function alreadyRunning() {
  try {
    await fetch(URL_, { signal: AbortSignal.timeout(2000) })
    return true
  } catch {
    return false
  }
}

function keep(name, args, onExit) {
  let stopping = false
  let child = null
  const start = () => {
    child = spawn(process.execPath, args, { cwd: ADMIN_DIR, stdio: ['ignore', 'pipe', 'pipe'], windowsHide: true })
    const out = (d) => {
      const s = d.toString().trim()
      // 빌드 진행 줄은 많아 오류만 남긴다.
      if (/error|fail|오류|실패/i.test(s)) log(`[${name}] ${s.slice(0, 2000)}`)
    }
    child.stdout.on('data', out)
    child.stderr.on('data', out)
    child.on('exit', async (code) => {
      if (stopping) return
      if (onExit && (await onExit())) return
      log(`[${name}] 끝남(코드 ${code}) — 5초 뒤 다시 띄움`)
      setTimeout(() => { if (!stopping) start() }, 5000)
    })
    log(`[${name}] 시작(pid ${child.pid})`)
  }
  start()
  return () => { stopping = true; child?.kill() }
}

function stopAll(why) {
  log(why)
  stops.forEach((s) => s())
  process.exit(0)
}

if (await alreadyRunning()) {
  log('이미 4174 에서 미리보기가 돌고 있어 끝냄')
  process.exit(0)
}
log(`깨어남(pid ${process.pid})`)

const vite = path.join(ADMIN_DIR, 'node_modules', 'vite', 'bin', 'vite.js')
const stops = []
stops.push(keep('빌드', [vite, 'build', '--mode', 'mock', '--outDir', 'dist-mock', '--watch']))
// 서버가 끝났는데 4174 에서 다른 미리보기가 답하면(앱이 띄운 것 등) 자리를 내주고 통째로 끝낸다 — 안 그러면 5초마다 끝없이 다시 띄운다.
stops.push(keep('서버', [path.join(HERE, 'mock-server.mjs')], async () => {
  if (!(await alreadyRunning())) return false
  stopAll('[서버] 4174 를 다른 미리보기가 쓰고 있어 실행기를 끝냄')
  return true
}))
for (const sig of ['SIGINT', 'SIGTERM']) process.on(sig, () => stopAll('신호를 받아 끝냄'))
