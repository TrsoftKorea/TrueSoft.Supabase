// purchase-price-sync
// 환율표를 받아 두고, 결제 기록의 빈 금액·빈 원화 값을 채운다.
//
// 부르는 곳: install.sql 9절 cron(매일 UTC 4시 30분 = 한국 13시 30분, pg_net). 입력이 없고 같은 값을 두 번 쓰지 않으며
// 10분 안의 재호출은 건너뛰므로, 인증 없이 받는다(verify_jwt 끔). cron 이 키 없이 부를 수 있게 하려는 것이다.
//
// 하는 일(이 순서로 — 앞에서 금액을 채운 결제가 같은 실행에서 원화 값까지 받게):
//   1. 환율표 — Frankfurter(키 없음, 중앙은행·공식 출처 환율)에서 원화 기준 전체 통화를 받아 fx_rates 에 남긴다.
//   2. 애플 결제 금액 — 옛 방식(StoreKit 1) 결제는 영수증에 가격이 없다. In-App Purchase 키가 있으면 애플에
//      그 거래의 실제 결제 금액을 묻는다. 한 번에 50건, 애플이 없다고 하거나 답하지 못한 건은 7일 뒤에 다시 묻는다.
//   3. 원화 값 — 금액은 있는데 원화 값이 빈 결제(환율 서비스가 잠깐 안 됐던 때 등)를 결제 날짜 환율로 채운다.
// 시간 상한(TOTAL_BUDGET_MS)을 넘기면 남은 건은 다음 실행으로 넘긴다.
//
// 필요한 값(운영 콘솔 "설정" 또는 Edge Function Secrets, 2번만):
//   APPLE_IAP_ISSUER_ID·APPLE_IAP_KEY_ID·APPLE_IAP_PRIVATE_KEY — App Store Connect 의 In-App Purchase 키.
//   없으면 2번을 건너뛰고 금액이 빈 애플 결제 수만 남긴다.
//
// 금액·원화 값이 바뀌면 플레이어 누적 결제액은 DB 트리거(tr_after_purchase_paid_change)가 맞춘다.

import { createClient, type SupabaseClient } from "npm:@supabase/supabase-js@2";

// ═══ BEGIN server-config ═══ (Tools~/SdkAudit R16 이 엣지 함수마다 이 구간이 글자 그대로 같은지 대조한다)
// 스토어·애플 연동 값. 운영 콘솔 "설정"에서 넣은 값(DB Vault)을 먼저 쓰고, 없으면 Edge Function Secrets 를 쓴다.
// 1분 동안 기억해 둔다 — 콘솔에서 바꾼 값은 늦어도 1분 뒤부터 걸린다. 읽기에 실패하면 기억하지 않고 Secrets 로 넘어간다.
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

// ═══ BEGIN krw-rate ═══ (Tools~/SdkAudit R16 이 엣지 함수마다 이 구간이 글자 그대로 같은지 대조한다)
// 결제 금액(micros = 주 단위 ×1,000,000)을 원화 정수로 바꾼다. KRW 면 그대로, 통화를 모르면 null(원화로 치지 않는다).
// 환율은 fx_rates(purchase-price-sync 가 하루 한 번 받아 두는 표)에서 결제 날짜(UTC) 이전 4일 안의 가장 가까운 값 —
// 주말·연휴로 발표가 없는 날을 넘기는 만큼 — 을 쓰고, 표에 없으면 Frankfurter(키 없음, 중앙은행·공식 출처 환율)에
// 그 날짜를 물어 표에 남긴다. 둘 다 안 되면 null — purchase-price-sync 가 다음 실행에서 채운다.
async function toKrw(db: SupabaseClient, micros: number, currency: string | null, at: Date): Promise<number | null> {
  const major = micros / 1_000_000;
  if (!currency) return null;
  const cur = currency.toUpperCase();
  if (cur === "KRW") return Math.round(major);
  const day = at.toISOString().slice(0, 10);
  const windowStart = new Date(at.getTime() - 4 * 86_400_000).toISOString().slice(0, 10);
  try {
    const { data: stored } = await db.from("fx_rates").select("krw_per_unit")
      .eq("currency", cur).lte("rate_date", day).gte("rate_date", windowStart)
      .order("rate_date", { ascending: false }).limit(1).maybeSingle();
    if (stored) return Math.round(major * Number(stored.krw_per_unit));

    const res = await fetch(
      `https://api.frankfurter.dev/v2/rates?base=${encodeURIComponent(cur)}&quotes=KRW&date=${day}`,
      { signal: AbortSignal.timeout(3000) },
    );
    if (!res.ok) {
      await res.body?.cancel();
      return null;
    }
    const rows = (await res.json()) as { date?: string; quote?: string; rate?: number }[];
    const hit = Array.isArray(rows) ? rows.find((r) => r.quote === "KRW") : undefined;
    if (!hit || typeof hit.rate !== "number" || !(hit.rate > 0) || !hit.date) return null;
    await db.from("fx_rates").upsert(
      { currency: cur, rate_date: hit.date, krw_per_unit: hit.rate },
      { onConflict: "currency,rate_date", ignoreDuplicates: true },
    );
    return Math.round(major * hit.rate);
  } catch {
    return null;
  }
}
// ═══ END krw-rate ═══

