// push-send — 푸시 대기열(install.sql 23절 push_outbox)을 비운다. 안드로이드는 구글(FCM HTTP v1), 아이폰은 애플(APNs)로 보낸다.
//
// 부르는 곳: DB 의 ts_push_kick(대기열에 넣은 직후 + cron 매분). 대기열만 처리하므로 Verify JWT 를 끄고 배포한다.
// 키(운영 콘솔 "설정 › 서비스 연결 › 푸시 알림"):
//   FCM_SERVICE_ACCOUNT_JSON — Firebase 프로젝트의 서비스 계정 키(JSON). 프로젝트 ID 도 여기서 읽는다.
//   APNS_KEY_ID · APNS_PRIVATE_KEY — 애플 푸시 키(.p8). 팀 ID·번들 ID 는 APPLE_TEAM_ID·APPLE_BUNDLE_ID(첫 값)를 같이 쓴다.
// 한 번 깨어나면 45초 동안 200줄씩 가져가 보낸다. 남은 줄은 다음 깨우기(cron 매분)가 이어 간다.
// 구글·애플이 잠시 바쁘면(429·5xx·시간 초과) 그 줄을 몇 분 뒤 다시 보내고, 줄마다 정한 기한이 지나면 보내지 않는다.
import { createClient } from "npm:@supabase/supabase-js@2";

// ═══ BEGIN server-config ═══ (Tools~/SdkAudit R16 이 엣지 함수마다 이 구간이 글자 그대로 같은지 대조한다)
// 스토어·애플 연동 값. 운영 콘솔 "설정"에서 넣은 값(DB Vault)을 먼저 쓰고, 없으면 Edge Function Secrets 를 쓴다.
// 1분 동안 기억해 둔다 — 콘솔에서 바꾼 값은 늦어도 1분 뒤부터 걸린다. 읽기에 실패하면 이미 기억해 둔 값을 계속 쓰고,
// 기억해 둔 값이 없을 때만(함수가 막 깨어났을 때) Secrets 로 넘어간다.
let serverConfigCache: { at: number; values: Record<string, string> } | null = null;

async function serverConfig(key: string): Promise<string> {
  if (!serverConfigCache || Date.now() - serverConfigCache.at > 60_000) {
    const db = createClient(Deno.env.get("SUPABASE_URL")!, JSON.parse(Deno.env.get("SUPABASE_SECRET_KEYS")!).default, {
      auth: { autoRefreshToken: false, persistSession: false },
    });
    const { data, error } = await db.rpc("ts_server_config_get");
    if (error) console.warn(`[server-config] 콘솔 설정값을 읽지 못해 Secrets 를 씁니다: ${error.message}`);
    else serverConfigCache = { at: Date.now(), values: (data ?? {}) as Record<string, string> };
  }
  const fromConsole = (serverConfigCache?.values[key] ?? "").trim();
  return fromConsole !== "" ? fromConsole : (Deno.env.get(key) ?? "").trim();
}

async function serverConfigList(key: string): Promise<string[]> {
  return (await serverConfig(key)).split(",").map((s) => s.trim()).filter((s) => s.length > 0);
}
// ═══ END server-config ═══

type Text = { title?: string; body?: string };
type AdText = { title_prefix?: string; footer?: string };
type Device = { token: string; platform: "android" | "ios"; language: string | null; apns_sandbox: boolean };
type Item = {
  id: number;
  trigger: string;
  vars: Record<string, string>;
  vars_localized: Record<string, Record<string, string>>;
  data: Record<string, unknown>;
  is_ad: boolean;
  expires_at: string;
  devices: Device[];
};
type Claim = {
  triggers: Record<string, Text & { localized: Record<string, Text> }>;
  ad: AdText & { localized: Record<string, AdText> };
  items: Item[];
};
// retry — 구글·애플이 잠시 바쁘거나(429·5xx) 응답이 늦은 경우. 그 줄은 몇 분 뒤 다시 보낸다(ts_push_report).
type DeviceResult = { token: string; action: "sent" | "sandbox" | "delete" | "failed" | "retry"; error?: string };

