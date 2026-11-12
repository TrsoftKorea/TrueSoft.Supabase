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
// 제가(에이전트) 미리 적어 두는 커밋 요약. 없으면 기본 문구.
const MESSAGE_FILE = path.join(ADMIN_DIR, '.publish-message')
const SDK_MESSAGE_FILE = path.join(ADMIN_DIR, '.publish-message-sdk')
// 이번에 올릴 파일 목록(한 줄에 경로 하나, 저장소 기준). 요약과 함께 적어 둔다 — 같은 폴더를 쓰는 다른 세션의
// 하다 만 변경이 이 요약을 달고 섞여 올라가지 않게, 목록에 없는 파일이 있으면 게시를 멈춘다. 없으면 검사하지 않는다.
const FILES_FILE = path.join(ADMIN_DIR, '.publish-files')
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

/** 요약 글(첫 줄 제목 + 항목) → 커밋 메시지. git 은 빈 줄이 있어야 첫 줄만 제목으로 본다 — 없으면 항목까지 한 줄 제목이 된다. */
function commitMessage(text) {
  const [title = '', ...body] = text.split(/\r?\n/).map((l) => l.trimEnd())
  const rest = body.filter((l) => l.trim())
  return rest.length ? `${title}\n\n${rest.join('\n')}` : title
}

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
    out.push(orig ? { status, file, orig } : { status, file })
  }
  return out
}

// 비밀값이 들었을 법한 새 파일 — 무시 목록에 없어도 게시하지 않는다. 미리 담아 둔(A) 새 파일도 본다.
// 이름으로 못 거르는 것(구글 서비스 계정 키의 기본 이름 "프로젝트-1a2b….json" 등)은 내용에 키가 들었는지로 본다.
const SECRET_LIKE = /(^|\/)\.env(\.[^/]*)?$|\.(pem|key|p8|p12|pfx|jks|keystore)$|(secret|credential|service[-_]?account)[^/]*$/i
const SECRET_CONTENT = /"private_key"\s*:|-----BEGIN [A-Z ]*PRIVATE KEY-----/
function secretLike(files) {
  return files
    .filter((f) => f.status === '??' || f.status === 'A')
    .filter((f) => {
      if (SECRET_LIKE.test(f.file)) return true
      try {
        const full = path.join(REPO, f.file)
        return fs.statSync(full).size < 1 << 20 && SECRET_CONTENT.test(fs.readFileSync(full, 'utf8'))
      } catch { return false }
    })
    .map((f) => f.file)
}

/** 요약과 함께 적어 둔 파일 목록. 없으면 null — 그때는 섞임 검사를 하지 않는다. */
function plannedFiles() {
  if (!fs.existsSync(FILES_FILE)) return null
  return new Set(fs.readFileSync(FILES_FILE, 'utf8').split(/\r?\n/).map((l) => l.trim()).filter(Boolean))
}

/** 목록에 없는 변경 — 다른 세션이 하던 것일 수 있다. 이름 변경은 새 이름으로 본다. */
function unplannedFiles(files) {
  const plan = plannedFiles()
  // 판 번호만 바뀐 package.json 은 확인 창에서 고른 것이라 목록에 없어도 된다.
  const skipPkg = pkgVersionOnly()
  return plan ? files.filter((f) => !plan.has(f.file) && !(skipPkg && f.file === PKG)).map((f) => f.file) : []
}

/** package.json 이 마지막 게시와 판 번호만 다른지(또는 같은지). */
function pkgVersionOnly() {
  try { return withoutVersion(fs.readFileSync(PKG_PATH, 'utf8')) === withoutVersion(headPkgText()) } catch { return false }
}

const textHash = (s) => crypto.createHash('sha1').update(s).digest('hex')

