// 가짜 데이터 모드 미리보기 서버 + "게시" 버튼 처리. 이 PC(127.0.0.1)에서만 듣는다.
// 게시 = 저장소의 변경을 전부 올린다. 콘솔 화면(Admin~)과 그 밖(SDK·DB·문서)을 커밋 두 개로 나눠 만들고 한 번에 푸시한다.
// 푸시되면 GitHub Actions(admin-deploy.yml)가 실제 콘솔에 배포한다. 게시 버튼이 사용자 승인이다(CLAUDE.md「운영 콘솔 화면 수정」).
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
const SCOPE = 'Admin~/'
// 실제 콘솔(Cloudflare Pages)에 배포되는 건 화면 코드뿐이다 — 콘솔 서버(admin-api)는 따로 올린다.
const SERVER_SCOPE = 'Admin~/supabase/'
// 커밋에 넣지 않는 파일 — Supabase CLI 가 혼자 고치는 버전 기록.
const NEVER = new Set(['Admin~/supabase/.temp/cli-latest'])
// 제가(에이전트) 미리 적어 두는 커밋 요약. 없으면 기본 문구.
const MESSAGE_FILE = path.join(ADMIN_DIR, '.publish-message')
const SDK_MESSAGE_FILE = path.join(ADMIN_DIR, '.publish-message-sdk')
const LIVE_URL = 'https://truebase-admin.pages.dev'
const ACTIONS_URL = 'https://github.com/TrsoftKorea/TrueSoft.Supabase/actions/workflows/admin-deploy.yml'
const TOKEN = crypto.randomBytes(24).toString('hex')
const ALLOWED_ORIGINS = new Set([`http://localhost:${PORT}`, `http://127.0.0.1:${PORT}`])
// 주소창 이름(Host)이 이 PC 가 아니면 거절한다 — 남의 도메인을 127.0.0.1 로 돌려 화면에 심은 암호값을 읽는 공격(DNS rebinding) 차단.
const ALLOWED_HOSTS = new Set([`localhost:${PORT}`, `127.0.0.1:${PORT}`])

const TYPES = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript', '.css': 'text/css', '.svg': 'image/svg+xml', '.png': 'image/png', '.ico': 'image/x-icon', '.json': 'application/json', '.woff2': 'font/woff2' }

const gitRaw = (...args) => execFileSync('git', args, { cwd: REPO, encoding: 'utf8', maxBuffer: 32 << 20 })
const git = (...args) => gitRaw(...args).trim()
const gitOk = (...args) => {
  try { execFileSync('git', args, { cwd: REPO, stdio: 'ignore' }); return true } catch { return false }
}
const readMessage = (file) => (fs.existsSync(file) ? fs.readFileSync(file, 'utf8').trim() : '')

/**
 * 올라갈 변경(새 파일 포함). git status 경로는 저장소 기준이다.
 * -z 로 읽는다 — 줄 형식은 한글 경로를 "\355\225..." 로 감싸 내보내 그대로 쓰면 git 이 못 찾는다.
 * 이름 변경은 "R  새\0옛\0" 으로 온다. 옛 경로도 커밋에 넣어야 옛 파일이 지워진다(안 넣으면 둘 다 남아 C# 클래스가 겹친다).
 */
function pendingFiles() {
  const parts = gitRaw('status', '--porcelain=v1', '-z', '--untracked-files=all').split('\0')
  const out = []
  for (let i = 0; i < parts.length; i++) {
    const entry = parts[i]
    if (!entry) continue
    const status = entry.slice(0, 2).trim()
    const file = entry.slice(3)
    const orig = /^[RC]/.test(entry) ? parts[++i] : undefined
    if (!NEVER.has(file)) out.push(orig ? { status, file, orig } : { status, file })
  }
  return out
}

// 비밀값이 들었을 법한 새 파일 — 무시 목록에 없어도 게시하지 않는다.
const SECRET_LIKE = /(^|\/)\.env(\.[^/]*)?$|\.(pem|key|p8|p12|pfx|jks|keystore)$|(secret|credential|service[-_]?account)[^/]*$/i
const secretLike = (files) => files.filter((f) => f.status === '??' && SECRET_LIKE.test(f.file)).map((f) => f.file)

/** 확인 창이 본 내용과 누를 때의 내용이 같은지 가르는 지문. 커밋은 작업 폴더 내용을 담으므로 파일 내용으로 잰다. */
function fingerprint(f) {
  const full = path.join(REPO, f.file)
  if (!fs.existsSync(full)) return 'deleted'
  try { return git('hash-object', '--', f.file) } catch { return 'unreadable' }
}