const RUN_MS = 45_000;
const BATCH = 200;
const PARALLEL = 20;
// 한 묶음에서 이만큼 넘게, 그리고 절반 넘게 "없는 기기"가 나오면 기기 탓이 아니라 키·설정 탓으로 보고 지우지 않는다.
const DELETE_GUARD_MIN = 20;

// 남은 유효 시간(초). 구글·애플도 이 시간이 지나면 기기가 꺼져 있던 동안 쌓인 알림을 버린다.
const ttlSeconds = (item: Item) => Math.max(0, Math.floor((Date.parse(item.expires_at) - Date.now()) / 1000));

const b64url = (bytes: Uint8Array) =>
  btoa(String.fromCharCode(...bytes)).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
const b64urlJson = (o: unknown) => b64url(new TextEncoder().encode(JSON.stringify(o)));
// 붙여넣다 줄바꿈이 글자 그대로의 "\n" 으로 들어가는 경우가 있어 그것도 걷어 낸다.
const pemDer = (pem: string) =>
  Uint8Array.from(atob(pem.replace(/-----[^-]+-----/g, "").replace(/\\n/g, "").replace(/\s+/g, "")), (c) => c.charCodeAt(0));

// ── 문구 ──────────────────────────────────────────────────────────────────────
// 기기 언어에 맞는 값: 똑같은 언어 → 앞 마디(zh-CN → zh) → 없으면 null(기본 문구를 쓴다).
function pick<T>(localized: Record<string, T> | null | undefined, lang: string | null): T | null {
  if (!localized || !lang) return null;
  if (localized[lang]) return localized[lang];
  const head = lang.split(/[-_]/)[0];
  if (localized[head]) return localized[head];
  const sameHead = Object.keys(localized).find((k) => k.split(/[-_]/)[0] === head);
  return sameHead ? localized[sameHead] : null;
}

function render(claim: Claim, item: Item, lang: string | null): { title: string; body: string } | null {
  const t = claim.triggers[item.trigger];
  if (!t) return null;
  const local = pick(t.localized, lang) ?? {};
  const vars = { ...item.vars, ...(pick(item.vars_localized, lang) ?? {}) };
  const fill = (s: string) => s.replace(/\{(\w+)\}/g, (m, k) => (k in vars ? String(vars[k] ?? "") : m));
  let title = fill(local.title || t.title || "");
  let body = fill(local.body || t.body || "");
  if (item.is_ad) {
    const ad = { ...claim.ad, ...(pick(claim.ad.localized, lang) ?? {}) };
    if (ad.title_prefix) title = `${ad.title_prefix} ${title}`;
    if (ad.footer) body = `${body}\n${ad.footer}`;
  }
  return { title: title.slice(0, 200), body: body.slice(0, 1000) };
}

// ── 구글(FCM HTTP v1) ─────────────────────────────────────────────────────────
type Fcm = { projectId: string; token: string; exp: number };
let fcmCache: Fcm | null = null;

