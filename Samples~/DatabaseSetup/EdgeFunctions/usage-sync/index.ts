// usage-sync
// 지난 시간의 Supabase 사용량을 시간 단위로 남긴다 — 운영 콘솔 "서버 상태"가 읽는다.
//
// 부르는 곳: install.sql 9절 cron(매시 5분, pg_net). 입력이 없고, 세어 둔 시간을 기억해 같은 시간을 두 번 세지 않으며,
// 5분 안의 재호출은 건너뛰므로 인증 없이 받는다(verify_jwt 끔). cron 이 키 없이 부를 수 있게 하려는 것이다.
//
// 하는 일:
//   1. 저장 용량 — DB 크기와 파일 저장 공간을 그날(게임 시간대) 값으로 덮어쓴다.
//   2. 요청·서버 함수 호출 수와 응답 크기 — Supabase 기록(로그)을 Management API 로 끝난 시간마다 세어 대상(주소·함수)별로 남긴다.
//      기록 읽기 토큰(SUPABASE_ACCESS_TOKEN — 이 프로젝트의 Logs 읽기 권한만 준 개인 토큰)이 있어야 하고, 없으면 이
//      단계만 건너뛴다. 기록은 오래 남지 않으므로 밀린 시간은 최근 24시간까지만 메운다.
//      서버 지표(Metrics API)의 송신 바이트는 쓰지 않는다 — 접속자가 0명이어도 시간당 수 MB 가 나가는 DB 서버 자체의
//      송신이라 플레이어에게 나간 양(요금의 전송량)을 가린다(2026-11-02 두 게임 실측).
// 시간 상한(BUDGET_MS)을 넘기면 남은 시간은 다음 실행으로 넘긴다.

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

const SUPABASE_URL        = Deno.env.get("SUPABASE_URL")!;
const SUPABASE_SECRET_KEY = JSON.parse(Deno.env.get("SUPABASE_SECRET_KEYS")!).default;
const PROJECT_REF         = new URL(SUPABASE_URL).hostname.split(".")[0];

const MIN_GAP_SECONDS = 300;
const HOUR_MS = 3_600_000;
const BACKFILL_HOURS = 24;
const BUDGET_MS = 100_000;

// 요청은 주소에서 대상(표·함수 이름)까지만, 서버 함수는 함수 이름만 남긴다. 미리 거치는 OPTIONS 요청은 세지 않는다
// (서버 함수 요금도 이것은 안 센다). 응답 크기는 관문이 조각으로 나눠 보낸 응답에서 비므로 크기가 남은 건수(sized)를
// 따로 세어 둔다 — 전송량은 ts_admin_usage 가 크기가 남은 응답의 평균으로 빈 것을 채워 어림한다.
const LOGS_SQL = `select 'request' as kind, extract(log_attributes['request.path'], '^/[a-z]+/v1/(?:rpc/)?[^/?]+') as target, count() as n,
  sum(toUInt64OrZero(log_attributes['response.headers.content_length'])) as bytes,
  countIf(log_attributes['response.headers.content_length'] != '') as sized
from logs where source = 'edge_logs' and log_attributes['request.method'] != 'OPTIONS' group by target
union all
select 'function' as kind, extract(log_attributes['request.pathname'], '^/functions/v1/([^/?]+)') as target, count() as n,
  sum(toUInt64OrZero(log_attributes['response.headers.content_length'])) as bytes,
  countIf(log_attributes['response.headers.content_length'] != '') as sized
from logs where source = 'function_edge_logs' and log_attributes['request.method'] != 'OPTIONS' group by target`;

type LogRow = { kind: string; target: string; n: number; bytes: number; sized: number };

let lastResponseMeta = "";

async function countHour(token: string, hourStart: Date): Promise<LogRow[]> {
  const qs = new URLSearchParams({
    sql: LOGS_SQL,
    iso_timestamp_start: hourStart.toISOString(),
    iso_timestamp_end: new Date(hourStart.getTime() + HOUR_MS).toISOString(),
  });
  const res = await fetch(`https://api.supabase.com/v1/projects/${PROJECT_REF}/analytics/endpoints/logs?${qs}`, {
    headers: { Authorization: `Bearer ${token}` },
    signal: AbortSignal.timeout(30_000),
  });
  if (!res.ok) {
    const detail = (await res.text()).slice(0, 200);
    throw new Error(`기록 조회 HTTP ${res.status} ${detail}`);
  }
  type Raw = { kind: string; target: string; n: number | string; bytes: number | string; sized: number | string };
  const body = (await res.json()) as { result?: Raw[]; error?: unknown } & Record<string, unknown>;
  if (body.error) throw new Error(`기록 조회 오류: ${JSON.stringify(body.error).slice(0, 200)}`);
  if (!Array.isArray(body.result)) throw new Error("기록 조회 응답에 결과가 없습니다");
  // 결과 밖에 읽은 양 같은 값이 오는지 본다 — 기록 조회 한도를 조회마다 셀 수 있는지 확인용(2026-11-02).
  const { result: _result, ...meta } = body;
  lastResponseMeta = JSON.stringify({ keys: Object.keys(body), meta, headers: Object.fromEntries(res.headers) }).slice(0, 600);
  return body.result.map((r) => ({
    kind: r.kind, target: r.target || "(알 수 없음)",
    n: Number(r.n) || 0, bytes: Number(r.bytes) || 0, sized: Number(r.sized) || 0,
  }));
}

