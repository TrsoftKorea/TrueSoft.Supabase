// purchase-refund-sync-google
// Google Play 환불(무효화된 결제) 목록을 가져와 purchases 에 기록한다.
//
// 부르는 곳: install.sql 9절 cron(매일 UTC 4시 = 한국 13시, pg_net) · 운영 콘솔 구매 내역의 "환불 지금 확인".
// 입력이 없고 같은 환불을 두 번 기록하지 않으며 10분 안의 재호출은 건너뛰므로, 인증 없이 받는다
// (verify_jwt 끔). cron 이 키 없이 부를 수 있게 하려는 것이다.
//
// 필요한 값: GOOGLE_SERVICE_ACCOUNT_JSON (운영 콘솔 "설정" 또는 Edge Function Secrets, purchase-verify-google 과 같은 값)
//
// 구글은 무효화된 지 30일 안의 것만 돌려준다. 하루 한 번 30일치를 다시 받으므로 며칠 멈춰도 따라잡는다.
// 앱 이름(package_name)은 최근 120일 안에 결제가 있었던 것만 묻는다 — 오래전에 쓰던 앱까지 매번 묻지 않으려는 것.
//
// 같은 실행에서 시험 결제 여부를 모르는 구글 결제(is_test 칸이 생기기 전 기록)를 구글에 다시 물어 is_test·is_free 를 채운다.
// 한 번에 50건, 답을 못 받은 건은 7일 뒤에 다시 묻는다. 시험·무료로 밝혀지면 누적 결제액은 DB 트리거가 맞춘다.

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

type VoidedPurchase = {
  purchaseToken?: string;
  orderId?: string;
  voidedTimeMillis?: string;
  voidedReason?: number;
  voidedSource?: number;
};

type VoidedList = {
  voidedPurchases?: VoidedPurchase[];
  tokenPagination?: { nextPageToken?: string };
};

const SUPABASE_URL        = Deno.env.get("SUPABASE_URL")!;
const secretKeys          = JSON.parse(Deno.env.get("SUPABASE_SECRET_KEYS")!);
const SUPABASE_SECRET_KEY = secretKeys.default;

const STORE = "google_play";
const MIN_GAP_SECONDS = 600;
const PACKAGE_RECENT_DAYS = 120;
const MAX_PAGES = 50;
// 시험 결제 여부를 모르는 결제를 한 번 돌 때 몇 건까지 구글에 다시 물을지. 결제마다 요청 하나다.
const TEST_BACKFILL_LIMIT = 50;
// 구글이 답하지 않은 결제를 다시 물을 때까지 기다리는 날 수.
const TEST_RETRY_DAYS = 7;

// ═══ BEGIN google-token ═══ (Tools~/SdkAudit R16 이 엣지 함수마다 이 구간이 글자 그대로 같은지 대조한다)
// Google 서비스 계정 JSON → OAuth2 액세스 토큰 발급
// RS256 JWT를 서명해 google token endpoint에 교환 요청합니다.
async function getGoogleAccessToken(serviceAccountJson: string): Promise<string> {
  const sa = JSON.parse(serviceAccountJson);
  const scope = "https://www.googleapis.com/auth/androidpublisher";
  const aud = "https://oauth2.googleapis.com/token";

  const now = Math.floor(Date.now() / 1000);
  const header = { alg: "RS256", typ: "JWT" };
  const payload = {
    iss: sa.client_email,
    scope,
    aud,
    iat: now,
    exp: now + 3600,
  };

  function b64url(obj: unknown): string {
    const json = JSON.stringify(obj);
    const bytes = new TextEncoder().encode(json);
    return btoa(String.fromCharCode(...bytes))
      .replaceAll("+", "-").replaceAll("/", "_").replaceAll("=", "");
  }

  const signingInput = `${b64url(header)}.${b64url(payload)}`;

  // PEM private key → CryptoKey
  const pemBody = sa.private_key
    .replace(/-----BEGIN PRIVATE KEY-----/, "")
    .replace(/-----END PRIVATE KEY-----/, "")
    .replace(/\s+/g, "");
  const keyDer = Uint8Array.from(atob(pemBody), (c) => c.charCodeAt(0));
  const cryptoKey = await crypto.subtle.importKey(
    "pkcs8",
    keyDer,
    { name: "RSASSA-PKCS1-v1_5", hash: "SHA-256" },
    false,
    ["sign"],
  );

  const sigBytes = await crypto.subtle.sign(
    "RSASSA-PKCS1-v1_5",
    cryptoKey,
    new TextEncoder().encode(signingInput),
  );
  const sig = btoa(String.fromCharCode(...new Uint8Array(sigBytes)))
    .replaceAll("+", "-").replaceAll("/", "_").replaceAll("=", "");

  const assertion = `${signingInput}.${sig}`;

  const tokenRes = await fetch(aud, {
    method: "POST",
    headers: { "Content-Type": "application/x-www-form-urlencoded" },
    body: new URLSearchParams({
      grant_type: "urn:ietf:params:oauth:grant-type:jwt-bearer",
      assertion,
    }),
  });

  const tokenJson = await tokenRes.json();
  if (!tokenJson.access_token) {
    throw new Error(`google_token_error: ${JSON.stringify(tokenJson)}`);
  }
  return tokenJson.access_token as string;
}
// ═══ END google-token ═══