// 게임이 받아 쓰는 SDK 코드. 이것이 바뀌면 package.json 의 판 번호도 올라가야 게시한다(2026-11-12 사용자 결정) —
// 게임은 판 번호로 무엇이 바뀌었는지 가른다. DB·서버 함수(Samples~/DatabaseSetup)·문서·검사 도구는 게임 빌드에 안 들어가 뺀다.
const SDK_CODE = /^(Runtime\/|Editor\/|Samples~\/(?!DatabaseSetup\/))/
const parseVersion = (s) => (/^(\d+)\.(\d+)\.(\d+)/.exec(s ?? '') ?? []).slice(1).map(Number)
// 게시 한 번에 한 단계만 오른다 — 게시 전에 쌓인 변경은 모두 같은 판이다. 다음 판이 될 수 있는 셋 중 하나여야 한다.
const nextVersions = (v) => {
  const [x = 0, y = 0, z = 0] = parseVersion(v)
  return [`${x}.${y}.${z + 1}`, `${x}.${y + 1}.0`, `${x + 1}.0.0`]
}
const PKG = 'package.json'
const PKG_PATH = path.join(REPO, PKG)
const VERSION_FIELD = /("version"\s*:\s*")([^"]*)(")/
const headPkgText = () => { try { return gitRaw('show', `HEAD:${PKG}`) } catch { return '' } }
const versionOf = (text) => { try { return JSON.parse(text).version ?? '' } catch { return '' } }
// 판 번호만 빼고 본 package.json — 확인 창에서 판을 고르면 이 파일이 바뀌므로, 창이 본 내용과 대조할 때 판 번호는 뺀다.
const withoutVersion = (text) => text.replace(/\r\n/g, '\n').replace(VERSION_FIELD, '$1$3')
const hasSdkCode = (files) => files.some((f) => SDK_CODE.test(f.file) || (f.orig && SDK_CODE.test(f.orig)))
const tagName = (v) => `v${v}`
const tagExists = (tag) => gitOk('rev-parse', '-q', '--verify', `refs/tags/${tag}`)

/** 확인 창에 보일 판 번호 — 마지막 게시 판, 지금 적힌 판, 고를 수 있는 판. SDK 코드가 그대로면 "올리지 않음"도 고를 수 있다. */
function versionInfo(files) {
  const before = versionOf(headPkgText())
  const now = versionOf(fs.readFileSync(PKG_PATH, 'utf8'))
  const sdkChanged = hasSdkCode(files)
  const choices = [...(sdkChanged ? [] : [before]), ...nextVersions(before)]
  return { before, now, sdkChanged, choices }
}

/** 고른 판 번호를 package.json 에 적는다. 다른 줄은 건드리지 않는다. */
function writeVersion(v) {
  const text = fs.readFileSync(PKG_PATH, 'utf8')
  if (versionOf(text) === v) return
  fs.writeFileSync(PKG_PATH, text.replace(VERSION_FIELD, `$1${v}$3`))
}

function versionNotRaised(files) {
  const code = files.filter((f) => SDK_CODE.test(f.file) || (f.orig && SDK_CODE.test(f.orig)))
  const now = versionOf(fs.readFileSync(PKG_PATH, 'utf8'))
  const before = versionOf(headPkgText())
  if (now === before) {
    return code.length ? { now, files: code.map((f) => f.file) } : null
  }
  // 판 번호를 바꿨으면 SDK 코드가 없어도 한 단계인지 본다.
  const allowed = nextVersions(before)
  return allowed.includes(now) ? null : { now, before, allowed }
}

/** 확인 창이 본 내용과 누를 때의 내용이 같은지 가르는 지문. 커밋은 작업 폴더 내용을 담으므로 파일 내용으로 잰다. */
function fingerprint(f) {
  const full = path.join(REPO, f.file)
  if (!fs.existsSync(full)) return 'deleted'
  if (f.file === PKG) return textHash(withoutVersion(fs.readFileSync(full, 'utf8')))
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
  const out = git('log', '--format=%H\t%s', '@{u}..HEAD')
  return out ? out.split('\n').map((l) => { const [full, subject] = l.split('\t'); return { hash: full.slice(0, 7), full, subject } }) : []
}

/** 확인 창이 본 요약 글 — 창을 연 사이 요약이 바뀌면 사용자가 못 본 메시지로 커밋된다. */
const messagesNow = () => textHash(`${readMessage(SDK_MESSAGE_FILE)}\n\0\n${readMessage(MESSAGE_FILE)}`)

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
  git('commit', '-q', '-m', commitMessage(message), '--', ...paths)
  return git('rev-parse', '--short', 'HEAD')
}

