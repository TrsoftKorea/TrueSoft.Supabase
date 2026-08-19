import { useCallback, useEffect, useState } from 'react'
import { Loader2, Mail, Plus, RotateCcw, UserMinus } from 'lucide-react'
import { callAdmin, NotAuthenticatedError } from '../lib/api'
import { getProject, type ProjectTarget } from '../lib/projectTarget'
import { WhiteCard } from '../components/ui/Card'
import { PageHeader } from '../components/ui/PageHeader'
import { TableStatusRow } from '../components/ui/TableStatusRow'
import { ConfirmDialog } from '../components/ui/ConfirmDialog'
import { formatDateTime } from '../components/ui/format'

type Operator = {
  email: string
  display_name: string
  disabled_at: string | null
  created_at: string
  created_by: string | null
  hasPassword: boolean
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
      const result = await callAdmin<{ warning: string | null }>(target, 'operators.upsert', {
        email: email.trim(),
        displayName: name.trim(),
      })
      if (result.warning) setError(result.warning)
      setEmail('')
      setName('')
      await load()
    } catch (e: unknown) {
      report(e, '추가하지 못했습니다.')
    } finally {
      setAdding(false)
    }
  }

  const resendInvite = async (op: Operator) => {
    if (busy) return
    setBusy(true)
    setError('')
    try {
      await callAdmin(target, 'operators.resendInvite', { email: op.email })
    } catch (e: unknown) {
      report(e, '초대 메일을 다시 보내지 못했습니다.')
    } finally {
      setBusy(false)
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
          <button
            onClick={() => void add()}
            disabled={adding || !email.trim()}
            className="inline-flex items-center justify-center gap-1 h-9 px-4 rounded-md bg-[#1677ff] text-white text-sm disabled:opacity-50"
          >
            {adding ? <Loader2 className="w-4 h-4 animate-spin" /> : <Plus className="w-4 h-4" />}
            추가
          </button>
        </div>
        <p className="mt-2 text-xs text-neutral-400">
          비밀번호를 아직 설정하지 않은 이메일이면 초대 메일이 자동으로 갑니다. 이미 있는 이메일을 다시 추가하면 비활성 상태가 풀립니다.
        </p>
      </WhiteCard>

      <WhiteCard>
        <table className="w-full text-sm">
          <thead className="bg-neutral-50 text-neutral-500 text-xs">
            <tr>
              <th className="text-left px-4 py-2.5 font-medium">이메일</th>
              <th className="text-left px-4 py-2.5 font-medium">이름</th>
              <th className="text-left px-4 py-2.5 font-medium w-28">상태</th>
              <th className="text-left px-4 py-2.5 font-medium w-40">추가일자</th>
              <th className="px-4 py-2.5 w-28"></th>
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
                    <td className="px-4 py-2.5">
                      {disabled ? (
                        <span className="text-xs text-neutral-500">비활성</span>
                      ) : op.hasPassword ? (
                        <span className="text-xs text-emerald-600">사용 중</span>
                      ) : (
                        <span className="text-xs text-amber-600">초대 대기 중</span>
                      )}
                    </td>
                    <td className="px-4 py-2.5 text-neutral-600">{formatDateTime(op.created_at)}</td>
                    <td className="px-4 py-2.5 text-right space-x-1.5">
                      {!disabled && !op.hasPassword && (
                        <button
                          onClick={() => void resendInvite(op)}
                          disabled={busy}
                          className="inline-flex items-center gap-1 px-2 py-1 rounded text-xs border border-neutral-200 text-neutral-600 hover:bg-neutral-50 disabled:opacity-40"
                        >
                          <Mail className="w-3 h-3" /> 초대 재발송
                        </button>
                      )}
                      {disabled ? (
                        <button
                          onClick={() => void setDisabled(op, false)}
                          disabled={busy}
                          className="inline-flex items-center gap-1 px-2 py-1 rounded text-xs border border-neutral-200 text-neutral-600 hover:bg-neutral-50 disabled:opacity-40"
                        >
                          <RotateCcw className="w-3 h-3" /> 복구
                        </button>
                      ) : (
                        <button
                          onClick={() => setDisableTarget(op)}
                          disabled={busy}
                          className="inline-flex items-center gap-1 px-2 py-1 rounded text-xs border border-neutral-200 text-neutral-600 hover:bg-red-50 hover:text-red-600 disabled:opacity-40"
                        >
                          <UserMinus className="w-3 h-3" /> 비활성화
                        </button>
                      )}
                    </td>
                  </tr>
                )
              })
            )}
          </tbody>
        </table>
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
    </div>
  )
}
