// auth-config-sync
// 운영 콘솔 "서비스 연결"에 넣은 구글·애플 로그인 값을 Supabase 로그인 설정에 넣는다.
//
// 부르는 곳: install.sql 9절 cron(매주 월요일 UTC 5시 15분, pg_net). 입력이 없고 1시간 안의 재호출은 건너뛰므로
// 인증 없이 받는다(verify_jwt 끔). 값을 바꿀 때는 콘솔 서버(admin-api)가 같은 코드로 바로 반영한다 — 이 함수는
// 애플 Secret Key(최대 6개월 유효)가 만료되지 않게 주기적으로 새로 만들어 끼우는 몫이다.
//
// 필요한 값(운영 콘솔 "서비스 연결"): SUPABASE_MANAGEMENT_TOKEN — 이 프로젝트 하나에 Auth Config·Project Settings
// 읽기·쓰기만 준 Supabase 관리 열쇠. 없으면 아무것도 바꾸지 않는다.
// 결과는 purchase_refund_sync('auth_sync')에 남고 콘솔 "서비스 연결 › 로그인"에 보인다.

import { createClient } from "npm:@supabase/supabase-js@2";
import { importPKCS8, SignJWT } from "npm:jose@5";

// ═══ BEGIN auth-sync ═══ (Tools~/SdkAudit R16 이 엣지 함수마다 이 구간이 글자 그대로 같은지 대조한다)
// 콘솔 "서비스 연결"의 로그인 값을 Supabase 로그인 설정(관리 API)에 넣는다. 관리 열쇠는 이 프로젝트 하나에
// Auth Config·Project Settings 읽기·쓰기만 준 열쇠다.
// 기존 설정에 더하기만 하고 지우지 않는다 — 대시보드에서 직접 넣어 둔 클라이언트 ID·돌아올 주소가 콘솔에 없다고
// 빠지면 그 로그인이 깨진다. 값이 없는 로그인은 건드리지 않는다.
// 애플 Secret Key 는 Sign in with Apple 키로 서버가 만든다. 애플이 6개월까지만 받아 150일로 만들고 매주 다시 만든다.
type AuthSyncResult = { ok: boolean; skipped?: boolean; message: string };

const AUTH_SYNC_APPLE_SECRET_DAYS = 150;

function authSyncList(v: string | null | undefined): string[] {
  return (v ?? "").split(",").map((s) => s.trim()).filter((s) => s.length > 0);
}

// 앞의 것을 앞에 두고 겹치는 것은 한 번만 — 애플은 Services ID, 구글은 웹 클라이언트 ID 가 맨 앞이어야 웹·Android 로그인이 된다.
// 맨 앞에 둘 값이 콘솔에 없으면 기존 목록을 앞에 두고 뒤에 더한다 — 대시보드에서 넣어 둔 맨 앞 값이 밀려나지 않게.
function authSyncMerge(first: string[], existing: string[]): string {
  const out: string[] = [];
  for (const s of [...first, ...existing]) if (!out.includes(s)) out.push(s);
  return out.join(",");
}

async function authSyncAppleSecret(teamId: string, keyId: string, privateKey: string, servicesId: string): Promise<string> {
  const key = await importPKCS8(privateKey.replace(/\\n/g, "\n"), "ES256");
  return await new SignJWT({})
    .setProtectedHeader({ alg: "ES256", kid: keyId })
    .setIssuer(teamId)
    .setIssuedAt()
    .setExpirationTime(`${AUTH_SYNC_APPLE_SECRET_DAYS}d`)
    .setAudience("https://appleid.apple.com")
    .setSubject(servicesId)
    .sign(key);
}