/** 바뀐 줄 수. 추적 중인 파일은 HEAD 와 비교하고, 새 파일은 줄을 센다. */
function lineCounts(files) {
  let added = 0
  let removed = 0
  const tracked = files.filter((f) => f.status !== '??').map((f) => f.file)
  if (tracked.length) {
    for (const row of gitRaw('diff', '--numstat', 'HEAD', '--', ...tracked).split('\n').filter(Boolean)) {
      const [a, d] = row.split('\t')
      added += Number(a) || 0
      removed += Number(d) || 0
    }
  }
  for (const f of files.filter((x) => x.status === '??')) {
    try { added += fs.readFileSync(path.join(REPO, f.file), 'utf8').split('\n').length } catch { /* 읽을 수 없는 파일은 세지 않는다 */ }
  }
  return { added, removed }
}

function groups() {
  const files = pendingFiles()
  const consoleFiles = files.filter((f) => f.file.startsWith(SCOPE))
  const sdkFiles = files.filter((f) => !f.file.startsWith(SCOPE))
  return { consoleFiles, sdkFiles }
}

/** 아직 원격에 안 올린 커밋 — 푸시하면 함께 나간다. */
function unpushed() {
  const out = git('log', '--format=%h\t%s', '@{u}..HEAD')
  return out ? out.split('\n').map((l) => { const [hash, subject] = l.split('\t'); return { hash, subject } }) : []
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

/**
 * 그 파일만 커밋한다. 다른 곳에 담겨 있던 변경은 섞지 않는다.
 * `commit -- 경로` 는 그 경로의 작업 폴더 내용을 그대로 담으므로(수정·삭제·이름 변경 포함) 따로 담을 건 새 파일뿐이다.
 * (`add -A`·`add -u` 는 쓰지 않는다 — 저장소 .gitignore 의 `supabase/` 아래 경로나 이미 담긴 삭제를 받으면 오류로 멈춘다.)
 */
function commitFiles(files, message) {
  const fresh = files.filter((f) => f.status === '??').map((f) => f.file)
  if (fresh.length) git('add', '--', ...fresh)
  const paths = files.flatMap((f) => (f.orig ? [f.file, f.orig] : [f.file]))
  git('commit', '-q', '-m', message, '--', ...paths)
  return git('rev-parse', '--short', 'HEAD')
}

/** 확인 창이 본 목록·내용과 지금이 같은지. 다르면 그사이 누가 고친 것이다 — 본 적 없는 것을 올리지 않는다. */
function sameAsSeen(seen, files) {
  if (!Array.isArray(seen) || seen.length !== files.length) return false
  const now = new Map(files.map((f) => [f.file, fingerprint(f)]))
  return seen.every((s) => s && now.get(s.file) === s.hash)
}

function publish(seen) {
  const { consoleFiles, sdkFiles } = groups()
  const all = [...sdkFiles, ...consoleFiles]
  const before = unpushed()
  if (all.length === 0 && before.length === 0) return { ok: false, error: '게시할 변경이 없습니다.' }
  const secrets = secretLike(all)
  if (secrets.length) return { ok: false, error: `비밀값이 들었을 수 있는 새 파일이 있어 게시하지 않았습니다: ${secrets.join(', ')}` }
  if (!sameAsSeen(seen?.files, all) || (seen?.unpushed ?? 0) !== before.length) {
    return { ok: false, stale: true, error: '확인 창을 연 뒤에 파일이 바뀌었습니다. 창을 닫고 다시 열어 바뀐 목록을 확인해 주세요.' }
  }

  const commits = []
  const sdkMessage = readMessage(SDK_MESSAGE_FILE) || 'SDK·DB 변경 게시'
  const consoleMessage = readMessage(MESSAGE_FILE) || '콘솔 화면 게시'
  // SDK·DB 먼저 — 배포 작업은 푸시의 마지막 커밋을 빌드하므로 콘솔 커밋이 맨 위에 오게 한다.
  if (sdkFiles.length) commits.push({ kind: 'sdk', hash: commitFiles(sdkFiles, sdkMessage) })
  if (consoleFiles.length) commits.push({ kind: 'console', hash: commitFiles(consoleFiles, consoleMessage) })
  // 요약은 커밋에 쓰였으니 바로 지운다 — 푸시가 실패해 남으면 다음 게시에 엉뚱한 요약이 붙는다.
  for (const f of [MESSAGE_FILE, SDK_MESSAGE_FILE]) if (fs.existsSync(f)) fs.rmSync(f)
  try {
    git('push', '-q')
  } catch (e) {
    const made = commits.map((c) => c.hash).join(', ')
    return { ok: false, error: `커밋(${made})은 했지만 푸시하지 못했습니다. 다시 게시하면 이 커밋도 함께 올라갑니다: ${String(e.message ?? e).split('\n')[0]}` }
  }
  deployCache = null
  return { ok: true, commits, alsoPushed: before.length, actionsUrl: ACTIONS_URL }
}

function readBody(req) {
  return new Promise((resolve) => {
    let raw = ''
    req.on('data', (c) => { raw += c; if (raw.length > 1 << 20) req.destroy() })
    req.on('end', () => { try { resolve(JSON.parse(raw || '{}')) } catch { resolve({}) } })
  })
}

// ── 실제 반영 상태 ──
// 실제 콘솔이 지금 화면 코드의 마지막 커밋을 담은 판인지 본다. 배포 작업 상태(GitHub)와 실제 콘솔의 version.json 을 함께 본다.
// 외부에 묻는 일이라 20초 안의 재요청은 앞 결과를 쓴다.
let deployCache = null

async function deployStatus() {
  if (deployCache && Date.now() - deployCache.at < 20_000) return deployCache.body
  const checkedAt = new Date().toISOString()
  // 화면 코드(콘솔 서버 제외)를 마지막으로 바꾼 커밋.
  const last = git('log', '-1', '--format=%H%x09%cI%x09%s', 'HEAD', '--', 'Admin~', ':(exclude)Admin~/supabase')
  const [sha = '', committedAt = '', subject = ''] = last.split('\t')
  const pushed = !!sha && gitOk('merge-base', '--is-ancestor', sha, '@{u}')

  let run = null
  try {
    const runs = JSON.parse(execFileSync('gh', ['run', 'list', '--workflow', 'admin-deploy.yml', '--limit', '10',
      '--json', 'headSha,status,conclusion,url,createdAt'], { cwd: REPO, encoding: 'utf8' }))
    // 그 커밋을 담은 가장 최근 배포 작업.
    const hit = runs.find((r) => gitOk('cat-file', '-e', r.headSha) && gitOk('merge-base', '--is-ancestor', sha, r.headSha))
    if (hit) run = { status: hit.status, conclusion: hit.conclusion, url: hit.url, createdAt: hit.createdAt }
  } catch { run = { error: true } }

  let live = null
  try {
    const r = await fetch(`${LIVE_URL}/version.json?t=${Date.now()}`, { cache: 'no-store', signal: AbortSignal.timeout(8000) })
    const v = await r.json()
    const liveSha = typeof v.sha === 'string' ? v.sha : ''
    const known = !!liveSha && gitOk('cat-file', '-e', liveSha)
    live = { sha: liveSha.slice(0, 7), builtAt: v.builtAt ?? null, current: known && gitOk('merge-base', '--is-ancestor', sha, liveSha) }
  } catch { live = { error: true } } // 아직 version.json 이 없는 옛 판이거나 연결 실패

  const { consoleFiles, sdkFiles } = groups()
  const body = {
    ok: true, checkedAt,
    console: { sha: sha.slice(0, 7), committedAt, subject, pushed },
    run, live,
    waiting: { console: consoleFiles.length, sdk: sdkFiles.length, unpushed: unpushed().length },
    liveUrl: LIVE_URL, actionsUrl: ACTIONS_URL,
  }
  deployCache = { at: Date.now(), body }
  return body
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
  // 다른 사이트가 이 화면을 틀(iframe) 안에 넣어 게시 버튼을 몰래 누르게 하지 못하게 막는다.
  res.writeHead(200, {
    'Content-Type': TYPES[ext] ?? 'application/octet-stream',
    'Cache-Control': ext === '.html' ? 'no-store' : 'max-age=3600',
    'X-Frame-Options': 'DENY',
    'Content-Security-Policy': "frame-ancestors 'none'",
  })
  res.end(body)
}

http.createServer(async (req, res) => {
  try {
    if (!ALLOWED_HOSTS.has(req.headers.host ?? '')) return json(res, 403, { ok: false, error: 'forbidden host' })
    if (req.url === '/__publish/status' && req.method === 'GET') {
      if (!checkCaller(req)) return json(res, 403, { ok: false, error: 'forbidden' })
      const { consoleFiles, sdkFiles } = groups()
      const withHash = (files) => files.map((f) => ({ ...f, hash: fingerprint(f) }))
      return json(res, 200, {
        ok: true,
        console: { files: withHash(consoleFiles), message: readMessage(MESSAGE_FILE), lines: lineCounts(consoleFiles), deploys: consoleFiles.some((f) => !f.file.startsWith(SERVER_SCOPE)) },
        sdk: { files: withHash(sdkFiles), message: readMessage(SDK_MESSAGE_FILE), lines: lineCounts(sdkFiles) },
        unpushed: unpushed(),
        blocked: secretLike([...consoleFiles, ...sdkFiles]),
      })
    }
    if (req.url === '/__publish/deploy' && req.method === 'GET') {
      if (!checkCaller(req)) return json(res, 403, { ok: false, error: 'forbidden' })
      return json(res, 200, await deployStatus())
    }
    if (req.url === '/__publish' && req.method === 'POST') {
      if (!checkCaller(req)) return json(res, 403, { ok: false, error: 'forbidden' })
      return json(res, 200, publish(await readBody(req)))
    }
    serveStatic(req, res)
  } catch (e) {
    json(res, 500, { ok: false, error: String(e?.message ?? e).split('\n')[0] })
  }
}).listen(PORT, '127.0.0.1', () => {
  console.log(`가짜 데이터 모드 미리보기: http://localhost:${PORT}  (게시 버튼은 이 PC 에서만 동작)`)
})
