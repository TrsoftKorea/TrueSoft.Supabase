// 로그 조회 사용량 장부와 이 PC의 Claude 호출을 날짜별로 맞대 보여 준다.
//   기록: node report.mjs record '<read-chart.js 가 돌려준 JSON>'
//   보기: node report.mjs [시작일, 기본 14일 전]   예) node report.mjs 2026-10-27
// Claude 호출은 이 PC 세션만 센다 — 사람이 연 대시보드, 다른 PC, 클라우드 세션은 안 잡힌다.
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import readline from 'node:readline';
import { fileURLToPath } from 'node:url';

const LEDGER = path.join(path.dirname(fileURLToPath(import.meta.url)), 'ledger.jsonl');
const NAMES = { owumqjyctqhuyailqutd: 'DevilSlayer', wxivrmvtpufeczltward: 'ProjectR' };
const nameOf = (ref) => NAMES[ref] ?? ref;
const MONTHS = { Jan: '01', Feb: '02', Mar: '03', Apr: '04', May: '05', Jun: '06', Jul: '07', Aug: '08', Sep: '09', Oct: '10', Nov: '11', Dec: '12' };
const toGB = (v) => {
  const m = String(v).replace(/,/g, '').match(/^([\d.]+)(KB|MB|GB)$/);
  if (!m) throw new Error(`값을 읽지 못했습니다: ${v}`);
  return Number(m[1]) * { KB: 1e-6, MB: 1e-3, GB: 1 }[m[2]];
};
const toDay = (d) => { const [dd, mon, yyyy] = d.split(' '); return `${yyyy}-${MONTHS[mon]}-${dd}`; };
const gb = (n) => n.toFixed(3).padStart(9);

if (process.argv[2] === 'record') {
  const s = JSON.parse(process.argv[3]);
  if (!s.chart || !s.total) { console.error('그래프나 합계를 못 읽어 기록하지 않았습니다.', process.argv[3]); process.exit(1); }
  const days = Object.fromEntries(Object.entries(s.days).map(([d, v]) => [toDay(d), toGB(v)]));
  fs.appendFileSync(LEDGER, JSON.stringify({ at: new Date().toISOString(), ref: s.ref, total: Number(s.total), days }) + '\n');
  console.log(`기록함: ${nameOf(s.ref)} 합계 ${s.total}GB, ${Object.keys(days).length}일`);
  process.exit(0);
}

const root = path.join(os.homedir(), '.claude', 'projects');
const since = process.argv[2] ? new Date(process.argv[2] + 'T00:00:00+09:00') : new Date(Date.now() - 14 * 864e5);
const SHELL_LOG = /analytics\/endpoints\/(logs|usage|functions)|functions\s+logs/;
const LOG_SCREEN = /supabase\.com\/dashboard\/project\/[^/"\s]+\/(logs|observability|reports)/;

function* walk(d) {
  for (const e of fs.readdirSync(d, { withFileTypes: true })) {
    const p = path.join(d, e.name);
    if (e.isDirectory()) yield* walk(p);
    else if (e.name.endsWith('.jsonl')) yield p;
  }
}
const kstDay = (t) => new Date(new Date(t).getTime() + 9 * 3600e3).toISOString().slice(0, 10);

const claude = {};
let files = 0, toolCalls = 0;
for (const f of walk(root)) {
  if (fs.statSync(f).mtime < since) continue;
  files++;
  for await (const line of readline.createInterface({ input: fs.createReadStream(f), crlfDelay: Infinity })) {
    if (!line.includes('tool_use')) continue;
    let j; try { j = JSON.parse(line); } catch { continue; }
    const t = new Date(j.timestamp);
    if (isNaN(t) || t < since || !Array.isArray(j?.message?.content)) continue;
    for (const c of j.message.content) {
      if (c?.type !== 'tool_use') continue;
      toolCalls++;
      const s = JSON.stringify(c.input ?? {});
      const d = (claude[kstDay(t)] ??= { logs: 0, shell: 0, screen: 0, refs: new Set() });
      if (/query_logs|get_logs/.test(c.name)) { d.logs++; if (c.input?.project_id) d.refs.add(nameOf(c.input.project_id)); }
      else if (/^(Bash|PowerShell)$/.test(c.name) && SHELL_LOG.test(s) && !s.includes('report.mjs')) d.shell++;
      else if (LOG_SCREEN.test(s)) d.screen++;
    }
  }
}

const snaps = fs.existsSync(LEDGER) ? fs.readFileSync(LEDGER, 'utf8').split('\n').filter(Boolean).map((l) => JSON.parse(l)) : [];
const byRef = {};
for (const s of snaps) if (s.ref !== 'org') (byRef[s.ref] ??= []).push(s);
const refs = Object.keys(byRef);
console.log(`장부 ${snaps.length}건 · 세션 파일 ${files}개 · 도구 호출 ${toolCalls}건을 봤습니다.${toolCalls === 0 ? ' 0건이면 기록을 못 읽은 것입니다.' : ''}`);
if (refs.length === 0) console.log('장부가 비어 있습니다. read-chart.js 로 읽어 record 하세요.');

const minDay = since.toISOString().slice(0, 10);
const allDays = new Set(Object.keys(claude).filter((d) => { const c = claude[d]; return c.logs + c.shell + c.screen > 0; }));
for (const r of refs) for (const d of Object.keys(byRef[r].at(-1).days)) if (d >= minDay) allDays.add(d);

console.log(`\n날짜        ${refs.map((r) => nameOf(r).padStart(12)).join('')}   Claude 호출(로그 조회·명령줄·로그 화면)`);
for (const d of [...allDays].sort()) {
  const c = claude[d];
  const note = c ? `${c.logs}·${c.shell}·${c.screen}${c.refs.size ? ` (${[...c.refs].join(', ')})` : ''}` : '0·0·0';
  console.log(`${d}  ${refs.map((r) => gb(byRef[r].at(-1).days[d] ?? 0).padStart(12)).join('')}   ${note}`);
}
console.log('단위 GB. 그래프에 없는 날은 사용량 0 입니다.');

for (const r of refs) {
  const last = byRef[r].at(-1), prev = byRef[r].at(-2);
  const sum = Object.values(last.days).reduce((a, b) => a + b, 0);
  console.log(`\n${nameOf(r)} 합계 ${last.total.toFixed(3)}GB, 날짜별 합 ${sum.toFixed(3)}GB, 그래프에 없는 몫 ${(last.total - sum).toFixed(3)}GB (읽은 때 ${last.at})`);
  if (!prev) continue;
  const grown = Object.entries(last.days).map(([d, v]) => [d, v - (prev.days[d] ?? 0)]).filter(([, v]) => Math.abs(v) > 0.0005);
  console.log(`  지난 기록(${prev.at}) 뒤 늘어난 양: 합계 ${(last.total - prev.total).toFixed(3)}GB${grown.length ? ' · ' + grown.map(([d, v]) => `${d} ${v.toFixed(3)}GB`).join(', ') : ''}`);
}
