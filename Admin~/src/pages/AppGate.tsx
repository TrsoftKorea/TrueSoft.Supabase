import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react'
import { ChevronDown, ChevronUp, CircleHelp, Save, Users } from 'lucide-react'
import { callAdmin, NotAuthenticatedError } from '../lib/api'
import type { ProjectTarget } from '../lib/projectTarget'
import { isoToLocalInput, localInputToIso, timeZoneName, useGameTimeZone } from '../lib/gameTime'
import { useDefaultLanguage } from '../lib/languages'
import { CardHeader, FormCard } from '../components/ui/Card'
import { Button } from '../components/ui/Button'
import { ConfirmDialog } from '../components/ui/ConfirmDialog'
import { Switch } from '../components/ui/Switch'
import { StatusText } from '../components/ui/StatusText'
import type { OperatorAccount } from './OperatorAccounts'
import { inputCls } from '../components/ui/Input'
import { formatCount } from '../components/ui/format'
import { LastEdited } from '../components/ui/LastEdited'
import { fromRows, LocalizedFields, toRows } from './PushSettings'

type Gate = {
  android_min_version: string | null
  ios_min_version: string | null
  android_store_url: string | null
  ios_store_url: string | null
  maintenance_enabled: boolean
  maintenance_active: boolean
  maintenance_starts_at: string | null
  maintenance_ends_at: string | null
  maintenance_message: string
  maintenance_localized: Record<string, string> | null
  updated_at: string | null
  updated_by: string | null
}
type Report = (e: unknown, fallback: string) => void

const VERSION = /^[0-9]+(\.[0-9]+){0,3}$/
const BAD_VERSION = '버전은 숫자와 점으로 넣어 주세요.'
const BAD_TIME = '예상 종료 시각은 시작 시각보다 뒤여야 합니다.'
// 게임이 이 주소를 그대로 연다. admin-api 도 같은 꼴로 검사한다.
const URL_FORM = /^https?:\/\/\S+$/
const BAD_URL = 'https:// 로 시작하는 주소를 넣어 주세요.'
const REASON_TEXT: Record<string, string> = {
  app_version_invalid: BAD_VERSION,
  maintenance_time_invalid: BAD_TIME,
  maintenance_localized_invalid: '언어별 안내 문구가 올바르지 않습니다.',
  app_gate_conflict:'다른 운영자가 방금 이 설정을 바꿨습니다. 새로 읽은 값을 확인하고 다시 저장해 주세요.',
}
const reasonText = (e: unknown, fallback: string) => {
  const msg = e instanceof Error ? e.message : ''
  const key = Object.keys(REASON_TEXT).find((k) => msg.includes(k))
  return key ? REASON_TEXT[key]! : msg || fallback
}

/** 저장 요청 — 서버는 값 전부를 한 번에 받는다. 한 카드를 저장할 때 다른 카드는 서버에 있는 값 그대로 보낸다. */
const toParams = (g: Gate) => ({
  androidMinVersion: g.android_min_version ?? '',
  iosMinVersion: g.ios_min_version ?? '',
  androidStoreUrl: g.android_store_url ?? '',
  iosStoreUrl: g.ios_store_url ?? '',
  maintenanceEnabled: g.maintenance_enabled,
  maintenanceStartsAt: g.maintenance_starts_at ?? '',
  maintenanceEndsAt: g.maintenance_ends_at ?? '',
  maintenanceMessage: g.maintenance_message,
  maintenanceLocalized: g.maintenance_localized ?? {},
})

