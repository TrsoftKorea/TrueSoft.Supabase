import { useEffect, useRef, useState, type ReactNode } from 'react'
import { Loader2, Send, X, Plus, Check, ChevronDown, Search } from 'lucide-react'
import { callAdmin, NotAuthenticatedError } from '../lib/api'
import { useSearchParams } from 'react-router-dom'
import { type ProjectTarget } from '../lib/projectTarget'
import { useProjectLook } from '../lib/projectLook'
import { WhiteCard } from '../components/ui/Card'
import { Button } from '../components/ui/Button'
import { PageHeader } from '../components/ui/PageHeader'
import { ConfirmDialog } from '../components/ui/ConfirmDialog'
import { ErrorBanner } from '../components/ui/ErrorBanner'
import { Segmented } from '../components/ui/Segmented'
import { formatCount, itemsText } from '../components/ui/format'
import { areaCls, inputCls } from '../components/ui/Input'
import { PLATFORMS, platformLabel } from '../lib/platforms'
import { localInputToIso, timeZoneName, useGameTimeZone } from '../lib/gameTime'

type Mode = 'all' | 'server' | 'players'
type Sched = 'immediate' | 'scheduled' | 'repeat'
type RepeatUnit = 'day' | 'week' | 'month'
type ItemRow = { key: string; count: number }
type LangRow = { lang: string; title: string; content: string }
type ServerRow = { id: string; server_code: string; display_name: string }
type GameItem = { key: string; display_name: string }
type CategoryRow = { key: string; display_name: string }
type PlayerRow = { account_id: string; display_name: string }

