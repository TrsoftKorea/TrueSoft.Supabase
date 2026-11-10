// 트루베이스 초대 저장소 — 운영 콘솔과 같은 주소(Cloudflare Pages)의 서버 기능. POST /hub
//
// 트루베이스에 초대된 운영자 목록 하나만 둔다. 게임 프로젝트(Supabase)마다 운영자를 따로 두지만, 아직 운영자가 없는
// 대기 프로젝트는 "누가 새 프로젝트를 만들 수 있는지"를 스스로 알 수 없어 여기에 묻는다.
//   - 콘솔: 로그인 때 login 으로 초대 표(7일)를 받는다. 마스터는 invites.* 로 초대 목록을 관리하고, 구글 계정이 없는
//     구성원에게 passwords.reset 으로 임시 비밀번호를 준다. 비밀번호는 본인이 passwords.change 로 정한다 — 마스터는
//     실제 비밀번호를 모른다. 비밀번호는 여기 한 곳에만 있다.
//   - 각 프로젝트의 admin-api: 운영자가 보낸 초대 표를 verify 로 확인한다(새 프로젝트 만들기·운영자 추가·비밀번호 로그인).
// 표는 이 저장소만 서명·확인한다(HS256, 비밀값은 처음 요청 때 D1 에 만든다) — 프로젝트들은 비밀값을 갖지 않는다.
// 쿠키를 쓰지 않고 모든 요청이 구글 토큰·초대 표를 직접 싣기 때문에 다른 주소에서 불러도 된다(로컬에서 띄운 콘솔로 시험할 때).
//
// 설정(hub/wrangler.toml): D1 바인딩 HUB_DB, 공개값 GOOGLE_CLIENT_ID. 마스터 이메일은 비밀값 MASTER_EMAILS(쉼표 구분)
// — 각 프로젝트 admin-api 의 ADMIN_EMAILS 와 같은 사람이어야 한다.

interface D1Stmt {
  bind(...values: unknown[]): D1Stmt
  first<T>(): Promise<T | null>
  all<T>(): Promise<{ results: T[] }>
  run(): Promise<unknown>
}
interface D1Database {
  prepare(query: string): D1Stmt
  batch(statements: D1Stmt[]): Promise<unknown>
}
interface Env {
  HUB_DB: D1Database
  GOOGLE_CLIENT_ID: string
  MASTER_EMAILS?: string
}
type Ctx = { request: Request; env: Env }

type Params = Record<string, unknown>
type Invite = { email: string; display_name: string; invited_by: string | null; invited_at: string }

const TOKEN_TTL_SECONDS = 7 * 24 * 3600
const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

class HubError extends Error {
  constructor(message: string, readonly status: number) {
    super(message)
  }
}

const CORS = {
  'Access-Control-Allow-Origin': '*',
  'Access-Control-Allow-Headers': 'authorization, content-type',
  'Access-Control-Allow-Methods': 'POST, OPTIONS',
}

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { ...CORS, 'Content-Type': 'application/json' } })

export const onRequestOptions = (): Response => new Response(null, { status: 204, headers: CORS })

// ── 저장소 준비 ─────────────────────────────────────────────────────────────────
// 표와 서명 비밀값을 처음 요청 때 만든다 — 저장소를 만든 뒤 따로 돌릴 설치 단계가 없게.
let ready: Promise<void> | null = null
function ensureSchema(db: D1Database): Promise<void> {
  ready ??= (async () => {
    await db.batch([
      db.prepare(`create table if not exists invites (
        email text primary key,
        display_name text not null default '',
        invited_by text,
        invited_at text not null
      )`),
      db.prepare('create table if not exists meta (key text primary key, value text not null)'),
      // 구글 계정이 없는 구성원의 비밀번호. 해시만 둔다. 바꾼 시각보다 먼저 받은 표는 무효다(verifyToken).
      // must_change 1 = 마스터가 초기화한 임시 비밀번호 — 본인이 새로 정하기 전에는 비밀번호 바꾸기만 된다.
      db.prepare(`create table if not exists passwords (
        email text primary key,
        password_hash text not null,
        updated_at text not null,
        updated_by text,
        must_change integer not null default 0
      )`),
    ])
    // must_change 가 생기기 전에 만든 표에 칸을 더한다.
    const { results: cols } = await db.prepare('pragma table_info(passwords)').all<{ name: string }>()
    if (!cols.some((c) => c.name === 'must_change')) {
      await db.prepare('alter table passwords add column must_change integer not null default 0').run()
    }
  })().catch((e) => {
    ready = null // 실패는 기억하지 않는다 — 다음 요청이 다시 시도한다.
    throw e
  })
  return ready
}