// 뒤 쪽이 실패해도 이미 받은 쪽은 돌려준다 — 통째로 버리면 환불이 많은 날마다 한 건도 표시되지 않는다.
async function listVoided(accessToken: string, packageName: string): Promise<{ items: VoidedPurchase[]; error?: string }> {
  const items: VoidedPurchase[] = [];
  let pageToken: string | undefined;
  for (let i = 0; i < MAX_PAGES; i++) {
    const url = new URL(
      `https://androidpublisher.googleapis.com/androidpublisher/v3/applications/${encodeURIComponent(packageName)}/purchases/voidedpurchases`,
    );
    url.searchParams.set("startTime", String(Date.now() - 30 * 24 * 3600 * 1000 + 60_000));
    url.searchParams.set("type", "0");   // 인앱 상품만. 구독은 SDK 가 다루지 않는다.
    if (pageToken) url.searchParams.set("pageSelection.token", pageToken);
    try {
      const res = await fetch(url, { headers: { Authorization: `Bearer ${accessToken}` } });
      if (!res.ok) return { items, error: `HTTP ${res.status} ${(await res.text()).slice(0, 200)}` };
      const body = (await res.json()) as VoidedList;
      items.push(...(body.voidedPurchases ?? []));
      pageToken = body.tokenPagination?.nextPageToken;
    } catch (e) {
      return { items, error: String(e instanceof Error ? e.message : e) };
    }
    if (!pageToken) break;
  }
  return { items };
}

