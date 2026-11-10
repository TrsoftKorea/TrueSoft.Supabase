import { useState, type ReactNode } from 'react'
import { ChevronDown, ChevronUp, Pencil, Plus, RefreshCw, RotateCcw, Trash2 } from 'lucide-react'
import { CardHeader, WhiteCard } from '../components/ui/Card'
import { PageHeader } from '../components/ui/PageHeader'
import { InfoList } from '../components/ui/InfoList'
import { Button } from '../components/ui/Button'
import { inputCls } from '../components/ui/Input'
import { CardStack, DataTable, TableCard, type Column } from '../components/ui/DataTable'
import { StatusText, Tag, type Tone } from '../components/ui/StatusText'
import { TableFooter } from '../components/ui/TableFooter'
import { StatusRows, type StatusRowItem } from '../components/ui/StatusRows'
import { formatDateTime, formatKRW } from '../components/ui/format'

/**
 * 디자인 견본 — 가짜 데이터 콘솔에만 있다(일반 빌드에서 빠진다). 화면 꼴을 바꾸기 전에 "지금"과 "제안"을 나란히 보고
 * 크기·간격을 정하는 곳이다. 정해지면 공용 부품에 옮기고, 이 화면은 공용 부품만 쓰는 살아 있는 견본으로 남긴다.
 * "지금" 쪽은 현재 화면 코드를 그대로 옮겨 온 것이다.
 */

// ── 제안 꼴(정해지면 공용 부품으로 옮긴다) ──────────────────────────────────────
function NewCard({ title, description, actions, fit, children }: {
  title: string; description?: string; actions?: ReactNode; fit?: boolean; children: ReactNode
}) {
  return (
    <WhiteCard className={`p-6 ${fit ? 'w-fit max-w-full' : ''}`}>
      <div className="mb-4 flex items-start justify-between gap-4">
        <div className="min-w-0">
          <h3 className="text-base font-semibold text-neutral-900">{title}</h3>
          {description && <p className="mt-1 text-sm text-neutral-500">{description}</p>}
        </div>
        {actions && <div className="flex shrink-0 items-center gap-2">{actions}</div>}
      </div>
      {children}
    </WhiteCard>
  )
}

function NewInfoList({ items }: { items: { label: string; value: ReactNode }[] }) {
  return (
    <dl className="flex flex-wrap gap-x-10 gap-y-3">
      {items.map((i) => (
        <div key={i.label}>
          <dt className="text-xs text-neutral-500">{i.label}</dt>
          <dd className="mt-0.5 text-lg font-semibold tabular-nums text-neutral-900 whitespace-nowrap">{i.value}</dd>
        </div>
      ))}
    </dl>
  )
}

function JobStrip() {
  const [open, setOpen] = useState(true)
  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-center gap-x-4 gap-y-2 text-sm">
        <StatusText tone="ok">결제 자동 작업 모두 정상</StatusText>
        <button type="button" onClick={() => setOpen((v) => !v)} className="inline-flex items-center gap-0.5 text-blue-700 hover:underline">
          {open ? '접기' : '자세히'}
          {open ? <ChevronUp className="h-3.5 w-3.5" /> : <ChevronDown className="h-3.5 w-3.5" />}
        </button>
        <Button size="compact" className="ml-auto"><RefreshCw className="h-4 w-4" /> 구글 환불 지금 확인</Button>
      </div>
      {open && <div className="border-t border-neutral-100 pt-3"><StatusRows items={JOBS} /></div>}
    </div>
  )
}

// ── 견본 틀 ────────────────────────────────────────────────────────────────────
function Section({ no, title, children }: { no: number; title: string; children: ReactNode }) {
  return (
    <section className="space-y-4">
      <h2 className="text-lg font-semibold text-neutral-900">{no}. {title}</h2>
      {children}
    </section>
  )
}

function CompareLabel({ kind }: { kind: 'now' | 'next' }) {
  return kind === 'now'
    ? <span className="inline-flex rounded bg-neutral-200 px-2 py-0.5 text-xs font-medium text-neutral-700">지금</span>
    : <span className="inline-flex rounded bg-brand-soft px-2 py-0.5 text-xs font-medium text-blue-700">제안</span>
}