// ═══ BEGIN apple-transaction ═══ (Tools~/SdkAudit R16 이 엣지 함수마다 이 구간이 글자 그대로 같은지 대조한다)
// 애플 App Store Server API "Get Transaction Info" 로 거래 하나의 실제 결제 금액을 읽는다. 옛 방식(StoreKit 1)으로
// 끝낸 거래도 된다(애플 문서). In-App Purchase 키 — 애플 로그인 키와 다른 키 — 의 발급자 ID·키 ID·.p8 이 필요하다.
// 응답의 서명(JWS)은 검증하지 않는다. 우리 키로 애플 주소에 직접 물어 받은 값이라 중간에 바뀔 길이 없다.
type AppleIapKey = { issuerId: string; keyId: string; privateKey: string };
type AppleTxPrice = { priceMicros: number | null; currency: string | null; environment: string | null };

const APPLE_TX_HOSTS = { production: "https://api.storekit.apple.com", sandbox: "https://api.storekit-sandbox.apple.com" };

async function appleIapKey(): Promise<AppleIapKey | null> {
  const [issuerId, keyId, privateKey] = await Promise.all([
    serverConfig("APPLE_IAP_ISSUER_ID"), serverConfig("APPLE_IAP_KEY_ID"), serverConfig("APPLE_IAP_PRIVATE_KEY"),
  ]);
  return issuerId && keyId && privateKey ? { issuerId, keyId, privateKey } : null;
}

const appleTxB64url = (bytes: Uint8Array) =>
  btoa(String.fromCharCode(...bytes)).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");

// 애플 문서 "Generating JSON Web Tokens for API requests": ES256, kid=키 ID, iss=발급자 ID,
// aud=appstoreconnect-v1, bid=번들 ID, 만료는 발급 뒤 60분 이내.
async function appleServerToken(key: AppleIapKey, bundleId: string): Promise<string> {
  // 붙여넣다 줄바꿈이 글자 그대로의 "\n" 으로 들어가는 경우가 있어 그것도 걷어 낸다.
  const pem = key.privateKey.replace(/-----[^-]+-----/g, "").replace(/\\n/g, "").replace(/\s+/g, "");
  const der = Uint8Array.from(atob(pem), (c) => c.charCodeAt(0));
  const signKey = await crypto.subtle.importKey("pkcs8", der, { name: "ECDSA", namedCurve: "P-256" }, false, ["sign"]);
  const now = Math.floor(Date.now() / 1000);
  const enc = (o: unknown) => appleTxB64url(new TextEncoder().encode(JSON.stringify(o)));
  const signingInput =
    enc({ alg: "ES256", kid: key.keyId, typ: "JWT" }) + "." +
    enc({ iss: key.issuerId, iat: now, exp: now + 600, aud: "appstoreconnect-v1", bid: bundleId });
  // WebCrypto ECDSA 서명은 JWS 가 요구하는 r||s(64바이트) 형식 그대로다.
  const sig = new Uint8Array(await crypto.subtle.sign({ name: "ECDSA", hash: "SHA-256" }, signKey, new TextEncoder().encode(signingInput)));
  return `${signingInput}.${appleTxB64url(sig)}`;
}