async function fcmAuth(): Promise<Fcm | null> {
  if (fcmCache && fcmCache.exp - Date.now() > 5 * 60_000) return fcmCache;
  const raw = await serverConfig("FCM_SERVICE_ACCOUNT_JSON");
  if (!raw) return null;
  const sa = JSON.parse(raw) as { client_email: string; private_key: string; project_id: string };
  const now = Math.floor(Date.now() / 1000);
  const input = b64urlJson({ alg: "RS256", typ: "JWT" }) + "." + b64urlJson({
    iss: sa.client_email,
    scope: "https://www.googleapis.com/auth/firebase.messaging",
    aud: "https://oauth2.googleapis.com/token",
    iat: now,
    exp: now + 3600,
  });
  const key = await crypto.subtle.importKey("pkcs8", pemDer(sa.private_key), { name: "RSASSA-PKCS1-v1_5", hash: "SHA-256" }, false, ["sign"]);
  const sig = new Uint8Array(await crypto.subtle.sign("RSASSA-PKCS1-v1_5", key, new TextEncoder().encode(input)));
  const res = await fetch("https://oauth2.googleapis.com/token", {
    method: "POST",
    headers: { "Content-Type": "application/x-www-form-urlencoded" },
    body: new URLSearchParams({ grant_type: "urn:ietf:params:oauth:grant-type:jwt-bearer", assertion: `${input}.${b64url(sig)}` }),
    signal: AbortSignal.timeout(10_000),
  });
  const json = await res.json() as { access_token?: string; expires_in?: number; error_description?: string };
  if (!json.access_token) throw new Error(`Firebase 키로 인증하지 못했습니다: ${json.error_description ?? res.status}`);
  fcmCache = { projectId: sa.project_id, token: json.access_token, exp: Date.now() + (json.expires_in ?? 3600) * 1000 };
  return fcmCache;
}

// 기기 쪽(SDK)은 설치 ID(FID)로 등록한다 — FCM 의 token 필드는 폐지 예정이고 fid 를 쓰라고 한다.
async function sendFcm(fcm: Fcm, device: Device, text: { title: string; body: string }, data: Record<string, string>, ttl: number): Promise<DeviceResult> {
  const res = await fetch(`https://fcm.googleapis.com/v1/projects/${fcm.projectId}/messages:send`, {
    method: "POST",
    headers: { Authorization: `Bearer ${fcm.token}`, "Content-Type": "application/json" },
    body: JSON.stringify({
      message: {
        fid: device.token,
        notification: { title: text.title, body: text.body },
        data,
        android: { priority: "high", ttl: `${ttl}s`, notification: { sound: "default" } },
      },
    }),
    signal: AbortSignal.timeout(10_000),
  });
  if (res.ok) {
    await res.body?.cancel();
    return { token: device.token, action: "sent" };
  }
  const err = await res.json().catch(() => null) as { error?: { status?: string; message?: string; details?: { errorCode?: string }[] } } | null;
  const code = err?.error?.details?.find((d) => d.errorCode)?.errorCode ?? err?.error?.status ?? `HTTP ${res.status}`;
  const error = `FCM ${code}: ${(err?.error?.message ?? "").slice(0, 120)}`;
  // 지우는 건 앱이 지워진 등록(UNREGISTERED)뿐이다. SENDER_ID_MISMATCH·맨 404 는 콘솔에 넣은 Firebase 키가 게임과
  // 다른 프로젝트일 때도 나와서, 지우면 키 하나 잘못 넣은 것으로 안드로이드 등록이 전부 사라진다.
  if (code === "UNREGISTERED") return { token: device.token, action: "delete", error: code };
  if (res.status === 429 || res.status >= 500 || code === "UNAVAILABLE" || code === "INTERNAL" || code === "QUOTA_EXCEEDED") {
    return { token: device.token, action: "retry", error };
  }
  return { token: device.token, action: "failed", error };
}

// ── 애플(APNs) ───────────────────────────────────────────────────────────────
type Apns = { jwt: string; at: number; topic: string };
let apnsCache: Apns | null = null;
const APNS_HOSTS = { production: "https://api.push.apple.com", sandbox: "https://api.sandbox.push.apple.com" };