/** 설정 › 접속 탭의 버전·점검 카드 — 작은 탭마다 하나씩, 테스트 플레이어로 가는 링크를 함께 둔다. */
export default function AppGateCards({ target, report, part, onOpenOperators }: {
  target: ProjectTarget; report: Report; part: 'version' | 'maintenance'; onOpenOperators: () => void
}) {
  const [gate, setGate] = useState<Gate | null>(null)
  const [loading, setLoading] = useState(true)
  const [operators, setOperators] = useState<OperatorAccount[] | null>(null)

  useEffect(() => {
    let alive = true
    setOperators(null)
    callAdmin<OperatorAccount[]>(target, 'operatorAccounts.list')
      .then((list) => { if (alive) setOperators(list) })
      .catch((e: unknown) => report(e, '테스트 플레이어를 불러오지 못했습니다.'))
    return () => { alive = false }
  }, [target, report])
  const countOf = (f: 'maintenance_bypass' | 'version_bypass') => (operators ? operators.filter((o) => o[f]).length : null)
  const allowCount = countOf('maintenance_bypass')

  const load = useCallback(async () => {
    setLoading(true)
    try {
      setGate(await callAdmin<Gate>(target, 'appGate.get'))
    } catch (e: unknown) {
      setGate(null)
      report(e, '점검·버전 설정을 불러오지 못했습니다.')
    } finally {
      setLoading(false)
    }
  }, [target, report])

  useEffect(() => { void load() }, [load])

  // 서버는 값 전부를 덮어쓴다. 화면이 읽어 둔 시각을 함께 보내, 그 사이 다른 운영자가 저장했으면
  // 서버가 거절한다(app_gate_conflict) — 그때는 새로 읽어 보이고 멈춘다. 다른 카드의 값은 읽어 둔 값 그대로 보낸다.
  const gateRef = useRef(gate)
  gateRef.current = gate
  const save = async (apply: (current: Gate) => Gate) => {
    const current = gateRef.current
    if (!current) return
    try {
      setGate(await callAdmin<Gate>(target, 'appGate.update', { ...toParams(apply(current)), expectedUpdatedAt: current.updated_at ?? '' }))
    } catch (e: unknown) {
      if (e instanceof Error && e.message.includes('app_gate_conflict')) {
        setGate(await callAdmin<Gate>(target, 'appGate.get').catch(() => current))
      }
      throw e
    }
  }

  return (
    <div className="space-y-4">
      {part === 'version' && <VersionCard gate={gate} loading={loading} save={save} report={report}
        bypass={<BypassLink count={countOf('version_bypass')} onOpen={onOpenOperators} />} />}
      {part === 'maintenance' && <MaintenanceCard target={target} gate={gate} loading={loading} save={save} report={report} allowCount={allowCount}
        bypass={<BypassLink count={allowCount} onOpen={onOpenOperators} />} />}
    </div>
  )
}

type SaveFn = (apply: (fresh: Gate) => Gate) => Promise<void>

function useSaveFlow(save: SaveFn, report: Report) {
  const [confirming, setConfirming] = useState(false)
  const [saving, setSaving] = useState(false)
  const [saved, setSaved] = useState(false)
  const [error, setError] = useState('')
  const run = async (apply: (fresh: Gate) => Gate) => {
    setSaving(true)
    setError('')
    try {
      await save(apply)
      setSaved(true)
      setConfirming(false)
    } catch (e: unknown) {
      if (e instanceof NotAuthenticatedError) report(e, '')
      else setError(reasonText(e, '설정을 저장하지 못했습니다.'))
    } finally {
      setSaving(false)
    }
  }
  return {
    confirming, saving, saved, error, run,
    open: () => { setError(''); setConfirming(true) },
    close: () => setConfirming(false),
    edited: () => setSaved(false),
  }
}

function LastEdit({ gate }: { gate: Gate | null }) {
  return <LastEdited at={gate?.updated_at} by={gate?.updated_by} />
}

function Before({ label, before, after }: { label: string; before: string; after: string }) {
  const same = before === after
  return (
    <li className="flex items-center gap-2 flex-wrap">
      <span className="text-neutral-500">{label}</span>
      <span className={same ? 'text-neutral-400' : 'text-neutral-400 line-through'}>{before || '없음'}</span>
      {!same && <span className="text-neutral-900 font-medium">→ {after || '없음'}</span>}
    </li>
  )
}