let secretCache: CryptoKey | null = null
async function signingKey(db: D1Database): Promise<CryptoKey> {
  if (secretCache) return secretCache
  const fresh = [...crypto.getRandomValues(new Uint8Array(32))].map((b) => b.toString(16).padStart(2, '0')).join('')
  await db.prepare("insert or ignore into meta (key, value) values ('token_secret', ?)").bind(fresh).run()
  const row = await db.prepare("select value from meta where key = 'token_secret'").first<{ value: string }>()
  if (!row) throw new HubError('저장소를 준비하지 못했습니다.', 503)
  secretCache = await crypto.subtle.importKey(
    'raw', new TextEncoder().encode(row.value), { name: 'HMAC', hash: 'SHA-256' }, false, ['sign', 'verify'],
  )
  return secretCache
}

// ── 표(JWT) ─────────────────────────────────────────────────────────────────────
const b64url = (bytes: Uint8Array) =>
  btoa(String.fromCharCode(...bytes)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '')
const fromB64url = (s: string) => Uint8Array.from(atob(s.replace(/-/g, '+').replace(/_/g, '/')), (c) => c.charCodeAt(0))
const encodeJson = (v: unknown) => b64url(new TextEncoder().encode(JSON.stringify(v)))

// 임시 비밀번호로 받은 표(pwc)는 비밀번호 바꾸기에만 쓴다 — 다른 요청과 프로젝트 로그인에는 안 통한다.
const PENDING_TOKEN_TTL_SECONDS = 30 * 60

async function issueToken(db: D1Database, email: string, pending = false): Promise<string> {
  const now = Math.floor(Date.now() / 1000)
  const claims = pending
    ? { email, iat: now, exp: now + PENDING_TOKEN_TTL_SECONDS, aud: 'truebase-hub', pwc: true }
    : { email, iat: now, exp: now + TOKEN_TTL_SECONDS, aud: 'truebase-hub' }
  const head = `${encodeJson({ alg: 'HS256', typ: 'JWT' })}.${encodeJson(claims)}`
  const sig = await crypto.subtle.sign('HMAC', await signingKey(db), new TextEncoder().encode(head))
  return `${head}.${b64url(new Uint8Array(sig))}`
}

async function verifyToken(db: D1Database, token: string, allowPending = false): Promise<string | null> {
  const [h, p, s] = token.split('.')
  if (!h || !p || !s) return null
  try {
    const ok = await crypto.subtle.verify('HMAC', await signingKey(db), fromB64url(s), new TextEncoder().encode(`${h}.${p}`))
    if (!ok) return null
    const payload = JSON.parse(new TextDecoder().decode(fromB64url(p))) as {
      email?: unknown; exp?: unknown; aud?: unknown; iat?: unknown; pwc?: unknown
    }
    if (payload.aud !== 'truebase-hub' || typeof payload.exp !== 'number' || payload.exp * 1000 < Date.now()) return null
    if (typeof payload.email !== 'string' || typeof payload.iat !== 'number') return null
    if (payload.pwc === true && !allowPending) return null
    // 비밀번호를 바꾸면 그 전에 받은 표를 끊는다 — 유출이 의심돼 바꿨을 때 남의 로그인을 바로 막으려는 것이다.
    // 발급 시각은 초까지만 있어 초로 맞춘다(바꾼 바로 그 초에 다시 로그인한 표까지 끊지 않게).
    const pw = await db.prepare('select updated_at from passwords where email = ?').bind(payload.email).first<{ updated_at: string }>()
    if (pw && payload.iat < Math.floor(Date.parse(pw.updated_at) / 1000)) return null
    return payload.email
  } catch {
    return null
  }
}

// ── 비밀번호 해시 (PBKDF2-SHA256) ─────────────────────────────────────────────────
// Workers 의 PBKDF2 반복 수 상한이 10만이다.
const PBKDF2_ITERATIONS = 100_000
const MIN_PASSWORD_LENGTH = 8
const toB64 = (bytes: Uint8Array) => btoa(String.fromCharCode(...bytes))
const fromB64 = (s: string) => Uint8Array.from(atob(s), (c) => c.charCodeAt(0))

async function pbkdf2(secret: string, salt: Uint8Array<ArrayBuffer>, iterations: number): Promise<Uint8Array> {
  const material = await crypto.subtle.importKey('raw', new TextEncoder().encode(secret), 'PBKDF2', false, ['deriveBits'])
  return new Uint8Array(await crypto.subtle.deriveBits({ name: 'PBKDF2', salt, iterations, hash: 'SHA-256' }, material, 256))
}