/** 확인 창이 본 목록·내용과 지금이 같은지. 다르면 그사이 누가 고친 것이다 — 본 적 없는 것을 올리지 않는다. */
function sameAsSeen(seen, files) {
  if (!Array.isArray(seen)) return false
  // 판 번호만 바뀐 package.json 은 창에서 판을 고르면 생기거나 사라진다 — 양쪽에서 빼고 본다.
  if (pkgVersionOnly()) {
    seen = seen.filter((s) => s?.file !== PKG)
    files = files.filter((f) => f.file !== PKG)
  }
  if (seen.length !== files.length) return false
  const now = new Map(files.map((f) => [f.file, fingerprint(f)]))
  return seen.every((s) => s && now.get(s.file) === s.hash)
}

/**
 * 이 PC 에만 있고 GitHub 에 없는 판 이름표. 앞 게시에서 이름표만 못 올린 경우다.
 * 지금 브랜치에 든 이름표만 본다 — 다른 로컬 브랜치의 이름표까지 올리면 그 브랜치 커밋도 GitHub 에 올라간다.
 * GitHub 조회는 1분 기억한다 — 상태 조회마다 물으면 연결이 느릴 때 그동안 미리보기 서버 전체가 멈춘다.
 */
let remoteTagCache = null
function missingRemoteTags({ fresh = false } = {}) {
  const local = git('tag', '-l', 'v*', '--merged', 'HEAD').split('\n').filter((t) => /^v\d+\.\d+\.\d+$/.test(t))
  if (!local.length) return []
  try {
    if (fresh || !remoteTagCache || Date.now() - remoteTagCache.at > 60_000) {
      remoteTagCache = {
        at: Date.now(),
        tags: new Set(git('ls-remote', '--tags', 'origin').split('\n')
          .map((l) => l.split('\t')[1]?.replace(/^refs\/tags\//, '').replace(/\^\{\}$/, '')).filter(Boolean)),
      }
    }
    return local.filter((t) => !remoteTagCache.tags.has(t))
  } catch { return [] } // GitHub 에 못 물으면 모른다 — 게시를 막지 않는다(이번에 만든 이름표는 publish 가 따로 올린다)
}

/** 이름표를 올린다. 실패하면 사유 한 줄, 성공하면 빈 글. */
function pushTags(tags) {
  if (!tags.length) return ''
  try {
    git('push', '-q', 'origin', ...tags.map((t) => `refs/tags/${t}`))
    remoteTagCache = null
    return ''
  } catch (e) {
    return `${e.stderr || e.message || e}`.trim().split('\n')[0]
  }
}

function publish(seen) {
  let { consoleFiles, sdkFiles } = groups()
  let all = [...sdkFiles, ...consoleFiles]
  const before = unpushed()
  if (all.length === 0 && before.length === 0) {
    // 올릴 변경은 없어도 앞 게시에서 못 올린 이름표가 있으면 그것만 올린다.
    const left = missingRemoteTags({ fresh: true })
    if (!left.length) return { ok: false, error: '게시할 변경이 없습니다.' }
    const tagError = pushTags(left)
    return tagError ? { ok: false, error: `판 이름표(${left.join(', ')})를 올리지 못했습니다: ${tagError}` } : { ok: true, commits: [], tags: left, alsoPushed: 0 }
  }
  const secrets = secretLike(all)
  if (secrets.length) return { ok: false, error: `비밀값이 들었을 수 있는 새 파일이 있어 게시하지 않았습니다: ${secrets.join(', ')}` }
  const unplanned = unplannedFiles(all)
  if (unplanned.length) {
    return { ok: false, error: `이번 요약에 없는 파일이 섞여 있어 게시하지 않았습니다(다른 작업일 수 있습니다): ${unplanned.join(', ')}` }
  }
  const seenUnpushed = Array.isArray(seen?.unpushed) ? seen.unpushed : []
  if (!sameAsSeen(seen?.files, all)
      || seenUnpushed.length !== before.length || !before.every((c, i) => seenUnpushed[i] === c.full)
      || seen?.messages !== messagesNow()) {
    return { ok: false, stale: true, error: '확인 창을 연 뒤에 파일이나 요약이 바뀌었습니다. 창을 닫고 다시 열어 바뀐 내용을 확인해 주세요.' }
  }

  // 확인 창에서 고른 판 번호를 적는다. 고를 수 있는 판이 아니면 거절한다(창을 연 사이 마지막 게시가 바뀐 경우 등).
  const info = versionInfo(all)
  const chosen = typeof seen?.version === 'string' ? seen.version : info.now
  if (!info.choices.includes(chosen)) {
    return { ok: false, error: `판 번호 ${chosen} 은 고를 수 없습니다. 고를 수 있는 판: ${info.choices.join(' · ')}` }
  }
  const raised = chosen !== info.before
  if (raised && tagExists(tagName(chosen))) {
    return { ok: false, error: `판 ${chosen} 은 이미 게시된 적이 있어(이름표 ${tagName(chosen)}) 다시 쓸 수 없습니다.` }
  }
  writeVersion(chosen)
  ;({ consoleFiles, sdkFiles } = groups())
  all = [...sdkFiles, ...consoleFiles]

  const stale = versionNotRaised(all)
  if (stale?.allowed) {
    return { ok: false, error: `판 번호가 ${stale.before} 에서 ${stale.now} 로 한 번에 두 단계 이상 바뀌어 게시하지 않았습니다. 게시 한 번에는 ${stale.allowed.join(' · ')} 중 하나만 됩니다.` }
  }
  if (stale) {
    return { ok: false, error: `SDK 코드가 바뀌었는데 판 번호(${stale.now})가 그대로라 게시하지 않았습니다. 판 번호를 올려 주세요: ${stale.files.slice(0, 5).join(', ')}${stale.files.length > 5 ? ' 외' : ''}` }
  }

  // 게임이 판 번호로 골라 받을 수 있게 이름표(태그)를 단다. 지난 판에 이름표가 없으면(이 기능 전에 낸 판) 그 판에도 단다 —
  // 마지막 게시 커밋이 곧 그 판의 마지막 모습이다.
  const tags = []
  const headBefore = git('rev-parse', 'HEAD')
  if (raised && info.before && !tagExists(tagName(info.before))) {
    git('tag', '-a', tagName(info.before), '-m', `SDK ${info.before}`, headBefore)
    tags.push(tagName(info.before))
  }

  const commits = []
  const sdkMessage = readMessage(SDK_MESSAGE_FILE) || 'SDK·DB 변경 게시'
  const consoleMessage = readMessage(MESSAGE_FILE) || '콘솔 화면 게시'
  // SDK·DB 먼저 — 배포 작업은 푸시의 마지막 커밋을 빌드하므로 콘솔 커밋이 맨 위에 오게 한다.
  if (sdkFiles.length) commits.push({ kind: 'sdk', hash: commitFiles(sdkFiles, sdkMessage) })
  // 이번 판의 이름표는 판 번호를 마지막으로 바꾼 커밋(= 그 판의 SDK 커밋)에 단다. 판을 올린 게시가 푸시에 실패해
  // 다시 게시할 때도 여기서 붙는다 — "이번에 올렸는지"가 아니라 "이름표가 있는지"로 가른다.
  const current = versionOf(fs.readFileSync(PKG_PATH, 'utf8'))
  if (current && !tagExists(tagName(current))) {
    git('tag', '-a', tagName(current), '-m', `SDK ${current}`, git('log', '-1', '--format=%H', '--', PKG))
    tags.push(tagName(current))
  }
  if (consoleFiles.length) commits.push({ kind: 'console', hash: commitFiles(consoleFiles, consoleMessage) })
  // 요약은 커밋에 쓰였으니 바로 지운다 — 푸시가 실패해 남으면 다음 게시에 엉뚱한 요약이 붙는다.
  for (const f of [MESSAGE_FILE, SDK_MESSAGE_FILE, FILES_FILE]) if (fs.existsSync(f)) fs.rmSync(f)
  try {
    git('push', '-q')
  } catch (e) {
    // 받아온 뒤 커밋이 다시 얹히면 이름표가 옛 커밋을 가리킨다 — 지우고, 다시 게시할 때 새로 단다.
    for (const t of tags) gitOk('tag', '-d', t)
    const made = commits.map((c) => c.hash).join(', ')
    const text = `${e.stderr || ''}\n${e.message ?? e}`.trim()
    // GitHub 에 더 새 커밋이 있으면(다른 PC 에서 게시) 받아오기 전까지 몇 번을 눌러도 거절된다.
    if (/rejected|non-fast-forward|fetch first/i.test(text)) {
      return { ok: false, error: `커밋(${made})은 했지만 GitHub 에 더 새 커밋이 있어 올리지 못했습니다(다른 PC 에서 게시한 것). 먼저 받아온(pull) 뒤 다시 게시해야 합니다.` }
    }
    return { ok: false, error: `커밋(${made})은 했지만 푸시하지 못했습니다. 다시 게시하면 이 커밋도 함께 올라갑니다: ${text.split('\n')[0]}` }
  }
  deployCache = null
  // 이름표는 커밋이 올라간 뒤에 올린다 — 먼저 올리면 GitHub 에 커밋 없는 이름표만 남을 수 있다.
  // 여기서 실패해도 커밋은 이미 올라갔다. 이름표는 남겨 두고, 다음 게시(변경이 없어도)가 올린다.
  // 이번에 만든 이름표는 GitHub 조회와 상관없이 올린다 — 조회가 실패해 빈 목록이 오면 안 올리고도 성공으로 보인다.
  const tagError = pushTags([...new Set([...tags, ...missingRemoteTags({ fresh: true })])])
  if (tagError) {
    return { ok: false, error: `커밋(${commits.map((c) => c.hash).join(', ')})은 올렸지만 판 이름표(${tags.join(', ') || '-'})를 올리지 못했습니다. 게시를 다시 누르면 이름표만 올라갑니다: ${tagError}` }
  }
  return { ok: true, commits, tags, version: chosen, alsoPushed: before.length, actionsUrl: ACTIONS_URL }
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
// 외부에 묻는 일이라 짧게(4초) 앞 결과를 쓴다 — 화면이 3초마다 물어도 GitHub 에는 그보다 드물게 묻는다.
let deployCache = null
let lastFetchAt = 0

// 배포 작업을 화면에 보일 묶음. 단계 대부분이 2초 안에 끝나 번호로 보이면 건너뛰는 것처럼 보인다.
const DEPLOY_STAGES = ['준비', '콘솔 빌드', '콘솔 올리기', '고객센터 올리기']

/** 지금 몇 번째 묶음인지. 올리는 단계는 콘솔·고객센터가 이름이 같아 admin-deploy.yml 의 순서로 가른다. */
function deployProgress(steps) {
  let stage = 0
  const stages = steps.map((s) => {
    if (/npm run build:support/i.test(s.name)) stage = 3
    else if (stage === 0 && /npm run build/i.test(s.name)) stage = 1
    else if (stage === 1 && /wrangler/i.test(s.name)) stage = 2
    return stage
  })
  const at = stages.find((_, i) => steps[i].status !== 'completed') ?? DEPLOY_STAGES.length - 1
  return { done: at, total: DEPLOY_STAGES.length, current: DEPLOY_STAGES[at] }
}

/** 진행 중인 배포 작업의 단계 — 몇 번째 묶음을 하고 있는지. */
function runSteps(id) {
  try {
    const { jobs = [] } = JSON.parse(execFileSync('gh', ['run', 'view', String(id), '--json', 'jobs'], { cwd: REPO, encoding: 'utf8' }))
    const steps = jobs.flatMap((j) => j.steps ?? [])
    if (!steps.length) return null
    return deployProgress(steps)
  } catch { return null }
}

async function deployStatus() {
  if (deployCache && Date.now() - deployCache.at < 4_000) return deployCache.body
  const checkedAt = new Date().toISOString()
  // 화면 코드(콘솔 서버 제외)를 마지막으로 바꾼 커밋. %s 는 제목 한 줄(빈 줄 앞)만 준다.
  const last = git('log', '-1', '--format=%H%x09%cI%x09%s', 'HEAD', '--', 'Admin~', ':(exclude)Admin~/supabase')
  const [sha = '', committedAt = '', subject = ''] = last.split('\t')
  const pushed = !!sha && gitOk('merge-base', '--is-ancestor', sha, '@{u}')

  let run = null
  try {
    const runs = JSON.parse(execFileSync('gh', ['run', 'list', '--workflow', 'admin-deploy.yml', '--limit', '10',
      '--json', 'databaseId,headSha,status,conclusion,url,createdAt,updatedAt'], { cwd: REPO, encoding: 'utf8' }))
    // 그 커밋을 담은 가장 최근 배포 작업.
    const hit = runs.find((r) => gitOk('cat-file', '-e', r.headSha) && gitOk('merge-base', '--is-ancestor', sha, r.headSha))
    if (hit) {
      run = { status: hit.status, conclusion: hit.conclusion, url: hit.url, createdAt: hit.createdAt, updatedAt: hit.updatedAt }
      if (hit.status !== 'completed') run.steps = runSteps(hit.databaseId)
    }
  } catch { run = { error: true } }

  let live = null
  try {
    const r = await fetch(`${LIVE_URL}/version.json?t=${Date.now()}`, { cache: 'no-store', signal: AbortSignal.timeout(8000) })
    const v = await r.json()
    const liveSha = typeof v.sha === 'string' ? v.sha : ''
    let known = !!liveSha && gitOk('cat-file', '-e', liveSha)
    // 다른 PC 에서 게시한 판이면 이 PC 가 그 커밋을 모른다 — 받아와 보되, 매번 하면 느려서 5분에 한 번만.
    if (liveSha && !known && Date.now() - lastFetchAt > 5 * 60_000) {
      lastFetchAt = Date.now()
      gitOk('fetch', '-q')
      known = gitOk('cat-file', '-e', liveSha)
    }
    live = { sha: liveSha.slice(0, 7), builtAt: v.builtAt ?? null, known, current: known && gitOk('merge-base', '--is-ancestor', sha, liveSha) }
  } catch { live = { error: true } } // 아직 version.json 이 없는 옛 판이거나 연결 실패

  // 지금 어느 단계인지 한 단어로. 화면은 이것만 보고 문구를 고른다.
  const phase = !pushed ? 'unpushed'
    : live?.current ? 'live'
    : run?.error ? 'unknown'
    : !run ? 'starting'                                   // 푸시는 됐는데 GitHub 가 아직 배포 작업을 안 만들었다
    : run.status !== 'completed' ? (run.status === 'in_progress' ? 'running' : 'starting')
    : run.conclusion !== 'success' ? 'failed'
    : live?.error ? 'unknown'
    : !live?.known ? 'elsewhere'                          // 실제 콘솔이 이 PC 가 모르는 판 — 다른 PC 에서 게시했다
    : 'propagating'                                       // 배포는 끝났고 실제 콘솔에 새 판이 퍼지는 중

  const { consoleFiles, sdkFiles } = groups()
  const body = {
    ok: true, checkedAt, phase,
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
        messages: messagesNow(),
        blocked: secretLike([...consoleFiles, ...sdkFiles]),
        unplanned: unplannedFiles([...consoleFiles, ...sdkFiles]),
        version: versionInfo([...consoleFiles, ...sdkFiles]),
        tagsToPush: missingRemoteTags(),
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
