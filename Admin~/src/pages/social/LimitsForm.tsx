import { useCallback, useEffect, useState } from 'react'
import { Save } from 'lucide-react'
import { callAdmin } from '../../lib/api'
import type { ProjectTarget } from '../../lib/projectTarget'
import { WhiteCard } from '../../components/ui/Card'
import { ConfirmDialog } from '../../components/ui/ConfirmDialog'
import { formatDateTime } from '../../components/ui/format'

export const inputCls =
  'h-9 px-3 rounded-md border border-neutral-200 text-sm outline-none focus:ring-2 focus:ring-[#1677ff]/30 focus:border-[#1677ff]'

/** 제한값 한 칸. min·max 는 서버(admin-api·RPC)가 막는 범위와 같게 둔다. */
export type LimitField = {
  /** 저장할 때 서버로 보낼 파라미터 이름 */
  param: string
  /** 조회 응답에서 현재 값을 읽을 키 */
  field: string
  label: string
  hint: string
  min: number
  max: number
}

/** 제한값 조회 응답. 값 칸은 제한마다 다르고 아래 둘은 공통이다. */
export type LimitsResponse = Record<string, number | string | null> & {
  updated_at?: string | null
  updated_by?: string | null
}

/**
 * 제한값 폼. 친구와 로비가 모양이 같아 하나로 쓴다 — 복제해 두면 한쪽만 고쳐져 갈라진다.
 *
 * 범위 검사는 화면·엣지 함수·DB 세 곳에 같은 값으로 둔다. 화면만 막으면 검증이 없는 것과
 * 같고, DB 만 막으면 운영자가 저장을 누른 뒤에야 영문 제약조건 오류를 본다.
 */
export function LimitsForm({
  target,
  report,
  title,
  getAction,
  updateAction,
  fields,
}: {
  target: ProjectTarget
  report: (e: unknown, fallback: string) => void
  title: string
  getAction: string
  updateAction: string
  fields: LimitField[]
}) {
  const [current, setCurrent] = useState<LimitsResponse | null>(null)
  const [values, setValues] = useState<Record<string, string>>({})
  const [loading, setLoading] = useState(false)
  const [saving, setSaving] = useState(false)
  const [saved, setSaved] = useState(false)
  const [confirming, setConfirming] = useState(false)

  const apply = useCallback((s: LimitsResponse) => {
    setCurrent(s)
    const next: Record<string, string> = {}
    for (const f of fields) next[f.param] = String(s[f.field] ?? '')
    setValues(next)
  }, [fields])

  const load = useCallback(async () => {
    setLoading(true)
    try {
      apply(await callAdmin<LimitsResponse>(target, getAction))
    } catch (e: unknown) {
      setCurrent(null)
      report(e, '설정을 불러오지 못했습니다.')
    } finally {
      setLoading(false)
    }
  }, [target, getAction, report, apply])

  useEffect(() => { void load() }, [load])

  // 빈 칸은 Number('') === 0 이라 그냥 두면 0 이 저장된다. 간격은 0 이 "제한 없음"이라
  // DB CHECK 도 안 걸려, 칸을 지운 실수가 도배 방지를 끄는 것으로 이어진다.
  const parsed: Record<string, number | null> = {}
  for (const f of fields) parsed[f.param] = parseField(values[f.param] ?? '', f)

  const invalid = fields.some((f) => parsed[f.param] === null)
  const changed = !!current && fields.some((f) => parsed[f.param] !== current[f.field])

  const edit = (param: string) => (v: string) => {
    setValues((prev) => ({ ...prev, [param]: v }))
    setSaved(false)
  }

  const save = async () => {
    if (invalid) return
    setSaving(true)
    setSaved(false)
    try {
      apply(await callAdmin<LimitsResponse>(target, updateAction, parsed))
      setSaved(true)
      setConfirming(false)
    } catch (e: unknown) {
      report(e, '설정을 저장하지 못했습니다.')
    } finally {
      setSaving(false)
    }
  }

  return (
    <WhiteCard className="p-5 space-y-4 max-w-xl">
      <h2 className="text-sm font-medium text-neutral-900">{title}</h2>

      {fields.map((f) => (
        <Field
          key={f.param}
          label={f.label}
          hint={f.hint}
          value={values[f.param] ?? ''}
          onChange={edit(f.param)}
          disabled={loading || !current}
          limits={f}
        />
      ))}

      <div className="flex items-center gap-3 pt-1">
        <button
          onClick={() => setConfirming(true)}
          disabled={saving || loading || !current || invalid || !changed}
          className="h-9 px-4 inline-flex items-center gap-1.5 rounded-md bg-[#1677ff] text-white text-sm hover:bg-[#1677ff]/90 disabled:opacity-40 whitespace-nowrap"
        >
          <Save className="w-3.5 h-3.5" />
          저장
        </button>
        {saved && <span className="text-sm text-green-600 whitespace-nowrap">저장했습니다.</span>}
        {typeof current?.updated_at === 'string' && (
          <span className="text-xs text-neutral-400">
            마지막 수정 {formatDateTime(current.updated_at)}
            {typeof current.updated_by === 'string' ? ` · ${current.updated_by}` : ''}
          </span>
        )}
      </div>

      <ConfirmDialog
        open={confirming}
        title="제한값을 바꿀까요?"
        description="모든 플레이어에게 곧바로 적용됩니다."
        confirmLabel="저장"
        busy={saving}
        onConfirm={() => void save()}
        onCancel={() => setConfirming(false)}
      >
        {current && (
          <ul className="space-y-1 text-sm text-neutral-700">
            {fields.map((f) => (
              <ChangeRow key={f.param} label={f.label} before={Number(current[f.field])} after={parsed[f.param] ?? null} />
            ))}
          </ul>
        )}
      </ConfirmDialog>
    </WhiteCard>
  )
}

/** 빈 칸·범위 밖이면 null. 저장 버튼은 null 이 하나라도 있으면 눌리지 않는다. */
function parseField(raw: string, limits: { min: number; max: number }): number | null {
  if (raw.trim() === '') return null
  const n = Number(raw)
  if (!Number.isInteger(n) || n < limits.min || n > limits.max) return null
  return n
}

function ChangeRow({ label, before, after }: { label: string; before: number; after: number | null }) {
  const same = before === after
  return (
    <li className="flex items-center gap-2 whitespace-nowrap">
      <span className="text-neutral-500">{label}</span>
      <span className={same ? 'text-neutral-400' : 'text-neutral-400 line-through'}>{before}</span>
      {!same && <span className="text-neutral-900 font-medium">→ {after}</span>}
    </li>
  )
}

function Field({
  label,
  hint,
  value,
  onChange,
  disabled,
  limits,
}: {
  label: string
  hint: string
  value: string
  onChange: (v: string) => void
  disabled: boolean
  limits: { min: number; max: number }
}) {
  const bad = !disabled && parseField(value, limits) === null
  return (
    <div className="space-y-1">
      <label className="block text-sm font-medium text-neutral-800">{label}</label>
      <input
        type="number"
        min={limits.min}
        max={limits.max}
        className={`${inputCls} w-40 ${bad ? 'border-red-400 focus:border-red-400 focus:ring-red-200' : ''}`}
        value={value}
        disabled={disabled}
        onChange={(e) => onChange(e.target.value)}
      />
      <p className={`text-xs ${bad ? 'text-red-600' : 'text-neutral-500'}`}>
        {bad ? `${limits.min}에서 ${limits.max} 사이의 정수를 넣어 주세요.` : hint}
      </p>
    </div>
  )
}