function Compare({ now, next, notes, width, nextWidth }: {
  now: ReactNode; next: ReactNode; notes: string[]; width?: string; nextWidth?: string
}) {
  return (
    <div className="space-y-5">
      <div className={`space-y-2 ${width ?? ''}`}><CompareLabel kind="now" />{now}</div>
      <div className={`space-y-2 ${nextWidth ?? width ?? ''}`}><CompareLabel kind="next" />{next}</div>
      <ul className="list-disc space-y-1 pl-5 text-sm text-neutral-700">
        {notes.map((n) => <li key={n}>{n}</li>)}
      </ul>
    </div>
  )
}

// ── 견본 데이터 ─────────────────────────────────────────────────────────────────
type Invite = { email: string; name: string; by: string; at: string }
const INVITES: Invite[] = [
  { email: 'kim.dev@example.com', name: '김개발', by: 'master@example.com', at: '2026-11-10T14:57:00+09:00' },
  { email: 'lee.operation.team@example.com', name: '이운영', by: 'master@example.com', at: '2026-11-10T13:40:00+09:00' },
  { email: 'park@example.com', name: '박서버관리', by: 'master@example.com', at: '2026-11-03T11:10:00+09:00' },
]

type Purchase = { id: string; at: string; player: string; product: string; store: string; krw: number; order: string; refunded?: boolean; test?: boolean }
const PURCHASES: Purchase[] = [
  { id: '1', at: '2026-11-10T13:02:00+09:00', player: '불꽃전사', product: 'com.truesoft.game.gem_pack_large_limited_event', store: 'Google Play', krw: 33000, order: 'GPA.3312-5521-8890-12345' },
  { id: '2', at: '2026-11-10T11:48:00+09:00', player: '달빛', product: 'com.truesoft.game.monthly_pass', store: 'App Store', krw: 5500, order: '2000000812345678', test: true },
  { id: '3', at: '2026-11-09T22:15:00+09:00', player: '초보모험가123', product: 'com.truesoft.game.gem_pack_small', store: 'Google Play', krw: 1200, order: 'GPA.3390-1182-4410-98765', refunded: true },
]

const JOBS: StatusRowItem[] = [
  {
    label: '구글 환불 확인', tone: 'ok', state: '정상', at: formatDateTime('2026-11-10T13:00:00+09:00'),
    facts: [
      { label: '새 환불', value: '1건' },
      { label: '옛 결제 시험 여부 확인', value: '12건' },
      { label: '그중 시험', value: '3건' },
      { label: '구글이 답하지 않음', value: '2건', tone: 'warn' },
    ],
  },
  { label: '애플 환불 알림', tone: 'ok', state: '정상', at: formatDateTime('2026-11-08T04:11:00+09:00') },
  {
    label: '원화 환산', tone: 'ok', state: '정상', at: formatDateTime('2026-11-10T13:30:00+09:00'),
    facts: [
      { label: '환율', value: '170개 통화' },
      { label: '애플 결제 금액 확인', value: '1건' },
      { label: '원화 환산', value: '1건' },
    ],
  },
]

const KIND_RULES: { kind: string; where: string; width: string; align: string }[] = [
  { kind: '이름', where: '닉네임·이메일·상품 이름·작업 이름', width: '내용 길이만큼, 줄바꿈 없음', align: '왼쪽' },
  { kind: '고정폭', where: '계정 ID·주문번호처럼 글자 하나하나가 중요한 값', width: '내용 길이만큼, 줄바꿈 없음', align: '왼쪽' },
  { kind: '날짜·시각', where: '"2026. 11. 10. 13:00" 한 가지 형식', width: '항상 같은 폭', align: '왼쪽' },
  { kind: '숫자', where: '개수·금액·점수', width: '내용 길이만큼, 천 단위 쉼표', align: '오른쪽(자릿수 맞춤)' },
  { kind: '상태', where: '정상·실패·임시 등 한두 단어', width: '가장 긴 상태 이름만큼', align: '왼쪽' },
  { kind: '긴 글', where: '제목·메모·상품 ID처럼 길이를 모르는 글', width: '남는 폭을 전부 가져감(최소 256px), 넘치면 "…"', align: '왼쪽' },
  { kind: '버튼', where: '바꾸기·삭제 등', width: '버튼 전부 합친 폭', align: '오른쪽' },
]