async function syncAuthConfig(values: Record<string, string>): Promise<AuthSyncResult> {
  const v = (k: string) => (values[k] ?? "").trim();
  const token = v("SUPABASE_MANAGEMENT_TOKEN");
  if (!token) return { ok: true, skipped: true, message: "no_token" };

  const ref = new URL(Deno.env.get("SUPABASE_URL")!).hostname.split(".")[0];
  const api = `https://api.supabase.com/v1/projects/${ref}/config/auth`;
  const headers = { Authorization: `Bearer ${token}`, "Content-Type": "application/json" };
  const failed = (status: number, what: string): AuthSyncResult => ({
    ok: false,
    message: status === 401 || status === 403
      ? "Supabase 관리 열쇠가 맞지 않거나 권한이 모자랍니다. 열쇠를 다시 넣어 주세요."
      : status === 429
      ? "Supabase 가 요청을 잠시 막았습니다. 잠시 뒤 다시 반영해 주세요."
      : `${what}에 실패했습니다(HTTP ${status}).`,
  });

  let current: Response;
  try {
    current = await fetch(api, { headers, signal: AbortSignal.timeout(15_000) });
  } catch {
    return { ok: false, message: "Supabase 에 연결하지 못했습니다. 잠시 뒤 다시 반영해 주세요." };
  }
  if (!current.ok) {
    await current.body?.cancel();
    return failed(current.status, "로그인 설정 읽기");
  }
  const cfg = (await current.json()) as {
    uri_allow_list?: string | null; external_google_client_id?: string | null; external_apple_client_id?: string | null;
  };

  // 이메일 가입은 끈다 — SDK 에 이메일 로그인이 없고, 켜 두면 남의 지메일 주소로 먼저 가입해 두었다가 그 사람이
  // 같은 주소의 구글 계정으로 처음 로그인할 때 계정을 가로채는 길이 생긴다.
  // 이메일 자동 확인(mailer_autoconfirm)도 끈다 — 이메일 가입을 꺼도 게스트가 자기 정보 수정으로 이메일을 넣을 수 있고,
  // 자동 확인이 켜져 있으면 그 이메일이 확인 메일 없이 확인된 것으로 적혀 같은 주소의 구글·애플 첫 로그인이 그 게스트 계정에 붙는다.
  // 구글·애플은 확인된 이메일만 주므로 꺼도 소셜 첫 가입은 막히지 않는다.
  const patch: Record<string, unknown> = {
    external_anonymous_users_enabled: true,
    external_email_enabled: false,
    mailer_autoconfirm: false,
    security_manual_linking_enabled: true,
  };
  const done: string[] = ["기본 설정"];
  let problem = "";

  const googleWeb = authSyncList(values["GOOGLE_WEB_CLIENT_ID"]);
  const googleIos = authSyncList(values["GOOGLE_IOS_CLIENT_ID"]);
  if (googleWeb.length + googleIos.length > 0) {
    const existing = authSyncList(cfg.external_google_client_id);
    patch.external_google_enabled = true;
    patch.external_google_client_id = googleWeb.length > 0
      ? authSyncMerge([...googleWeb, ...googleIos], existing)
      : authSyncMerge(existing, googleIos);
    if (v("GOOGLE_WEB_CLIENT_SECRET")) patch.external_google_secret = v("GOOGLE_WEB_CLIENT_SECRET");
    done.push("Google");
  }

  const bundles = authSyncList(values["APPLE_BUNDLE_ID"]);
  const servicesId = v("APPLE_SERVICES_ID");
  if (bundles.length > 0) {
    const existing = authSyncList(cfg.external_apple_client_id);
    patch.external_apple_enabled = true;
    patch.external_apple_client_id = servicesId ? authSyncMerge([servicesId, ...bundles], existing) : authSyncMerge(existing, bundles);
    if (servicesId && v("APPLE_TEAM_ID") && v("APPLE_KEY_ID") && v("APPLE_PRIVATE_KEY")) {
      // Secret Key 를 못 만들면 그것만 빼고 나머지는 반영한다 — 키 하나 때문에 구글·돌아올 주소까지 멈추지 않게.
      try {
        patch.external_apple_secret = await authSyncAppleSecret(v("APPLE_TEAM_ID"), v("APPLE_KEY_ID"), v("APPLE_PRIVATE_KEY"), servicesId);
        done.push("Apple · Secret Key");
      } catch {
        problem = "Sign in with Apple 키로 Secret Key 를 만들지 못해 그것만 빼고 반영했습니다. Private key 를 확인해 주세요.";
        done.push("Apple");
      }
    } else {
      done.push("Apple");
    }
  }

  // 안드로이드 애플 로그인이 브라우저에서 앱으로 돌아오는 주소.
  const packages = authSyncList(values["GOOGLE_PACKAGE_NAME"]);
  if (packages.length > 0) {
    patch.uri_allow_list = authSyncMerge(authSyncList(cfg.uri_allow_list), packages.map((p) => `${p}://login-callback`));
    done.push("돌아올 주소");
  }

  let res: Response;
  try {
    res = await fetch(api, { method: "PATCH", headers, body: JSON.stringify(patch), signal: AbortSignal.timeout(15_000) });
  } catch {
    return { ok: false, message: "Supabase 에 연결하지 못했습니다. 잠시 뒤 다시 반영해 주세요." };
  }
  await res.body?.cancel();
  if (!res.ok) return failed(res.status, "로그인 설정 저장");
  if (problem) return { ok: false, message: problem };
  return { ok: true, message: done.join(" · ") };
}