const DOW_LABEL = ['일', '월', '화', '수', '목', '금', '토']
const LANGS = ['en', 'ja', 'ko', 'zh-CN', 'zh-TW', 'de', 'fr', 'es', 'pt', 'ru', 'id', 'th', 'vi']
const LANG_LABEL: Record<string, string> = {
  en: '영어', ja: '일본어', ko: '한국어', 'zh-CN': '중국어(간체)', 'zh-TW': '중국어(번체)',
  de: '독일어', fr: '프랑스어', es: '스페인어', pt: '포르투갈어', ru: '러시아어',
  id: '인도네시아어', th: '태국어', vi: '베트남어',
}

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
  n: number; title: string; state: 'active' | 'done' | 'locked'; summary?: string; onEdit?: () => void; children: ReactNode
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
      {expanded && <div className="px-4 pb-4 pt-1 space-y-3 border-t border-neutral-100">{children}</div>}
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
  const look = useProjectLook()
  // 플레이어 상세의 "우편 보내기"가 ?account=&name= 으로 넘겨 그 사람을 미리 골라 둔다.
  const [params] = useSearchParams()
  const presetAccount = params.get('account')
  const [mode, setMode] = useState<Mode>('players')
  const [selected, setSelected] = useState<Record<string, string>>(() =>
    presetAccount ? { [presetAccount]: params.get('name') || presetAccount.slice(0, 8) } : {},
  )
  const [serverId, setServerId] = useState('')
  const [platform, setPlatform] = useState('')
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
  const [error, setError] = useState('')
  const [playerQuery, setPlayerQuery] = useState('')
  const [playerInput, setPlayerInput] = useState('')
  const [playerRows, setPlayerRows] = useState<PlayerRow[]>([])
  const playerReqRef = useRef(0)
  const [servers, setServers] = useState<ServerRow[]>([])
  const [catalog, setCatalog] = useState<GameItem[]>([])
  const [categories, setCategories] = useState<CategoryRow[]>([])
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
  const togglePlayer = (p: PlayerRow) => setSelected((s) => { const n = { ...s }; if (n[p.account_id]) delete n[p.account_id]; else n[p.account_id] = p.display_name; return n })

  const addLang = () => setLangRows((x) => [...x, { lang: LANGS.find((l) => !x.some((r) => r.lang === l)) ?? '', title: '', content: '' }])
  const setLang = (i: number, patch: Partial<LangRow>) => setLangRows((x) => x.map((r, j) => (j === i ? { ...r, ...patch } : r)))
  const delLang = (i: number) => setLangRows((x) => x.filter((_, j) => j !== i))

  const playerIds = Object.keys(selected)

  const localized = (() => {
    const o: Record<string, { title: string; content: string }> = {}
    for (const r of langRows) { const l = r.lang.trim(); if (l) o[l] = { title: r.title, content: r.content } }
    return Object.keys(o).length > 0 ? o : null
  })()

  const schedOk = sched === 'immediate' || (sched === 'scheduled' && !!scheduledAt) || (sched === 'repeat' && !!repeatTime)
  const targetOk = mode === 'all' || (mode === 'server' && !!serverId) || (mode === 'players' && playerIds.length > 0)
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
    return c ? (c.display_name ? `${c.display_name}` : c.key) : key
  }
  // 플레이어를 직접 고른 발송에는 플랫폼이 걸리지 않는다(서버도 무시한다).
  const platformPart = mode !== 'players' && platform ? ` · ${platformLabel(platform)}만` : ''
  // 반복 발송에는 걸 수 없다 — 회차마다 열려 있어 나중에 가입한 사람이 지난 회차를 한꺼번에 받는다.
  const openApplies = mode !== 'players' && sched !== 'repeat' && openForNew
  const openPart = openApplies ? ' · 나중에 들어온 플레이어도' : ''
  const targetSummary = (mode === 'all' ? '전체 플레이어'
    : mode === 'server' ? (servers.find((s) => s.id === serverId)?.display_name ?? '서버 미선택')
    : `플레이어 ${formatCount(playerIds.length)}명`) + platformPart + openPart
  const repeatDesc = repeatUnit === 'day' ? `매일 ${repeatTime}`
    : repeatUnit === 'week' ? `매주 ${DOW_LABEL[repeatDow]}요일 ${repeatTime}`
    : `매월 ${repeatDom}일 ${repeatTime}`
  const schedSummary = sched === 'immediate' ? '즉시 발송'
    : sched === 'scheduled' ? `예약 · ${scheduledAt ? scheduledAt.replace('T', ' ') : ''}`
    : `반복 · ${repeatDesc}`

  // 예약 시각은 게임 시간대로 읽는다 — 시간대를 받기 전에는 예약을 보내지 않는다.
  const tz = useGameTimeZone(target)
  const tzText = tz ? `${timeZoneName(tz)} 기준` : ''
  const canSend = msgOk && expiresDays > 0 && targetOk && schedOk && (sched === 'immediate' || !!tz)
    && items.every((it) => it.key && Number(it.count) > 0)

  const resetForm = () => {
    setTitle(''); setContent(''); setItems([]); setSelected({}); setLangRows([])
    setSched('immediate'); setMode('players'); setScheduledAt('')
    setRepeatUnit('day'); setRepeatTime('12:00'); setRepeatDow(1); setRepeatDom(1)
    setCategory('default'); setExpiresDays(7); setServerId(''); setPlatform(''); setOpenForNew(false)
    setActive(0); setReached(0)
  }

  // 보낸 우편은 회수할 수 없어, 대상과 상관없이 매번 확인 창을 거친다.
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
        platform: mode === 'players' ? null : platform || null,
        openForNew: openApplies,
        title, content, category: category.trim() || 'default',
        items: items.map((it) => ({ key: it.key, count: Number(it.count) })),
        localized,
      }
      if (sched === 'immediate') {
        const expiresAt = new Date(Date.now() + expiresDays * 86400000).toISOString()
        const res = await callAdmin<{ recipient_count: number }>(target, 'mails.send', { ...common, expiresAt })
        setNotice(`발송 완료 — ${formatCount(res?.recipient_count ?? 0)}명에게 보냈습니다.`)
      } else {
        await callAdmin(target, 'mails.createSchedule', {
          ...common, scheduleType: sched, expiresDays,
          // 입력칸 값에는 시간대가 없다 — 운영자 PC 시간대가 아니라 안내대로 게임 시간대로 읽는다.
          scheduledAt: sched === 'scheduled' && tz ? localInputToIso(scheduledAt, tz) : null,
          repeatTime: sched === 'repeat' ? repeatTime : null,
          repeatUnit, repeatDow, repeatDom,
        })
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
    <div className="space-y-4 max-w-3xl">
      <PageHeader title="우편 발송" description="플레이어에게 우편을 발송합니다." />

      <ErrorBanner message={error} onDismiss={() => setError('')} />
      {notice && <div className="rounded-md border border-emerald-200 bg-emerald-50 px-4 py-2.5 text-sm text-emerald-700">{notice}</div>}

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
            options={[{ v: 'all', label: '전체' }, { v: 'server', label: '서버' }, { v: 'players', label: '플레이어' }]} />

          {mode === 'all' && (
            <div className="rounded-md bg-amber-50 border border-amber-200 text-amber-700 text-sm px-3 py-2">
              {platform ? `${platformLabel(platform)} 플레이어 모두에게 발송됩니다.` : '모든 플레이어에게 발송됩니다.'}
            </div>
          )}

          {mode === 'server' && (
            <Field label="서버">
              <select value={serverId} onChange={(e) => setServerId(e.target.value)} className={inputCls + ' w-full max-w-sm'}>
                <option value="">서버 선택…</option>
                {servers.map((s) => (<option key={s.id} value={s.id}>{s.display_name} ({s.server_code})</option>))}
              </select>
            </Field>
          )}

          {mode !== 'players' && (
            <Field label="플랫폼">
              <Segmented value={platform} onChange={setPlatform}
                options={[{ v: '', label: '전체' }, ...PLATFORMS.map((p) => ({ v: p.v as string, label: p.label }))]} />
            </Field>
          )}

          {mode !== 'players' && sched !== 'repeat' && (
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
                    <span className="truncate">{p.display_name}</span>
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
          <Field label="제목"><input value={title} onChange={(e) => setTitle(e.target.value)} className={inputCls + ' w-full'} /></Field>
          <Field label="본문"><textarea value={content} onChange={(e) => setContent(e.target.value)} rows={4} className={areaCls + ' w-full'} /></Field>

          <div className="flex items-center justify-between pt-1">
            <span className="text-xs font-medium text-neutral-500">다국어 메시지</span>
            <button onClick={addLang} disabled={langRows.length >= LANGS.length}
              className="inline-flex items-center gap-1 text-xs text-[#1677ff] disabled:opacity-40 disabled:cursor-not-allowed">
              <Plus className="w-3 h-3" />언어 추가
            </button>
          </div>
          {langRows.length === 0 && <p className="text-xs text-neutral-400">추가한 언어를 쓰는 플레이어에게는 이 제목·본문이 보입니다. 나머지 플레이어에게는 위의 제목·본문이 보입니다.</p>}
          {langRows.map((r, i) => (
            <div key={i} className="border border-neutral-200 rounded-md p-2.5 space-y-2 bg-neutral-50/50">
              <div className="flex items-center gap-2">
                <div className="w-28"><select value={r.lang} onChange={(e) => setLang(i, { lang: e.target.value })} className={inputCls + ' w-full'}>
                  {LANGS.filter((l) => l === r.lang || !langRows.some((x, j) => j !== i && x.lang === l))
                    .map((l) => (<option key={l} value={l}>{LANG_LABEL[l] ?? l} ({l})</option>))}
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
            <span className="text-sm font-semibold text-neutral-800">보상 · 발송</span>
          </div>

          <div className="space-y-2">
            <div className="flex items-center justify-between">
              <span className="text-xs font-medium text-neutral-500">보상 아이템</span>
              <button onClick={addItem} className="inline-flex items-center gap-1 text-xs text-[#1677ff]"><Plus className="w-3 h-3" />추가</button>
            </div>
            {items.length === 0 && <p className="text-xs text-neutral-400">첨부할 보상이 없으면 비워 두세요.</p>}
            {items.map((it, i) => (
              <div key={i} className="flex items-center gap-2">
                <select value={it.key} onChange={(e) => setItem(i, { key: e.target.value })} className={inputCls + ' flex-1'}>
                  {catalog.map((c) => (<option key={c.key} value={c.key}>{c.display_name} ({c.key})</option>))}
                </select>
                <input type="number" min={1} value={it.count} onChange={(e) => setItem(i, { count: Number(e.target.value) })} className={inputCls + ' w-24'} />
                <button onClick={() => delItem(i)} className="text-neutral-400 hover:text-red-500 shrink-0"><X className="w-4 h-4" /></button>
              </div>
            ))}
            {catalog.length === 0 && <div className="text-xs text-amber-600">카탈로그가 비어 있습니다. 아이템 카탈로그 페이지에서 먼저 등록하세요.</div>}
          </div>

          <div className="flex items-center gap-2 border-t border-neutral-100 pt-3">
            <span className="text-sm text-neutral-600 shrink-0">수령 기한</span>
            <input type="number" min={1} value={expiresDays} onChange={(e) => setExpiresDays(Number(e.target.value))} className={inputCls + ' w-20'} />
            <span className="text-sm text-neutral-500">일</span>
          </div>

          <div className="rounded-md px-3 py-2 text-sm font-medium text-white" style={{ backgroundColor: look.color(target) }}>
            대상 게임: {look.name(target)}
          </div>
          <div className="rounded-md bg-neutral-50 border border-neutral-200 px-3 py-2 text-sm text-neutral-600">
            <span className="font-medium text-neutral-800">{schedSummary}</span> · 대상 <span className="font-medium text-neutral-800">{targetSummary}</span> · 분류 <span className="font-medium text-neutral-800">{catLabel(category)}</span>
            {items.length > 0 && <> · 보상 {items.length}종</>}
            {localized && <> · 다국어 {Object.keys(localized).length}개</>}
          </div>

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
        description="보낸 우편은 회수할 수 없습니다. 대상과 아이템을 다시 확인하세요."
      >
        <div className="rounded-md bg-neutral-50 border border-neutral-200 px-3 py-2 text-sm space-y-1">
          <div className="text-neutral-800">{title || '제목 없음'}</div>
          <div className="text-xs text-neutral-500">
            {schedSummary} · 대상 <span className={mode === 'all' ? 'font-semibold text-red-600' : 'font-medium text-neutral-700'}>{targetSummary}</span>
          </div>
          <div className="text-xs text-neutral-500">
            {items.length > 0 ? itemsText(items, catalog) : '첨부 아이템 없음'}
          </div>
          <div className="text-xs text-neutral-500">수령 기한 {formatCount(expiresDays)}일</div>
        </div>
      </ConfirmDialog>
    </div>
  )
}