/** "pbkdf2$반복수$salt$hash" 꼴. 원문은 어디에도 남지 않는다. */
async function hashPassword(password: string): Promise<string> {
  const salt = crypto.getRandomValues(new Uint8Array(16))
  return `pbkdf2$${PBKDF2_ITERATIONS}$${toB64(salt)}$${toB64(await pbkdf2(password, salt, PBKDF2_ITERATIONS))}`
}

async function passwordMatches(password: string, stored: string): Promise<boolean> {
  const [kind, rounds, salt, hash] = stored.split('$')
  if (kind !== 'pbkdf2' || !rounds || !salt || !hash) return false
  const iterations = Number(rounds)
  if (!Number.isFinite(iterations) || iterations <= 0) return false
  try {
    const expected = fromB64(hash)
    const actual = await pbkdf2(password, fromB64(salt), iterations)
    if (actual.length !== expected.length) return false
    let diff = 0 // 길이가 같을 때 상수 시간으로 비교한다.
    for (let i = 0; i < actual.length; i++) diff |= actual[i]! ^ expected[i]!
    return diff === 0
  } catch {
    return false
  }
}

// 임시 비밀번호 — 읽어 전하기 쉽게 헷갈리는 글자(0·O·1·l·I)를 뺀 12자.
const TEMP_ALPHABET = 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789'
function tempPassword(): string {
  const out: string[] = []
  // 나머지 편향이 없게 알파벳 길이의 배수 밖 값은 버린다.
  const limit = 256 - (256 % TEMP_ALPHABET.length)
  while (out.length < 12) {
    for (const b of crypto.getRandomValues(new Uint8Array(16))) {
      if (b < limit && out.length < 12) out.push(TEMP_ALPHABET[b % TEMP_ALPHABET.length]!)
    }
  }
  return out.join('')
}

// ── 구글 ID 토큰 ────────────────────────────────────────────────────────────────
type Jwk = JsonWebKey & { kid?: string }
let googleKeys: { keys: Jwk[]; until: number } | null = null

async function googleKey(kid: string): Promise<Jwk | undefined> {
  if (!googleKeys || googleKeys.until < Date.now() || !googleKeys.keys.some((k) => k.kid === kid)) {
    const res = await fetch('https://www.googleapis.com/oauth2/v3/certs')
    if (!res.ok) throw new HubError('구글 로그인을 확인하지 못했습니다.', 503)
    googleKeys = { keys: ((await res.json()) as { keys: Jwk[] }).keys, until: Date.now() + 3600_000 }
  }
  return googleKeys.keys.find((k) => k.kid === kid)
}

async function verifyGoogleIdToken(idToken: string, clientId: string): Promise<string | null> {
  const [h, p, s] = idToken.split('.')
  if (!h || !p || !s) return null
  try {
    const header = JSON.parse(new TextDecoder().decode(fromB64url(h))) as { kid?: string; alg?: string }
    if (header.alg !== 'RS256' || !header.kid) return null
    const jwk = await googleKey(header.kid)
    if (!jwk) return null
    const key = await crypto.subtle.importKey('jwk', jwk, { name: 'RSASSA-PKCS1-v1_5', hash: 'SHA-256' }, false, ['verify'])
    const ok = await crypto.subtle.verify('RSASSA-PKCS1-v1_5', key, fromB64url(s), new TextEncoder().encode(`${h}.${p}`))
    if (!ok) return null
    const c = JSON.parse(new TextDecoder().decode(fromB64url(p))) as {
      iss?: string; aud?: string; exp?: number; email?: string; email_verified?: boolean
    }
    if (c.iss !== 'accounts.google.com' && c.iss !== 'https://accounts.google.com') return null
    if (c.aud !== clientId || typeof c.exp !== 'number' || c.exp * 1000 < Date.now()) return null
    if (!c.email || c.email_verified !== true) return null
    return c.email.toLowerCase()
  } catch (e) {
    if (e instanceof HubError) throw e
    return null
  }
}

// ── 처리 ────────────────────────────────────────────────────────────────────────
const masters = (env: Env) =>
  new Set((env.MASTER_EMAILS ?? '').split(',').map((s) => s.trim().toLowerCase()).filter(Boolean))

const isInvited = async (db: D1Database, email: string) =>
  (await db.prepare('select 1 as x from invites where email = ?').bind(email).first<{ x: number }>()) !== null