// ── 화면 ───────────────────────────────────────────────────────────────────────
export default function DesignSample() {
  const inviteCols: Column<Invite>[] = [
    { key: 'email', header: '이메일', kind: 'name', cell: (r) => r.email },
    { key: 'name', header: '이름', kind: 'name', cell: (r) => r.name },
    { key: 'by', header: '초대한 사람', kind: 'name', cell: (r) => <span className="text-neutral-600">{r.by}</span> },
    { key: 'at', header: '초대 시각', kind: 'datetime', cell: (r) => formatDateTime(r.at) },
    {
      key: 'actions', header: '', kind: 'actions', cell: () => (
        <span className="inline-flex gap-1.5">
          <Button size="sm"><RotateCcw className="h-3 w-3" /> 비밀번호 초기화</Button>
          <Button size="sm" variant="dangerOutline"><Trash2 className="h-3 w-3" /> 삭제</Button>
        </span>
      ),
    },
  ]

  const purchaseCols: Column<Purchase>[] = [
    { key: 'at', header: '결제 확인 시각', kind: 'datetime', cell: (r) => formatDateTime(r.at) },
    { key: 'player', header: '플레이어', kind: 'name', cell: (r) => <span className="text-blue-700">{r.player}</span> },
    {
      key: 'product', header: '상품', kind: 'text', full: (r) => r.product, cell: (r) => (
        <>
          {r.refunded && <span className="mr-1.5"><Tag tone="bad">환불</Tag></span>}
          {r.test && <span className="mr-1.5"><Tag>테스트</Tag></span>}
          <span className={r.refunded ? 'text-neutral-400 line-through' : ''}>{r.product}</span>
        </>
      ),
    },
    { key: 'store', header: '스토어', kind: 'name', cell: (r) => r.store },
    { key: 'krw', header: '금액', kind: 'number', cell: (r) => <span className={r.refunded ? 'text-neutral-400 line-through' : ''}>{formatKRW(r.krw)}</span> },
    { key: 'order', header: '주문번호', kind: 'mono', cell: (r) => r.order },
  ]

  return (
    <div className="space-y-10 pb-16">
      <PageHeader title="디자인 견본" description="화면 꼴을 바꾸기 전에 지금과 제안을 비교합니다. 가짜 데이터 콘솔에만 있습니다." />

      <Section no={1} title="글자 크기 — 4단계로 고정">
        <NewCard title="글자 단계" fit>
          <DataTable
            rowKey={(r) => r.use}
            rows={[
              { use: '페이지 제목', sample: <span className="text-2xl font-semibold">구매 내역</span>, spec: '24px · 굵게', now: '24px · 굵게(그대로)' },
              { use: '카드 제목', sample: <span className="text-base font-semibold">결제 자동 작업</span>, spec: '16px · 굵게', now: '14px · 보통 — 본문과 같아 안 갈림' },
              { use: '본문·표 내용·입력칸', sample: <span className="text-sm">새 환불 1건</span>, spec: '14px · 보통', now: '대부분 12px' },
              { use: '칸 이름·표 머리·보조 안내', sample: <span className="text-xs text-neutral-500">결제 확인 시각</span>, spec: '12px · 회색', now: '12px(그대로)' },
            ]}
            columns={[
              { key: 'use', header: '쓰임', kind: 'name', cell: (r) => r.use },
              { key: 'sample', header: '보기', kind: 'name', cell: (r) => r.sample },
              { key: 'spec', header: '제안', kind: 'name', cell: (r) => r.spec },
              { key: 'now', header: '지금', kind: 'name', cell: (r) => <span className="text-neutral-500">{r.now}</span> },
            ]}
          />
        </NewCard>
      </Section>

      <Section no={2} title="카드 제목과 내용">
        <Compare
          now={
            <WhiteCard className="p-5 space-y-4">
              <CardHeader title="동시접속자" description="지금 접속 중인 플레이어 수와 그동안의 흐름을 봅니다." />
              <InfoList items={[
                { label: '지금 접속 중', value: '49명' },
                { label: '오늘 최고', value: '85명' },
                { label: '서버별', value: 'GLOBAL 32 · KR 17' },
              ]} />
            </WhiteCard>
          }
          next={
            <NewCard title="동시접속자">
              <NewInfoList items={[
                { label: '지금 접속 중', value: '49명' },
                { label: '오늘 최고', value: '85명' },
                { label: 'GLOBAL', value: '32명' },
                { label: 'KR', value: '17명' },
              ]} />
            </NewCard>
          }
          notes={[
            '카드 제목 16px 굵게, 제목과 내용 사이 16px.',
            '제목만으로 뜻이 분명하면 설명 줄을 뺀다.',
            '핵심 숫자는 크게 굵게. 한 칸에 숫자 두 개를 이어 쓰지 않고 칸을 나눈다.',
          ]}
        />
      </Section>

      <Section no={3} title="칸이 적은 표 — 줄바꿈 없이, 카드는 내용 폭만큼">
        <p className="text-sm text-neutral-600">지금 쪽은 실제 트루베이스 설정 화면과 같은 폭(860px)입니다.</p>
        <Compare
          width="max-w-[860px]"
          nextWidth=""
          now={
            <WhiteCard className="p-5 space-y-5">
              <CardHeader title="초대" description="트루베이스에 들어올 수 있는 사람을 정하고, 비밀번호로 들어오는 사람에게 임시 비밀번호를 줍니다." />
              <div className="overflow-x-auto rounded-md border border-neutral-200">
                <table className="w-full text-sm">
                  <thead className="bg-neutral-50 text-neutral-500 text-xs">
                    <tr>
                      <th className="text-left px-4 py-2.5 font-medium">이메일</th>
                      <th className="text-left px-4 py-2.5 font-medium">이름</th>
                      <th className="text-left px-4 py-2.5 font-medium">초대한 사람</th>
                      <th className="text-left px-4 py-2.5 font-medium w-40">초대 시각</th>
                      <th className="px-4 py-2.5 w-56"></th>
                    </tr>
                  </thead>
                  <tbody>
                    {INVITES.map((r) => (
                      <tr key={r.email} className="border-t border-neutral-100">
                        <td className="px-4 py-2.5 font-mono text-xs">{r.email}</td>
                        <td className="px-4 py-2.5">{r.name}</td>
                        <td className="px-4 py-2.5 text-xs text-neutral-500">{r.by}</td>
                        <td className="px-4 py-2.5 text-neutral-600 whitespace-nowrap">{formatDateTime(r.at)}</td>
                        <td className="px-4 py-2.5 text-right space-x-1.5 whitespace-nowrap">
                          <Button size="sm"><RotateCcw className="w-3 h-3" /> 비밀번호 초기화</Button>
                          <Button size="sm" variant="dangerOutline"><Trash2 className="w-3 h-3" /> 삭제</Button>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </WhiteCard>
          }
          next={
            <NewCard title="초대" actions={<Button variant="primary" size="compact"><Plus className="h-4 w-4" /> 초대</Button>} fit>
              <div className="rounded-md border border-neutral-200">
                <DataTable columns={inviteCols} rows={INVITES} rowKey={(r) => r.email} />
              </div>
            </NewCard>
          }
          notes={[
            '이름·이메일·시각 칸은 줄바꿈하지 않는다 — 내용이 길면 칸이 넓어진다.',
            '이 표는 860px 안에 줄바꿈 없이 들어가지 않는다. 그래서 페이지 폭을 다른 화면과 같은 콘솔 기본 폭(1,152px)으로 넓힌다.',
            '긴 글 칸이 없는 표는 카드가 내용 폭만큼만 차지한다 — 페이지가 넓어져도 칸 사이가 휑하게 벌어지지 않는다.',
            '창이 좁아 그래도 모자라면 표만 옆으로 밀어 볼 수 있게 한다(줄바꿈 대신).',
            '이메일 입력칸은 [초대] 버튼을 누르면 창으로 띄운다 — 카드 위가 목록만 남아 짧아진다.',
          ]}
        />
      </Section>

      <Section no={4} title="긴 글 칸이 있는 표 — 그 칸만 늘고 줄어든다">
        <Compare
          now={
            <WhiteCard>
              <div className="overflow-auto">
                <table className="w-full text-sm">
                  <thead className="bg-neutral-50 text-neutral-500 text-xs">
                    <tr>
                      <th className="text-left px-4 py-2.5 font-medium w-40">결제 확인 시각</th>
                      <th className="text-left px-4 py-2.5 font-medium">플레이어</th>
                      <th className="text-left px-4 py-2.5 font-medium">상품</th>
                      <th className="text-left px-4 py-2.5 font-medium w-28">스토어</th>
                      <th className="text-left px-4 py-2.5 font-medium w-28">금액</th>
                      <th className="text-left px-4 py-2.5 font-medium">주문번호</th>
                    </tr>
                  </thead>
                  <tbody>
                    {PURCHASES.map((r) => (
                      <tr key={r.id} className="border-t border-neutral-100">
                        <td className="px-4 py-3 text-neutral-600 whitespace-nowrap">{formatDateTime(r.at)}</td>
                        <td className="px-4 py-3 text-blue-700">{r.player}</td>
                        <td className="px-4 py-3">
                          <span className={`font-mono text-xs ${r.refunded ? 'line-through text-neutral-400' : ''}`}>{r.product}</span>
                          {r.refunded && <span className="ml-2 inline-flex items-center rounded bg-red-50 px-1.5 py-0.5 text-[11px] text-red-600 whitespace-nowrap">환불</span>}
                          {r.test && <span className="ml-2 inline-flex items-center rounded bg-neutral-100 px-1.5 py-0.5 text-[11px] text-neutral-600 whitespace-nowrap">테스트</span>}
                        </td>
                        <td className="px-4 py-3 text-neutral-600 whitespace-nowrap">{r.store}</td>
                        <td className={`px-4 py-3 whitespace-nowrap ${r.refunded ? 'line-through text-neutral-400' : ''}`}>{formatKRW(r.krw)}</td>
                        <td className="px-4 py-3 text-neutral-600 font-mono text-xs">{r.order}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </WhiteCard>
          }
          next={
            <WhiteCard>
              <DataTable columns={purchaseCols} rows={PURCHASES} rowKey={(r) => r.id} />
            </WhiteCard>
          }
          notes={[
            '상품처럼 길이를 모르는 칸 하나가 남는 폭을 가져가고, 넘치면 "…"로 줄인다(마우스를 올리면 전체).',
            '창이 좁아도 그 칸은 256px 아래로 줄지 않는다 — 대신 표를 옆으로 밀어 본다.',
            '금액은 오른쪽 정렬·자릿수 맞춤.',
            '환불·테스트 표시는 글자 앞에 둬서 줄마다 같은 자리에 온다.',
          ]}
        />
      </Section>

      <Section no={5} title="상태 줄 — 문장 대신 칸으로">
        <Compare
          now={
            <WhiteCard className="p-5">
              <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1 text-xs">
                <dt className="text-neutral-500 whitespace-nowrap">구글 환불 확인</dt>
                <dd className="text-neutral-600">2026. 11. 10. 13:00 확인 · 새 환불 1건 · 옛 결제 시험 여부 12건 확인(시험 3건), 구글이 답하지 않음 2건</dd>
                <dt className="text-neutral-500 whitespace-nowrap">애플 환불 알림</dt>
                <dd className="text-neutral-600">마지막 알림 2026. 11. 08. 04:11</dd>
                <dt className="text-neutral-500 whitespace-nowrap">원화 환산</dt>
                <dd className="text-neutral-600">2026. 11. 10. 13:30 확인 · 환율 170개 통화 / 애플 결제 금액 확인 1건 / 원화 환산 1건</dd>
              </dl>
            </WhiteCard>
          }
          next={
            <WhiteCard className="p-6"><JobStrip /></WhiteCard>
          }
          notes={[
            '평소엔 지금처럼 한 줄로 접혀 있다 — 정상이면 "모두 정상", 문제가 있으면 그 작업만 빨갛게.',
            '펴면 작업 이름 · 상태 · 마지막 시각 · 숫자들이 줄마다 같은 자리에 온다.',
            '숫자만 굵게, 이름은 옅게. 주의할 숫자만 주황.',
            '"·" "/" 로 문장을 이어 붙이지 않는다.',
          ]}
        />
      </Section>

      <Section no={6} title="상태 색과 작은 표시">
        <NewCard title="상태 표시" fit>
          <div className="flex flex-wrap items-center gap-6 text-sm">
            <StatusText tone="ok">정상 · 수령</StatusText>
            <StatusText tone="plain">미수령</StatusText>
            <StatusText tone="warn">주의 · 늦어짐</StatusText>
            <StatusText tone="bad">실패</StatusText>
            <StatusText tone="muted">해당 없음 · 삭제</StatusText>
          </div>
          <div className="mt-5 flex flex-wrap items-center gap-6 text-sm">
            <span className="whitespace-nowrap font-mono text-[13px]">GLOBAL <Tag tone="brand" title="새로 가입한 플레이어가 들어가는 서버">기본</Tag></span>
            <span className="whitespace-nowrap"><Tag>테스트</Tag> 월정액</span>
            <span className="whitespace-nowrap"><Tag tone="bad">환불</Tag> 보석 묶음</span>
          </div>
          <ul className="mt-5 list-disc space-y-1 pl-5 text-sm text-neutral-700">
            <li>정상·보통은 글자색만, 문제일 때만 바탕을 깐다. 문제도 정상도 아닌 상태(미수령)는 색을 쓰지 않는다.</li>
            <li>다른 값 옆에 붙는 작은 표시는 12px 하나로. 뜻을 설명해야 하면 마우스를 올리면 보이게 한다("기본").</li>
            <li>빈 값은 회색 "-" 하나로.</li>
          </ul>
        </NewCard>
      </Section>

      <Section no={7} title="입력 창">
        <Compare
          width="max-w-[560px]"
          now={
            <WhiteCard className="p-5 space-y-3">
              <label className="flex flex-col gap-1">
                <span className="text-xs text-neutral-500">이메일</span>
                <input className={inputCls} defaultValue="kim.dev@example.com" />
              </label>
              <p className="text-xs text-neutral-400">초대할 사람의 이메일을 넣습니다.</p>
            </WhiteCard>
          }
          next={
            <NewCard title="초대">
              <label className="flex flex-col gap-1.5">
                <span className="text-xs font-medium text-neutral-700">이메일</span>
                <input className={`${inputCls} w-full`} defaultValue="kim.dev@example.com" />
              </label>
              <div className="mt-6 flex justify-end gap-2">
                <Button>취소</Button>
                <Button variant="primary"><Pencil className="h-4 w-4" /> 초대</Button>
              </div>
            </NewCard>
          }
          notes={[
            '칸 이름은 12px이되 진하게 — 아래 안내 글과 갈린다.',
            '"~을 넣습니다"처럼 칸 이름으로 이미 아는 안내는 뺀다.',
          ]}
        />
      </Section>

      <Section no={8} title="표 칸 종류 규칙">
        <NewCard title="칸 종류" description="표의 칸마다 종류를 정하면 폭·정렬·줄바꿈이 저절로 맞춰집니다." fit>
          <div className="rounded-md border border-neutral-200">
            <DataTable
              rowKey={(r) => r.kind}
              rows={KIND_RULES}
              columns={[
                { key: 'kind', header: '칸 종류', kind: 'name', cell: (r) => <span className="font-medium">{r.kind}</span> },
                { key: 'where', header: '쓰는 곳', kind: 'name', cell: (r) => r.where },
                { key: 'width', header: '폭', kind: 'name', cell: (r) => r.width },
                { key: 'align', header: '정렬', kind: 'name', cell: (r) => r.align },
              ]}
            />
          </div>
        </NewCard>
      </Section>

      <Section no={9} title="표 카드 — 빈 공간 없이, 쪽을 넘겨도 흔들리지 않게">
        <Compare
          now={<MatchNow />}
          next={<MatchNext />}
          notes={[
            '긴 글 칸이 없는 표는 카드째 내용 폭으로 줄어든다 — 표만 줄이면 카드 오른쪽이 텅 빈다.',
            '위아래로 쌓인 표 카드는 가장 넓은 것에 폭을 맞춘다(요약·기록).',
            '쪽을 넘겨 이름이 짧아져도 카드가 줄지 않는다 — 한 번 넓어진 폭을 지킨다. [다음 쪽]을 눌러 보세요.',
            '자세히 볼 것이 있는 줄은 왼쪽 화살표가 붙고, 누르면 아래로 펼친다.',
            '카드 제목 크기는 1·2번이 정해지면 함께 바뀐다.',
          ]}
        />
      </Section>
    </div>
  )
}

// ── 9번 견본: 대전 기록 탭 ────────────────────────────────────────────────────
type MatchSummary = { code: string; status: string; tone: Tone; wins: number; losses: number; max: number; min: number }
const MATCH_SUMMARY: MatchSummary[] = [
  { code: 'pvp_1v1', status: '확인됨', tone: 'ok', wins: 3, losses: 5, max: 1906, min: 849 },
  { code: 'pvp_1v1', status: '상대 미신고', tone: 'warn', wins: 0, losses: 1, max: 1117, min: 1117 },
  { code: 'stage_clear', status: '상대 없음', tone: 'plain', wins: 1, losses: 1, max: 1797, min: 1243 },
]
type MatchRecord = { id: string; at: string; code: string; win: boolean; score: number; status: string; tone: Tone; opponent: string | null; rewarded: boolean }
const MATCH_PAGES: MatchRecord[][] = [
  [
    { id: 's1', at: '2026-11-10T09:15:00+09:00', code: 'pvp_1v1', win: false, score: 1117, status: '상대 미신고', tone: 'warn', opponent: '용사김', rewarded: false },
    { id: 's2', at: '2026-11-09T10:02:00+09:00', code: 'pvp_1v1', win: false, score: 849, status: '확인됨', tone: 'ok', opponent: '마법사이다크나이트', rewarded: false },
    { id: 's3', at: '2026-11-08T11:40:00+09:00', code: 'pvp_1v1', win: true, score: 1906, status: '확인됨', tone: 'ok', opponent: '탈퇴한 계정', rewarded: true },
  ],
  [
    { id: 's4', at: '2026-11-07T20:11:00+09:00', code: 'stage_clear', win: true, score: 1797, status: '상대 없음', tone: 'plain', opponent: null, rewarded: false },
    { id: 's5', at: '2026-11-06T18:30:00+09:00', code: 'pvp_1v1', win: true, score: 1502, status: '무효', tone: 'bad', opponent: '달빛', rewarded: false },
  ],
]

const summaryCols: Column<MatchSummary>[] = [
  { key: 'code', header: '게임 코드', kind: 'mono', cell: (r) => r.code },
  { key: 'status', header: '상태', kind: 'status', cell: (r) => <StatusText tone={r.tone}>{r.status}</StatusText> },
  { key: 'wins', header: '승', kind: 'number', cell: (r) => r.wins.toLocaleString('ko-KR') },
  { key: 'losses', header: '패', kind: 'number', cell: (r) => r.losses.toLocaleString('ko-KR') },
  { key: 'max', header: '최고 점수', kind: 'number', cell: (r) => r.max.toLocaleString('ko-KR') },
  { key: 'min', header: '최저 점수', kind: 'number', cell: (r) => r.min.toLocaleString('ko-KR') },
]
const recordCols: Column<MatchRecord>[] = [
  { key: 'at', header: '신고 시각', kind: 'datetime', cell: (r) => formatDateTime(r.at) },
  { key: 'code', header: '게임 코드', kind: 'mono', cell: (r) => r.code },
  { key: 'win', header: '결과', kind: 'status', cell: (r) => <StatusText tone="plain">{r.win ? '승' : '패'}</StatusText> },
  { key: 'score', header: '점수', kind: 'number', cell: (r) => r.score.toLocaleString('ko-KR') },
  { key: 'status', header: '상태', kind: 'status', cell: (r) => <StatusText tone={r.tone}>{r.status}</StatusText> },
  { key: 'opponent', header: '상대', kind: 'name', cell: (r) => r.opponent && <span className="text-blue-700">{r.opponent}</span> },
  { key: 'reward', header: '보상 우편', kind: 'name', cell: (r) => (r.rewarded ? '보냄' : null) },
]

function MatchNow() {
  return (
    <div className="space-y-4">
      <WhiteCard>
        <div className="p-5 pb-3"><CardHeader title="요약" description="게임 코드와 상태마다 승패와 점수 범위를 봅니다." /></div>
        <div className="overflow-auto">
        <table className="w-full text-sm">
          <thead className="bg-neutral-50 text-neutral-500 text-xs">
            <tr>
              <th className="text-left px-4 py-2.5 font-medium w-44">게임 코드</th>
              <th className="text-left px-4 py-2.5 font-medium w-28">상태</th>
              <th className="text-right px-4 py-2.5 font-medium w-20">승</th>
              <th className="text-right px-4 py-2.5 font-medium w-20">패</th>
              <th className="text-right px-4 py-2.5 font-medium w-28">최고 점수</th>
              <th className="text-right px-4 py-2.5 font-medium">최저 점수</th>
            </tr>
          </thead>
          <tbody>
            {MATCH_SUMMARY.map((s) => (
              <tr key={s.code + s.status} className="border-t border-neutral-100">
                <td className="px-4 py-3 font-mono text-neutral-700 whitespace-nowrap">{s.code}</td>
                <td className="px-4 py-3 whitespace-nowrap"><StatusText tone={s.tone === 'plain' ? 'muted' : s.tone}>{s.status}</StatusText></td>
                <td className="px-4 py-3 text-right text-neutral-700">{s.wins}</td>
                <td className="px-4 py-3 text-right text-neutral-700">{s.losses}</td>
                <td className="px-4 py-3 text-right text-neutral-600">{s.max.toLocaleString('ko-KR')}</td>
                <td className="px-4 py-3 text-right text-neutral-600">{s.min.toLocaleString('ko-KR')}</td>
              </tr>
            ))}
          </tbody>
        </table>
        </div>
      </WhiteCard>
    </div>
  )
}

function MatchNext() {
  const [page, setPage] = useState(1)
  return (
    <CardStack>
      <TableCard title="요약" columns={summaryCols} rows={MATCH_SUMMARY} rowKey={(r) => r.code + r.status} />
      <TableCard
        title="기록"
        columns={recordCols}
        rows={MATCH_PAGES[page - 1] ?? []}
        rowKey={(r) => r.id}
        expand={(r) => (r.tone === 'ok' || r.tone === 'plain' ? null : (
          <InfoList items={[
            { label: '세션 ID', value: <span className="font-mono text-[13px]">{`b0c7e1d2-${r.id}`}</span> },
            { label: '상대가 신고한 결과', value: r.tone === 'bad' ? '승' : '신고 없음' },
          ]} />
        ))}
        footer={<TableFooter summary="총 5건" page={page} totalPages={MATCH_PAGES.length} onPageChange={setPage} />}
      />
    </CardStack>
  )
}
