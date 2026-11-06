// 사용량 화면(/dashboard/org/<조직>/usage?projectRef=<ref>)에서 Log Query 값을 읽는다.
// 크롬 javascript_tool 에 그대로 붙인다. 로그인 토큰을 건드리지 않고 화면에 표시되는 글자만 읽는다.
// 그래프에 마우스를 올린 것처럼 움직여 말풍선 글자를 모은다. 사용량이 있는 날만 나온다.
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
for (let i = 0; i < 40 && !document.body.innerText.includes('Log Ingestion'); i++) await sleep(500);
const desc = [...document.querySelectorAll('p')].find((e) => e.textContent.startsWith("Logs Query usage isn't billed"));
desc?.scrollIntoView({ block: 'center' });
let wrap = null, section = null;
for (let i = 0; i < 30 && !wrap; i++) {
  await sleep(500);
  let box = desc;
  while (box && !box.querySelector('.recharts-wrapper') && box.innerText.length < 3000) box = box.parentElement;
  if (box && box.innerText.length < 3000) { section = box; wrap = box.querySelector('.recharts-wrapper'); }
}
const days = {};
if (wrap) {
  wrap.scrollIntoView({ block: 'center' });
  await sleep(300);
  const r = wrap.getBoundingClientRect();
  for (let x = r.left + 2; x < r.right; x += 4) {
    const y = r.top + r.height / 2;
    (document.elementFromPoint(x, y) || wrap).dispatchEvent(new MouseEvent('mousemove', { bubbles: true, clientX: x, clientY: y, view: window }));
    await sleep(30);
    const m = wrap.querySelector('.recharts-tooltip-wrapper')?.innerText?.replace(/\s+/g, ' ').match(/Log Query ([\d.,]+)\s*(KB|MB|GB) (\d{2} \w{3} \d{4})/);
    if (m) days[m[3]] = `${m[1]}${m[2]}`;
  }
}
// 조직 화면은 이 칸의 "Used in period", 프로젝트 화면은 맨 위 요약표에 합계가 있다.
const total = (section?.innerText.match(/Used in period\s+([\d.,]+)\s*GB/) ?? document.body.innerText.match(/Log Query\s+([\d.,]+)\s*GB/))?.[1] ?? null;
JSON.stringify({ ref: new URLSearchParams(location.search).get('projectRef') ?? 'org', total, chart: !!wrap, days });
