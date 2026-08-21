import { useCallback, useEffect, useState } from 'react'
import { KeyRound, Loader2, Plus, RotateCcw, Trash2, UserMinus } from 'lucide-react'
import { callAdmin, NotAuthenticatedError } from '../lib/api'
import { getProject, type ProjectTarget } from '../lib/projectTarget'
import { WhiteCard } from '../components/ui/Card'
import { PageHeader } from '../components/ui/PageHeader'
import { TableStatusRow } from '../components/ui/TableStatusRow'
import { ConfirmDialog } from '../components/ui/ConfirmDialog'
import { Button } from '../components/ui/Button'
import { formatDateTime } from '../components/ui/format'

type Operator = {
  email: string
  display_name: string
  disabled_at: string | null
  created_at: string
  created_by: string | null
  hasPassword: boolean
}

// admin-api/index.ts 의 MIN_PASSWORD_LENGTH 와 같은 값이어야 한다 — 프런트와 백엔드가
// 런타임이 달라(Vite 번들 vs Deno 함수) 상수를 공유할 수 없다.
const MIN_PASSWORD_LENGTH = 8

function SetPasswordModal({
  target,
  op,
  onClose,
  onSaved,
  report,
}: {
  target: ProjectTarget
  op: Operator
  onClose: () => void
  onSaved: () => void
  report: (e: unknown, fallback: string) => void
}) {
  const [newPassword, setNewPassword] = useState('')
  const [confirm, setConfirm] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  const tooShort = newPassword.length > 0 && newPassword.length < MIN_PASSWORD_LENGTH
  const mismatch = confirm.length > 0 && newPassword !== confirm
  const canSubmit = newPassword.length >= MIN_PASSWORD_LENGTH && newPassword === confirm && !busy

  const submit = async () => {
    if (!canSubmit) return
    setBusy(true)
    setError('')
    try {
      await callAdmin(target, 'operators.setPassword', { email: op.email, newPassword })
      onSaved()
    } catch (e: unknown) {
      if (e instanceof NotAuthenticatedError) {
        report(e, '설정하지 못했습니다.')
        return
      }
      setError(e instanceof Error ? e.message : '설정하지 못했습니다.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <ConfirmDialog
      open
      title="비밀번호 설정"
      confirmLabel="설정"
      busy={busy}
      onConfirm={() => void submit()}
      onCancel={onClose}
    >
      <div className="space-y-3">
        <div className="px-3 py-2 rounded-md bg-neutral-50 border border-neutral-200 text-sm font-mono">
          {op.email}
        </div>
        <div>
          <label className="block text-xs text-neutral-500 mb-1">새 비밀번호 ({MIN_PASSWORD_LENGTH}자 이상)</label>
          <input
            type="password"
            value={newPassword}
            onChange={(e) => setNewPassword(e.target.value)}
            className="h-9 w-full px-2 rounded-md border border-neutral-300 text-sm bg-white"
          />
          {tooShort && <p className="mt-1 text-xs text-amber-600">{MIN_PASSWORD_LENGTH}자 이상이어야 합니다.</p>}
        </div>
        <div>
          <label className="block text-xs text-neutral-500 mb-1">새 비밀번호 확인</label>
          <input
            type="password"
            value={confirm}
            onChange={(e) => setConfirm(e.target.value)}
            onKeyDown={(e) => e.key === 'Enter' && void submit()}
            className="h-9 w-full px-2 rounded-md border border-neutral-300 text-sm bg-white"
          />
          {mismatch && <p className="mt-1 text-xs text-amber-600">비밀번호가 서로 다릅니다.</p>}
        </div>
        <p className="text-xs text-neutral-400">이 비밀번호는 이메일로 전달되지 않습니다. 직접 알려주세요.</p>
        {error && <div className="text-sm text-red-500">{error}</div>}
      </div>
    </ConfirmDialog>
  )
}

export default function Operators({
  target,
  masterEmail,
  onUnauthenticated,
}: {
  target: ProjectTarget
  masterEmail: string
  onUnauthenticated: () => void
}) {
  const [rows, setRows] = useState<Operator[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  const [email, setEmail] = useState('')
  const [name, setName] = useState('')
  const [adding, setAdding] = useState(false)
  const [disableTarget, setDisableTarget] = useState<Operator | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<Operator | null>(null)
  const [passwordTarget, setPasswordTarget] = useState<Operator | null>(null)
  const [busy, setBusy] = useState(false)

  const project = getProject(target)

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
    try {
      setRows(await callAdmin<Operator[]>(target, 'operators.list'))
    } catch (e: unknown) {
      report(e, '불러오지 못했습니다.')
      setRows([])
    } finally {
      setLoading(false)
    }
  }, [target, report])

  useEffect(() => {
    void load()
  }, [load])

  const add = async () => {
    if (!email.trim() || adding) return
    setAdding(true)
    setError('')
    try {
      await callAdmin(target, 'operators.upsert', {
        email: email.trim(),
        displayName: name.trim(),
      })
      setEmail('')
      setName('')
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
    try {
      await callAdmin(target, 'operators.setDisabled', { email: op.email, disabled })
      setDisableTarget(null)
      await load()
    } catch (e: unknown) {
      report(e, '변경하지 못했습니다.')
    } finally {
      setBusy(false)
    }
  }

  const deleteOperator = async (op: Operator) => {
    if (busy) return
    setBusy(true)
    setError('')
    try {
      await callAdmin(target, 'operators.delete', { email: op.email })
      setDeleteTarget(null)
      await load()
    } catch (e: unknown) {
      report(e, '삭제하지 못했습니다.')
    } finally {
      setBusy(false)
    }
  }

  const inputCls = 'h-9 px-2 rounded-md border border-neutral-300 text-sm bg-white'

  return (
    <div className="space-y-4">
      <PageHeader
        title="운영자 관리"
        description={`${project.label}의 운영 도구에 들어올 수 있는 계정입니다. 게임 계정과는 별개입니다.`}
      />

      <div className="rounded-md border border-neutral-200 bg-white px-4 py-2.5 text-sm text-neutral-600">
        마스터 <span className="font-medium text-neutral-800">{masterEmail}</span> 는 목록에 없고
        지울 수도 없습니다. 목록이 비어도 잠기지 않도록 남겨둔 계정입니다.
      </div>

      {error && (
        <div className="rounded-md border border-red-200 bg-red-50 px-4 py-2.5 text-sm text-red-700">
          {error}
        </div>
      )}

      <WhiteCard className="p-4">
        <div className="text-xs font-medium text-neutral-500 mb-2">운영자 추가</div>
        <div className="grid grid-cols-[1.5fr_1fr_auto] gap-2 items-end">
          <div className="flex flex-col gap-1">
            <span className="text-xs text-neutral-500">이메일</span>
            <input
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              onKeyDown={(e) => e.key === 'Enter' && void add()}
              className={inputCls}
            />
          </div>
          <div className="flex flex-col gap-1">
            <span className="text-xs text-neutral-500">이름</span>
            <input
              value={name}
              onChange={(e) => setName(e.target.value)}
              onKeyDown={(e) => e.key === 'Enter' && void add()}
              className={inputCls}
            />
          </div>
          <Button variant="primary" onClick={() => void add()} disabled={adding || !email.trim()}>
            {adding ? <Loader2 className="w-4 h-4 animate-spin" /> : <Plus className="w-4 h-4" />}
            추가
          </Button>
        </div>
        <p className="mt-2 text-xs text-neutral-400">
          구글 계정으로 바로 로그인할 수 있습니다. 비밀번호 로그인이 필요하면 추가 후 "비밀번호 설정"으로 정해서 알려주세요. 이미 있는 이메일을 다시 추가하면 비활성 상태가 풀립니다.
        </p>
      </WhiteCard>

      <WhiteCard>
        <div className="overflow-x-auto">
        <table className="w-full text-sm">
          <thead className="bg-neutral-50 text-neutral-500 text-xs">
            <tr>
              <th className="text-left px-4 py-2.5 font-medium">이메일</th>
              <th className="text-left px-4 py-2.5 font-medium">이름</th>
              <th className="text-left px-4 py-2.5 font-medium w-36">상태</th>
              <th className="text-left px-4 py-2.5 font-medium w-40">추가일자</th>
              <th className="px-4 py-2.5 w-72"></th>
            </tr>
          </thead>
          <tbody>
            {loading || rows.length === 0 ? (
              <TableStatusRow
                loading={loading}
                empty={rows.length === 0}
                colSpan={5}
                emptyText="등록된 운영자가 없습니다. 마스터만 들어올 수 있는 상태입니다."
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
                    <td className="px-4 py-2.5">{op.display_name || '-'}</td>
                    <td className="px-4 py-2.5 whitespace-nowrap">
                      {disabled ? (
                        <span className="text-xs text-neutral-500">비활성</span>
                      ) : op.hasPassword ? (
                        <span className="text-xs text-emerald-600">비밀번호 사용 중</span>
                      ) : (
                        <span className="text-xs text-amber-600">비밀번호 미설정</span>
                      )}
                    </td>
                    <td className="px-4 py-2.5 text-neutral-600 whitespace-nowrap">{formatDateTime(op.created_at)}</td>
                    <td className="px-4 py-2.5 text-right space-x-1.5 whitespace-nowrap">
                      {!disabled && (
                        <Button variant="outline" size="sm" onClick={() => setPasswordTarget(op)} disabled={busy}>
                          <KeyRound className="w-3 h-3" /> 비밀번호 설정
                        </Button>
                      )}
                      {disabled ? (
                        <>
                          <Button variant="outline" size="sm" onClick={() => void setDisabled(op, false)} disabled={busy}>
                            <RotateCcw className="w-3 h-3" /> 복구
                          </Button>
                          <Button variant="danger" size="sm" onClick={() => setDeleteTarget(op)} disabled={busy}>
                            <Trash2 className="w-3 h-3" /> 삭제
                          </Button>
                        </>
                      ) : (
                        <Button variant="danger" size="sm" onClick={() => setDisableTarget(op)} disabled={busy}>
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

      {passwordTarget && (
        <SetPasswordModal
          target={target}
          op={passwordTarget}
          onClose={() => setPasswordTarget(null)}
          onSaved={() => {
            setPasswordTarget(null)
            void load()
          }}
          report={report}
        />
      )}
    </div>
  )
}
