import { useCallback, useEffect, useRef, useState } from 'react'
import { Pencil, Plus, Save, Trash2 } from 'lucide-react'
import { callAdmin, NotAuthenticatedError } from '../lib/api'
import type { ProjectTarget } from '../lib/projectTarget'
import { TableCard } from '../components/ui/DataTable'
import { Button } from '../components/ui/Button'
import { ConfirmDialog } from '../components/ui/ConfirmDialog'
import { EditDialog } from '../components/ui/EditDialog'
import { Switch } from '../components/ui/Switch'
import { PlayerLink } from '../components/ui/PlayerLink'
import { dangerInputCls, inputCls } from '../components/ui/Input'
import { formatDateTime } from '../components/ui/format'
import { Tags } from '../components/ui/StatusText'

export type OperatorAccount = {
  user_id: string
  note: string
  maintenance_bypass: boolean
  version_bypass: boolean
  hide_from_stats: boolean
  config_preview: boolean
  test_recipient: boolean
  account_id: string | null
  display_name: string | null
  created_at: string
  created_by: string | null
  updated_at: string
}
type Flag = 'maintenance_bypass' | 'version_bypass' | 'hide_from_stats' | 'config_preview' | 'test_recipient'
type Report = (e: unknown, fallback: string) => void

/** 권한 — 이름과, 켜면 그 계정에 무엇이 달라지는지. */
export const OPERATOR_FLAGS: readonly (readonly [Flag, string, string])[] = [
  ['maintenance_bypass', '점검 통과', '점검 중에도 게임에 들어옵니다.'],
  ['version_bypass', '버전 통과', '최소 버전보다 낮은 앱으로도 들어옵니다.'],
  ['hide_from_stats', '순위·통계 제외', '리더보드·대시보드·통계·동시접속자에서 빠집니다. 매출은 그대로 셉니다.'],
  ['config_preview', '원격 설정 미리 받기', '원격 설정의 운영 계정 값을 받습니다.'],
  ['test_recipient', '시험 우편 받기', '우편 발송의 시험 발송 대상이 됩니다.'],
]

const REASON_TEXT: Record<string, string> = {
  operator_user_not_found: '그 유저 ID 의 플레이어가 없습니다.',
}
const reasonText = (e: unknown, fallback: string) => {
  const msg = e instanceof Error ? e.message : ''
  const key = Object.keys(REASON_TEXT).find((k) => msg.includes(k))
  return key ? REASON_TEXT[key]! : msg || fallback
}

type Draft = { editing: boolean; user_id: string; note: string } & Record<Flag, boolean>
const EMPTY: Draft = {
  editing: false, user_id: '', note: '',
  maintenance_bypass: false, version_bypass: false, hide_from_stats: false, config_preview: false, test_recipient: false,
}