/** 접어 둔 「적용 대상」 안내 — 어느 빌드에 효과가 있는지. 같은 줄 오른쪽에 테스트 플레이어 링크(bypass)를 둔다. */
function AppliesTo({ children, bypass }: { children: ReactNode; bypass: ReactNode }) {
  const [open, setOpen] = useState(false)
  return (
    <div>
      <div className="flex flex-wrap items-center gap-x-4 gap-y-1">
        <button type="button" onClick={() => setOpen((v) => !v)}
          className="inline-flex items-center gap-1 text-xs text-[#1677ff] hover:underline">
          <CircleHelp className="w-3.5 h-3.5" />
          적용 대상
          {open ? <ChevronUp className="w-3 h-3" /> : <ChevronDown className="w-3 h-3" />}
        </button>
        {bypass}
      </div>
      {open && (
        <div className="mt-2 rounded-md border border-neutral-200 bg-neutral-50 px-3 py-2.5 text-xs text-neutral-700 leading-relaxed space-y-1">
          {children}
        </div>
      )}
    </div>
  )
}

const VERSION_KEYS = ['android_min_version', 'ios_min_version', 'android_store_url', 'ios_store_url'] as const
const MAINT_KEYS = ['maintenance_enabled', 'maintenance_starts_at', 'maintenance_ends_at', 'maintenance_message', 'maintenance_localized'] as const
/** 카드가 맡은 값만 묶은 표시 — 다른 카드를 저장해도 이 값이 같으면 입력 중인 칸을 되돌리지 않는다. */
const sectionKey = (gate: Gate | null, keys: readonly (keyof Gate)[]) => (gate ? JSON.stringify(keys.map((k) => gate[k])) : '')

