import { useCallback, useEffect, useRef, useState } from 'react'
import { Loader2, Plus, RotateCcw, Trash2, UserMinus } from 'lucide-react'
import { callAdmin, callHub, NotAuthenticatedError } from '../lib/api'
import { getHubToken } from '../lib/googleAuth'
import { type ProjectTarget } from '../lib/projectTarget'
import { projectName } from '../lib/projectLook'
import { inputCls } from '../components/ui/Input'
import { PageHeader } from '../components/ui/PageHeader'
import { RefreshButton } from '../components/ui/FilterBar'
import { TableCard, type Column } from '../components/ui/DataTable'
import { StatusText } from '../components/ui/StatusText'
import { EditDialog } from '../components/ui/EditDialog'
import { ConfirmDialog } from '../components/ui/ConfirmDialog'
import { ErrorBanner } from '../components/ui/ErrorBanner'
import { Button } from '../components/ui/Button'
import { formatDateTime } from '../components/ui/format'

type Operator = {
  email: string
  display_name: string
  disabled_at: string | null
  created_at: string
  created_by: string | null
}

type Member = { email: string; display_name: string }

// 운영자는 서로 권한이 같아 누구나 초대된 사람을 더할 수 있다. 빼기(비활성화·삭제)는 마스터만 — 서버도 막는다.
// 비밀번호는 프로젝트가 아니라 트루베이스 구성원에 붙는다(트루베이스 설정).
export default function Operators({
  target,
  isMaster,
  onUnauthenticated,
}: {
  target: ProjectTarget
  isMaster: boolean
  onUnauthenticated: () => void
}) {
  const [rows, setRows] = useState<Operator[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  // 더할 수 있는 사람은 트루베이스 구성원(초대된 사람)뿐이라 목록에서 고른다. null 은 아직 못 불러온 것,
  // 'failed' 는 못 불러온 것 — 빈 목록으로 두면 실패가 "더할 수 있는 구성원이 없습니다"로 보인다.
  // 'expired' 는 트루베이스 로그인이 끝난 것 — 다시 불러와도 안 되므로 다시 로그인하라고 보인다.
  const [members, setMembers] = useState<Member[] | null | 'failed' | 'expired'>(null)
  const membersReq = useRef(0)
  const [email, setEmail] = useState('')
  const [adding, setAdding] = useState(false)
  const [addOpen, setAddOpen] = useState(false)
  const [addError, setAddError] = useState('')
  const [disableTarget, setDisableTarget] = useState<Operator | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<Operator | null>(null)
  const [busy, setBusy] = useState(false)
  const [failed, setFailed] = useState(false)
  // 확인 창이 떠 있는 동안 실패하면 창 안에 보여준다 — 페이지 배너는 창 배경에 가린다.
  const [dialogError, setDialogError] = useState('')


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

  const load = useCallback(async () => {
    setLoading(true)
    setError('')
    setFailed(false)
    try {
      setRows(await callAdmin<Operator[]>(target, 'operators.list'))
    } catch (e: unknown) {
      report(e, '불러오지 못했습니다.')
      setRows([])
      setFailed(true)
    } finally {
      setLoading(false)
    }
  }, [target, report])

  useEffect(() => {
    void load()
  }, [load])

  // 추가 창을 열 때마다 다시 부른다 — 화면을 연 뒤 새로 초대된 사람도 고를 수 있고, 한 번 실패해도 다시 열면 된다.
  // 실패는 창 안에 보인다(페이지 띠는 창 뒤에 가린다). 저장소 표가 없거나 만료돼도 콘솔에서 내보내지 않는다 —
  // 이 프로젝트의 다른 화면은 그대로 쓸 수 있다.
  // 창을 닫았다 다시 열면 앞 요청이 늦게 올 수 있다 — 마지막 요청의 답만 반영한다.
  const loadMembers = useCallback(() => {
    const req = ++membersReq.current
    setMembers(null)
    callHub<Member[]>('members.list')
      .then((list) => { if (req === membersReq.current) setMembers(list) })
      .catch((e: unknown) => {
        if (req === membersReq.current) setMembers(e instanceof NotAuthenticatedError ? 'expired' : 'failed')
      })
  }, [])

  const memberList = Array.isArray(members) ? members : []
  // 이미 이 프로젝트에 있는 사람(비활성 포함 — 되살리기는 마스터의 "복구")은 빼고 보인다.
  const candidates = memberList.filter((m) => !rows.some((r) => r.email === m.email))

  const add = async () => {
    const picked = candidates.find((m) => m.email === email)
    if (!picked || adding) return
    setAdding(true)
    setAddError('')
    try {
      const hubToken = getHubToken()
      if (!hubToken) throw new Error('초대 확인이 만료됐습니다. 다시 로그인해 주세요.')
      await callAdmin(target, 'operators.upsert', {
        email: picked.email,
        displayName: picked.display_name,
        hubToken,
      })
      setEmail('')
      setAddOpen(false)
      await load()
    } catch (e: unknown) {
      // 창은 열어 둔다 — 실패 사유가 창 배경에 가리지 않게.
      if (e instanceof NotAuthenticatedError) { setAddOpen(false); onUnauthenticated(); return }
      setAddError(e instanceof Error ? e.message : '추가하지 못했습니다.')
    } finally {
      setAdding(false)
    }
  }

  const columns: Column<Operator>[] = [
    { key: 'email', header: '이메일', kind: 'name', cell: (op) => op.email },
    { key: 'name', header: '이름', kind: 'name', cell: (op) => op.display_name },
    { key: 'state', header: '상태', kind: 'status', cell: (op) => (op.disabled_at ? <StatusText tone="muted">비활성</StatusText> : <StatusText tone="ok">활성</StatusText>) },
    { key: 'created', header: '등록 시각', kind: 'datetime', cell: (op) => formatDateTime(op.created_at) },
  ]
  // 빼기(비활성화·삭제)는 마스터만 — 아니면 버튼 칸을 아예 두지 않는다.
  if (isMaster) {
    columns.push({
      key: 'actions', header: '', kind: 'actions', cell: (op) => (
        <span className="inline-flex gap-1.5">
          {op.disabled_at ? (
            <>
              <Button variant="outline" size="sm" onClick={() => void setDisabled(op, false)} disabled={busy}>
                <RotateCcw className="w-3 h-3" /> 복구
              </Button>
              <Button variant="dangerOutline" size="sm" onClick={() => { setDialogError(''); setDeleteTarget(op) }} disabled={busy}>
                <Trash2 className="w-3 h-3" /> 삭제
              </Button>
            </>
          ) : (
            <Button variant="dangerOutline" size="sm" onClick={() => { setDialogError(''); setDisableTarget(op) }} disabled={busy}>
              <UserMinus className="w-3 h-3" /> 비활성화
            </Button>
          )}
        </span>
      ),
    })
  }

  const setDisabled = async (op: Operator, disabled: boolean) => {
    if (busy) return
    setBusy(true)
    setError('')
    setDialogError('')
    try {
      await callAdmin(target, 'operators.setDisabled', { email: op.email, disabled })
      setDisableTarget(null)
      await load()
    } catch (e: unknown) {
      // 복구는 확인 창 없이 바로 부르므로 배너로, 비활성화는 창 안으로.
      if (disabled && !(e instanceof NotAuthenticatedError)) setDialogError(e instanceof Error ? e.message : '변경하지 못했습니다.')
      else report(e, '변경하지 못했습니다.')
    } finally {
      setBusy(false)
    }
  }

  const deleteOperator = async (op: Operator) => {
    if (busy) return
    setBusy(true)
    setError('')
    setDialogError('')
    try {
      await callAdmin(target, 'operators.delete', { email: op.email })
      setDeleteTarget(null)
      await load()
    } catch (e: unknown) {
      if (e instanceof NotAuthenticatedError) { onUnauthenticated(); return }
      setDialogError(e instanceof Error ? e.message : '삭제하지 못했습니다.')
    } finally {
      setBusy(false)
    }
  }


  return (
    <div className="space-y-4">
      <PageHeader
        title="운영자 관리"
        actions={<>
          <RefreshButton onClick={() => void load()} loading={loading} />
          <Button variant="primary" onClick={() => { setEmail(''); setAddError(''); setAddOpen(true); loadMembers() }}>
            <Plus className="w-4 h-4" />운영자 추가
          </Button>
        </>}
        description={`${projectName(target)}의 운영 콘솔에 들어올 계정을 관리합니다.`}
      />

      <ErrorBanner message={error} onDismiss={() => setError('')} />

      <TableCard
        columns={columns}
        rows={rows}
        rowKey={(op) => op.email}
        loading={loading}
        failed={failed}
        emptyText="등록된 운영자가 없습니다."
      />

      {addOpen && (
        <EditDialog
          title="운영자 추가"
          busy={adding}
          onClose={() => setAddOpen(false)}
          error={addError || undefined}
          footer={<>
            <Button variant="outline" onClick={() => setAddOpen(false)} disabled={adding}>취소</Button>
            <Button variant="primary" onClick={() => void add()} disabled={adding || !email}>
              {adding && <Loader2 className="w-4 h-4 animate-spin" />}추가
            </Button>
          </>}
        >
          <label className="flex flex-col gap-1">
            <span className="text-xs font-medium text-neutral-500">트루베이스 구성원</span>
            <select
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              disabled={!Array.isArray(members) || candidates.length === 0}
              className={inputCls + ' w-full'}
            >
              <option value="" />
              {candidates.map((m) => (
                <option key={m.email} value={m.email}>{m.display_name ? `${m.display_name} · ${m.email}` : m.email}</option>
              ))}
            </select>
          </label>
          {members === 'expired' ? (
            <p className="mt-2 text-xs text-red-600">트루베이스 로그인이 만료됐습니다. 다시 로그인해 주세요.</p>
          ) : members === 'failed' ? (
            <p className="mt-2 flex items-center gap-2 text-xs text-red-600">
              트루베이스 구성원을 불러오지 못했습니다.
              <button type="button" onClick={loadMembers} className="text-[#1677ff] hover:underline">다시 불러오기</button>
            </p>
          ) : Array.isArray(members) && candidates.length === 0 && (
            <p className="mt-2 text-xs text-neutral-400">더할 수 있는 구성원이 없습니다.</p>
          )}
        </EditDialog>
      )}

      <ConfirmDialog
        open={!!disableTarget}
        title="운영자 비활성화"
        confirmLabel="비활성화"
        danger
        busy={busy}
        error={disableTarget ? dialogError : ''}
        onConfirm={() => {
          if (disableTarget) void setDisabled(disableTarget, true)
        }}
        onCancel={() => setDisableTarget(null)}
        description="즉시 접근이 막힙니다. 기록은 남고, 나중에 복구할 수 있습니다."
      >
        {disableTarget && (
          <div className="rounded-md bg-neutral-50 border border-neutral-200 px-3 py-2 text-sm">
            <div className="text-neutral-800">{disableTarget.display_name || '(이름 없음)'}</div>
            <div className="text-xs text-neutral-500 mt-1 font-mono">{disableTarget.email}</div>
          </div>
        )}
      </ConfirmDialog>

      <ConfirmDialog
        open={!!deleteTarget}
        title="운영자 삭제"
        confirmLabel="삭제"
        danger
        busy={busy}
        error={deleteTarget ? dialogError : ''}
        onConfirm={() => {
          if (deleteTarget) void deleteOperator(deleteTarget)
        }}
        onCancel={() => setDeleteTarget(null)}
        description="되돌릴 수 없습니다. 다시 넣으려면 이메일을 처음부터 다시 추가해야 합니다."
      >
        {deleteTarget && (
          <div className="rounded-md bg-neutral-50 border border-neutral-200 px-3 py-2 text-sm">
            <div className="text-neutral-800">{deleteTarget.display_name || '(이름 없음)'}</div>
            <div className="text-xs text-neutral-500 mt-1 font-mono">{deleteTarget.email}</div>
          </div>
        )}
      </ConfirmDialog>
    </div>
  )
}
