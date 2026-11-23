import { useCallback, useEffect, useRef, useState, type KeyboardEvent, type ReactNode } from 'react'
import { Check, CircleHelp, Loader2, Pencil, Plus, Trash2, X } from 'lucide-react'
import { callAdmin, NotAuthenticatedError } from '../lib/api'
import type { ProjectTarget } from '../lib/projectTarget'
import { EditDialog } from '../components/ui/EditDialog'
import { inputCls, inputCompactCls } from '../components/ui/Input'
import { PageHeader } from '../components/ui/PageHeader'
import { RefreshButton } from '../components/ui/FilterBar'
import { TableCard } from '../components/ui/DataTable'
import { ConfirmDialog } from '../components/ui/ConfirmDialog'
import { formatCount, formatDateTime } from '../components/ui/format'
import { Link } from 'react-router-dom'
import { ErrorBanner } from '../components/ui/ErrorBanner'
import { Button } from '../components/ui/Button'
import { StatusText } from '../components/ui/StatusText'
import { elementField, elementFits, GRANT_NAME, isBrokenGrant, type CollectionKind, type GrantMode } from '../lib/gameItems'

type Item = { key: string; display_name: string; created_at: string; source?: 'save' | 'manual'; grant_mode?: GrantMode }
// grantable: 서버가 수량을 더할 수 있는 숫자 데이터 필드. 예약 필드(id·account_id)와 숫자가 아닌 필드는 false.
// collection: 리스트·딕셔너리 데이터 필드 — 이름 그대로는 못 쓰고 원소 꼴(칸[번호]·칸.키)이면 자동 지급.
// collection_kind: 저장된 값의 모양 — 맞는 원소 꼴만 자동 지급. 옛 서버는 둘 다 안 보낸다.
type DataField = { column_name: string; grantable: boolean; registered: boolean; collection?: boolean; collection_kind?: CollectionKind }

const LIST_ELEMENT_NOTE = '리스트 원소는 플레이어 데이터에 그 번호의 칸이 있어야 받습니다.'

/** 리스트·딕셔너리 필드를 원소 꼴로 덜 적었거나 모양에 안 맞게 적었을 때의 안내. */
function elementGuide(c: DataField): ReactNode {
  if (c.collection_kind === 'other') return '숫자가 아닌 값을 담은 필드라 원소로도 쓸 수 없습니다.'
  const n = c.column_name
  const forms = c.collection_kind === 'list' ? [`${n}[0]`] : c.collection_kind === 'dict' ? [`${n}.키`] : [`${n}[0]`, `${n}.키`]
  const what = c.collection_kind === 'list' ? '리스트' : c.collection_kind === 'dict' ? '딕셔너리' : '리스트·딕셔너리'
  return <>{what} 데이터 필드는 원소로 적습니다 — {forms.map((f, i) => (
    <span key={f}>{i > 0 && ' · '}<span className="font-mono">{f}</span></span>
  ))}</>
}
// 제안 한 줄 — value 를 고르면 입력칸에 들어간다. 원소 꼴은 칸 이름 뒤 `[`·`.` 까지만 넣고 나머지는 운영자가 적는다.
type Suggestion = { value: string; name: string; hint?: string }
type FormItem = { key: string; display_name: string }
const EMPTY: FormItem = { key: '', display_name: '' }

const GRANT_TONE: Record<GrantMode, 'ok' | 'plain' | 'bad'> = { auto: 'ok', game: 'plain', missing: 'bad', blocked: 'bad' }
const GRANT_LABEL = Object.fromEntries(
  (Object.keys(GRANT_TONE) as GrantMode[]).map((m) => [m, <StatusText tone={GRANT_TONE[m]}>{GRANT_NAME[m]}</StatusText>]),
) as Record<GrantMode, ReactNode>