Deno.serve(async () => {
  const reply = (status: number, body: Record<string, unknown>) =>
    new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });

  const db = createClient(SUPABASE_URL, SUPABASE_SECRET_KEY, {
    auth: { autoRefreshToken: false, persistSession: false },
  });

  const { data: state, error: startError } = await db.rpc("ts_usage_sync_start", { p_min_gap_seconds: MIN_GAP_SECONDS });
  // 인증 없이 열려 있다 — 응답에는 성공 여부만 싣고, 자세한 내용은 usage_sync_state(운영 콘솔)에만 남긴다.
  if (startError) {
    console.error(`[usage-sync] 시작 기록 실패: ${startError.message}`);
    return reply(500, { ok: false });
  }
  if (!state) return reply(200, { ok: true, skipped: true });
  const prev = state as { logs_until: string | null };

  const notes: string[] = [];
  let allOk = true;
  let logsUntil: string | null = null;
  const startedAt = Date.now();

  // 끝 기록을 못 남기면 콘솔이 지난 결과를 그대로 보인다 — 무슨 일이 있어도 아래 finish 까지 간다.
  try {
    // ── 1. 저장 용량(DB 크기·파일 저장 공간) — 그날 줄을 지금 값으로 ─────────────
    {
      const { error } = await db.rpc("ts_usage_record_size");
      if (error) {
        allOk = false;
        notes.push(`저장 용량: ${error.message}`);
      }
    }

    // ── 2. 요청·서버 함수 호출 수─────────────────────────────────────────────
    const token = await serverConfig("SUPABASE_ACCESS_TOKEN");
    if (!token) {
      notes.push("호출 수: 기록 읽기 토큰 없음");
    } else {
      const currentHour = Math.floor(Date.now() / HOUR_MS) * HOUR_MS;
      const oldest = currentHour - BACKFILL_HOURS * HOUR_MS;
      let from = prev.logs_until ? new Date(prev.logs_until).getTime() : oldest;
      if (from < oldest) {
        notes.push(`호출 수: 기록이 남지 않은 ${Math.round((oldest - from) / HOUR_MS)}시간은 건너뜀`);
        from = oldest;
      }
      let hours = 0;
      try {
        for (let h = from; h < currentHour; h += HOUR_MS) {
          if (Date.now() - startedAt > BUDGET_MS) {
            notes.push(`호출 수: 시간이 모자라 ${Math.round((currentHour - h) / HOUR_MS)}시간은 다음으로 미룸`);
            break;
          }
          const hourStart = new Date(h);
          const rows = await countHour(token, hourStart);
          if (rows.length > 0) {
            const { error } = await db.from("usage_hourly").upsert(
              rows.map((r) => ({
                hour: hourStart.toISOString(), kind: r.kind, target: r.target, calls: r.n, bytes: r.bytes, sized: r.sized,
              })),
              { onConflict: "hour,kind,target" },
            );
            if (error) throw new Error(error.message);
          }
          logsUntil = new Date(h + HOUR_MS).toISOString();
          hours++;
        }
        notes.push(`호출 수 ${hours}시간`);
        if (lastResponseMeta) notes.push(`응답 정보 ${lastResponseMeta}`);
      } catch (e) {
        allOk = false;
        notes.push(`호출 수: ${e instanceof Error ? e.message : e}`);
      }
    }
  } catch (e) {
    allOk = false;
    notes.push(String(e instanceof Error ? e.message : e).slice(0, 300));
  }

  await db.rpc("ts_usage_sync_finish", {
    p_ok: allOk, p_message: notes.join(" / ").slice(0, 1000),
    p_logs_until: logsUntil,
  });
  return reply(allOk ? 200 : 500, { ok: allOk });
});