// 애플 문서: 같은 증명을 20분 안에 새로 만들지 말고, 60분 넘게 쓰지 말 것. 40분마다 새로 만든다.
async function apnsAuth(): Promise<Apns | null> {
  if (apnsCache && Date.now() - apnsCache.at < 40 * 60_000) return apnsCache;
  const [keyId, privateKey, teamId, bundles] = await Promise.all([
    serverConfig("APNS_KEY_ID"), serverConfig("APNS_PRIVATE_KEY"), serverConfig("APPLE_TEAM_ID"), serverConfigList("APPLE_BUNDLE_ID"),
  ]);
  if (!keyId || !privateKey || !teamId || bundles.length === 0) return null;
  const key = await crypto.subtle.importKey("pkcs8", pemDer(privateKey), { name: "ECDSA", namedCurve: "P-256" }, false, ["sign"]);
  const input = b64urlJson({ alg: "ES256", kid: keyId }) + "." + b64urlJson({ iss: teamId, iat: Math.floor(Date.now() / 1000) });
  // WebCrypto ECDSA 서명은 JWS 가 요구하는 r||s(64바이트) 형식 그대로다.
  const sig = new Uint8Array(await crypto.subtle.sign({ name: "ECDSA", hash: "SHA-256" }, key, new TextEncoder().encode(input)));
  apnsCache = { jwt: `${input}.${b64url(sig)}`, at: Date.now(), topic: bundles[0] };
  return apnsCache;
}

async function postApns(apns: Apns, host: string, device: Device, payload: string, expiresAt: number): Promise<{ status: number; reason: string }> {
  const res = await fetch(`${host}/3/device/${device.token}`, {
    method: "POST",
    headers: {
      authorization: `bearer ${apns.jwt}`,
      "apns-topic": apns.topic,
      "apns-push-type": "alert",
      "apns-priority": "10",
      "apns-expiration": String(expiresAt),
    },
    body: payload,
    signal: AbortSignal.timeout(10_000),
  });
  const text = await res.text();
  let reason = "";
  try { reason = (JSON.parse(text) as { reason?: string }).reason ?? ""; } catch { reason = text.slice(0, 80); }
  return { status: res.status, reason };
}

// 개발용 빌드의 토큰은 개발 서버로만 간다 — 운영 서버가 BadDeviceToken 이면 개발 서버로 한 번 더 보내고, 되면 기억한다.
async function sendApns(apns: Apns, device: Device, text: { title: string; body: string }, data: Record<string, string>, ttl: number): Promise<DeviceResult> {
  const payload = JSON.stringify({ aps: { alert: { title: text.title, body: text.body }, sound: "default" }, ...data });
  const expiresAt = Math.floor(Date.now() / 1000) + ttl;
  const first = device.apns_sandbox ? APNS_HOSTS.sandbox : APNS_HOSTS.production;
  const second = device.apns_sandbox ? APNS_HOSTS.production : APNS_HOSTS.sandbox;
  let r = await postApns(apns, first, device, payload, expiresAt);
  if (r.status === 200) return { token: device.token, action: "sent" };
  if (r.status === 400 && r.reason === "BadDeviceToken") {
    const again = await postApns(apns, second, device, payload, expiresAt);
    if (again.status === 200) return { token: device.token, action: second === APNS_HOSTS.sandbox ? "sandbox" : "sent" };
    r = again;
  }
  if (r.status === 410 || (r.status === 400 && r.reason === "BadDeviceToken")) {
    return { token: device.token, action: "delete", error: r.reason || `HTTP ${r.status}` };
  }
  const error = `APNs ${r.status} ${r.reason}`;
  if (r.status === 429 || r.status >= 500) return { token: device.token, action: "retry", error };
  return { token: device.token, action: "failed", error };
}

// 한 묶음에서 지울 기기가 많으면 기기들이 아니라 키·설정이 틀린 것이다 — 지우지 않고 실패로만 남긴다.
function guardDeletes(devices: DeviceResult[]): DeviceResult[] {
  const deletes = devices.filter((d) => d.action === "delete").length;
  if (deletes < DELETE_GUARD_MIN || deletes * 2 <= devices.length) return devices;
  console.warn(`[push-send] 한 묶음에서 ${deletes}/${devices.length}대가 없는 기기로 나와 지우지 않았습니다 — 푸시 키·번들 ID 를 확인하세요.`);
  return devices.map((d): DeviceResult => d.action === "delete" ? { ...d, action: "failed", error: `${d.error} · 대량이라 지우지 않음` } : d);
}

