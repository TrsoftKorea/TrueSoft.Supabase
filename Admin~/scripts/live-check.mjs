// 서버 쪽 파일(install.sql·서버 함수)이 든 게시는 두 게임의 실제 서버가 저장소와 같은지 먼저 본다 —
// 화면이 새로 부르는 DB 함수·action 이 서버에 없으면 게시 직후 실제 콘솔이 깨진다.
// 대조는 Tools~/LiveCheck/check.mjs 가 하고, 여기는 무엇을 돌릴지와 결과 읽기만 맡는다(따로 시험할 수 있게 나눴다).
import { spawn } from 'node:child_process'

export const LIVE_TOOL = 'Tools~/LiveCheck/check.mjs'
const DB_DIR = 'Samples~/DatabaseSetup/SQL/'
const FUNCTION_DIRS = ['Samples~/DatabaseSetup/EdgeFunctions/', 'Admin~/supabase/functions/']

/** 고른 파일로 정한 대조 인자 — 돌릴 것이 없으면 null. 이름 변경은 옛 경로도 본다. */
export function liveArgs(files) {
  const paths = files.flatMap((f) => (f.orig ? [f.file, f.orig] : [f.file]))
  const db = paths.some((p) => p.startsWith(DB_DIR))
  const functions = paths.some((p) => FUNCTION_DIRS.some((d) => p.startsWith(d)))
  if (db && functions) return []
  if (db) return ['--only', 'db']
  if (functions) return ['--only', 'functions']
  return null
}

// 도구가 끝까지 돌았다는 표시 — 끝 줄 "N개 중 M개 통과". 없으면 중간에 멈춘 것이다.
const SUMMARY = /^(\d+)개 중 (\d+)개 통과\s*$/m

/**
 * 도구 결과를 멈출 사유로 읽는다(없으면 null).
 * 어긋남과 도구 실패를 가른다 — 도구가 던지거나 die 로 멈춰도 종료 코드는 1 이라 코드만으로는 못 가른다.
 * 끝 줄까지 나왔을 때만 결과로 믿는다: 0 이라도 끝 줄이 없거나 숫자가 안 맞으면 도구 실패로 본다.
 */
export function liveProblem({ status, stdout = '', stderr = '', error }) {
  const summary = stdout.match(SUMMARY)
  const failed = (why) => `실제 서버 대조 도구가 실패해 게시하지 않았습니다(서버가 같은지 모름). ${why}`
  if (error) return failed(String(error.message ?? error).split('\n')[0])
  if (!summary) {
    const last = (stderr.trim() || stdout.trim()).split('\n').filter((l) => l.trim()).slice(-3).join(' / ')
    return failed(last ? `마지막 출력: ${last}` : `종료 코드 ${status}, 출력 없음`)
  }
  const [, total, passed] = summary.map(Number)
  if (status === 0 && total > 0 && total === passed) return null
  if (status === 1 && passed < total) {
    // ✘ 줄과 그 아래 · 항목만 옮긴다.
    const lines = stdout.split(/\r?\n/)
    const out = []
    let inBad = false
    for (const l of lines) {
      if (/^✘ /.test(l)) { inBad = true; out.push(l); continue }
      if (/^✔ /.test(l) || !l.trim()) { inBad = false; continue }
      if (inBad && /^\s+· /.test(l)) out.push(l.trimEnd())
    }
    return `실제 서버가 저장소와 달라 게시하지 않았습니다. 두 게임에 먼저 올려 주세요(${total - passed}건):\n${out.join('\n')}`
  }
  return failed(`종료 코드 ${status}, ${total}개 중 ${passed}개 통과`)
}

/** 도구를 돌린다 — 서버를 붙잡지 않게 비동기로. 서버 함수 코드를 새로 받으면 2분 가까이 걸려 넉넉히 기다린다. */
export function runLiveCheck(repo, args, timeoutMs = 5 * 60_000) {
  return new Promise((resolve) => {
    let stdout = ''
    let stderr = ''
    let settled = false
    const done = (r) => { if (!settled) { settled = true; clearTimeout(timer); resolve(r) } }
    const child = spawn(process.execPath, [LIVE_TOOL, ...args], { cwd: repo, windowsHide: true })
    child.stdout.setEncoding('utf8').on('data', (d) => { stdout += d })
    child.stderr.setEncoding('utf8').on('data', (d) => { stderr += d })
    child.on('error', (error) => done({ status: null, stdout, stderr, error }))
    child.on('close', (status) => done({ status, stdout, stderr }))
    const timer = setTimeout(() => {
      // 도구가 띄운 supabase CLI 까지 끈다 — 윈도는 kill 이 자식 프로세스를 남긴다.
      if (process.platform === 'win32') spawn('taskkill', ['/pid', String(child.pid), '/T', '/F'], { windowsHide: true })
      else child.kill()
      done({ status: null, stdout, stderr, error: new Error(`${Math.round(timeoutMs / 60_000)}분 안에 끝나지 않았습니다`) })
    }, timeoutMs)
  })
}
