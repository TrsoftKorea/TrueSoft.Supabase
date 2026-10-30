import { useCallback, useEffect, useState } from 'react'
import { Loader2, Pencil, Plus, X } from 'lucide-react'
import { callAdmin, NotAuthenticatedError } from '../lib/api'
import type { ProjectTarget } from '../lib/projectTarget'
import { Segmented } from '../components/ui/Segmented'
import { CardHeader, GroupBox, WhiteCard } from '../components/ui/Card'
import { Button } from '../components/ui/Button'
import { EditDialog } from '../components/ui/EditDialog'
import { InfoList } from '../components/ui/InfoList'
import { TableStatusRow } from '../components/ui/TableStatusRow'
import { areaCls, inputCls } from '../components/ui/Input'
import { formatCount, formatDateTime } from '../components/ui/format'

type Tab = 'triggers' | 'ad' | 'results'
type Text = { title: string; body: string }
type Trigger = Text & { code: string; off_count: number; localized: Record<string, Partial<Text>>; updated_at: string | null; updated_by: string | null }
type AdText = { title_prefix: string; footer: string }
type Settings = {
  triggers: Trigger[]
  ad: AdText & { localized: Record<string, Partial<AdText>>; updated_at: string | null; updated_by: string | null }
  devices: { android: number; ios: number; ad_consent: number }
}
type Stats = {
  by_trigger: { trigger: string; status: string; count: number }[]
  pending: number
  recent_problems: { id: number; trigger: string; status: string; result: string | null; created_at: string }[]
}

const TRIGGER_LABEL: Record<string, string> = {
  mail: '우편 도착',
  support_reply: '고객센터 답변',
  friend_request: '친구 요청',
  lobby_invite: '로비 초대',
}
// 문구에 넣을 수 있는 자리. 입력 창 아래 안내로 보인다.
const TRIGGER_VARS: Record<string, string> = {
  mail: '{title} 자리에는 우편 제목이 들어갑니다. 우편을 보낼 때 "푸시 알림"을 켠 우편만 보냅니다.',
  support_reply: '운영자가 문의에 답하면 그 플레이어에게 보냅니다.',
  friend_request: '{name} 자리에는 요청을 보낸 플레이어의 닉네임이 들어갑니다.',
  lobby_invite: '{name} 자리에는 초대한 플레이어의 닉네임, {lobby} 자리에는 방 이름이 들어갑니다.',
}
const STATUS_LABEL: Record<string, string> = { sent: '보냄', skipped: '건너뜀', failed: '실패', pending: '대기', sending: '보내는 중' }

const LANGS = ['en', 'ja', 'ko', 'zh-CN', 'zh-TW', 'de', 'fr', 'es', 'pt', 'ru', 'id', 'th', 'vi']
const LANG_LABEL: Record<string, string> = {
  en: '영어', ja: '일본어', ko: '한국어', 'zh-CN': '중국어(간체)', 'zh-TW': '중국어(번체)',
  de: '독일어', fr: '프랑스어', es: '스페인어', pt: '포르투갈어', ru: '러시아어',
  id: '인도네시아어', th: '태국어', vi: '베트남어',
}

