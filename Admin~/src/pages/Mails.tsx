import { useEffect, useRef, useState, type ReactNode } from 'react'
import { Loader2, Send, X, Plus, Check, ChevronDown, Search } from 'lucide-react'
import { callAdmin, NotAuthenticatedError } from '../lib/api'
import { Link, useSearchParams } from 'react-router-dom'
import { type ProjectTarget } from '../lib/projectTarget'
import { WhiteCard } from '../components/ui/Card'
import { InfoList } from '../components/ui/InfoList'
import { Button } from '../components/ui/Button'
import { PageHeader } from '../components/ui/PageHeader'
import { ConfirmDialog } from '../components/ui/ConfirmDialog'
import { ErrorBanner } from '../components/ui/ErrorBanner'
import { Segmented } from '../components/ui/Segmented'
import { Switch } from '../components/ui/Switch'
import { formatCount, itemLabels } from '../components/ui/format'
import { Tag, Tags } from '../components/ui/StatusText'
import { areaCls, inputCls } from '../components/ui/Input'
import { localInputToIso, timeZoneName, useGameTimeZone } from '../lib/gameTime'
import { playerLabel } from '../components/ui/PlayerLink'
import { brokenItemsIn, itemOptionLabel, type CatalogItem } from '../lib/gameItems'
import { BrokenItemNote, BrokenItemsConfirm } from '../components/ItemGrantNote'
import { defaultLangField, langLabel, pickableLangs, useDefaultLanguage } from '../lib/languages'

type Mode = 'all' | 'server' | 'players' | 'operators'
type Sched = 'immediate' | 'scheduled' | 'repeat'
type RepeatUnit = 'day' | 'week' | 'month'
type ItemRow = { key: string; count: number }
type LangRow = { lang: string; title: string; content: string }
type ServerRow = { id: string; server_code: string; display_name: string }
type GameItem = CatalogItem
type CategoryRow = { key: string; display_name: string }
type PlayerRow = { account_id: string; display_name: string | null }

const DOW_LABEL = ['일', '월', '화', '수', '목', '금', '토']

function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="min-w-0">
      <label className="block text-xs font-medium text-neutral-500 mb-1">{label}</label>
      {children}
    </div>
  )
}

function StepNextButton({ label, disabled, onClick }: { label: string; disabled: boolean; onClick: () => void }) {
  return (
    <div className="pt-3 mt-1 border-t border-neutral-100">
      <Button variant="primary" onClick={onClick} disabled={disabled}>{label}</Button>
    </div>
  )
}

function Step({ n, title, state, summary, onEdit, children }: {
  n: number; title: string; state: 'active' | 'done' | 'locked'; summary?: ReactNode; onEdit?: () => void; children: ReactNode
}) {
  const expanded = state === 'active'
  const badge = state === 'done' ? 'bg-emerald-100 text-emerald-600' : 'bg-[#1677ff] text-white'
  return (
    <WhiteCard className="overflow-hidden">
      <button type="button" onClick={state === 'done' ? onEdit : undefined} disabled={state !== 'done'}
        className={['w-full flex items-center gap-3 px-4 py-3 text-left', state === 'done' ? 'hover:bg-neutral-50 cursor-pointer' : 'cursor-default'].join(' ')}>
        <span className={['w-6 h-6 shrink-0 rounded-full flex items-center justify-center text-xs font-semibold', badge].join(' ')}>
          {state === 'done' ? <Check className="w-3.5 h-3.5" /> : n}
        </span>
        <span className="text-sm font-semibold text-neutral-800">{title}</span>
        {state === 'done' && (
          <>
            <span className="ml-auto text-sm text-neutral-500 truncate max-w-[50%]">{summary}</span>
            <ChevronDown className="w-4 h-4 text-neutral-400 shrink-0" />
          </>
        )}
      </button>
      {/* 단계 카드는 탭줄·머리줄과 끝을 맞춰 전체 폭, 안의 입력칸·글은 읽기 좋은 폭으로 묶는다. */}
      {expanded && <div className="px-4 pb-4 pt-1 space-y-3 border-t border-neutral-100 [&>*]:max-w-3xl">{children}</div>}
    </WhiteCard>
  )
}

