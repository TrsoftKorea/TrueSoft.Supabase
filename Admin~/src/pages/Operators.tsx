import { useCallback, useEffect, useState } from 'react'
import { Loader2, Plus, RotateCcw, Trash2, UserMinus } from 'lucide-react'
import { callAdmin, callHub, NotAuthenticatedError } from '../lib/api'
import { getHubToken } from '../lib/googleAuth'
import { type ProjectTarget } from '../lib/projectTarget'
import { projectName } from '../lib/projectLook'
import { CardHeader, WhiteCard } from '../components/ui/Card'
import { inputCls } from '../components/ui/Input'
import { PageHeader } from '../components/ui/PageHeader'
import { RefreshButton } from '../components/ui/FilterBar'
import { TableStatusRow } from '../components/ui/TableStatusRow'
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

  // 더할 수 있는 사람은 트루베이스 구성원(초대된 사람)뿐이라 목록에서 고른다. null 은 아직 못 불러온 것.
  const [members, setMembers] = useState<Member[] | null>(null)
  const [email, setEmail] = useState('')
  const [adding, setAdding] = useState(false)
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

  useEffect(() => {
    callHub<Member[]>('members.list')
      .then(setMembers)
      .catch((e: unknown) => {
        setMembers([])
        report(e, '트루베이스 구성원을 불러오지 못했습니다.')
      })
  }, [report])

  // 이미 이 프로젝트에 있는 사람(비활성 포함 — 되살리기는 마스터의 "복구")은 빼고 보인다.
  const candidates = (members ?? []).filter((m) => !rows.some((r) => r.email === m.email))

  const add = async () => {
    const picked = candidates.find((m) => m.email === email)
    if (!picked || adding) return
    setAdding(true)
    setError('')
    try {
      const hubToken = getHubToken()
      if (!hubToken) throw new Error('초대 확인이 만료됐습니다. 다시 로그인해 주세요.')
      await callAdmin(target, 'operators.upsert', {
        email: picked.email,
        displayName: picked.display_name,
        hubToken,
      })
      setEmail('')
      await load()
    } catch (e: unknown) {
      report(e, '추가하지 못했습니다.')
    } finally {
      setAdding(false)
    }
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
        actions={<RefreshButton onClick={() => void load()} loading={loading} />}
        description={`${projectName(target)}의 운영 콘솔에 들어올 계정을 관리합니다.`}
      />

      <ErrorBanner message={error} onDismiss={() => setError('')} />

      <WhiteCard className="p-5 space-y-4">
        <CardHeader title="운영자 추가" description="트루베이스 구성원을 이 프로젝트의 운영자로 더합니다." />
        <div>
        <div className="grid grid-cols-[1fr_auto] gap-2 items-end">
          <label className="flex flex-col gap-1">
            <span className="text-xs text-neutral-500">트루베이스 구성원</span>
            <select
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              disabled={members === null || candidates.length === 0}
              className={inputCls}
            >
              <option value="" />
              {candidates.map((m) => (
                <option key={m.email} value={m.email}>{m.display_name ? `${m.display_name} · ${m.email}` : m.email}</option>
              ))}
            </select>
          </label>
          <Button variant="primary" onClick={() => void add()} disabled={adding || !email}>
            {adding ? <Loader2 className="w-4 h-4 animate-spin" /> : <Plus className="w-4 h-4" />}
            추가
          </Button>
        </div>
        {members !== null && candidates.length === 0 && (
          <p className="mt-2 text-xs text-neutral-400">더할 수 있는 구성원이 없습니다.</p>
        )}
        </div>
      </WhiteCard>

      <WhiteCard>
        <div className="overflow-x-auto">
        <table className="w-full text-sm">
          <thead className="bg-neutral-50 text-neutral-500 text-xs">
            <tr>
              <th className="text-left px-4 py-2.5 font-medium">이메일</th>
              <th className="text-left px-4 py-2.5 font-medium">이름</th>
              <th className="text-left px-4 py-2.5 font-medium w-20">상태</th>
              <th className="text-left px-4 py-2.5 font-medium w-40">등록 시각</th>
              <th className="px-4 py-2.5 w-48"></th>
            </tr>
          </thead>
          <tbody>
            {loading || rows.length === 0 ? (
              <TableStatusRow
                loading={loading}
                empty={rows.length === 0}
                failed={failed}
                colSpan={5}
                emptyText="등록된 운영자가 없습니다."
              />
            ) : (
              rows.map((op) => {
                const disabled = op.disabled_at !== null
                return (
                  <tr
                    key={op.email}
                    className={`border-t border-neutral-100 ${disabled ? 'bg-neutral-50/60' : ''}`}
                  >
                    <td className="px-4 py-2.5 font-mono text-xs">{op.email}</td>
                    <td className="px-4 py-2.5 whitespace-nowrap">{op.display_name || '-'}</td>
                    <td className="px-4 py-2.5 whitespace-nowrap">
                      {disabled ? (
                        <span className="text-xs text-neutral-500">비활성</span>
                      ) : (
                        <span className="text-xs text-emerald-600">활성</span>
                      )}
                    </td>
                    <td className="px-4 py-2.5 text-neutral-600 whitespace-nowrap">{formatDateTime(op.created_at)}</td>
                    <td className="px-4 py-2.5 text-right space-x-1.5 whitespace-nowrap">
                      {!isMaster ? null : disabled ? (
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
                    </td>
                  </tr>
                )
              })
            )}
          </tbody>
        </table>
        </div>
      </WhiteCard>

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