/** 기본 문구 + 언어별 문구 입력. fields 는 [키, 이름, 여러 줄] — 우편 발송 화면의 다국어 입력과 같은 꼴. */
function LocalizedFields<K extends string>({
  fields, base, onBase, rows, onRows, hint,
}: {
  fields: readonly (readonly [K, string, boolean])[]
  base: Record<K, string>
  onBase: (v: Record<K, string>) => void
  rows: ({ lang: string } & Record<K, string>)[]
  onRows: (v: ({ lang: string } & Record<K, string>)[]) => void
  hint?: string | undefined
}) {
  const empty = Object.fromEntries(fields.map(([k]) => [k, ''])) as Record<K, string>
  const input = (value: string, onChange: (v: string) => void, multiline: boolean) => multiline
    ? <textarea value={value} onChange={(e) => onChange(e.target.value)} rows={2} className={`${areaCls} w-full`} />
    : <input value={value} onChange={(e) => onChange(e.target.value)} className={`${inputCls} w-full`} />
  return (
    <div className="space-y-3">
      {fields.map(([k, label, multiline]) => (
        <div key={k}>
          <label className="block text-xs font-medium text-neutral-500 mb-1">{label}</label>
          {input(base[k], (v) => onBase({ ...base, [k]: v }), multiline)}
        </div>
      ))}
      {hint && <p className="text-xs text-neutral-500">{hint}</p>}
      <div className="flex items-center justify-between pt-1">
        <span className="text-xs font-medium text-neutral-500">다른 언어</span>
        <button type="button" disabled={rows.length >= LANGS.length}
          onClick={() => onRows([...rows, { lang: LANGS.find((l) => !rows.some((r) => r.lang === l)) ?? '', ...empty }])}
          className="inline-flex items-center gap-1 text-xs text-[#1677ff] disabled:opacity-40 disabled:cursor-not-allowed">
          <Plus className="w-3 h-3" />언어 추가
        </button>
      </div>
      {rows.length === 0 && <p className="text-xs text-neutral-400">기기 언어가 추가한 언어인 플레이어에게는 그 문구가, 나머지에게는 위 문구가 갑니다.</p>}
      {rows.map((r, i) => (
        <div key={i} className="border border-neutral-200 rounded-md p-2.5 space-y-2 bg-neutral-50">
          <div className="flex items-center gap-2">
            <select value={r.lang} onChange={(e) => onRows(rows.map((x, j) => (j === i ? { ...x, lang: e.target.value } : x)))}
              className={`${inputCls} w-36`}>
              {LANGS.filter((l) => l === r.lang || !rows.some((x, j) => j !== i && x.lang === l))
                .map((l) => <option key={l} value={l}>{LANG_LABEL[l] ?? l}</option>)}
            </select>
            <span className="flex-1" />
            <button type="button" onClick={() => onRows(rows.filter((_, j) => j !== i))} title="이 언어 삭제" aria-label="이 언어 삭제"
              className="text-neutral-400 hover:text-red-500"><X className="w-4 h-4" /></button>
          </div>
          {fields.map(([k, label, multiline]) => (
            <div key={k}>
              <label className="block text-xs font-medium text-neutral-500 mb-1">{label}</label>
              {input(r[k], (v) => onRows(rows.map((x, j) => (j === i ? { ...x, [k]: v } : x))), multiline)}
            </div>
          ))}
        </div>
      ))}
    </div>
  )
}

function toRows<K extends string>(localized: Record<string, Partial<Record<K, string>>> | null | undefined, keys: readonly K[]) {
  return Object.entries(localized ?? {}).map(([lang, v]) => ({
    lang, ...(Object.fromEntries(keys.map((k) => [k, v?.[k] ?? ''])) as Record<K, string>),
  }))
}

function fromRows<K extends string>(rows: ({ lang: string } & Record<K, string>)[], keys: readonly K[]) {
  const o: Record<string, Partial<Record<K, string>>> = {}
  for (const r of rows) {
    if (!r.lang.trim()) continue
    const v: Partial<Record<K, string>> = {}
    for (const k of keys) if (r[k].trim()) v[k] = r[k].trim()
    if (Object.keys(v).length > 0) o[r.lang.trim()] = v
  }
  return o
}

const TEXT_KEYS = ['title', 'body'] as const
const AD_KEYS = ['title_prefix', 'footer'] as const

const TABS: { v: Tab; label: string }[] = [
  { v: 'triggers', label: '상황별 문구' },
  { v: 'ad', label: '광고성 표시' },
  { v: 'results', label: '보낸 결과' },
]