// gameItems.usage 한 줄 — 받기가 실패할 수 있는 우편이 어디 남아 있는지.
type Usage = { key: string; pending_mails: number; open_batches: number; active_schedules: number; active_coupons: number }
const USAGE_PARTS: { field: keyof Omit<Usage, 'key'>; label: string; to: string }[] = [
  { field: 'pending_mails', label: '안 받은 우편', to: '/mails/records' },
  { field: 'open_batches', label: '배달 중인 발송', to: '/mails/records' },
  { field: 'active_schedules', label: '켜진 예약', to: '/mails/schedules' },
  { field: 'active_coupons', label: '쓸 수 있는 쿠폰', to: '/coupons' },
]

/** 막힌 아이템 줄의 둘째 줄 — 0인 것은 빼고, 각 수는 처리할 화면으로 간다. */
function UsageLine({ usage }: { usage: Usage | null | undefined }) {
  if (usage === undefined) return <div className="text-xs text-neutral-400">세는 중</div>
  if (usage === null) return <div className="text-xs text-neutral-500">남은 우편 수를 세지 못했습니다.</div>
  const parts = USAGE_PARTS.filter((p) => usage[p.field] > 0)
  if (parts.length === 0) return <div className="text-xs text-neutral-500">남은 우편 없음</div>
  return (
    <div className="text-xs text-neutral-500">
      {parts.map((p, i) => (
        <span key={p.field}>
          {i > 0 && ' · '}
          <Link to={p.to} className="hover:underline">{p.label} {formatCount(usage[p.field])}</Link>
        </span>
      ))}
    </div>
  )
}
// 콘솔은 게임에 처리 코드가 등록됐는지 알 수 없다 — 그 한계를 물음표에 숨겨 둔다.
const GRANT_HELP = [
  '자동 지급: 플레이어가 받으면 같은 이름의 숫자 데이터 필드나, 리스트·딕셔너리 데이터 필드의 그 원소에 수량이 더해집니다. 게임이 이 코드에 처리 코드를 넣어 두었으면 그 코드가 대신 지급합니다.',
  LIST_ELEMENT_NOTE,
  '게임 처리: 게임에 이 코드의 처리 코드가 있어야 받을 수 있습니다.',
  '필드 없음: 이 이름의 데이터 필드가 사라졌습니다. 게임에 처리 코드가 없으면 받지 못합니다.',
  '형식 다름: 이 이름의 데이터 필드가 숫자가 아닙니다. 게임에 처리 코드가 없으면 받지 못합니다.',
  '콘솔은 게임에 처리 코드가 있는지 알 수 없습니다.',
].join('\n')

