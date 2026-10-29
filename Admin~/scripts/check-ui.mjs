// 콘솔 화면 꼴 검사(npm run check:ui). 규칙과 이유는 .claude/rules/console.md 에 있다.
//
// 비슷한 화면이 공용 부품 대신 각자 짠 꼴로 갈라지는 것을 잡는다. 기계가 확실히 가를 수 있는 것만 여기 두고,
// 판단이 필요한 것(설명 문구·이름 표기 등)은 규칙 문장으로 둔다.
//
// 이미 있는 어긋남은 check-ui-baseline.json 에 "규칙|파일" 별 개수로 적어 두고(기준선), 그보다 늘면 실패한다.
// 줄었는데 기준선을 그대로 두어도 실패한다 — 안 줄이면 나중에 같은 만큼 다시 늘어도 못 잡는다.
// 기준선은 --update-baseline 으로 줄일 수만 있다. 정말 예외인 곳은 아래 EXEMPT 에 이유와 함께 적는다.
//
//   node scripts/check-ui.mjs                  검사
//   node scripts/check-ui.mjs --list           남은 어긋남을 규칙별로 전부 나열
//   node scripts/check-ui.mjs --update-baseline 줄어든 개수를 기준선에 반영
//   node scripts/check-ui.mjs --if-changed     콘솔 화면 파일이 바뀌었을 때만 검사(작업 끝 훅용)
//
// 실패는 종료 코드 2 — 작업 끝 훅이 이 출력을 에이전트에게 돌려줘 고치게 한다.
import fs from 'node:fs'
import path from 'node:path'
import { execFileSync } from 'node:child_process'
import { fileURLToPath } from 'node:url'

const ADMIN = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const REPO = path.resolve(ADMIN, '..')
const SRC = path.join(ADMIN, 'src')
const BASELINE = path.join(ADMIN, 'scripts', 'check-ui-baseline.json')
const args = new Set(process.argv.slice(2))

// 공용 부품 자신은 검사하지 않는다 — 꼴을 정의하는 곳이다.
const SKIP_DIRS = [path.join(SRC, 'components', 'ui'), path.join(SRC, 'mock')]

/** 상대 휘도(0~1). 연한 색 바탕을 가르는 데만 쓴다. */
function luminance(hex) {
  const c = [0, 2, 4].map((i) => parseInt(hex.slice(i, i + 2), 16) / 255)
    .map((v) => (v <= 0.03928 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4))
  return 0.2126 * c[0] + 0.7152 * c[1] + 0.0722 * c[2]
}