/** 설정 › 푸시 알림 탭. 오류 띠는 설정 페이지의 것을 쓴다(report). */
export default function PushSettings({ target, report, onUnauthenticated }: {
  target: ProjectTarget
  report: (e: unknown, fallback: string) => void
  onUnauthenticated: () => void
}) {
  const [tab, setTab] = useState<Tab>('triggers')
  const [settings, setSettings] = useState<Settings | null>(null)
  const [loading, setLoading] = useState(true)
  const [failed, setFailed] = useState(false)

  const load = useCallback(async () => {
    setLoading(true)
    setFailed(false)
    try {
      setSettings(await callAdmin<Settings>(target, 'push.get'))
    } catch (e: unknown) {
      setFailed(true)
      report(e, '푸시 설정을 불러오지 못했습니다.')
    } finally {
      setLoading(false)
    }
  }, [target, report])

  useEffect(() => { void load() }, [load])

  // 문구 편집 창
  const [editing, setEditing] = useState<Trigger | null>(null)
  const [base, setBase] = useState<Record<'title' | 'body', string>>({ title: '', body: '' })
  const [rows, setRows] = useState<({ lang: string } & Record<'title' | 'body', string>)[]>([])
  const [saving, setSaving] = useState(false)
  const [saveError, setSaveError] = useState('')
  const openEdit = (t: Trigger) => {
    setEditing(t)
    setBase({ title: t.title, body: t.body })
    setRows(toRows(t.localized, TEXT_KEYS))
    setSaveError('')
  }
  const textOk = base.title.trim() !== '' && base.body.trim() !== '' && rows.every((r) => r.lang.trim() !== '')
  const saveEdit = async () => {
    if (!editing || !textOk || saving) return
    setSaving(true)
    setSaveError('')
    try {
      await callAdmin(target, 'push.updateTrigger', {
        code: editing.code, title: base.title.trim(), body: base.body.trim(), localized: fromRows(rows, TEXT_KEYS),
      })
      setEditing(null)
      await load()
    } catch (e: unknown) {
      if (e instanceof NotAuthenticatedError) { onUnauthenticated(); return }
      setSaveError(e instanceof Error ? e.message : '저장하지 못했습니다.')
    } finally {
      setSaving(false)
    }
  }

  return (
    <WhiteCard className="p-5 space-y-5">
      <CardHeader title="푸시 알림" description="상황마다 플레이어 기기로 가는 알림의 문구를 정하고 보낸 결과를 봅니다." />
      <Segmented value={tab} onChange={setTab} options={TABS} />

      {tab === 'triggers' && (
        <div className="space-y-4">
          <InfoList items={[
            { label: 'Android 기기', value: settings ? `${formatCount(settings.devices.android)}대` : loading ? '…' : null },
            { label: 'iOS 기기', value: settings ? `${formatCount(settings.devices.ios)}대` : loading ? '…' : null },
            { label: '광고 수신 동의', value: settings ? `${formatCount(settings.devices.ad_consent)}명` : loading ? '…' : null },
          ]} />
          <div className="overflow-auto border border-neutral-200 rounded-md">
            <table className="w-full text-sm">
              <thead className="bg-neutral-50 text-neutral-500 text-xs">
                <tr>
                  <th className="text-left px-4 py-2.5 font-medium w-36">상황</th>
                  <th className="text-left px-4 py-2.5 font-medium min-w-[14rem]">문구</th>
                  <th className="text-right px-4 py-2.5 font-medium w-28 whitespace-nowrap">끈 플레이어</th>
                  <th className="px-4 py-2.5 w-24" />
                </tr>
              </thead>
              <tbody>
                {loading || failed || !settings ? (
                  <TableStatusRow loading={loading} failed={failed} empty={!settings} colSpan={4} />
                ) : settings.triggers.map((t) => (
                  <tr key={t.code} className="border-t border-neutral-100 align-top">
                    <td className="px-4 py-3 text-neutral-800 whitespace-nowrap">{TRIGGER_LABEL[t.code] ?? t.code}</td>
                    <td className="px-4 py-3">
                      <div className="font-medium text-neutral-900">{t.title}</div>
                      <div className="text-neutral-600 break-words">{t.body}</div>
                      {Object.keys(t.localized ?? {}).length > 0 && (
                        <div className="mt-1 text-xs text-neutral-400">
                          {Object.keys(t.localized).map((l) => LANG_LABEL[l] ?? l).join(' · ')}
                        </div>
                      )}
                    </td>
                    <td className="px-4 py-3 text-right tabular-nums text-neutral-700 whitespace-nowrap">{formatCount(t.off_count)}명</td>
                    <td className="px-4 py-3 text-right whitespace-nowrap">
                      <Button variant="outline" size="compact" onClick={() => openEdit(t)}><Pencil className="w-3.5 h-3.5" />수정</Button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <p className="text-xs text-neutral-500">알림을 받을지는 플레이어가 게임 설정에서 상황마다 정합니다.</p>
        </div>
      )}

      {tab === 'ad' && <AdForm target={target} settings={settings} onSaved={load} report={report} onUnauthenticated={onUnauthenticated} />}
      {tab === 'results' && <Results target={target} report={report} />}

      {editing && (
        <EditDialog
          title="알림 문구"
          description={TRIGGER_LABEL[editing.code] ?? editing.code}
          size="lg"
          busy={saving}
          error={saveError || undefined}
          onClose={() => setEditing(null)}
          footer={(
            <>
              <Button variant="outline" onClick={() => setEditing(null)} disabled={saving}>취소</Button>
              <Button variant="primary" onClick={() => void saveEdit()} disabled={!textOk || saving}>
                {saving && <Loader2 className="w-3.5 h-3.5 animate-spin" />}저장
              </Button>
            </>
          )}
        >
          <LocalizedFields fields={[['title', '제목', false], ['body', '본문', true]]}
            base={base} onBase={setBase} rows={rows} onRows={setRows} hint={TRIGGER_VARS[editing.code]} />
        </EditDialog>
      )}
    </WhiteCard>
  )
}

function AdForm({ target, settings, onSaved, report, onUnauthenticated }: {
  target: ProjectTarget
  settings: Settings | null
  onSaved: () => Promise<void>
  report: (e: unknown, fallback: string) => void
  onUnauthenticated: () => void
}) {
  const [base, setBase] = useState<Record<'title_prefix' | 'footer', string>>({ title_prefix: '', footer: '' })
  const [rows, setRows] = useState<({ lang: string } & Record<'title_prefix' | 'footer', string>)[]>([])
  const [saving, setSaving] = useState(false)
  const [saved, setSaved] = useState(false)

  useEffect(() => {
    if (!settings) return
    setBase({ title_prefix: settings.ad.title_prefix, footer: settings.ad.footer })
    setRows(toRows(settings.ad.localized, AD_KEYS))
  }, [settings])

  const ok = base.title_prefix.trim() !== '' && base.footer.trim() !== '' && rows.every((r) => r.lang.trim() !== '')
  const save = async () => {
    if (!ok || saving) return
    setSaving(true)
    setSaved(false)
    try {
      await callAdmin(target, 'push.updateAd', {
        titlePrefix: base.title_prefix.trim(), footer: base.footer.trim(), localized: fromRows(rows, AD_KEYS),
      })
      await onSaved()
      setSaved(true)
    } catch (e: unknown) {
      if (e instanceof NotAuthenticatedError) { onUnauthenticated(); return }
      report(e, '저장하지 못했습니다.')
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="space-y-5 max-w-2xl">
      <p className="text-xs text-neutral-500">광고성으로 보낸 우편 알림에 붙는 제목 앞 표시와 수신 거부 안내입니다.</p>
      {!settings ? (
        <div className="py-6 flex justify-center text-neutral-400"><Loader2 className="w-5 h-5 animate-spin" /></div>
      ) : (
        <>
          <LocalizedFields fields={[['title_prefix', '제목 앞 표시', false], ['footer', '수신 거부 안내', false]]}
            base={base} onBase={(v) => { setBase(v); setSaved(false) }} rows={rows} onRows={(v) => { setRows(v); setSaved(false) }}
            hint="제목 앞 표시는 알림 제목 맨 앞에, 수신 거부 안내는 본문 맨 끝에 붙습니다." />
          <div className="flex items-center gap-3">
            {saved && <span className="text-xs text-emerald-600">저장됨</span>}
            <Button variant="primary" className="ml-auto" onClick={() => void save()} disabled={!ok || saving}>
              {saving && <Loader2 className="w-3.5 h-3.5 animate-spin" />}저장
            </Button>
          </div>
        </>
      )}
    </div>
  )
}

function Results({ target, report }: { target: ProjectTarget; report: (e: unknown, fallback: string) => void }) {
  const [stats, setStats] = useState<Stats | null>(null)
  const [loading, setLoading] = useState(true)
  const [failed, setFailed] = useState(false)

  useEffect(() => {
    void (async () => {
      setLoading(true)
      setFailed(false)
      try {
        setStats(await callAdmin<Stats>(target, 'push.stats', { days: 7 }))
      } catch (e: unknown) {
        setFailed(true)
        report(e, '보낸 결과를 불러오지 못했습니다.')
      } finally {
        setLoading(false)
      }
    })()
  }, [target, report])

  const count = (trigger: string, status: string) =>
    stats?.by_trigger.filter((r) => r.trigger === trigger && (status === 'pending' ? r.status === 'pending' || r.status === 'sending' : r.status === status))
      .reduce((s, r) => s + r.count, 0) ?? 0
  const triggers = Object.keys(TRIGGER_LABEL)

  return (
    <div className="space-y-4">
      <GroupBox title="최근 7일">
        <div className="overflow-auto">
          <table className="w-full text-sm">
            <thead className="bg-neutral-50 text-neutral-500 text-xs">
              <tr>
                <th className="text-left px-4 py-2.5 font-medium">상황</th>
                {['sent', 'skipped', 'failed', 'pending'].map((s) => (
                  <th key={s} className="text-right px-4 py-2.5 font-medium w-24">{STATUS_LABEL[s]}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              {loading || failed || !stats ? (
                <TableStatusRow loading={loading} failed={failed} empty={!stats} colSpan={5} />
              ) : triggers.map((t) => (
                <tr key={t} className="border-t border-neutral-100">
                  <td className="px-4 py-2.5 text-neutral-800 whitespace-nowrap">{TRIGGER_LABEL[t]}</td>
                  {['sent', 'skipped', 'failed', 'pending'].map((s) => (
                    <td key={s} className={`px-4 py-2.5 text-right tabular-nums ${s === 'failed' && count(t, s) > 0 ? 'text-red-600' : 'text-neutral-700'}`}>
                      {formatCount(count(t, s))}
                    </td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </GroupBox>

      <GroupBox title="최근 실패·건너뜀">
        <div className="overflow-auto">
          <table className="w-full text-sm">
            <thead className="bg-neutral-50 text-neutral-500 text-xs">
              <tr>
                <th className="text-left px-4 py-2.5 font-medium w-44">시각</th>
                <th className="text-left px-4 py-2.5 font-medium w-32">상황</th>
                <th className="text-left px-4 py-2.5 font-medium w-24">결과</th>
                <th className="text-left px-4 py-2.5 font-medium min-w-[12rem]">사유</th>
              </tr>
            </thead>
            <tbody>
              {loading || failed || !stats || stats.recent_problems.length === 0 ? (
                <TableStatusRow loading={loading} failed={failed} empty={!stats || stats.recent_problems.length === 0} colSpan={4}
                  emptyText="최근 7일 동안 실패·건너뜀이 없습니다." />
              ) : stats.recent_problems.map((p) => (
                <tr key={p.id} className="border-t border-neutral-100">
                  <td className="px-4 py-2.5 text-neutral-600 whitespace-nowrap">{formatDateTime(p.created_at)}</td>
                  <td className="px-4 py-2.5 text-neutral-800 whitespace-nowrap">{TRIGGER_LABEL[p.trigger] ?? p.trigger}</td>
                  <td className={`px-4 py-2.5 whitespace-nowrap ${p.status === 'failed' ? 'text-red-600' : 'text-neutral-600'}`}>{STATUS_LABEL[p.status] ?? p.status}</td>
                  <td className="px-4 py-2.5 text-neutral-700 break-words">{p.result ?? ''}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </GroupBox>
    </div>
  )
}