export default function GameItems({
  target,
  onUnauthenticated,
}: {
  target: ProjectTarget
  onUnauthenticated: () => void
}) {
  const [rows, setRows] = useState<Item[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  const [form, setForm] = useState<FormItem>(EMPTY)
  const [adding, setAdding] = useState(false)
  const [addOpen, setAddOpen] = useState(false)
  // 추가 실패는 창 안에 보인다 — 페이지 띠는 창 뒤에 가린다.
  const [addError, setAddError] = useState('')
  const [editingKey, setEditingKey] = useState<string | null>(null)
  const [editName, setEditName] = useState('')
  const [rowSaving, setRowSaving] = useState(false)
  const [delTarget, setDelTarget] = useState<Item | null>(null)
  const [deleting, setDeleting] = useState(false)
  const [deleteError, setDeleteError] = useState('')
  const [loadFailed, setLoadFailed] = useState(false)
  const [sugIndex, setSugIndex] = useState(-1)
  // null = 불러오는 중. 실패는 colError 로 따로 — 실패를 "제안할 필드가 없음"으로 보이게 하지 않는다.
  const [columns, setColumns] = useState<DataField[] | null>(null)
  const [colError, setColError] = useState('')
  const colReq = useRef(0)

  // 토큰 만료는 화면에 오류로 띄우지 않고 로그인으로 돌려보낸다.
  const report = useCallback(
    (e: unknown, fallback: string) => {
      if (e instanceof NotAuthenticatedError) {
        onUnauthenticated()
        return
      }
      setError(e instanceof Error ? e.message : fallback)
    },
    [onUnauthenticated],
  )

  // 막힌 아이템이 아직 붙어 있는 우편·발송·예약·쿠폰 수. undefined = 세는 중, null = 못 셈.
  // 우편 표를 훑는 조회라 막힌 코드만 묻는다(서버 한도 50개).
  const [usage, setUsage] = useState<Record<string, Usage> | null | undefined>(undefined)
  const usageReq = useRef(0)
  const loadUsage = useCallback(async (list: Item[]) => {
    const req = ++usageReq.current
    const keys = list.filter((r) => isBrokenGrant(r.grant_mode)).map((r) => r.key).slice(0, 50)
    if (keys.length === 0) { setUsage({}); return }
    setUsage(undefined)
    try {
      const res = await callAdmin<Usage[]>(target, 'gameItems.usage', { keys })
      if (req === usageReq.current) setUsage(Object.fromEntries(res.map((u) => [u.key, u])))
    } catch (e: unknown) {
      if (req !== usageReq.current) return
      if (e instanceof NotAuthenticatedError) { onUnauthenticated(); return }
      setUsage(null)
    }
  }, [target, onUnauthenticated])

  const load = useCallback(async () => {
    setLoading(true)
    setError('')
    setLoadFailed(false)
    try {
      const list = await callAdmin<Item[]>(target, 'gameItems.list')
      setRows(list)
      void loadUsage(list)
    } catch (e: unknown) {
      report(e, '불러오지 못했습니다.')
      setRows([])
      setLoadFailed(true)
    } finally {
      setLoading(false)
    }
  }, [target, report, loadUsage])

  useEffect(() => {
    void load()
  }, [load])

  const run = async (fn: () => Promise<void>, setBusy: (b: boolean) => void) => {
    setBusy(true)
    setError('')
    try {
      await fn()
      await load()
    } catch (e: unknown) {
      report(e, '실패했습니다.')
    } finally {
      setBusy(false)
    }
  }

  // 창을 다시 열면 앞서 보낸 요청의 늦은 응답은 버린다.
  const loadColumns = async () => {
    const req = ++colReq.current
    setColumns(null)
    setColError('')
    try {
      const cols = await callAdmin<DataField[]>(target, 'gameItems.columns')
      if (req === colReq.current) setColumns(cols)
    } catch (e: unknown) {
      if (req !== colReq.current) return
      if (e instanceof NotAuthenticatedError) { setAddOpen(false); onUnauthenticated(); return }
      setColError(e instanceof Error ? e.message : '데이터 필드를 불러오지 못했습니다.')
    }
  }

  const openAdd = () => {
    setForm(EMPTY)
    setAddError('')
    setSugIndex(-1)
    setAddOpen(true)
    void loadColumns()
  }

  const brokenCount = rows.filter((r) => isBrokenGrant(r.grant_mode)).length

  const typedKey = form.key.trim()
  // 서버 판정은 정확 일치다 — 대소문자가 다르면 다른 필드다(Gold 는 gold 필드가 아님).
  const exactField = columns?.find((c) => c.column_name === typedKey)
  const elemBase = elementField(typedKey)
  const isElement = !!elemBase && !!columns?.some((c) => c.column_name === elemBase && c.collection && elementFits(typedKey, c.collection_kind))
  const isListElement = isElement && typedKey.endsWith(']')
  const isAuto = !!exactField?.grantable || isElement
  // 숫자가 아닌 데이터 필드 이름은 서버가 새 코드로 받지 않는다 — 미리 막는다.
  const isNonNumeric = !!exactField && !exactField.grantable
  // 리스트·딕셔너리 필드 이름 뒤에 `[`·`.` 를 적었는데 원소 꼴이 아직 아님(번호·키를 덜 적음, 0 으로 시작하는 번호 등).
  const collectionNamed = (columns ?? []).find((c) => c.collection
    && (typedKey === c.column_name || typedKey.startsWith(c.column_name + '[') || typedKey.startsWith(c.column_name + '.')))
  const isPartialElement = !!collectionNamed && !isElement && typedKey !== collectionNamed.column_name
  const isDuplicate = rows.some((r) => r.key === typedKey)
  const lowered = typedKey.toLowerCase()
  const suggestions: Suggestion[] = !typedKey || elemBase ? [] : (columns ?? []).flatMap((c): Suggestion[] => {
    if (!c.column_name.toLowerCase().includes(lowered)) return []
    if (c.collection) return [
      ...(c.collection_kind === 'dict' || c.collection_kind === 'other' ? [] : [{ value: `${c.column_name}[`, name: c.column_name, hint: '[번호]' }]),
      ...(c.collection_kind === 'list' || c.collection_kind === 'other' ? [] : [{ value: `${c.column_name}.`, name: c.column_name, hint: '.키' }]),
    ]
    return c.grantable && !c.registered && c.column_name !== typedKey ? [{ value: c.column_name, name: c.column_name }] : []
  })
  const blocked = isNonNumeric || isPartialElement || isDuplicate

  const pick = (value: string) => {
    setForm({ ...form, key: value })
    setSugIndex(-1)
  }

  const onKeyKeyDown = (e: KeyboardEvent<HTMLInputElement>) => {
    if (suggestions.length > 0 && (e.key === 'ArrowDown' || e.key === 'ArrowUp')) {
      e.preventDefault()
      const step = e.key === 'ArrowDown' ? 1 : -1
      setSugIndex((i) => (i + step + suggestions.length) % suggestions.length)
      return
    }
    if (e.key !== 'Enter') return
    if (sugIndex >= 0 && suggestions[sugIndex]) pick(suggestions[sugIndex].value)
    else void add()
  }

  // 코드 아래 한 줄 — 등록 전에 지급 방식을 알게 한다. 필드 목록을 못 불러왔으면 지급 방식 대신 그 사실을 보인다.
  const keyStatus = colError ? (
    <span className="text-xs text-red-600">데이터 필드를 불러오지 못했습니다. {colError}</span>
  ) : !typedKey || columns === null ? null : isDuplicate ? (
    <span className="text-xs text-red-600">이미 있는 아이템 코드입니다.</span>
  ) : collectionNamed && !isElement ? (
    <span className="text-xs text-red-600">{elementGuide(collectionNamed)}</span>
  ) : isNonNumeric ? (
    <span className="text-xs text-red-600">숫자가 아닌 데이터 필드 이름이라 쓸 수 없습니다.</span>
  ) : (
    <span className="text-xs text-neutral-500 inline-flex items-center gap-1.5">
      지급 방식 {isAuto ? GRANT_LABEL.auto : GRANT_LABEL.game}
      {isListElement && (
        <span title={LIST_ELEMENT_NOTE} aria-label={LIST_ELEMENT_NOTE} className="cursor-help text-neutral-400">
          <CircleHelp className="w-3.5 h-3.5" />
        </span>
      )}
    </span>
  )

  const add = async () => {
    const key = form.key.trim()
    // 필드 목록이 오기 전에는 데이터 필드인지 몰라 source 를 정할 수 없다. 실패했으면 manual 로 보낸다(형식 다름은 서버가 거절).
    if (!key || adding || (columns === null && !colError) || isNonNumeric || isPartialElement) return
    // 서버는 같은 코드면 이름을 덮어쓴다 — 추가 창에서 기존 아이템 이름이 몰래 바뀌지 않게 막는다.
    if (rows.some((r) => r.key === key)) {
      setAddError('이미 있는 아이템 코드입니다.')
      return
    }
    setAdding(true)
    setAddError('')
    try {
      await callAdmin(target, 'gameItems.upsert', { key, displayName: form.display_name, source: isAuto ? 'save' : 'manual' })
      setForm(EMPTY)
      setAddOpen(false)
      await load()
    } catch (e: unknown) {
      if (e instanceof NotAuthenticatedError) { setAddOpen(false); onUnauthenticated(); return }
      setAddError(e instanceof Error ? e.message : '추가하지 못했습니다.')
    } finally {
      setAdding(false)
    }
  }

  const startEdit = (it: Item) => {
    setEditingKey(it.key)
    setEditName(it.display_name)
  }
  const cancelEdit = () => {
    setEditingKey(null)
    setEditName('')
  }
  const saveEdit = (key: string) => {
    if (rowSaving) return
    void run(async () => {
      await callAdmin(target, 'gameItems.upsert', { key, displayName: editName })
      cancelEdit()
    }, setRowSaving)
  }

  const confirmDelete = async () => {
    if (!delTarget || deleting) return
    const key = delTarget.key
    setDeleting(true)
    setDeleteError('')
    try {
      await callAdmin(target, 'gameItems.delete', { key })
      if (editingKey === key) cancelEdit()
      setDelTarget(null)
      await load()
    } catch (e: unknown) {
      if (e instanceof NotAuthenticatedError) onUnauthenticated()
      else setDeleteError(e instanceof Error ? e.message : '삭제하지 못했습니다.')
    } finally {
      setDeleting(false)
    }
  }


  return (
    <div className="space-y-4">
      <PageHeader
        title="아이템 카탈로그"
        description="우편·쿠폰으로 지급할 아이템을 등록합니다."
        actions={<>
          <RefreshButton onClick={() => void load()} loading={loading} />
          <Button variant="primary" onClick={openAdd} disabled={loadFailed}>
            <Plus className="w-4 h-4" />아이템 추가
          </Button>
        </>}
      />

      <ErrorBanner message={error} onDismiss={() => setError('')} />

      {addOpen && (
        <EditDialog
          title="아이템 추가"
          busy={adding}
          onClose={() => setAddOpen(false)}
          error={addError || undefined}
          footer={<>
            <Button variant="outline" onClick={() => setAddOpen(false)} disabled={adding}>취소</Button>
            <Button variant="primary" onClick={() => void add()} disabled={adding || !form.key.trim() || (columns === null && !colError) || blocked}>
              {adding && <Loader2 className="w-4 h-4 animate-spin" />}추가
            </Button>
          </>}
        >
          <div className="space-y-4">
            <div className="flex flex-col gap-1">
              <label htmlFor="item-key" className="text-xs font-medium text-neutral-500">코드</label>
              <input
                id="item-key"
                value={form.key}
                onChange={(e) => { setForm({ ...form, key: e.target.value }); setSugIndex(-1) }}
                onKeyDown={onKeyKeyDown}
                className={inputCls + ' w-full font-mono'}
                autoComplete="off"
                role="combobox"
                aria-expanded={suggestions.length > 0}
                aria-controls="item-key-suggestions"
                autoFocus
              />
              {suggestions.length > 0 && (
                <ul id="item-key-suggestions" role="listbox"
                  className="rounded-md border border-neutral-200 bg-surface py-1 max-h-48 overflow-y-auto">
                  {suggestions.map((s, i) => (
                    <li key={s.value} role="option" aria-selected={i === sugIndex}>
                      <button type="button" onMouseDown={(e) => e.preventDefault()} onClick={() => pick(s.value)}
                        className={`w-full text-left px-3 py-1.5 font-mono text-sm hover:bg-neutral-50 ${i === sugIndex ? 'bg-brand-soft' : ''}`}>
                        {s.name}{s.hint && <span className="text-neutral-400">{s.hint}</span>}
                      </button>
                    </li>
                  ))}
                </ul>
              )}
              {keyStatus}
            </div>
            <label className="flex flex-col gap-1">
              <span className="text-xs font-medium text-neutral-500">이름</span>
              <input
                value={form.display_name}
                onChange={(e) => setForm({ ...form, display_name: e.target.value })}
                onKeyDown={(e) => e.key === 'Enter' && void add()}
                className={inputCls + ' w-full'}
              />
            </label>
          </div>
        </EditDialog>
      )}

      {brokenCount > 0 && (
        <div className="text-sm"><StatusText tone="bad">필드 없음·형식 다름 {brokenCount}개</StatusText></div>
      )}

      <TableCard
        columns={[
          { key: 'key', header: '코드', kind: 'mono', cell: (it) => it.key },
          {
            key: 'name', header: '이름', kind: 'name', cell: (it) => editingKey === it.key ? (
              <input
                value={editName}
                onChange={(e) => setEditName(e.target.value)}
                onKeyDown={(e) => {
                  if (e.key === 'Enter') saveEdit(it.key)
                  if (e.key === 'Escape') cancelEdit()
                }}
                autoFocus
                className={inputCompactCls + ' w-full max-w-xs'}
              />
            ) : it.display_name || null,
          },
          {
            key: 'grant', kind: 'status',
            header: (
              <span className="inline-flex items-center gap-1">
                지급 방식
                <span title={GRANT_HELP} aria-label={GRANT_HELP} className="cursor-help text-neutral-400">
                  <CircleHelp className="w-3.5 h-3.5" />
                </span>
              </span>
            ),
            cell: (it) => !it.grant_mode ? null : isBrokenGrant(it.grant_mode) ? (
              <div>
                {GRANT_LABEL[it.grant_mode]}
                <UsageLine usage={usage === null ? null : usage === undefined ? undefined : usage[it.key] ?? null} />
              </div>
            ) : GRANT_LABEL[it.grant_mode],
          },
          { key: 'created', header: '등록 시각', kind: 'datetime', cell: (it) => formatDateTime(it.created_at) },
          {
            key: 'actions', header: '', kind: 'actions', cell: (it) => editingKey === it.key ? (
              <span className="inline-flex gap-1.5">
                <button
                  onClick={() => saveEdit(it.key)}
                  disabled={rowSaving}
                  className="text-emerald-600 hover:text-emerald-700 disabled:opacity-50"
                  aria-label="확인"
                  title="저장"
                >
                  {rowSaving ? <Loader2 className="w-4 h-4 animate-spin" /> : <Check className="w-4 h-4" />}
                </button>
                <button
                  onClick={cancelEdit}
                  disabled={rowSaving}
                  className="text-neutral-400 hover:text-neutral-700 disabled:opacity-50"
                  aria-label="취소"
                  title="취소"
                >
                  <X className="w-4 h-4" />
                </button>
              </span>
            ) : (
              <span className="inline-flex gap-1.5">
                <button
                  onClick={() => startEdit(it)}
                  className="text-neutral-400 hover:text-[#1677ff]"
                  aria-label="수정"
                  title="이름 수정"
                >
                  <Pencil className="w-4 h-4" />
                </button>
                <button
                  onClick={() => setDelTarget(it)}
                  className="text-neutral-400 hover:text-red-500"
                  aria-label="삭제"
                  title="삭제"
                >
                  <Trash2 className="w-4 h-4" />
                </button>
              </span>
            ),
          },
        ]}
        rows={rows}
        rowKey={(it) => it.key}
        loading={loading}
        failed={loadFailed}
        emptyText="등록된 아이템이 없습니다."
      />

      <ConfirmDialog
        open={!!delTarget}
        title="아이템 삭제"
        confirmLabel="삭제"
        danger
        busy={deleting}
        error={deleteError}
        onConfirm={() => void confirmDelete()}
        onCancel={() => { setDelTarget(null); setDeleteError('') }}
        description="이미 발송된 우편·쿠폰의 아이템은 그대로 남고, 앞으로 이 코드를 고를 수 없게 됩니다."
      >
        {delTarget && (
          <div className="rounded-md bg-neutral-50 border border-neutral-200 px-3 py-2 text-sm">
            <div className="text-neutral-800">{delTarget.display_name || '(이름 없음)'}</div>
            <div className="text-xs text-neutral-500 mt-1 font-mono">{delTarget.key}</div>
          </div>
        )}
      </ConfirmDialog>
    </div>
  )
}