// 각 규칙: find(text) 가 [위치…] 를 돌려준다. bad 는 반드시 걸려야 하고 good 은 걸리면 안 된다(검사기가 살아 있는지
// 매번 확인 — 정규식이 망가져 아무것도 못 잡으면 "깨끗함"과 구분되지 않는다).
const RULES = [
  {
    id: 'U1', name: '큰 탭 줄을 직접 짬', fix: '공용 큰 탭 Tabs 를 쓴다',
    find: (t) => matchAll(t, /border-b-2 -mb-px|-mb-px border-b-2/g),
    bad: "'h-10 px-4 text-sm border-b-2 -mb-px transition-colors'", good: "'border-b border-neutral-200'",
  },
  {
    id: 'U2', name: '화면을 덮는 창을 직접 짬', fix: '되돌리기 어려운 확인은 ConfirmDialog, 값을 넣는 창은 EditDialog 를 쓴다',
    find: (t) => matchAll(t, /fixed inset-0/g),
    bad: '<div className="fixed inset-0 z-50 bg-black/40">', good: '<div className="fixed top-0">',
  },
  {
    id: 'U3', name: '제목 태그를 직접 씀', fix: 'PageHeader(페이지)·CardHeader(카드)·창 부품의 title 을 쓴다',
    find: (t) => matchAll(t, /<h[1-6][\s>]/g),
    bad: '<h3 className="text-base font-semibold">', good: '<CardHeader title="우편" description="…" />',
  },
  {
    id: 'U4', name: '연한 고정색 바탕(어두운 화면에서 안 바뀜)', fix: 'bg-neutral-50·bg-surface·bg-brand-soft 같은 테마 색을 쓴다',
    find: (t) => matchAll(t, /bg-\[#([0-9a-fA-F]{6})\]/g).filter((m) => luminance(m[1]) > 0.6),
    bad: '<div className="bg-[#eef4ff]">', good: '<div className="bg-[#1677ff] text-white">',
  },
  {
    id: 'U5', name: '쪽 넘김 버튼을 직접 짬', fix: '표 바닥 줄 TableFooter 를 쓴다',
    find: (t) => matchAll(t, /w-7 h-7 inline-flex items-center justify-center rounded border border-neutral-200/g),
    bad: 'className="w-7 h-7 inline-flex items-center justify-center rounded border border-neutral-200 disabled:opacity-40"',
    good: 'className="w-7 h-7 inline-flex items-center justify-center rounded hover:bg-neutral-100"',
  },
  {
    id: 'U6', name: '파란·흰 테두리 버튼을 직접 짬', fix: '공용 Button(variant primary·outline·dangerOutline)을 쓴다',
    // 아이콘만 있는 작은 버튼·글자 링크·닫기(X)는 공용 Button 꼴이 아니라 대상이 아니다 — 꼴이 Button 과 겹치는 것만 본다.
    find: (t) => matchAll(t, /<button\b/g).filter((m) => {
      const end = t.indexOf('</button>', m.index)
      const cls = /className=["{`]([^"}`]*)/.exec(t.slice(m.index, end < 0 ? m.index + 600 : end))?.[1] ?? ''
      // 모서리는 rounded·rounded-md 둘 다 — rounded 만 쓴 흰 테두리 버튼(예약 발송 켜기·끄기)이 빠져나갔다.
      return /bg-\[#1677ff\]/.test(cls) || (/(^|\s)rounded(-md)?(\s|$)/.test(cls) && /border-neutral-300/.test(cls))
    }),
    bad: '<button onClick={toggle} className="px-2 py-1 rounded border border-neutral-300 text-xs">끄기</button>',
    good: '<button onClick={close} className="text-neutral-400 hover:text-neutral-700" aria-label="닫기">',
  },
  {
    id: 'U7', name: '입력칸 꼴을 화면마다 정의함', fix: 'components/ui/Input 의 inputCls(입력·고르기 칸)·areaCls(여러 줄)를 가져다 쓴다',
    // 이름이 아니라 내용으로 본다 — fieldCls·inp 처럼 이름을 바꿔 다시 정의한 것이 이름 검사를 빠져나갔다(2026-10-28).
    // 입력칸 테두리 꼴(둥근 모서리 + neutral-300 테두리)을 담은 문자열 상수. 버튼 꼴(hover·inline-flex)은 U6 몫이라 뺀다.
    find: (t) => matchAll(t, /\bconst\s+\w+\s*=\s*(['"`])([^'"`\n]*)\1/g)
      .filter((m) => /\bborder-neutral-300\b/.test(m[2]) && /\brounded/.test(m[2]) && !/hover:|inline-flex/.test(m[2])),
    bad: "const inp = 'w-full h-9 px-3 rounded-md border border-neutral-300 text-sm'",
    good: "const tagCls = 'inline-flex items-center rounded-md border border-neutral-300 hover:bg-neutral-50'",
  },
  {
    id: 'U9', name: '작은 탭을 직접 짬', fix: '카드 안에서 가르면 Segmented, 페이지를 가르면 큰 탭 Tabs 를 쓴다',
    find: (t) => matchAll(t, /inline-flex rounded-lg bg-neutral-100 p-0\.5/g),
    bad: '<div className="inline-flex rounded-lg bg-neutral-100 p-0.5">', good: '<Segmented value={v} onChange={set} options={opts} />',
  },
  {
    id: 'U10', name: '입력칸에 테두리 꼴을 직접 적음', fix: 'inputCls·areaCls·inputCompactCls·dangerInputCls(components/ui/Input) 뒤에 폭만 붙인다',
    // <input>·<select>·<textarea> 의 className 글자에 테두리가 있으면 직접 적은 것이다. 체크박스·라디오·파일은 꼴이 달라 뺀다.
    find: (t) => matchAll(t, /<(input|select|textarea)\b/g).filter((m) => {
      // 이 태그만 본다 — 다음 태그가 시작하거나 스스로 닫히는 데까지(여는 "<" 다음부터 찾아야 자기 자신에서 안 끊긴다)
      const tag = t.slice(m.index + 1, m.index + 700).split(/\/>|<[a-zA-Z]/)[0]
      const cls = /className="([^"]*)"/.exec(tag)?.[1] ?? ''
      return /\bborder\b/.test(cls) && !/type="(checkbox|radio|file|color)"/.test(tag)
    }),
    bad: '<input value={v} className="w-full h-9 px-3 rounded-md border border-neutral-300 text-sm" />',
    good: '<input value={v} className={inputCls + \' w-full\'} />',
  },
  {
    id: 'U11', name: '이름·버튼·선택지에 괄호로 부연함', fix: '괄호를 빼고 단위는 입력칸 옆, 조건은 칸 아래 안내로 옮긴다',
    // 입력칸 이름(label)·표 머리(th)·선택지(option)·버튼의 글자와, 탭·선택지·항목 이름 문자열(label:·name:)을 본다.
    // 글자나 값({…}) 바로 뒤, 또는 한 칸 띄운 뒤의 "(" 만 잡는다 — formatCount(x) 같은 함수 호출은 앞에 공백이 없어 빠진다.
    find: (t) => [
      ...matchAll(t, /<(label|th|option|button|Button)\b[^>]*>([^<]*)<\/\1>/g).filter((m) => ASIDE_PAREN.test(m[2])),
      ...matchAll(t, /\b(label|name): *'([^']*)'/g).filter((m) => ASIDE_PAREN.test(m[2])),
    ],
    bad: '<label className="block text-xs">차단 시간(분)</label>',
    good: '<label className="block text-xs">차단 시간</label>',
  },
  {
    id: 'U8', name: '브라우저 기본 알림·확인 창', fix: 'ConfirmDialog·ErrorBanner 를 쓴다',
    find: (t) => matchAll(t, /\bwindow\.(alert|confirm|prompt)\s*\(|(?<![.\w])alert\s*\(/g),
    bad: "if (window.confirm('지울까요?')) remove()", good: '<ConfirmDialog open={asking} title="지울까요?" />',
  },
]

// 정말 예외인 곳. 이유를 적지 않은 예외는 두지 않는다.
const EXEMPT = {
  // 로그인 전 화면은 콘솔 틀(메뉴·머리) 밖의 전체 화면이라 자체 배치를 쓴다.
  U3: ['pages/Login.tsx', 'pages/ProjectSelect.tsx'],
  // 스위치 손잡이는 켜짐(파랑)·꺼짐(회색) 바탕 위에 늘 흰색이어야 보인다 — 테마 색을 쓰면 어두운 화면에서 바탕과 섞인다.
  U4: ['components/ui/Switch.tsx'],
}

// U11 — 한글 뒤(붙거나 한 칸) · 영문/숫자/마침표 뒤 한 칸 · 값({…}) 뒤 한 칸의 여는 괄호.
const ASIDE_PAREN = /[가-힣]\s?\(|[A-Za-z0-9.%] \(|\} \(/

function matchAll(text, re) {
  return [...text.matchAll(re)]
}

function walk(dir, out = []) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, e.name)
    if (e.isDirectory()) { if (!SKIP_DIRS.includes(p)) walk(p, out) }
    else if (p.endsWith('.tsx')) out.push(p)
  }
  return out
}

function changedSinceCommit() {
  try {
    const out = execFileSync('git', ['status', '--porcelain'], { cwd: REPO, encoding: 'utf8' })
    return out.split('\n').some((l) => {
      const p = l.slice(3).trim().replace(/^"|"$/g, '')
      return p.startsWith('Admin~/src/') || p.startsWith('Admin~/scripts/check-ui')
    })
  } catch { return true } // git 을 못 부르면 판정을 포기하고 검사한다
}

function fail(lines) {
  process.stderr.write(lines.join('\n') + '\n')
  process.exit(2)
}

// 검사기가 도중에 죽어도 실패(2)로 알린다 — Node 는 예외로 죽으면 1 을 내고, 작업 끝 훅은 1 을 그냥 넘긴다.
// 문법·불러오기 오류는 이 처리기가 걸리기 전에 나므로 훅 명령 끝의 `|| exit 2`(.claude/settings.json)가 맡는다.
process.on('uncaughtException', (e) => fail([`[check-ui] 검사기가 도중에 멈췄다: ${e?.stack ?? e}`]))

if (args.has('--if-changed') && !changedSinceCommit()) process.exit(0)

// 검사기 자체 점검 — 규칙 하나라도 제 예시를 못 가르면 결과를 믿을 수 없다.
const broken = RULES.filter((r) => r.find(r.bad).length === 0 || r.find(r.good).length > 0).map((r) => r.id)
if (broken.length) fail([`[check-ui] 검사기 고장 — ${broken.join(', ')} 규칙이 제 예시를 못 가른다. scripts/check-ui.mjs 를 고친다.`])

// 현재 어긋남 모으기
const hits = [] // { rule, file, line }
for (const abs of walk(SRC)) {
  const rel = path.relative(SRC, abs).split(path.sep).join('/')
  const text = fs.readFileSync(abs, 'utf8')
  for (const r of RULES) {
    if (EXEMPT[r.id]?.includes(rel)) continue
    for (const m of r.find(text)) hits.push({ rule: r, file: rel, line: text.slice(0, m.index).split('\n').length })
  }
}
const counts = {}
for (const h of hits) counts[`${h.rule.id}|${h.file}`] = (counts[`${h.rule.id}|${h.file}`] ?? 0) + 1

if (args.has('--list')) {
  for (const r of RULES) {
    const mine = hits.filter((h) => h.rule === r)
    console.log(`${r.id} ${r.name} — ${mine.length}곳 → ${r.fix}`)
    for (const h of mine) console.log(`   src/${h.file}:${h.line}`)
  }
  process.exit(0)
}

const baseline = fs.existsSync(BASELINE) ? JSON.parse(fs.readFileSync(BASELINE, 'utf8')) : null

if (args.has('--update-baseline')) {
  if (baseline) {
    const grew = Object.keys(counts).filter((k) => counts[k] > (baseline[k] ?? 0))
    if (grew.length) fail([`[check-ui] 기준선은 줄일 수만 있다. 늘어난 곳: ${grew.join(', ')} — 고치거나, 정말 예외면 EXEMPT 에 이유와 함께 적는다.`])
  }
  const sorted = Object.fromEntries(Object.entries(counts).sort(([a], [b]) => a.localeCompare(b)))
  fs.writeFileSync(BASELINE, JSON.stringify(sorted, null, 2) + '\n')
  console.log(`[check-ui] 기준선 ${Object.values(counts).reduce((s, n) => s + n, 0)}곳으로 적었다.`)
  process.exit(0)
}

if (!baseline) fail(['[check-ui] 기준선 파일이 없다 — node scripts/check-ui.mjs --update-baseline 으로 만든다.'])

const added = []
for (const [k, n] of Object.entries(counts)) {
  const before = baseline[k] ?? 0
  if (n > before) {
    const [id, file] = k.split('|')
    const r = RULES.find((x) => x.id === id)
    const lines = hits.filter((h) => h.rule.id === id && h.file === file).map((h) => h.line).join(', ')
    added.push(`  ${id} ${r.name}: src/${file} (${before}곳 → ${n}곳, ${lines}번째 줄) → ${r.fix}`)
  }
}
const shrunk = Object.keys(baseline).filter((k) => (counts[k] ?? 0) < baseline[k])

if (added.length) {
  fail(['[check-ui] 콘솔 화면이 공용 꼴에서 새로 벗어났다(규칙: .claude/rules/console.md):', ...added])
}
if (shrunk.length) {
  fail([`[check-ui] 어긋남이 줄었다(${shrunk.join(', ')}). node scripts/check-ui.mjs --update-baseline 으로 기준선을 줄인다.`])
}
const left = Object.values(baseline).reduce((s, n) => s + n, 0)
console.log(`[check-ui] 통과 — 새 어긋남 없음(정리 대기 ${left}곳, --list 로 보기)`)