function VersionCard({ gate, loading, save, report, bypass }: {
  gate: Gate | null; loading: boolean; save: SaveFn; report: Report; bypass: ReactNode
}) {
  const [v, setV] = useState({ android: '', ios: '', androidUrl: '', iosUrl: '' })
  const flow = useSaveFlow(save, report)
  const key = sectionKey(gate, VERSION_KEYS)

  useEffect(() => {
    if (!gate) return
    setV({
      android: gate.android_min_version ?? '', ios: gate.ios_min_version ?? '',
      androidUrl: gate.android_store_url ?? '', iosUrl: gate.ios_store_url ?? '',
    })
    // eslint-disable-next-line react-hooks/exhaustive-deps -- 이 카드의 값이 서버에서 바뀌었을 때만 되돌린다
  }, [key])

  const t = { android: v.android.trim(), ios: v.ios.trim(), androidUrl: v.androidUrl.trim(), iosUrl: v.iosUrl.trim() }
  const badVersion = (s: string) => s !== '' && !VERSION.test(s)
  const badUrl = (s: string) => s !== '' && !URL_FORM.test(s)
  const invalid = badVersion(t.android) || badVersion(t.ios) || badUrl(t.androidUrl) || badUrl(t.iosUrl)
  const changed = !!gate && (t.android !== (gate.android_min_version ?? '') || t.ios !== (gate.ios_min_version ?? '')
    || t.androidUrl !== (gate.android_store_url ?? '') || t.iosUrl !== (gate.ios_store_url ?? ''))
  const disabled = loading || !gate
  const set = (k: keyof typeof v) => (s: string) => { setV((p) => ({ ...p, [k]: s })); flow.edited() }

  return (
    <FormCard>
      <div className="space-y-2">
        <CardHeader title="최소 버전" description="이보다 낮은 버전의 게임은 업데이트 안내를 띄우고 들어가지 않습니다." />
        <AppliesTo bypass={bypass}>
          <p>SDK 0.5.0 이상으로 만들고 게임이 앱 상태 확인을 부르는 빌드에만 안내가 뜹니다.</p>
          <p>그보다 옛 빌드는 버전이 낮아도 그대로 들어옵니다 — 서버는 버전으로 요청을 막지 않습니다.</p>
        </AppliesTo>
      </div>
      <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
        <TextField label="Android" value={v.android} onChange={set('android')} disabled={disabled}
          bad={badVersion(t.android)} hint="비우면 제한이 없습니다." badHint={BAD_VERSION} />
        <TextField label="iOS" value={v.ios} onChange={set('ios')} disabled={disabled}
          bad={badVersion(t.ios)} hint="비우면 제한이 없습니다." badHint={BAD_VERSION} />
      </div>
      <TextField label="Android 스토어 주소" value={v.androidUrl} onChange={set('androidUrl')} disabled={disabled}
        bad={badUrl(t.androidUrl)} hint="비우면 게임의 Google Play 페이지가 열립니다." badHint={BAD_URL} />
      <TextField label="iOS 스토어 주소" value={v.iosUrl} onChange={set('iosUrl')} disabled={disabled}
        bad={badUrl(t.iosUrl)} hint="App Store 의 게임 페이지 주소를 넣습니다. 비우면 업데이트 안내에서 스토어가 열리지 않습니다." badHint={BAD_URL} />

      <div className="flex items-center gap-3 pt-1 flex-wrap">
        <Button variant="primary" onClick={flow.open} disabled={disabled || flow.saving || invalid || !changed}>
          <Save className="w-3.5 h-3.5" />저장
        </Button>
        {flow.saved && <span className="text-sm text-green-600 whitespace-nowrap">저장했습니다.</span>}
        <LastEdit gate={gate} />
      </div>

      <ConfirmDialog
        open={flow.confirming}
        title="버전을 바꿀까요?"
        description="새로 들어오는 플레이어부터 곧바로 적용됩니다."
        confirmLabel="저장"
        busy={flow.saving}
        error={flow.error}
        onConfirm={() => void flow.run((fresh) => ({
          ...fresh,
          android_min_version: t.android || null, ios_min_version: t.ios || null,
          android_store_url: t.androidUrl || null, ios_store_url: t.iosUrl || null,
        }))}
        onCancel={flow.close}
      >
        {gate && (
          <ul className="space-y-1 text-sm text-neutral-700">
            <Before label="Android" before={gate.android_min_version ?? ''} after={t.android} />
            <Before label="iOS" before={gate.ios_min_version ?? ''} after={t.ios} />
            <Before label="Android 스토어 주소" before={gate.android_store_url ?? ''} after={t.androidUrl} />
            <Before label="iOS 스토어 주소" before={gate.ios_store_url ?? ''} after={t.iosUrl} />
          </ul>
        )}
      </ConfirmDialog>
    </FormCard>
  )
}

function TextField({ label, value, onChange, disabled, bad, hint, badHint, type = 'text' }: {
  label: string; value: string; onChange: (v: string) => void; disabled: boolean
  bad: boolean; hint: string; badHint: string; type?: string
}) {
  return (
    <div className="space-y-1">
      <label className="block text-sm font-medium text-neutral-800">{label}</label>
      <input type={type} value={value} disabled={disabled} onChange={(e) => onChange(e.target.value)}
        className={`${inputCls} w-full ${bad ? 'border-red-400 focus:border-red-400 focus:ring-red-200' : ''}`} />
      <p className={`text-xs ${bad ? 'text-red-600' : 'text-neutral-500'}`}>{bad ? badHint : hint}</p>
    </div>
  )
}

type MsgRow = { lang: string; message: string }
const MSG_KEYS = ['message'] as const

