// 가짜 데이터 모드 미리보기 서버 + "게시" 버튼 처리. 이 PC(127.0.0.1)에서만 듣는다.
// 게시 = 콘솔 파일(Admin~)만 커밋하고 푸시한다. 푸시되면 GitHub Actions(admin-deploy.yml)가 실제 콘솔에 배포한다.
//
// 다른 사이트가 이 PC 의 게시 기능을 몰래 부르지 못하게, 띄울 때마다 새 암호값을 만들어 미리보기 화면에만 심고
// 게시 요청은 그 값과 출처(Origin)가 맞을 때만 받는다.
import http from 'node:http'
import fs from 'node:fs'
import path from 'node:path'
import crypto from 'node:crypto'
import { execFileSync } from 'node:child_process'
import { fileURLToPath } from 'node:url'

const PORT = 4174
const HERE = path.dirname(fileURLToPath(import.meta.url))
const ADMIN_DIR = path.resolve(HERE, '..')
const DIST = path.join(ADMIN_DIR, 'dist-mock')
const REPO = path.resolve(ADMIN_DIR, '..')
const SCOPE = 'Admin~'
// 커밋에 넣지 않는 파일 — Supabase CLI 가 혼자 고치는 버전 기록.
const NEVER = new Set(['Admin~/supabase/.temp/cli-latest'])
// 제가(에이전트) 미리 적어 두는 커밋 요약. 없으면 기본 문구.
const MESSAGE_FILE = path.join(ADMIN_DIR, '.publish-message')
const ACTIONS_URL = 'https://github.com/TrsoftKorea/TrueSoft.Supabase/actions/workflows/admin-deploy.yml'
const TOKEN = crypto.randomBytes(24).toString('hex')
const ALLOWED_ORIGINS = new Set([`http://localhost:${PORT}`, `http://127.0.0.1:${PORT}`])

const TYPES = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript', '.css': 'text/css', '.svg': 'image/svg+xml', '.png': 'image/png', '.ico': 'image/x-icon', '.json': 'application/json', '.woff2': 'font/woff2' }

const gitRaw = (...args) => execFileSync('git', args, { cwd: REPO, encoding: 'utf8' })
const git = (...args) => gitRaw(...args).trim()

/** 게시될 콘솔 파일(새 파일 포함). git status 경로는 저장소 기준이다. */
function pendingFiles() {
  // 줄 앞 공백이 상태 칸의 일부다(" M") — 통째로 trim 하면 첫 줄만 한 칸 밀린다.
  const out = gitRaw('status', '--porcelain', '--untracked-files=all', '--', SCOPE)
  return out.split('\n').filter(Boolean)
    .map((line) => ({ status: line.slice(0, 2).trim(), file: line.slice(3).replace(/^"|"$/g, '') }))
    .filter((f) => !NEVER.has(f.file))
}

function json(res, status, body) {
  res.writeHead(status, { 'Content-Type': 'application/json; charset=utf-8', 'Cache-Control': 'no-store' })
  res.end(JSON.stringify(body))
}

// 암호값이 맞아야 하고, 출처가 실려 오면 미리보기 주소여야 한다. 같은 주소의 조회(GET)는 브라우저가 출처를 싣지 않는다.
// 다른 사이트는 암호값을 읽을 수 없고, 직접 지정한 헤더를 실은 교차 요청은 브라우저가 사전 확인에서 막는다.
function checkCaller(req) {
  if (req.headers['x-publish-token'] !== TOKEN) return false
  const origin = req.headers.origin
  if (origin && !ALLOWED_ORIGINS.has(origin)) return false
  const site = req.headers['sec-fetch-site']
  return !site || site === 'same-origin'
}

function publish() {
  const files = pendingFiles()
  if (files.length === 0) return { ok: false, error: '게시할 변경이 없습니다.' }

  const message = (fs.existsSync(MESSAGE_FILE) ? fs.readFileSync(MESSAGE_FILE, 'utf8').trim() : '') || '콘솔 화면 게시'
  const paths = files.map((f) => f.file)
  git('add', '-A', '--', ...paths)
  // 이 파일들만 커밋한다 — 다른 곳(SDK 등)에 담겨 있던 변경은 섞지 않는다.
  git('commit', '-q', '-m', message, '--', ...paths)
  const commit = git('rev-parse', '--short', 'HEAD')
  try {
    git('push', '-q')
  } catch (e) {
    return { ok: false, commit, error: `커밋(${commit})은 했지만 푸시하지 못했습니다: ${String(e.message ?? e).split('\n')[0]}` }
  }
  if (fs.existsSync(MESSAGE_FILE)) fs.rmSync(MESSAGE_FILE)
  return { ok: true, commit, message, files: paths.length, actionsUrl: ACTIONS_URL }
}

function serveStatic(req, res) {
  const url = new URL(req.url, `http://localhost:${PORT}`)
  let file = path.join(DIST, decodeURIComponent(url.pathname))
  if (!file.startsWith(DIST) || !fs.existsSync(file) || fs.statSync(file).isDirectory()) file = path.join(DIST, 'index.html')
  const ext = path.extname(file)
  let body = fs.readFileSync(file)
  if (ext === '.html') {
    body = Buffer.from(body.toString('utf8').replace('<head>', `<head><script>window.__PUBLISH_TOKEN=${JSON.stringify(TOKEN)}</script>`))
  }
  res.writeHead(200, { 'Content-Type': TYPES[ext] ?? 'application/octet-stream', 'Cache-Control': ext === '.html' ? 'no-store' : 'max-age=3600' })
  res.end(body)
}

http.createServer((req, res) => {
  try {
    if (req.url === '/__publish/status' && req.method === 'GET') {
      if (!checkCaller(req)) return json(res, 403, { ok: false, error: 'forbidden' })
      return json(res, 200, { ok: true, files: pendingFiles(), message: fs.existsSync(MESSAGE_FILE) ? fs.readFileSync(MESSAGE_FILE, 'utf8').trim() : '' })
    }
    if (req.url === '/__publish' && req.method === 'POST') {
      if (!checkCaller(req)) return json(res, 403, { ok: false, error: 'forbidden' })
      return json(res, 200, publish())
    }
    serveStatic(req, res)
  } catch (e) {
    json(res, 500, { ok: false, error: String(e?.message ?? e).split('\n')[0] })
  }
}).listen(PORT, '127.0.0.1', () => {
  console.log(`가짜 데이터 모드 미리보기: http://localhost:${PORT}  (게시 버튼은 이 PC 에서만 동작)`)
})