// 로그인 설정 반영에 쓰는 키. 콘솔 값이 없으면 Edge Function Secrets 를 쓴다(server-config 구간과 같은 순서).
// 콘솔 서버는 이 키가 저장되면 바로 반영한다.
const AUTH_SYNC_KEYS = [
  "SUPABASE_MANAGEMENT_TOKEN", "GOOGLE_WEB_CLIENT_ID", "GOOGLE_WEB_CLIENT_SECRET", "GOOGLE_IOS_CLIENT_ID",
  "APPLE_BUNDLE_ID", "APPLE_SERVICES_ID", "APPLE_TEAM_ID", "APPLE_KEY_ID", "APPLE_PRIVATE_KEY", "GOOGLE_PACKAGE_NAME",
];
// ═══ END auth-sync ═══

// ═══ BEGIN usage-count ═══ (Tools~/SdkAudit 가 엣지 함수마다 이 구간이 글자 그대로 같은지 대조한다)
// 운영 콘솔 "서버 상태"의 서버 함수 호출 수에 1 을 더한다. 응답을 기다리지 않고, 실패해도 이 함수의 동작에는 영향이 없다.
// 미리 거치는 OPTIONS 요청은 Supabase 요금도 세지 않아 빼고 센다.
function countCall(name: string, req: Request): void {
  if (req.method === "OPTIONS") return;
  try {
    const key = JSON.parse(Deno.env.get("SUPABASE_SECRET_KEYS") ?? "{}").default ?? "";
    const done = fetch(`${Deno.env.get("SUPABASE_URL")}/rest/v1/rpc/ts_usage_count_call`, {
      method: "POST",
      headers: { apikey: key, Authorization: `Bearer ${key}`, "Content-Type": "application/json" },
      body: JSON.stringify({ p_name: name }),
    }).then((r) => r.body?.cancel()).catch(() => {});
    // 응답을 돌려준 뒤에도 이 요청이 끝날 때까지 함수가 살아 있게 한다.
    (globalThis as { EdgeRuntime?: { waitUntil(p: Promise<unknown>): void } }).EdgeRuntime?.waitUntil(done);
  } catch {
    // 세기 실패는 무시한다.
  }
}
// ═══ END usage-count ═══

const SUPABASE_URL        = Deno.env.get("SUPABASE_URL")!;
const SUPABASE_SECRET_KEY = JSON.parse(Deno.env.get("SUPABASE_SECRET_KEYS")!).default;

const STATUS_KEY = "auth_sync";
const MIN_GAP_SECONDS = 3600;

Deno.serve(async (req) => {
  countCall("auth-config-sync", req);
  const reply = (status: number, body: Record<string, unknown>) =>
    new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });

  const db = createClient(SUPABASE_URL, SUPABASE_SECRET_KEY, {
    auth: { autoRefreshToken: false, persistSession: false },
  });

  const { data: started, error: startError } = await db.rpc("ts_purchase_refund_sync_mark", {
    p_store: STATUS_KEY, p_phase: "start", p_min_gap_seconds: MIN_GAP_SECONDS,
  });
  // 인증 없이 열려 있다 — 응답에는 성공 여부만 싣고, 자세한 내용은 purchase_refund_sync 표(운영 콘솔)에만 남긴다.
  if (startError) {
    console.error(`[auth-config-sync] 시작 기록 실패: ${startError.message}`);
    return reply(500, { ok: false });
  }
  if (!started) return reply(200, { ok: true, skipped: true });

  let result: AuthSyncResult;
  try {
    const { data, error } = await db.rpc("ts_server_config_get");
    if (error) throw new Error(error.message);
    const fromConsole = (data ?? {}) as Record<string, string>;
    const values: Record<string, string> = {};
    for (const k of AUTH_SYNC_KEYS) values[k] = (fromConsole[k] ?? "").trim() || (Deno.env.get(k) ?? "").trim();
    result = await syncAuthConfig(values);
  } catch (e) {
    result = { ok: false, message: `반영하지 못했습니다: ${e instanceof Error ? e.message : e}` };
  }

  const { error: finishError } = await db.rpc("ts_purchase_refund_sync_mark", {
    p_store: STATUS_KEY, p_phase: "finish", p_ok: result.skipped ? null : result.ok, p_message: result.message,
  });
  if (finishError) console.error(`[auth-config-sync] 끝 기록 실패: ${finishError.message}`);
  return reply(result.ok ? 200 : 500, { ok: result.ok });
});