// 테스트 결제는 테스트 주소에만 있다. 한쪽에서 못 찾으면(404) 다른 쪽에 묻고, 둘 다 없으면 null.
// 키가 틀렸거나 권한이 없으면(401 등) 예외 — 호출한 쪽이 실패로 남긴다.
async function appleTransactionPrice(
  key: AppleIapKey, bundleId: string, transactionId: string, sandboxFirst: boolean,
): Promise<AppleTxPrice | null> {
  const token = await appleServerToken(key, bundleId);
  const hosts = sandboxFirst
    ? [APPLE_TX_HOSTS.sandbox, APPLE_TX_HOSTS.production]
    : [APPLE_TX_HOSTS.production, APPLE_TX_HOSTS.sandbox];
  for (const host of hosts) {
    const res = await fetch(`${host}/inApps/v1/transactions/${encodeURIComponent(transactionId)}`, {
      headers: { Authorization: `Bearer ${token}` },
      signal: AbortSignal.timeout(5000),
    });
    if (res.status === 404) {
      await res.body?.cancel();
      continue;
    }
    if (!res.ok) {
      const detail = (await res.text()).slice(0, 200);
      throw new Error(`apple_http_${res.status} ${detail}`);
    }
    const body = (await res.json()) as { signedTransactionInfo?: string };
    const part = body.signedTransactionInfo?.split(".")[1];
    if (!part) throw new Error("apple_no_transaction_info");
    const b64 = part.replace(/-/g, "+").replace(/_/g, "/");
    const tx = JSON.parse(new TextDecoder().decode(
      Uint8Array.from(atob(b64 + "=".repeat((4 - (b64.length % 4)) % 4)), (c) => c.charCodeAt(0)),
    )) as { price?: number; currency?: string; environment?: string };
    // price 는 1/1000 단위(milliunits). micros(1/1,000,000)로 맞춘다. 통화가 없으면 금액도 버린다(원화로 오인하지 않게).
    const currency = typeof tx.currency === "string" && tx.currency ? tx.currency : null;
    return {
      priceMicros: typeof tx.price === "number" && currency ? tx.price * 1000 : null,
      currency,
      environment: typeof tx.environment === "string" ? tx.environment : null,
    };
  }
  return null;
}
// ═══ END apple-transaction ═══

const SUPABASE_URL        = Deno.env.get("SUPABASE_URL")!;
const SUPABASE_SECRET_KEY = JSON.parse(Deno.env.get("SUPABASE_SECRET_KEYS")!).default;

const STATUS_KEY = "price_sync";
const MIN_GAP_SECONDS = 600;
// 한 번 돌 때 애플에 금액을 물을 결제 수(결제마다 요청 하나), 답을 못 받은 결제를 다시 물을 때까지 기다리는 날 수.
const APPLE_PRICE_LIMIT = 50;
const APPLE_RETRY_DAYS = 7;
// 한 번 돌 때 원화 값을 채울 결제 수. 같은 통화·날짜는 환율표에서 바로 읽으므로 대부분 바깥 요청이 없다.
const KRW_FILL_LIMIT = 500;
// 시간 상한 — 함수가 실행 시간 제한에 걸려 중간에 죽으면 "끝" 기록이 안 남고 뒤 단계가 그날 통째로 안 돈다.
// 애플이 느려도 3단계 몫이 남게 2단계를 먼저 끊는다. 남은 건은 다음 실행이 이어 받는다.
const APPLE_BUDGET_MS = 60_000;
const TOTAL_BUDGET_MS = 110_000;