Deno.serve(async () => {
  const reply = (status: number, body: Record<string, unknown>) =>
    new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });

  const db = createClient(SUPABASE_URL, SUPABASE_SECRET_KEY, {
    auth: { autoRefreshToken: false, persistSession: false },
  });

  const { data: started, error: startError } = await db.rpc("ts_purchase_refund_sync_mark", {
    p_store: STORE, p_phase: "start", p_min_gap_seconds: MIN_GAP_SECONDS,
  });
  // 이 함수는 인증 없이 열려 있다 — 응답에는 성공 여부만 싣고, 자세한 내용은 purchase_refund_sync 표(운영 콘솔)에만 남긴다.
  if (startError) {
    console.error(`[purchase-refund-sync-google] 시작 기록 실패: ${startError.message}`);
    return reply(500, { ok: false });
  }
  if (!started) return reply(200, { ok: true, skipped: true });

  const finish = async (ok: boolean, message: string, marked: number) => {
    await db.rpc("ts_purchase_refund_sync_mark", {
      p_store: STORE, p_phase: "finish", p_ok: ok, p_message: message, p_marked: marked,
    });
    return reply(ok ? 200 : 500, { ok });
  };

  const serviceAccountJson = await serverConfig("GOOGLE_SERVICE_ACCOUNT_JSON");
  if (!serviceAccountJson) return finish(false, "GOOGLE_SERVICE_ACCOUNT_JSON 없음", 0);

  const { data: pkgRows, error: pkgError } = await db.rpc("ts_purchase_recent_packages", {
    p_store: STORE, p_days: PACKAGE_RECENT_DAYS,
  });
  if (pkgError) return finish(false, pkgError.message, 0);
  const packages = (pkgRows ?? []) as string[];

  let accessToken: string;
  try {
    accessToken = await getGoogleAccessToken(serviceAccountJson);
  } catch (e) {
    return finish(false, String(e instanceof Error ? e.message : e).slice(0, 300), 0);
  }

  let marked = 0;
  const notes: string[] = [];
  let allOk = true;
  // 최근 결제가 없어도 아래 시험 여부 재확인은 돈다(옛 결제만 남은 게임).
  if (packages.length === 0) notes.push("최근 구글 결제 없음");
  for (const pkg of packages) {
    const { items: voided, error: listError } = await listVoided(accessToken, pkg);
    if (listError) {
      allOk = false;
      notes.push(`${pkg}: ${listError}${voided.length > 0 ? ` (받은 ${voided.length}건은 처리)` : ""}`);
      if (voided.length === 0) continue;
    }
    let pkgMarked = 0;
    const unmatched: string[] = [];
    for (const v of voided) {
      if (!v.purchaseToken) continue;
      const at = v.voidedTimeMillis ? new Date(Number(v.voidedTimeMillis)).toISOString() : new Date().toISOString();
      const { data: changed, error } = await db.rpc("ts_purchase_set_refund", {
        p_store: STORE,
        p_token: v.purchaseToken,
        p_refunded_at: at,
        p_reason: typeof v.voidedReason === "number" ? String(v.voidedReason) : null,
      });
      if (error) {
        allOk = false;
        notes.push(`${pkg}: ${error.message}`);
        break;
      }
      if (changed) pkgMarked++;
      else unmatched.push(v.orderId ?? v.purchaseToken.slice(0, 12));
    }
    // 이미 표시된 것과 우리 기록에 없는 것이 섞여 있다. 기록에 없는 결제를 추적할 때 쓰려고 남긴다.
    if (unmatched.length > 0) console.log(`[purchase-refund-sync-google] ${pkg} 새로 표시 안 된 환불: ${unmatched.join(", ")}`);
    marked += pkgMarked;
    notes.push(`${pkg}: 환불 ${voided.length}건 중 새로 ${pkgMarked}건`);
  }

  // 시험 결제 여부를 모르는 결제(그 칸이 생기기 전 기록)를 구글에 다시 물어 채운다. 한 번에 TEST_BACKFILL_LIMIT 건까지.
  // 물어본 시각(test_checked_at)을 남기고 답을 못 받은 건은 TEST_RETRY_DAYS 동안 뒤로 미룬다 — 영영 답이 없는 결제가
  // 맨 앞에 쌓여 같은 건만 되풀이해 묻는 일을 막는다. 답을 못 받은 결제는 그대로 둔다(모름은 실제 결제로 센다).
  const retryBefore = new Date(Date.now() - TEST_RETRY_DAYS * 86_400_000).toISOString();
  const { data: unknownRows, error: unknownError } = await db
    .from("purchases")
    .select("id, package_name, product_id, purchase_token")
    .eq("store", STORE)
    .is("is_test", null)
    .or(`test_checked_at.is.null,test_checked_at.lt.${retryBefore}`)
    .order("test_checked_at", { ascending: true, nullsFirst: true })
    .order("verified_at", { ascending: false })
    .limit(TEST_BACKFILL_LIMIT);
  if (unknownError) {
    allOk = false;
    notes.push(`시험 결제 확인: ${unknownError.message}`);
  } else if (unknownRows && unknownRows.length > 0) {
    let filled = 0;
    let tests = 0;
    let unanswered = 0;
    let denied = 0;
    for (const p of unknownRows) {
      const url =
        `https://androidpublisher.googleapis.com/androidpublisher/v3/applications/${encodeURIComponent(p.package_name)}` +
        `/purchases/products/${encodeURIComponent(p.product_id)}/tokens/${encodeURIComponent(p.purchase_token)}`;
      // 통신이 끊겨도 이 결제만 건너뛰고 끝까지 간다 — 중간에 함수가 죽으면 "끝남" 기록이 안 남아 콘솔이 지난 결과를 보인다.
      try {
        const res = await fetch(url, { headers: { Authorization: `Bearer ${accessToken}` } });
        const checkedAt = new Date().toISOString();
        if (!res.ok) {
          // 401·403 은 결제 문제가 아니라 서비스 계정 권한 문제다 — 실패로 남겨 콘솔에 보이게 한다.
          if (res.status === 401 || res.status === 403) denied++;
          else unanswered++;
          await res.body?.cancel();
          await db.from("purchases").update({ test_checked_at: checkedAt }).eq("id", p.id);
          continue;
        }
        // purchaseType 은 일반 결제가 아닐 때만 온다. 0 = 라이선스 테스트 계정의 시험 결제, 1·2 = 프로모션 코드·광고 보상(무료).
        const body = (await res.json()) as { purchaseType?: number };
        const isTest = body.purchaseType === 0;
        const isFree = body.purchaseType === 1 || body.purchaseType === 2;
        const { error } = await db.from("purchases")
          .update({ is_test: isTest, is_free: isFree, test_checked_at: checkedAt })
          .eq("id", p.id).is("is_test", null);
        if (error) {
          allOk = false;
          notes.push(`시험 결제 확인: ${error.message}`);
          break;
        }
        filled++;
        if (isTest) tests++;
      } catch (e) {
        unanswered++;
        console.error(`[purchase-refund-sync-google] 시험 여부 확인 실패 ${p.id}: ${e instanceof Error ? e.message : e}`);
      }
    }
    if (denied > 0) {
      allOk = false;
      notes.push(`시험 결제 확인: 구글이 권한 없음으로 거절 ${denied}건 — 서비스 계정 권한을 확인하세요`);
    }
    notes.push(`시험 결제 확인 ${filled}건(그중 시험 ${tests}건)${unanswered ? `, 구글이 답하지 않은 ${unanswered}건` : ""}`);
  }

  return finish(allOk, notes.join(" / ").slice(0, 1000), marked);
});