// ── 한 줄 보내기 ──────────────────────────────────────────────────────────────
async function sendItem(claim: Claim, item: Item): Promise<{ item: { id: number; status: string; result: string }; devices: DeviceResult[] }> {
  const data: Record<string, string> = {};
  for (const [k, v] of Object.entries(item.data ?? {})) if (v !== null && v !== undefined) data[k] = typeof v === "string" ? v : JSON.stringify(v);

  const ttl = ttlSeconds(item);
  const results: DeviceResult[] = [];
  const reasons = new Set<string>();
  for (const device of item.devices) {
    const text = render(claim, item, device.language);
    if (!text) { reasons.add("문구 없음"); continue; }
    try {
      if (device.platform === "android") {
        const fcm = await fcmAuth();
        if (!fcm) { reasons.add("Firebase 키 없음"); continue; }
        results.push(await sendFcm(fcm, device, text, data, ttl));
      } else {
        const apns = await apnsAuth();
        if (!apns) { reasons.add("애플 푸시 키 없음"); continue; }
        results.push(await sendApns(apns, device, text, data, ttl));
      }
    } catch (e) {
      // 시간 초과·연결 끊김은 다시 보내 볼 만하다. 키 형식 오류 같은 건 다시 해도 같다.
      const error = String(e instanceof Error ? e.message : e).slice(0, 200);
      const transient = e instanceof DOMException || e instanceof TypeError;
      results.push({ token: device.token, action: transient ? "retry" : "failed", error });
    }
  }
  const sent = results.filter((r) => r.action === "sent" || r.action === "sandbox").length;
  const retry = results.some((r) => r.action === "retry");
  for (const r of results) if (r.error) reasons.add(r.error);
  const note = [...reasons].slice(0, 3).join(" · ");
  // 한 대라도 받았으면 다시 보내지 않는다 — 받은 기기에 같은 알림이 두 번 간다.
  const status = sent > 0 ? "sent" : retry ? "retry" : "failed";
  return {
    item: { id: item.id, status, result: sent > 0 ? `${sent}대${note ? ` · ${note}` : ""}` : (note || "보낼 기기 없음") },
    devices: results,
  };
}

async function pool<T, R>(list: T[], size: number, fn: (x: T) => Promise<R>): Promise<R[]> {
  const out: R[] = new Array(list.length);
  let next = 0;
  await Promise.all(Array.from({ length: Math.min(size, list.length) }, async () => {
    while (next < list.length) {
      const i = next++;
      out[i] = await fn(list[i]);
    }
  }));
  return out;
}

Deno.serve(async () => {
  const db = createClient(Deno.env.get("SUPABASE_URL")!, JSON.parse(Deno.env.get("SUPABASE_SECRET_KEYS")!).default, {
    auth: { autoRefreshToken: false, persistSession: false },
  });
  const started = Date.now();
  let total = 0;
  try {
    while (Date.now() - started < RUN_MS) {
      const { data, error } = await db.rpc("ts_push_claim", { p_limit: BATCH });
      if (error) throw new Error(`대기열을 가져오지 못했습니다: ${error.message}`);
      const claim = data as Claim;
      if (!claim.items?.length) break;
      const done = await pool(claim.items, PARALLEL, (item) => sendItem(claim, item));
      const { error: reportError } = await db.rpc("ts_push_report", {
        p_items: done.map((d) => d.item),
        p_devices: guardDeletes(done.flatMap((d) => d.devices)),
      });
      if (reportError) throw new Error(`결과를 남기지 못했습니다: ${reportError.message}`);
      total += claim.items.length;
    }
    return new Response(JSON.stringify({ ok: true, processed: total }), { headers: { "Content-Type": "application/json" } });
  } catch (e) {
    // 가져간 줄은 10분 뒤 ts_push_claim 이 다시 보낸다(세 번까지).
    console.error(`[push-send] ${e instanceof Error ? e.message : e}`);
    return new Response(JSON.stringify({ ok: false, processed: total, error: String(e) }), { status: 500, headers: { "Content-Type": "application/json" } });
  }
});