function MaintenanceCard({ target, gate, loading, save, report, allowCount, bypass }: {
  target: ProjectTarget; gate: Gate | null; loading: boolean; save: SaveFn; report: Report
  allowCount: number | null; bypass: ReactNode
}) {
  // 시각은 게임 시간대로 읽고 쓴다 — 시간대를 받기 전에는 시각 칸과 저장을 막는다.
  const tz = useGameTimeZone(target)
  const defaultLang = useDefaultLanguage(target)
  const flow = useSaveFlow(save, report)
  const [enabled, setEnabled] = useState(false)
  const [startsAt, setStartsAt] = useState('')
  const [endsAt, setEndsAt] = useState('')
  const [base, setBase] = useState({ message: '' })
  const [rows, setRows] = useState<MsgRow[]>([])
  const key = sectionKey(gate, MAINT_KEYS)

  useEffect(() => {
    if (!gate || !tz) return
    setEnabled(gate.maintenance_enabled)
    setStartsAt(isoToLocalInput(gate.maintenance_starts_at, tz))
    setEndsAt(isoToLocalInput(gate.maintenance_ends_at, tz))
    setBase({ message: gate.maintenance_message })
    setRows(toRows(Object.fromEntries(Object.entries(gate.maintenance_localized ?? {}).map(([l, m]) => [l, { message: m }])), MSG_KEYS))
    // eslint-disable-next-line react-hooks/exhaustive-deps -- 이 카드의 값이 서버에서 바뀌었을 때만 되돌린다
  }, [key, tz])

  const startIso = startsAt && tz ? localInputToIso(startsAt, tz) : null
  const endIso = endsAt && tz ? localInputToIso(endsAt, tz) : null
  const badTime = !!startIso && !!endIso && Date.parse(endIso) <= Date.parse(startIso)
  const localized = Object.fromEntries(Object.entries(fromRows(rows, MSG_KEYS)).map(([l, v]) => [l, v.message ?? '']))
  const mine = {
    maintenance_enabled: enabled,
    maintenance_starts_at: startIso,
    maintenance_ends_at: endIso,
    maintenance_message: base.message.trim(),
    maintenance_localized: localized,
  }
  const sameTime = (a: string | null, b: string | null) => (a ? Date.parse(a) : null) === (b ? Date.parse(b) : null)
  const changed = !!gate && (mine.maintenance_enabled !== gate.maintenance_enabled
    || !sameTime(mine.maintenance_starts_at, gate.maintenance_starts_at)
    || !sameTime(mine.maintenance_ends_at, gate.maintenance_ends_at)
    || mine.maintenance_message !== gate.maintenance_message
    || JSON.stringify(localized) !== JSON.stringify(gate.maintenance_localized ?? {}))
  const disabled = loading || !gate || !tz
  const edited = <T,>(set: (v: T) => void) => (v: T) => { set(v); flow.edited() }

  // 저장하면 어떻게 되는지 — 확인창 본문. 시각은 게임 시간대로 보인다.
  const at = (iso: string) => `${isoToLocalInput(iso, tz ?? 'UTC').replace('T', ' ')} ${tz ? timeZoneName(tz) : ''}`.trim()
  const startsLater = !!startIso && Date.parse(startIso) > Date.now()
  const active = !!gate?.maintenance_active
  const blocked = `점검 통과 테스트 플레이어${allowCount === null ? '' : ` ${formatCount(allowCount)}명`} 말고는 들어오지 못하고, 게임 중인 플레이어도 다음 요청부터 막힙니다.`
  const outcome = !enabled
    ? (active ? '점검이 끝나 모든 플레이어가 곧바로 들어올 수 있습니다.' : '점검을 끈 채로 저장합니다.')
    : active
      ? (startsLater
        ? `지금 진행 중인 점검이 풀리고, ${at(startIso!)}에 다시 시작합니다.`
        : '진행 중인 점검의 설정을 바꿉니다.')
      : startsLater
        ? `${at(startIso!)}에 점검이 시작됩니다. 그때부터 ${blocked}`
        : `저장하면 곧바로 점검이 시작됩니다. ${blocked}`

  return (
    <FormCard>
      <div className="space-y-2">
        <CardHeader
          title="점검"
          description="점검 중에는 점검 통과 테스트 플레이어 말고는 게임에 들어오지 못하고 안내 문구를 봅니다."
          actions={gate && (gate.maintenance_active
            ? <StatusText tone="bad">지금 점검 중</StatusText>
            : gate.maintenance_enabled ? <StatusText tone="warn">시작 예정</StatusText> : <StatusText tone="muted">꺼짐</StatusText>)}
        />
        <AppliesTo bypass={bypass}>
          <p>서버는 모든 빌드의 로그인한 요청을 막습니다.</p>
          <p>안내 문구는 SDK 0.5.0 이상으로 만들고 게임이 앱 상태 확인을 부르는 빌드에만 뜹니다. 그보다 옛 빌드는 안내 없이 요청 실패만 겪습니다.</p>
        </AppliesTo>
      </div>

      <div className="flex items-center gap-3">
        <span className="text-sm font-medium text-neutral-800">점검</span>
        <Switch label="점검" value={enabled} disabled={disabled} onChange={edited(setEnabled)} />
      </div>

      <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
        <div className="space-y-1">
          <label className="block text-sm font-medium text-neutral-800">시작 시각</label>
          <input type="datetime-local" value={startsAt} disabled={disabled} onChange={(e) => edited(setStartsAt)(e.target.value)}
            className={`${inputCls} w-full`} />
          <p className="text-xs text-neutral-500">비우면 저장하는 즉시 시작합니다.{tz ? ` ${timeZoneName(tz)} 기준입니다.` : ''}</p>
        </div>
        <div className="space-y-1">
          <label className="block text-sm font-medium text-neutral-800">예상 종료 시각</label>
          <input type="datetime-local" value={endsAt} disabled={disabled} onChange={(e) => edited(setEndsAt)(e.target.value)}
            className={`${inputCls} w-full ${badTime ? 'border-red-400 focus:border-red-400 focus:ring-red-200' : ''}`} />
          <p className={`text-xs ${badTime ? 'text-red-600' : 'text-neutral-500'}`}>
            {badTime ? BAD_TIME : '안내 문구에 함께 보입니다. 이 시각이 지나도 점검은 끄기 전까지 이어집니다.'}
          </p>
        </div>
      </div>

      <LocalizedFields
        fields={[['message', '안내 문구', true]] as const}
        base={base}
        onBase={edited(setBase)}
        rows={rows}
        onRows={edited(setRows)}
        defaultLang={defaultLang}
        emptyHint="기기 언어가 추가한 언어인 플레이어에게는 그 안내가, 나머지에게는 위 안내가 보입니다."
      />

      <div className="flex items-center gap-3 pt-1 flex-wrap">
        <Button variant="primary" onClick={flow.open} disabled={disabled || flow.saving || badTime || !changed}>
          <Save className="w-3.5 h-3.5" />저장
        </Button>
        {flow.saved && <span className="text-sm text-green-600 whitespace-nowrap">저장했습니다.</span>}
        <LastEdit gate={gate} />
      </div>

      <ConfirmDialog
        open={flow.confirming}
        title={enabled && !gate?.maintenance_enabled ? '점검을 켤까요?' : '저장할까요?'}
        confirmLabel="저장"
        danger={enabled && !active && !startsLater}
        busy={flow.saving}
        error={flow.error}
        onConfirm={() => void flow.run((fresh) => ({ ...fresh, ...mine }))}
        onCancel={flow.close}
      >
        <p className="text-sm text-neutral-700">{outcome}</p>
      </ConfirmDialog>
    </FormCard>
  )
}

/** 점검·버전 통과는 테스트 플레이어에서 정한다 — 여기서는 몇 명인지와 그 작은 탭으로 가는 길만. */
function BypassLink({ count, onOpen }: { count: number | null; onOpen: () => void }) {
  return (
    <button type="button" onClick={onOpen} className="inline-flex items-center gap-1 text-xs text-[#1677ff] hover:underline whitespace-nowrap">
      <Users className="w-3.5 h-3.5" />
      테스트 플레이어 {count === null ? '…' : `${formatCount(count)}명`}
    </button>
  )
}