export default function Mails({
  target,
  onUnauthenticated,
}: {
  target: ProjectTarget
  onUnauthenticated: () => void
}) {
  const [sched, setSched] = useState<Sched>('immediate')
  const [repeatUnit, setRepeatUnit] = useState<RepeatUnit>('day')
  const [repeatDow, setRepeatDow] = useState(1)
  const [repeatDom, setRepeatDom] = useState(1)
  // 플레이어 상세의 "우편 보내기"가 ?account=&name= 으로 넘겨 그 사람을 미리 골라 둔다.
  const [params] = useSearchParams()
  const presetAccount = params.get('account')
  const [mode, setMode] = useState<Mode>('players')
  const [selected, setSelected] = useState<Record<string, string>>(() =>
    presetAccount ? { [presetAccount]: params.get('name') || presetAccount.slice(0, 8) } : {},
  )
  const [serverId, setServerId] = useState('')
  const [openForNew, setOpenForNew] = useState(false)
  const [title, setTitle] = useState('')
  const [content, setContent] = useState('')
  const [category, setCategory] = useState('default')
  const [expiresDays, setExpiresDays] = useState(7)
  const [items, setItems] = useState<ItemRow[]>([])
  const [langRows, setLangRows] = useState<LangRow[]>([])
  const [scheduledAt, setScheduledAt] = useState('')
  const [repeatTime, setRepeatTime] = useState('12:00')
  const [sending, setSending] = useState(false)
  const [askSend, setAskSend] = useState(false)
  const [sendError, setSendError] = useState('')
  const [notice, setNotice] = useState('')
  const [noticeLink, setNoticeLink] = useState({ to: '/mails/records', label: '우편 내역' })
  const [error, setError] = useState('')
  const [playerQuery, setPlayerQuery] = useState('')
  const [playerInput, setPlayerInput] = useState('')
  const [playerRows, setPlayerRows] = useState<PlayerRow[]>([])
  const playerReqRef = useRef(0)
  const [servers, setServers] = useState<ServerRow[]>([])
  const [catalog, setCatalog] = useState<GameItem[]>([])
  const [categories, setCategories] = useState<CategoryRow[]>([])
  const [push, setPush] = useState(false)
  const [pushIsAd, setPushIsAd] = useState(false)
  // 단계 게이팅: active = 현재 열린 단계, reached = 다음 버튼으로 도달한 최대 단계(0~3, 3=발송)
  const [active, setActive] = useState(0)
  const [reached, setReached] = useState(0)

  const report = (e: unknown, fallback: string) => {
    if (e instanceof NotAuthenticatedError) {
      onUnauthenticated()
      return
    }
    setError(e instanceof Error ? e.message : fallback)
  }

  useEffect(() => {
    void (async () => {
      try {
        const [srv, items, cat] = await Promise.all([
          callAdmin<{ rows: ServerRow[] }>(target, 'mails.getServers'),
          callAdmin<GameItem[]>(target, 'gameItems.list'),
          callAdmin<{ rows: CategoryRow[] }>(target, 'mails.getCategories'),
        ])
        setServers(srv.rows)
        setCatalog(items)
        setCategories(cat.rows)
      } catch (e: unknown) {
        report(e, '불러오지 못했습니다.')
      }
    })()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [target])

  useEffect(() => {
    const reqId = ++playerReqRef.current
    void (async () => {
      try {
        const res = await callAdmin<{ rows: PlayerRow[] }>(target, 'players.list', { search: playerQuery, page: 1, bannedOnly: false })
        if (reqId !== playerReqRef.current) return
        setPlayerRows(res.rows)
      } catch (e: unknown) {
        if (reqId !== playerReqRef.current) return
        report(e, '플레이어 검색에 실패했습니다.')
      }
    })()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [mode, playerQuery, target])

  const addItem = () => setItems((x) => [...x, { key: catalog[0]?.key ?? '', count: 1 }])
  const setItem = (i: number, patch: Partial<ItemRow>) => setItems((x) => x.map((r, j) => (j === i ? { ...r, ...patch } : r)))
  const delItem = (i: number) => setItems((x) => x.filter((_, j) => j !== i))
  const togglePlayer = (p: PlayerRow) => setSelected((s) => { const n = { ...s }; if (n[p.account_id]) delete n[p.account_id]; else n[p.account_id] = playerLabel(p.display_name, p.account_id); return n })

  const defaultLang = useDefaultLanguage(target)
  const addLang = () => setLangRows((x) => [...x, { lang: pickableLangs(defaultLang, x.map((r) => r.lang))[0] ?? '', title: '', content: '' }])
  const canAddLang = pickableLangs(defaultLang, langRows.map((r) => r.lang)).length > 0
  const setLang = (i: number, patch: Partial<LangRow>) => setLangRows((x) => x.map((r, j) => (j === i ? { ...r, ...patch } : r)))
  const delLang = (i: number) => setLangRows((x) => x.filter((_, j) => j !== i))

  const playerIds = Object.keys(selected)

  // 시험 발송은 즉시 발송만 된다 — 예약 발송은 운영 계정 대상을 모른다.
  useEffect(() => { if (sched !== 'immediate' && mode === 'operators') setMode('players') }, [sched, mode])
  const [operatorCount, setOperatorCount] = useState<number | null>(null)
  useEffect(() => {
    if (mode !== 'operators') return
    let alive = true
    setOperatorCount(null)
    callAdmin<number>(target, 'mails.getRecipientCount', { mode: 'operators' })
      .then((n) => { if (alive) setOperatorCount(n) })
      .catch((e: unknown) => report(e, '시험 발송 대상 수를 불러오지 못했습니다.'))
    return () => { alive = false }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [mode, target])

  const localized = (() => {
    const o: Record<string, { title: string; content: string }> = {}
    for (const r of langRows) { const l = r.lang.trim(); if (l) o[l] = { title: r.title, content: r.content } }
    return Object.keys(o).length > 0 ? o : null
  })()

  const schedOk = sched === 'immediate' || (sched === 'scheduled' && !!scheduledAt) || (sched === 'repeat' && !!repeatTime)
  const targetOk = mode === 'all' || (mode === 'server' && !!serverId) || (mode === 'players' && playerIds.length > 0)
    || (mode === 'operators' && (operatorCount ?? 0) > 0)
  const langFilled = langRows.every((r) => r.lang.trim() !== '' && (r.title.trim() !== '' || r.content.trim() !== ''))
  const langDup = langRows.some((r, i) => r.lang.trim() !== '' && langRows.some((x, j) => j < i && x.lang.trim() === r.lang.trim()))
  const langOk = langFilled && !langDup
  const msgOk = title.trim() !== '' && langOk
  const stepDone = [schedOk, targetOk, msgOk]

  const stepState = (i: number): 'active' | 'done' | 'locked' => {
    if (i === active) return 'active'
    if (i <= reached) return 'done'
    return 'locked'
  }

  const goNext = (i: number) => {
    if (!stepDone[i]) return
    if (i === reached) {
      const nx = Math.min(i + 1, 3)
      setReached(nx)
      setActive(nx)
    } else {
      setActive(reached)
    }
  }

  const catLabel = (key: string) => {
    const c = categories.find((x) => x.key === key)
    // 기본 분류 행이 없어도 고르는 목록처럼 "기본"으로 보인다.
    return c?.display_name || (key === 'default' ? '기본' : key)
  }
  // 반복 발송에는 걸 수 없다 — 회차마다 열려 있어 나중에 가입한 사람이 지난 회차를 한꺼번에 받는다.
  const openApplies = (mode === 'all' || mode === 'server') && sched !== 'repeat' && openForNew
  const targetSummary: ReactNode = mode === 'all' ? '전체 플레이어'
    : mode === 'server' ? (servers.find((s) => s.id === serverId)?.display_name ?? '서버 미선택')
    : mode === 'operators' ? <><Tag>시험 발송</Tag> 운영 계정 {operatorCount === null ? '…' : `${formatCount(operatorCount)}명`}</>
    : `플레이어 ${formatCount(playerIds.length)}명`
  // 확인 창에서는 "나중에 들어온 플레이어도"를 둘째 줄에 덧붙인다. 단계 머리 요약은 한 줄이라 대상만.
  const targetDetail: ReactNode = openApplies
    ? <>{targetSummary}<span className="block text-xs text-neutral-500">나중에 들어온 플레이어도</span></>
    : targetSummary
  const repeatDesc = repeatUnit === 'day' ? `매일 ${repeatTime}`
    : repeatUnit === 'week' ? `매주 ${DOW_LABEL[repeatDow]}요일 ${repeatTime}`
    : `매월 ${repeatDom}일 ${repeatTime}`
  const schedSummary: ReactNode = sched === 'immediate' ? '즉시 발송'
    : sched === 'scheduled' ? <><Tag>예약</Tag> {scheduledAt ? scheduledAt.replace('T', ' ') : ''}</>
    : <><Tag>반복</Tag> {repeatDesc}</>

  // 예약 시각은 게임 시간대로 읽는다 — 시간대를 받기 전에는 예약을 보내지 않는다.
  const tz = useGameTimeZone(target)
  const tzText = tz ? `${timeZoneName(tz)} 기준` : ''
  const pushApplies = push
  const pushSummary: ReactNode = pushApplies ? (pushIsAd ? <><Tag>광고</Tag> 보냄</> : '보냄') : null
  const canSend = msgOk && expiresDays > 0 && targetOk && schedOk && (sched === 'immediate' || !!tz)
    && items.every((it) => it.key && Number(it.count) > 0)

  const resetForm = () => {
    setTitle(''); setContent(''); setItems([]); setSelected({}); setLangRows([])
    setSched('immediate'); setMode('players'); setScheduledAt('')
    setRepeatUnit('day'); setRepeatTime('12:00'); setRepeatDow(1); setRepeatDom(1)
    setCategory('default'); setExpiresDays(7); setServerId(''); setOpenForNew(false)
    setPush(false); setPushIsAd(false)
    setActive(0); setReached(0)
  }

  // 받은 우편은 취소할 수 없어(우편 내역의 취소는 안 받은 것만 지운다), 대상과 상관없이 매번 확인 창을 거친다.
  const requestSubmit = () => {
    if (!canSend || sending) return
    setSendError('')
    setAskSend(true)
  }

  const submit = async () => {
    if (!canSend || sending) return
    setSending(true); setSendError(''); setNotice(''); setError('')
    try {
      const common = {
        mode,
        accountIds: mode === 'players' ? playerIds : [],
        serverId: mode === 'server' ? serverId : null,
        openForNew: openApplies,
        title, content, category: category.trim() || 'default',
        items: items.map((it) => ({ key: it.key, count: Number(it.count) })),
        localized,
        push: pushApplies,
        pushIsAd: pushApplies && pushIsAd,
      }
      if (sched === 'immediate') {
        const expiresAt = new Date(Date.now() + expiresDays * 86400000).toISOString()
        const res = await callAdmin<{ recipient_count: number; push_count?: number }>(target, 'mails.send', { ...common, expiresAt })
        const pushPart = res?.push_count === undefined ? ''
          : res.push_count < 0 ? ' 푸시는 넣지 못했습니다.'
          : ` 푸시 ${formatCount(res.push_count)}명.`
        setNoticeLink({ to: '/mails/records', label: '우편 내역' })
        setNotice(`발송 완료 — ${formatCount(res?.recipient_count ?? 0)}명에게 보냈습니다.${pushPart}`)
      } else {
        await callAdmin(target, 'mails.createSchedule', {
          ...common, scheduleType: sched, expiresDays,
          // 입력칸 값에는 시간대가 없다 — 운영자 PC 시간대가 아니라 안내대로 게임 시간대로 읽는다.
          scheduledAt: sched === 'scheduled' && tz ? localInputToIso(scheduledAt, tz) : null,
          repeatTime: sched === 'repeat' ? repeatTime : null,
          repeatUnit, repeatDow, repeatDom,
        })
        setNoticeLink({ to: '/mails/schedules', label: '예약 목록' })
        setNotice(sched === 'scheduled' ? '예약 등록을 마쳤습니다.' : '반복 예약 등록을 마쳤습니다.')
      }
      setAskSend(false)
      resetForm()
    } catch (e: unknown) {
      if (e instanceof NotAuthenticatedError) { onUnauthenticated(); return }
      setSendError(e instanceof Error ? e.message : '실패했습니다.')
    } finally { setSending(false) }
  }

  return (
    <div className="space-y-4">
      <PageHeader title="우편 발송" description="플레이어에게 우편을 발송합니다." />

      <ErrorBanner message={error} onDismiss={() => setError('')} />
      {notice && (
        <div className="flex items-center justify-between gap-3 rounded-md border border-emerald-200 bg-emerald-50 px-4 py-2.5 text-sm text-emerald-700">
          <span>{notice}</span>
          <Link to={noticeLink.to} className="shrink-0 underline hover:no-underline">{noticeLink.label}</Link>
        </div>
      )}

      {/* 1. 발송 시점 */}
      <Step n={1} title="발송 시점" state={stepState(0)} summary={schedSummary} onEdit={() => setActive(0)}>
        <Segmented value={sched} onChange={setSched}
          options={[{ v: 'immediate', label: '즉시' }, { v: 'scheduled', label: '예약' }, { v: 'repeat', label: '반복' }]} />
        {sched === 'scheduled' && (
          <Field label="예약 시각">
            <input type="datetime-local" value={scheduledAt} onChange={(e) => setScheduledAt(e.target.value)} className={inputCls + ' w-full max-w-xs'} />
            <p className="text-xs text-neutral-400 mt-1">{tzText}</p>
          </Field>
        )}
        {sched === 'repeat' && (
          <div className="space-y-3">
            <Field label="반복 주기">
              <Segmented value={repeatUnit} onChange={setRepeatUnit}
                options={[{ v: 'day', label: '매일' }, { v: 'week', label: '매주' }, { v: 'month', label: '매월' }]} />
            </Field>
            <div className="flex flex-wrap items-end gap-3">
              {repeatUnit === 'week' && (
                <Field label="요일">
                  <select value={repeatDow} onChange={(e) => setRepeatDow(Number(e.target.value))} className={inputCls + ' w-28'}>
                    {DOW_LABEL.map((d, i) => (<option key={i} value={i}>{d}요일</option>))}
                  </select>
                </Field>
              )}
              {repeatUnit === 'month' && (
                <Field label="일자">
                  <select value={repeatDom} onChange={(e) => setRepeatDom(Number(e.target.value))} className={inputCls + ' w-28'}>
                    {Array.from({ length: 31 }, (_, i) => i + 1).map((d) => (<option key={d} value={d}>{d}일</option>))}
                  </select>
                </Field>
              )}
              <Field label="발송 시각"><input type="time" value={repeatTime} onChange={(e) => setRepeatTime(e.target.value)} className={inputCls + ' w-32'} /></Field>
            </div>
            <p className="text-xs text-neutral-400">{tz ? `발송 시각은 ${timeZoneName(tz)} 기준입니다.` : ''}</p>
            {repeatUnit === 'month' && repeatDom > 28 && (
              <p className="text-xs text-amber-600">해당 일이 없는 달에는 말일에 발송됩니다.</p>
            )}
          </div>
        )}
        {active === 0 && <StepNextButton label={reached > 0 ? '완료' : '다음'} disabled={!schedOk} onClick={() => goNext(0)} />}
      </Step>

      {/* 2. 발송 대상 */}
      {reached >= 1 && (
        <Step n={2} title="발송 대상" state={stepState(1)} summary={targetSummary} onEdit={() => setActive(1)}>
          <Segmented value={mode} onChange={setMode}
            options={[{ v: 'all', label: '전체' }, { v: 'server', label: '서버' }, { v: 'players', label: '플레이어' },
              ...(sched === 'immediate' ? [{ v: 'operators' as const, label: '시험 발송' }] : [])]} />

          {mode === 'operators' && (
            <p className="text-sm text-neutral-700">
              {operatorCount === null ? '…'
                : operatorCount > 0 ? `시험 우편 받기 권한이 있는 운영 계정 ${formatCount(operatorCount)}명에게 보냅니다.`
                : '시험 우편 받기 권한이 있는 운영 계정이 없습니다.'}
            </p>
          )}

          {mode === 'all' && (
            <div className="rounded-md bg-amber-50 border border-amber-200 text-amber-700 text-sm px-3 py-2">
              모든 플레이어에게 발송됩니다.
            </div>
          )}

          {mode === 'server' && (
            <Field label="서버">
              <select value={serverId} onChange={(e) => setServerId(e.target.value)} className={inputCls + ' w-full max-w-sm'}>
                <option value="">서버 선택…</option>
                {servers.map((s) => (<option key={s.id} value={s.id}>{s.display_name || s.server_code}</option>))}
              </select>
            </Field>
          )}

          {(mode === 'all' || mode === 'server') && sched !== 'repeat' && (
            <div>
              <label className="inline-flex items-center gap-2 text-sm text-neutral-700 cursor-pointer">
                <input type="checkbox" checked={openForNew} onChange={(e) => setOpenForNew(e.target.checked)} />
                나중에 들어온 플레이어도 받기
              </label>
              <p className="text-xs text-neutral-400 mt-1">수령 기한까지 새로 가입하거나 이 대상이 된 플레이어에게도 1분 안에 보냅니다.</p>
            </div>
          )}

          {mode === 'players' && (
            <div className="space-y-3">
              <Field label="닉네임·ID 검색">
                <div className="flex items-center gap-2">
                  <input value={playerInput} onChange={(e) => setPlayerInput(e.target.value)} onKeyDown={(e) => e.key === 'Enter' && setPlayerQuery(playerInput)}
                    placeholder="닉네임 / 계정 ID" className={inputCls + ' flex-1'} />
                  <Button variant="primary" size="compact" className="shrink-0" onClick={() => setPlayerQuery(playerInput)}>
                    <Search className="w-3.5 h-3.5" />검색
                  </Button>
                </div>
              </Field>
              <div className="max-h-40 overflow-auto border border-neutral-200 rounded-md divide-y divide-neutral-100">
                {playerRows.map((p) => (
                  <label key={p.account_id} className="flex items-center gap-2 px-3 py-1.5 text-sm hover:bg-neutral-50 cursor-pointer">
                    <input type="checkbox" checked={!!selected[p.account_id]} onChange={() => togglePlayer(p)} />
                    <span className="truncate">{playerLabel(p.display_name, p.account_id)}</span>
                    <span className="text-xs text-neutral-400 font-mono ml-auto shrink-0">{p.account_id.slice(0, 8)}</span>
                  </label>
                ))}
                {playerRows.length === 0 && <div className="px-3 py-2 text-xs text-neutral-400">검색 결과 없음</div>}
              </div>
              {Object.keys(selected).length > 0 && (
                <div className="flex flex-wrap gap-1.5">
                  {Object.entries(selected).map(([id, name]) => (
                    <span key={id} className="inline-flex items-center gap-1 text-xs bg-neutral-100 rounded px-2 py-1">
                      {name}
                      <button onClick={() => setSelected((s) => { const n = { ...s }; delete n[id]; return n })}><X className="w-3 h-3" /></button>
                    </span>
                  ))}
                </div>
              )}
              <div className="text-xs text-neutral-500">
                선택 <span className="font-medium text-neutral-700">{playerIds.length}명</span>
              </div>
            </div>
          )}
          {active === 1 && <StepNextButton label={reached > 1 ? '완료' : '다음'} disabled={!targetOk} onClick={() => goNext(1)} />}
        </Step>
      )}

      {/* 3. 메시지 */}
      {reached >= 2 && (
        <Step n={3} title="메시지" state={stepState(2)} summary={title || '제목 없음'} onEdit={() => setActive(2)}>
          <Field label="분류">
            <select value={category} onChange={(e) => setCategory(e.target.value)} className={inputCls + ' w-full'}>
              {!categories.some((c) => c.key === 'default') && <option value="default">기본</option>}
              {categories.map((c) => (<option key={c.key} value={c.key}>{c.display_name || c.key}</option>))}
            </select>
          </Field>
          <Field label={defaultLangField(defaultLang, '제목')}><input value={title} onChange={(e) => setTitle(e.target.value)} className={inputCls + ' w-full'} /></Field>
          <Field label={defaultLangField(defaultLang, '본문')}><textarea value={content} onChange={(e) => setContent(e.target.value)} rows={4} className={areaCls + ' w-full'} /></Field>

          <div className="flex items-center justify-between pt-1">
            <span className="text-xs font-medium text-neutral-500">다국어 메시지</span>
            <button onClick={addLang} disabled={!canAddLang}
              className="inline-flex items-center gap-1 text-xs text-[#1677ff] disabled:opacity-40 disabled:cursor-not-allowed">
              <Plus className="w-3 h-3" />언어 추가
            </button>
          </div>
          {langRows.length === 0 && <p className="text-xs text-neutral-400">추가한 언어를 쓰는 플레이어에게는 이 제목·본문이 보입니다. 나머지 플레이어에게는 위의 제목·본문이 보입니다.</p>}
          {langRows.map((r, i) => (
            <div key={i} className="border border-neutral-200 rounded-md p-2.5 space-y-2 bg-neutral-50/50">
              <div className="flex items-center gap-2">
                <div className="w-28"><select value={r.lang} onChange={(e) => setLang(i, { lang: e.target.value })} className={inputCls + ' w-full'}>
                  {pickableLangs(defaultLang, langRows.filter((_, j) => j !== i).map((x) => x.lang), r.lang)
                    .map((l) => (<option key={l} value={l}>{langLabel(l)}</option>))}
                </select></div>
                <div className="flex-1" />
                <button onClick={() => delLang(i)} title="이 언어 삭제" aria-label="이 언어 삭제" className="text-neutral-400 hover:text-red-500 shrink-0"><X className="w-4 h-4" /></button>
              </div>
              <Field label="제목"><input value={r.title} onChange={(e) => setLang(i, { title: e.target.value })} className={inputCls + ' w-full'} /></Field>
              <Field label="본문"><textarea value={r.content} onChange={(e) => setLang(i, { content: e.target.value })} rows={2} className={areaCls + ' w-full'} /></Field>
            </div>
          ))}
          {!langFilled && (
            <p className="text-xs text-amber-600">추가한 언어에는 제목이나 본문 중 하나 이상을 입력하세요. 사용하지 않을 언어는 삭제하면 됩니다.</p>
          )}
          {langDup && (
            <p className="text-xs text-amber-600">같은 언어가 중복 선택됐습니다. 중복된 언어를 삭제하세요.</p>
          )}
          {active === 2 && <StepNextButton label={reached > 2 ? '완료' : '다음'} disabled={!msgOk} onClick={() => goNext(2)} />}
        </Step>
      )}

      {/* 4. 보상 · 발송 */}
      {active === 3 && (
        <WhiteCard className="p-4 space-y-4">
          <div className="flex items-center gap-3">
            <span className="w-6 h-6 shrink-0 rounded-full flex items-center justify-center text-xs font-semibold bg-[#1677ff] text-white">4</span>
            <span className="text-sm font-semibold text-neutral-800">보상·발송</span>
          </div>

          <div className="space-y-2">
            <div className="flex items-center justify-between">
              <span className="text-xs font-medium text-neutral-500">보상 아이템</span>
              <button onClick={addItem} className="inline-flex items-center gap-1 text-xs text-[#1677ff]"><Plus className="w-3 h-3" />추가</button>
            </div>
            {items.length === 0 && <p className="text-xs text-neutral-400">첨부할 보상이 없으면 비워 두세요.</p>}
            {items.map((it, i) => (
              <div key={i} className="space-y-1">
                <div className="flex items-center gap-2">
                  <select value={it.key} onChange={(e) => setItem(i, { key: e.target.value })} className={inputCls + ' flex-1'}>
                    {catalog.map((c) => (<option key={c.key} value={c.key}>{itemOptionLabel(c)}</option>))}
                  </select>
                  <input type="number" min={1} value={it.count} onChange={(e) => setItem(i, { count: Number(e.target.value) })} className={inputCls + ' w-24'} />
                  <button onClick={() => delItem(i)} className="text-neutral-400 hover:text-red-500 shrink-0"><X className="w-4 h-4" /></button>
                </div>
                <BrokenItemNote mode={catalog.find((c) => c.key === it.key)?.grant_mode} />
              </div>
            ))}
            {catalog.length === 0 && <div className="text-xs text-amber-600">카탈로그가 비어 있습니다. 아이템 카탈로그 페이지에서 먼저 등록하세요.</div>}
          </div>

          <div className="flex items-center gap-2 border-t border-neutral-100 pt-3">
            <span className="text-sm text-neutral-600 shrink-0">수령 기한</span>
            <input type="number" min={1} value={expiresDays} onChange={(e) => setExpiresDays(Number(e.target.value))} className={inputCls + ' w-20'} />
            <span className="text-sm text-neutral-500">일</span>
          </div>

          <div className="space-y-2 border-t border-neutral-100 pt-3">
            <div className="flex items-center gap-2">
              <span className="text-sm text-neutral-600 shrink-0 w-24">푸시 알림</span>
              <Switch value={pushApplies} onChange={(v) => { setPush(v); if (!v) setPushIsAd(false) }} />
            </div>
            {pushApplies && (
              <div className="flex items-center gap-2">
                <span className="text-sm text-neutral-600 shrink-0 w-24">광고 여부</span>
                <Switch value={pushIsAd} onChange={setPushIsAd} />
              </div>
            )}
            {pushApplies && (
              <p className="text-xs text-neutral-400">
                {pushIsAd
                  ? `광고 수신에 동의한 플레이어에게만 갑니다. ${tzText ? `${tzText}으로 ` : ''}밤 9시부터 아침 8시 사이에는 야간 수신에 동의하지 않은 플레이어에게 아침 8시에 갑니다.`
                  : '우편 알림을 켜 둔 플레이어의 기기로 우편 제목이 갑니다.'}
              </p>
            )}
          </div>

          <InfoList className="rounded-md bg-neutral-50 border border-neutral-200 px-3 py-2.5" items={[
            { label: '발송', value: schedSummary },
            { label: '대상', value: targetDetail },
            { label: '분류', value: catLabel(category) },
            { label: '보상', value: items.length > 0 ? `${items.length}종` : null },
            { label: '다국어', value: localized ? `${Object.keys(localized).length}개` : null },
            { label: '푸시', value: pushSummary },
          ]} />

          <div className="flex items-center gap-3">
            <Button variant="primary" className="ml-auto" onClick={requestSubmit} disabled={!canSend || sending}>
              {sending ? <Loader2 className="w-4 h-4 animate-spin" /> : <Send className="w-4 h-4" />}{sched === 'immediate' ? '발송' : '예약 등록'}
            </Button>
          </div>
        </WhiteCard>
      )}

      <ConfirmDialog
        open={askSend}
        title={sched === 'immediate' ? '우편 발송' : '우편 예약 등록'}
        confirmLabel={sched === 'immediate' ? '발송' : '예약 등록'}
        danger={mode === 'all'}
        busy={sending}
        error={sendError || undefined}
        onConfirm={() => { void submit() }}
        onCancel={() => setAskSend(false)}
        description="플레이어가 받은 우편은 취소할 수 없습니다. 받기 전 우편은 우편 내역에서 취소할 수 있습니다."
      >
        <BrokenItemsConfirm items={brokenItemsIn(items, catalog)} />
        <div className="rounded-md bg-neutral-50 border border-neutral-200 px-3 py-2 text-sm space-y-2">
          <div className="text-neutral-800">{title || '제목 없음'}</div>
          <InfoList items={[
            { label: '발송', value: schedSummary },
            { label: '대상', value: targetDetail, ...(mode === 'all' ? { tone: 'danger' as const } : {}) },
            { label: '아이템', value: items.length > 0 ? <Tags wrap items={itemLabels(items, catalog)} /> : '없음' },
            { label: '수령 기한', value: `${formatCount(expiresDays)}일` },
            { label: '푸시', value: pushSummary },
          ]} />
        </div>
      </ConfirmDialog>
    </div>
  )
}