const str = (p: Params, k: string): string => {
  const v = p[k]
  if (typeof v !== 'string' || v.trim() === '') throw new HubError(`${k} 값이 필요합니다.`, 400)
  return v.trim()
}

const cleanEmail = (raw: string): string => {
  const email = raw.trim().toLowerCase()
  if (!EMAIL_RE.test(email)) throw new HubError('이메일 형식이 아닙니다.', 400)
  return email
}

async function handle(env: Env, action: string, params: Params, bearer: string): Promise<unknown> {
  const db = env.HUB_DB
  await ensureSchema(db)

  switch (action) {
    // 콘솔 로그인. 구글 ID 토큰 또는 이메일·비밀번호. 초대됐거나 마스터면 표를 준다.
    // 구글 로그인은 표를 못 받아도 프로젝트 로그인이 따로 되므로 실패는 콘솔이 넘긴다. 비밀번호 로그인은 이 표가 전부다.
    case 'login': {
      let email: string | null
      let mustChange = false
      if (typeof params['password'] === 'string') {
        email = cleanEmail(str(params, 'email'))
        const row = await db
          .prepare('select password_hash, must_change from passwords where email = ?')
          .bind(email)
          .first<{ password_hash: string; must_change: number }>()
        if (!row || !(await passwordMatches(params['password'], row.password_hash))) {
          throw new HubError('이메일 또는 비밀번호가 올바르지 않습니다.', 401)
        }
        mustChange = row.must_change === 1
      } else {
        email = await verifyGoogleIdToken(str(params, 'idToken'), env.GOOGLE_CLIENT_ID)
        if (!email) throw new HubError('구글 로그인을 확인하지 못했습니다.', 401)
      }
      const isMaster = masters(env).has(email)
      const invited = await isInvited(db, email)
      if (!isMaster && !invited) throw new HubError('트루베이스에 초대되지 않은 계정입니다.', 403)
      // 임시 비밀번호면 비밀번호 바꾸기에만 쓰는 표를 준다 — 콘솔은 새 비밀번호를 정하게 한 뒤에 들어간다.
      return { token: await issueToken(db, email, mustChange), email, invited, isMaster, mustChange }
    }

    // 비밀번호로 들어온 구성원이 자기 비밀번호를 바꾼다. 임시 비밀번호로 받은 표도 여기서는 통한다.
    // 바꾸면 그 전에 받은 표가 모두 끊기므로(verifyToken) 새 표를 돌려준다.
    case 'passwords.change': {
      const email = bearer ? await verifyToken(db, bearer, true) : null
      if (!email) throw new HubError('로그인이 필요합니다.', 401)
      const isMaster = masters(env).has(email)
      const invited = await isInvited(db, email)
      if (!isMaster && !invited) throw new HubError('트루베이스에 초대되지 않은 계정입니다.', 403)
      const row = await db.prepare('select password_hash from passwords where email = ?').bind(email).first<{ password_hash: string }>()
      if (!row) throw new HubError('비밀번호로 들어온 계정만 바꿀 수 있습니다.', 400)
      const current = typeof params['currentPassword'] === 'string' ? params['currentPassword'] : ''
      if (!(await passwordMatches(current, row.password_hash))) throw new HubError('지금 비밀번호가 올바르지 않습니다.', 400)
      const next = typeof params['newPassword'] === 'string' ? params['newPassword'] : ''
      if (next.length < MIN_PASSWORD_LENGTH) throw new HubError(`비밀번호는 ${MIN_PASSWORD_LENGTH}자 이상이어야 합니다.`, 400)
      if (next === current) throw new HubError('지금과 다른 비밀번호를 넣어 주세요.', 400)
      await db
        .prepare('update passwords set password_hash = ?, updated_at = ?, updated_by = ?, must_change = 0 where email = ?')
        .bind(await hashPassword(next), new Date().toISOString(), email, email)
        .run()
      return { token: await issueToken(db, email), email, invited, isMaster, mustChange: false }
    }

    // 프로젝트 admin-api 가 운영자의 표를 확인한다. 초대 여부는 지금 다시 본다 — 초대를 지우면 바로 막힌다.
    case 'verify': {
      const email = await verifyToken(db, str(params, 'token'))
      if (!email) throw new HubError('초대 표가 없거나 만료됐습니다. 다시 로그인해 주세요.', 401)
      return { email, invited: await isInvited(db, email), isMaster: masters(env).has(email) }
    }

    // 프로젝트에 운영자를 더할 때 그 이메일이 초대됐는지. 묻는 사람도 초대됐거나 마스터여야 한다 — 초대 목록을 아무나 떠보지 못하게.
    case 'check': {
      const caller = await verifyToken(db, str(params, 'token'))
      if (!caller) throw new HubError('초대 표가 없거나 만료됐습니다. 다시 로그인해 주세요.', 401)
      if (!masters(env).has(caller) && !(await isInvited(db, caller))) throw new HubError('트루베이스에 초대되지 않은 계정입니다.', 403)
      return { invited: await isInvited(db, cleanEmail(str(params, 'email'))) }
    }

    // 운영자 관리에서 고를 트루베이스 구성원. 구성원(초대됐거나 마스터)이면 누구나 본다 — 이메일·이름만.
    case 'members.list': {
      const caller = bearer ? await verifyToken(db, bearer) : null
      if (!caller) throw new HubError('로그인이 필요합니다.', 401)
      if (!masters(env).has(caller) && !(await isInvited(db, caller))) throw new HubError('트루베이스에 초대되지 않은 계정입니다.', 403)
      const { results } = await db
        .prepare('select email, display_name from invites order by display_name, email')
        .all<{ email: string; display_name: string }>()
      return results
    }
  }

  // 아래는 마스터 전용.
  const caller = bearer ? await verifyToken(db, bearer) : null
  if (!caller) throw new HubError('로그인이 필요합니다.', 401)
  if (!masters(env).has(caller)) throw new HubError('마스터만 할 수 있습니다.', 403)

  switch (action) {
    case 'invites.list': {
      const { results } = await db
        .prepare(`select i.email, i.display_name, i.invited_by, i.invited_at, (p.email is not null) as has_password,
                         coalesce(p.must_change, 0) as must_change
                  from invites i left join passwords p on p.email = i.email order by i.invited_at desc`)
        .all<Invite & { has_password: number; must_change: number }>()
      return results.map((r) => ({ ...r, has_password: r.has_password === 1, must_change: r.must_change === 1 }))
    }

    // 비밀번호 초기화. 마스터는 비밀번호를 정하지 않는다 — 임시 비밀번호를 만들어 이 응답에서 한 번만 돌려주고,
    // 그 사람이 처음 들어올 때 새로 정하게 한다(must_change). 그 전에 받은 표는 모두 끊긴다. 초대된 사람만.
    case 'passwords.reset': {
      const email = cleanEmail(str(params, 'email'))
      if (!(await isInvited(db, email))) throw new HubError('트루베이스에 초대된 사람만 비밀번호를 받을 수 있습니다.', 400)
      const password = tempPassword()
      await db
        .prepare(`insert into passwords (email, password_hash, updated_at, updated_by, must_change) values (?, ?, ?, ?, 1)
                  on conflict (email) do update set password_hash = excluded.password_hash,
                    updated_at = excluded.updated_at, updated_by = excluded.updated_by, must_change = 1`)
        .bind(email, await hashPassword(password), new Date().toISOString(), caller)
        .run()
      return { password }
    }

    case 'invites.add': {
      const email = cleanEmail(str(params, 'email'))
      const name = typeof params['displayName'] === 'string' ? params['displayName'].trim().slice(0, 40) : ''
      await db
        .prepare(`insert into invites (email, display_name, invited_by, invited_at) values (?, ?, ?, ?)
                  on conflict (email) do update set display_name = excluded.display_name`)
        .bind(email, name, caller, new Date().toISOString())
        .run()
      return null
    }

    case 'invites.remove': {
      const email = cleanEmail(str(params, 'email'))
      await db.batch([
        db.prepare('delete from invites where email = ?').bind(email),
        db.prepare('delete from passwords where email = ?').bind(email),
      ])
      return null
    }

    default:
      throw new HubError(`알 수 없는 action: ${action}`, 400)
  }
}

export const onRequestPost = async ({ request, env }: Ctx): Promise<Response> => {
  let action = ''
  let params: Params = {}
  try {
    const body = (await request.json()) as { action?: unknown; params?: unknown }
    if (typeof body.action !== 'string') throw new Error()
    action = body.action
    params = (body.params ?? {}) as Params
  } catch {
    return json({ ok: false, error: '요청 본문을 읽을 수 없습니다.' }, 400)
  }
  const auth = request.headers.get('Authorization') ?? ''
  const bearer = auth.startsWith('Bearer ') ? auth.slice(7) : ''
  try {
    return json({ ok: true, data: await handle(env, action, params, bearer) })
  } catch (e) {
    if (e instanceof HubError) return json({ ok: false, error: e.message }, e.status)
    console.error(`[hub] ${action} 실패: ${e}`)
    return json({ ok: false, error: '처리에 실패했습니다.' }, 500)
  }
}