Deno.serve(async () => {
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
    console.error(`[purchase-price-sync] 시작 기록 실패: ${startError.message}`);
    return reply(500, { ok: false });
  }
  if (!started) return reply(200, { ok: true, skipped: true });

  const notes: string[] = [];
  let allOk = true;
  let filledTotal = 0;
  const startedAt = Date.now();
  const elapsed = () => Date.now() - startedAt;

  // 끝 기록을 못 남기면 콘솔이 지난 결과를 그대로 보인다 — 무슨 일이 있어도 아래 finish 까지 간다.
  try {
    // ── 1. 환율표 ──────────────────────────────────────────────────────────────
    // 원화 1원이 각 통화로 얼마인지 받아 뒤집는다. 출처마다 발표 날짜가 달라 줄마다 날짜를 그대로 남긴다.
    try {
      const res = await fetch("https://api.frankfurter.dev/v2/rates?base=KRW", { signal: AbortSignal.timeout(10_000) });
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      const rows = (await res.json()) as { date?: string; quote?: string; rate?: number }[];
      const upserts = (Array.isArray(rows) ? rows : [])
        .filter((r) => r.date && r.quote && typeof r.rate === "number" && r.rate > 0)
        .map((r) => ({ currency: r.quote!.toUpperCase(), rate_date: r.date!, krw_per_unit: 1 / r.rate! }));
      if (upserts.length === 0) throw new Error("빈 응답");
      // 결제 확인 때 그 통화로 직접 물어 남긴 값(더 정밀하다)이 있으면 그대로 둔다.
      const { error } = await db.from("fx_rates").upsert(upserts, { onConflict: "currency,rate_date", ignoreDuplicates: true });
      if (error) throw new Error(error.message);
      notes.push(`환율 ${upserts.length}개 통화`);
    } catch (e) {
      allOk = false;
      notes.push(`환율표 받기 실패: ${e instanceof Error ? e.message : e}`);
    }

    // ── 2. 애플 결제 금액 ──────────────────────────────────────────────────────
    const retryBefore = new Date(Date.now() - APPLE_RETRY_DAYS * 86_400_000).toISOString();
    const { data: noPrice, error: noPriceError } = await db
      .from("purchases")
      .select("id, package_name, purchase_token, is_test")
      .eq("store", "apple_app_store")
      .is("price_amount", null)
      .or(`price_checked_at.is.null,price_checked_at.lt.${retryBefore}`)
      .order("price_checked_at", { ascending: true, nullsFirst: true })
      .order("verified_at", { ascending: false })
      .limit(APPLE_PRICE_LIMIT);
    if (noPriceError) {
      allOk = false;
      notes.push(`애플 결제 금액: ${noPriceError.message}`);
    } else if (noPrice && noPrice.length > 0) {
      const key = await appleIapKey();
      if (!key) {
        notes.push(`애플 결제 금액: In-App Purchase 키 없음 — 금액 없는 애플 결제 ${noPrice.length}건${noPrice.length === APPLE_PRICE_LIMIT ? " 이상" : ""}`);
      } else {
        let priced = 0;
        let notFound = 0;
        let noPriceField = 0;
        let failed = 0;
        let lastError = "";
        let keyRejected = "";
        let left = 0;
        for (const p of noPrice) {
          if (elapsed() > APPLE_BUDGET_MS) {
            left = noPrice.length - (priced + notFound + noPriceField + failed);
            break;
          }
          const update: Record<string, unknown> = { price_checked_at: new Date().toISOString() };
          try {
            const price = await appleTransactionPrice(key, p.package_name, p.purchase_token, p.is_test === true);
            if (price === null) notFound++;
            else if (price.priceMicros == null) noPriceField++;
            else {
              update.price_amount = price.priceMicros;
              update.price_currency = price.currency;
              priced++;
            }
            // 시험 여부를 모르던 옛 결제는 애플이 알려 준 환경으로 채운다.
            if (p.is_test === null && price?.environment) update.is_test = price.environment === "Sandbox";
          } catch (e) {
            const msg = String(e instanceof Error ? e.message : e).slice(0, 200);
            // 키·권한 문제는 결제마다 같다 — 더 묻지 않고 멈춘다. 물어본 시각도 안 남겨 키를 고치면 다음 실행에서 바로 묻는다.
            if (/^apple_http_(401|403)\b/.test(msg)) {
              keyRejected = msg;
              break;
            }
            // 그 밖의 실패(애플 일시 오류·시간 초과)는 물어본 시각을 남겨 7일 뒤로 미룬다 — 같은 건이 매일 맨 앞에 쌓여
            // 뒤의 결제가 영영 차례를 못 받는 일을 막는다.
            failed++;
            lastError = msg;
          }
          const { error } = await db.from("purchases").update(update).eq("id", p.id).is("price_amount", null);
          if (error) {
            allOk = false;
            notes.push(`애플 결제 금액: ${error.message}`);
            break;
          }
        }
        if (keyRejected) {
          allOk = false;
          notes.push(`애플 결제 금액: 애플이 In-App Purchase 키를 거절했습니다(${keyRejected})`);
        }
        if (failed > 0) {
          allOk = false;
          notes.push(`애플 결제 금액 확인 실패 ${failed}건(7일 뒤 다시 확인): ${lastError}`);
        }
        notes.push(
          `애플 결제 금액 확인 ${priced}건` +
          (notFound ? `, 애플에 없는 거래 ${notFound}건` : "") +
          (noPriceField ? `, 애플이 금액을 주지 않은 거래 ${noPriceField}건` : "") +
          (left ? `, 시간이 모자라 다음으로 미룬 ${left}건` : ""),
        );
      }
    }

    // ── 3. 원화 값 ─────────────────────────────────────────────────────────────
    // 최근 결제부터 — 환율 출처가 모르는 통화(기기가 보낸 이상한 코드 등)가 오래된 쪽에 쌓여도 새 결제가 막히지 않게.
    const { data: noKrw, error: noKrwError } = await db
      .from("purchases")
      .select("id, price_amount, price_currency, verified_at")
      .not("price_amount", "is", null)
      .is("price_amount_krw", null)
      .order("verified_at", { ascending: false })
      .limit(KRW_FILL_LIMIT);
    if (noKrwError) {
      allOk = false;
      notes.push(`원화 환산: ${noKrwError.message}`);
    } else if (noKrw && noKrw.length > 0) {
      let filled = 0;
      let unconvertible = 0;
      let left = 0;
      // 한 실행 안에서 환율을 못 구한 통화·날짜는 다시 묻지 않는다.
      const missed = new Set<string>();
      for (const p of noKrw) {
        if (elapsed() > TOTAL_BUDGET_MS) {
          left = noKrw.length - filled - unconvertible;
          break;
        }
        const at = new Date(p.verified_at);
        const missKey = `${p.price_currency}|${at.toISOString().slice(0, 10)}`;
        const krw = missed.has(missKey) ? null : await toKrw(db, Number(p.price_amount), p.price_currency, at);
        if (krw === null) {
          missed.add(missKey);
          unconvertible++;
          continue;
        }
        const { error } = await db.from("purchases").update({ price_amount_krw: krw }).eq("id", p.id).is("price_amount_krw", null);
        if (error) {
          allOk = false;
          notes.push(`원화 환산: ${error.message}`);
          break;
        }
        filled++;
      }
      filledTotal += filled;
      // 환산할 수 없는 통화는 고장이 아니라 자료의 한계다 — 건수만 남기고 실패로 치지 않는다(콘솔 줄이 늘 빨갛게 되지 않게).
      notes.push(
        `원화 환산 ${filled}건` +
        (unconvertible ? `, 환율을 구할 수 없는 통화 ${unconvertible}건` : "") +
        (left ? `, 시간이 모자라 다음으로 미룬 ${left}건` : ""),
      );
    }
  } catch (e) {
    allOk = false;
    notes.push(String(e instanceof Error ? e.message : e).slice(0, 300));
  }

  await db.rpc("ts_purchase_refund_sync_mark", {
    p_store: STATUS_KEY, p_phase: "finish", p_ok: allOk, p_message: notes.join(" / ").slice(0, 1000), p_marked: filledTotal,
  });
  return reply(allOk ? 200 : 500, { ok: allOk });
});