/** 설정 › 접속 탭의 운영 계정 카드. */
export default function OperatorAccountsCard({ target, report }: { target: ProjectTarget; report: Report }) {
  const [rows, setRows] = useState<OperatorAccount[]>([])
  const [loading, setLoading] = useState(true)
  const [failed, setFailed] = useState(false)
  const [draft, setDraft] = useState<Draft | null>(null)
  const [busy, setBusy] = useState(false)
  const [dialogError, setDialogError] = useState('')
  const [removing, setRemoving] = useState<OperatorAccount | null>(null)
  const req = useRef(0)

  const load = useCallback(async () => {
    const id = ++req.current
    setLoading(true)
    setFailed(false)
    try {
      const list = await callAdmin<OperatorAccount[]>(target, 'operatorAccounts.list')
      if (id === req.current) setRows(list)
    } catch (e: unknown) {
      if (id !== req.current) return
      setRows([])
      setFailed(true)
      report(e, '운영 계정을 불러오지 못했습니다.')
    } finally {
      if (id === req.current) setLoading(false)
    }
  }, [target, report])

  useEffect(() => { void load() }, [load])

  const open = (r: OperatorAccount | null) => {
    setDialogError('')
    setDraft(r ? {
      editing: true, user_id: r.user_id, note: r.note,
      maintenance_bypass: r.maintenance_bypass, version_bypass: r.version_bypass, hide_from_stats: r.hide_from_stats,
      config_preview: r.config_preview, test_recipient: r.test_recipient,
    } : EMPTY)
  }

  // 서버 저장은 덮어쓰기라, 이미 있는 유저 ID 를 "추가"하면 권한·메모가 추가 창 값(전부 꺼짐)으로 조용히 바뀐다.
  // 그래서 추가는 목록을 읽은 뒤에만 열고, 같은 ID 면 막는다.
  const taken = !!draft && !draft.editing && rows.some((r) => r.user_id === draft.user_id.trim())

  const save = async () => {
    if (!draft || taken) return
    setBusy(true)
    setDialogError('')
    try {
      await callAdmin(target, 'operatorAccounts.set', {
        userId: draft.user_id.trim(), note: draft.note.trim(),
        maintenanceBypass: draft.maintenance_bypass, versionBypass: draft.version_bypass,
        hideFromStats: draft.hide_from_stats, configPreview: draft.config_preview, testRecipient: draft.test_recipient,
      })
      setDraft(null)
      void load()
    } catch (e: unknown) {
      if (e instanceof NotAuthenticatedError) report(e, '')
      else setDialogError(reasonText(e, '저장하지 못했습니다.'))
    } finally {
      setBusy(false)
    }
  }

  const remove = async () => {
    if (!removing) return
    setBusy(true)
    setDialogError('')
    try {
      await callAdmin(target, 'operatorAccounts.remove', { userId: removing.user_id })
      setRemoving(null)
      void load()
    } catch (e: unknown) {
      if (e instanceof NotAuthenticatedError) report(e, '')
      else setDialogError(reasonText(e, '지우지 못했습니다.'))
    } finally {
      setBusy(false)
    }
  }

  const flagLabels = (r: OperatorAccount) => OPERATOR_FLAGS.filter(([f]) => r[f]).map(([, label]) => label)

  return (
    <>
      <TableCard
        title="운영 계정"
        description="게임 안에서 운영자가 쓰는 계정과 그 계정에만 주는 권한입니다."
        actions={
          <Button variant="primary" onClick={() => open(null)} disabled={loading || failed}>
            <Plus className="w-3.5 h-3.5" />추가
          </Button>
        }
        columns={[
          { key: 'created_at', header: '추가 시각', kind: 'datetime', cell: (r) => formatDateTime(r.created_at) },
          // 계정이 사라졌으면 비운다 — 옆 유저 ID 칸과 같은 글자가 두 번 보인다.
          { key: 'player', header: '플레이어', kind: 'name', cell: (r) => (r.account_id ? <PlayerLink accountId={r.account_id} name={r.display_name} userId={r.user_id} /> : null) },
          { key: 'user_id', header: '유저 ID', kind: 'mono', cell: (r) => r.user_id },
          // 권한은 켠 것만 나열한다 — 칸 다섯 개로 펼치면 표가 옆으로 넘친다.
          { key: 'flags', header: '권한', kind: 'text', full: (r) => flagLabels(r).join('\n'), cell: (r) => { const l = flagLabels(r); return l.length ? <Tags items={l} /> : null } },
          { key: 'note', header: '메모', kind: 'name', cell: (r) => r.note || null },
          {
            key: 'actions', header: '', kind: 'actions', cell: (r) => (
              <span className="inline-flex gap-1.5">
                <Button variant="outline" size="compact" onClick={() => open(r)}>
                  <Pencil className="w-3.5 h-3.5" />수정
                </Button>
                <Button variant="dangerOutline" size="compact" onClick={() => { setDialogError(''); setRemoving(r) }}>
                  <Trash2 className="w-3.5 h-3.5" />삭제
                </Button>
              </span>
            ),
          },
        ]}
        rows={rows}
        rowKey={(r) => r.user_id}
        loading={loading}
        failed={failed}
        emptyText="등록한 운영 계정이 없습니다."
      />

      {draft && (
        <EditDialog
          title={draft.editing ? '운영 계정 수정' : '운영 계정 추가'}
          {...(draft.editing ? { description: draft.user_id } : {})}
          busy={busy}
          error={dialogError}
          onClose={() => setDraft(null)}
          footer={<>
            <Button onClick={() => setDraft(null)} disabled={busy}>취소</Button>
            <Button variant="primary" onClick={() => void save()} disabled={busy || !draft.user_id.trim() || taken}>
              {draft.editing ? <><Save className="w-3.5 h-3.5" />저장</> : <><Plus className="w-3.5 h-3.5" />추가</>}
            </Button>
          </>}
        >
          <div className="space-y-4">
            {!draft.editing && (
              <div className="space-y-1">
                <label className="block text-xs font-medium text-neutral-500">유저 ID</label>
                <input value={draft.user_id} onChange={(e) => setDraft({ ...draft, user_id: e.target.value })} className={`${taken ? dangerInputCls : inputCls} w-full font-mono`} />
                {taken
                  ? <p className="text-xs text-red-600">이미 등록된 계정입니다. 목록의 수정에서 바꿔 주세요.</p>
                  : <p className="text-xs text-neutral-500">플레이어 상세의 유저 ID 를 넣습니다.</p>}
              </div>
            )}
            <div className="space-y-1">
              <label className="block text-xs font-medium text-neutral-500">메모</label>
              <input value={draft.note} maxLength={100} onChange={(e) => setDraft({ ...draft, note: e.target.value })} className={`${inputCls} w-full`} />
            </div>
            <div className="space-y-3">
              <div className="text-xs font-medium text-neutral-500">권한</div>
              {OPERATOR_FLAGS.map(([flag, label, hint]) => (
                <div key={flag} className="flex items-start justify-between gap-4">
                  <div className="min-w-0">
                    <div className="text-sm text-neutral-800">{label}</div>
                    <div className="text-xs text-neutral-500">{hint}</div>
                  </div>
                  <Switch label={label} value={draft[flag]} disabled={busy} onChange={(v) => setDraft({ ...draft, [flag]: v })} />
                </div>
              ))}
            </div>
          </div>
        </EditDialog>
      )}

      <ConfirmDialog
        open={!!removing}
        title="계정을 뺄까요?"
        description="이 플레이어는 곧바로 운영 계정 권한을 모두 잃습니다."
        confirmLabel="삭제"
        danger
        busy={busy}
        error={dialogError}
        onConfirm={() => void remove()}
        onCancel={() => setRemoving(null)}
      >
        {removing && (
          <div className="text-sm text-neutral-700">
            <div>{removing.display_name || '닉네임 없음'}</div>
            <div className="font-mono text-xs text-neutral-500">{removing.user_id}</div>
          </div>
        )}
      </ConfirmDialog>
    </>
  )
}
