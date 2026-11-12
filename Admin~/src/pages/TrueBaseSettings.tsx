import { useCallback, useEffect, useState } from 'react'
import { ArrowLeft, Check, Copy, Loader2, Pencil, Plus, RefreshCw, RotateCcw, Trash2 } from 'lucide-react'
import { copyToClipboard } from '../lib/clipboard'
import { PROJECTS, type ProjectTarget } from '../lib/projectTarget'
import { projectHidden, projectName, useProjectLook } from '../lib/projectLook'
import { getToken } from '../lib/googleAuth'
import { callAdmin, callHub, NotAuthenticatedError } from '../lib/api'
import { CardHeader, WhiteCard } from '../components/ui/Card'
import { PageHeader } from '../components/ui/PageHeader'
import { ErrorBanner } from '../components/ui/ErrorBanner'
import { DataTable } from '../components/ui/DataTable'
import { StatusText } from '../components/ui/StatusText'
import { RefreshButton } from '../components/ui/FilterBar'
import { ConfirmDialog } from '../components/ui/ConfirmDialog'
import { EditDialog } from '../components/ui/EditDialog'
import { Button } from '../components/ui/Button'
import { inputCls } from '../components/ui/Input'
import { formatDateTime } from '../components/ui/format'
import { KeyGuideToggle, MANAGEMENT_KEY, type StoreKeyStatus } from './Settings'

/**
 * 마스터 전용 "트루베이스 설정" — 프로젝트 밖의 일을 한다. 프로젝트 선택 화면에서 연다.
 *  - 초대: 트루베이스에 들어올 수 있는 사람(콘솔 주소의 초대 저장소). 초대된 사람은 새 프로젝트를 만들고 운영자로 더해질 수 있다.
 *    초대를 지우면 콘솔이 모든 프로젝트의 운영자에서 먼저 뺀다(removeFromAllProjects) — 저장소는 프로젝트 주소·열쇠를 모른다.
 *    구글 계정이 없는 사람에게는 여기서 임시 비밀번호를 준다(초기화). 실제 비밀번호는 본인이 처음 들어올 때 정하고 마스터는
 *    모른다 — 비밀번호는 프로젝트가 아니라 사람에 붙어 모든 프로젝트에 같이 쓰인다.
 *  - 관리 열쇠: 프로젝트마다 Supabase 로그인 설정을 바꾸는 열쇠. 운영자는 열쇠를 만들지도 보지도 않는다 — 대기 프로젝트를
 *    준비할 때 마스터가 넣어 두면, 운영자가 서비스 연결에 넣은 로그인 값이 저절로 Supabase 에 들어간다.
 */

type Invite = {
  email: string; display_name: string; invited_by: string | null; invited_at: string
  has_password: boolean; must_change: boolean
}

// ok 가 null 이면 관리 열쇠가 없어 건너뛴 것.
type AuthSyncStatus = { finishedAt: string | null; ok: boolean | null; message: string | null }

type KeyRow = {
  target: ProjectTarget
  state: 'loading' | 'ready' | 'blocked'
  key?: StoreKeyStatus | undefined
  sync?: AuthSyncStatus | null
  error?: string
}

const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

type Operator = { email: string; disabled_at: string | null }

/**
 * 모든 프로젝트의 운영자에서 이 사람을 뺀다(비활성화 → 삭제 — 서버가 활성 운영자를 바로 지우지 못하게 막는다).
 * 운영자가 아닌 프로젝트는 건너뛴다. 빼지 못한 프로젝트 이름과 사유를 돌려준다 — 로그인 표가 없는 프로젝트도
 * 그 사람이 남아 있을 수 있으므로 실패로 센다.
 */
async function removeFromAllProjects(email: string): Promise<string[]> {
  const results = await Promise.all(
    PROJECTS.filter((p) => p.url || __MOCK__).map(async (p) => {
      const label = projectHidden(p.key) ? `대기 중 프로젝트 ${p.key}` : projectName(p.key)
      if (!getToken(p.key)) return `${label}(로그인 안 됨)`
      try {
        const ops = await callAdmin<Operator[]>(p.key, 'operators.list')
        const found = ops.find((o) => o.email === email)
        if (!found) return null
        if (!found.disabled_at) await callAdmin(p.key, 'operators.setDisabled', { email, disabled: true })
        await callAdmin(p.key, 'operators.delete', { email })
        return null
      } catch (e: unknown) {
        if (e instanceof NotAuthenticatedError) return `${label}(로그인 만료)`
        return `${label}(${e instanceof Error ? e.message : '실패'})`
      }
    }),
  )
  return results.filter((r): r is string => r !== null)
}

export default function TrueBaseSettings({ onBack, onUnauthenticated }: { onBack: () => void; onUnauthenticated: () => void }) {
  const [error, setError] = useState('')
  const report = useCallback((e: unknown, fallback: string) => {
    if (e instanceof NotAuthenticatedError) { onUnauthenticated(); return }
    setError(e instanceof Error ? e.message : fallback)
  }, [onUnauthenticated])

  return (
    <div className="min-h-screen bg-neutral-50">
      <div className="max-w-4xl mx-auto px-4 py-6 space-y-4">
        <Button variant="outline" size="sm" onClick={onBack}>
          <ArrowLeft className="w-3.5 h-3.5" />
          프로젝트 목록
        </Button>
        <PageHeader title="트루베이스 설정" description="프로젝트 전체에 걸친 설정을 정합니다." />
        <ErrorBanner message={error} onDismiss={() => setError('')} />
        <InvitesCard report={report} />
        <KeysCard report={report} />
      </div>
    </div>
  )
}

function InvitesCard({ report }: { report: (e: unknown, fallback: string) => void }) {
  const [rows, setRows] = useState<Invite[]>([])
  const [loading, setLoading] = useState(true)
  const [failed, setFailed] = useState(false)
  const [email, setEmail] = useState('')
  const [name, setName] = useState('')
  const [emailHint, setEmailHint] = useState('')
  const [adding, setAdding] = useState(false)
  const [removeTarget, setRemoveTarget] = useState<Invite | null>(null)
  const [passwordTarget, setPasswordTarget] = useState<Invite | null>(null)
  const [removing, setRemoving] = useState(false)
  const [dialogError, setDialogError] = useState('')

  const load = useCallback(async () => {
    setLoading(true)
    setFailed(false)
    try {
      setRows(await callHub<Invite[]>('invites.list'))
    } catch (e: unknown) {
      report(e, '초대 목록을 불러오지 못했습니다.')
      setRows([])
      setFailed(true)
    } finally {
      setLoading(false)
    }
  }, [report])

  useEffect(() => { void load() }, [load])

  const add = async () => {
    const e = email.trim()
    if (!e || adding) return
    if (!EMAIL_RE.test(e)) { setEmailHint('이메일 형식이 아닙니다.'); return }
    setEmailHint('')
    setAdding(true)
    try {
      await callHub('invites.add', { email: e, displayName: name.trim() })
      setEmail('')
      setName('')
      await load()
    } catch (err: unknown) {
      report(err, '초대하지 못했습니다.')
    } finally {
      setAdding(false)
    }
  }

  const remove = async (inv: Invite) => {
    setRemoving(true)
    setDialogError('')
    try {
      // 프로젝트에서 먼저 빼고, 전부 빠졌을 때만 초대를 지운다 — 초대가 남아 있어야 실패한 곳을 다시 눌러 마저 뺄 수 있다.
      const failed = await removeFromAllProjects(inv.email)
      if (failed.length > 0) {
        setDialogError(`${failed.join(' · ')} — 이 프로젝트에서 빼지 못해 초대를 남겨 두었습니다. 다시 삭제하면 남은 곳만 다시 뺍니다.`)
        return
      }
      await callHub('invites.remove', { email: inv.email })
      setRemoveTarget(null)
      await load()
    } catch (e: unknown) {
      if (e instanceof NotAuthenticatedError) { report(e, ''); return }
      setDialogError(e instanceof Error ? e.message : '초대를 삭제하지 못했습니다.')
    } finally {
      setRemoving(false)
    }
  }

  return (
    <WhiteCard className="p-5 space-y-5">
      <CardHeader
        title="초대"
        description="트루베이스에 들어올 수 있는 사람을 정하고, 비밀번호로 들어오는 사람에게 임시 비밀번호를 줍니다."
      />

      <div>
        <div className="grid grid-cols-[1.5fr_1fr_auto] gap-2 items-end">
          <label className="flex flex-col gap-1">
            <span className="text-xs text-neutral-500">이메일</span>
            <input
              value={email}
              onChange={(e) => { setEmail(e.target.value); setEmailHint('') }}
              onKeyDown={(e) => e.key === 'Enter' && void add()}
              className={inputCls}
            />
          </label>
          <label className="flex flex-col gap-1">
            <span className="text-xs text-neutral-500">이름</span>
            <input
              value={name}
              maxLength={40}
              onChange={(e) => setName(e.target.value)}
              onKeyDown={(e) => e.key === 'Enter' && void add()}
              className={inputCls}
            />
          </label>
          <Button variant="primary" onClick={() => void add()} disabled={adding || !email.trim()}>
            {adding ? <Loader2 className="w-4 h-4 animate-spin" /> : <Plus className="w-4 h-4" />}
            초대
          </Button>
        </div>
        {emailHint && <p className="mt-1 text-xs text-red-600">{emailHint}</p>}
        <p className="mt-2 text-xs text-neutral-400">초대할 사람의 이메일을 넣습니다.</p>
      </div>

      <div className="overflow-hidden rounded-md border border-neutral-200">
        <DataTable
          columns={[
            { key: 'email', header: '이메일', kind: 'name', cell: (r) => r.email },
            { key: 'name', header: '이름', kind: 'name', cell: (r) => r.display_name || null },
            { key: 'by', header: '초대한 사람', kind: 'name', cell: (r) => r.invited_by },
            { key: 'at', header: '초대 시각', kind: 'datetime', cell: (r) => formatDateTime(r.invited_at) },
            {
              key: 'actions', header: '', kind: 'actions', cell: (r) => (
                <span className="inline-flex gap-1.5">
                  <Button variant="outline" size="sm" onClick={() => setPasswordTarget(r)}>
                    <RotateCcw className="w-3 h-3" /> 비밀번호 초기화
                  </Button>
                  <Button variant="dangerOutline" size="sm" onClick={() => { setDialogError(''); setRemoveTarget(r) }}>
                    <Trash2 className="w-3 h-3" /> 삭제
                  </Button>
                </span>
              ),
            },
          ]}
          rows={rows}
          rowKey={(r) => r.email}
          loading={loading}
          failed={failed}
          emptyText="초대된 사람이 없습니다."
          wide
        />
      </div>

      <ConfirmDialog
        open={!!removeTarget}
        title="초대 삭제"
        description="모든 프로젝트의 운영자에서 빠지고 비밀번호도 함께 지워집니다. 다시 초대하기 전까지 트루베이스에 들어올 수 없습니다."
        confirmLabel="삭제"
        danger
        busy={removing}
        error={dialogError}
        onConfirm={() => { if (removeTarget) void remove(removeTarget) }}
        onCancel={() => setRemoveTarget(null)}
      >
        {removeTarget && (
          <div className="rounded-md bg-neutral-50 border border-neutral-200 px-3 py-2 text-sm">
            <div className="text-neutral-800">{removeTarget.display_name || '(이름 없음)'}</div>
            <div className="text-xs text-neutral-500 mt-1 font-mono">{removeTarget.email}</div>
          </div>
        )}
      </ConfirmDialog>

      {passwordTarget && (
        <ResetPasswordDialog
          invite={passwordTarget}
          onClose={() => setPasswordTarget(null)}
          onIssued={() => void load()}
          report={report}
        />
      )}
    </WhiteCard>
  )
}

// 마스터는 비밀번호를 정하지 않는다 — 임시 비밀번호를 받아 그 사람에게 전하고, 그 사람이 처음 들어올 때 새로 정한다.
// 임시 비밀번호는 이 창에서 한 번만 보인다. 초기화하면 그 사람의 기존 로그인이 몇 분 안에 끊긴다.
function ResetPasswordDialog({
  invite,
  onClose,
  onIssued,
  report,
}: {
  invite: Invite
  onClose: () => void
  onIssued: () => void
  report: (e: unknown, fallback: string) => void
}) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [issued, setIssued] = useState<string | null>(null)
  const [copied, setCopied] = useState(false)

  const issue = async () => {
    setBusy(true)
    setError('')
    try {
      const { password } = await callHub<{ password: string }>('passwords.reset', { email: invite.email })
      setIssued(password)
      onIssued()
    } catch (e: unknown) {
      if (e instanceof NotAuthenticatedError) { report(e, ''); return }
      setError(e instanceof Error ? e.message : '임시 비밀번호를 만들지 못했습니다.')
    } finally {
      setBusy(false)
    }
  }

  const copy = async () => {
    if (issued && (await copyToClipboard(issued))) setCopied(true)
  }

  const title = '비밀번호 초기화'
  return (
    <EditDialog
      title={title}
      description={invite.display_name ? `${invite.display_name} · ${invite.email}` : invite.email}
      busy={busy}
      onClose={onClose}
      error={error || undefined}
      footer={issued ? (
        <Button variant="primary" onClick={onClose}>닫기</Button>
      ) : (<>
        <Button variant="outline" onClick={onClose} disabled={busy}>취소</Button>
        <Button variant="primary" onClick={() => void issue()} disabled={busy}>
          {busy && <Loader2 className="w-4 h-4 animate-spin" />}
          초기화
        </Button>
      </>)}
    >
      {issued ? (
        <div className="space-y-3">
          <div className="flex items-center gap-2">
            <code className="flex-1 rounded-md border border-neutral-200 bg-neutral-50 px-3 py-2 font-mono text-base tracking-wider text-neutral-900 select-all">
              {issued}
            </code>
            <Button variant="outline" onClick={() => void copy()}>
              {copied ? <Check className="w-4 h-4" /> : <Copy className="w-4 h-4" />}
              {copied ? '복사됨' : '복사'}
            </Button>
          </div>
          <p className="text-xs text-neutral-500">이 창을 닫으면 다시 볼 수 없습니다. 이 사람은 이 비밀번호로 처음 들어올 때 새 비밀번호를 정합니다.</p>
        </div>
      ) : (
        <p className="text-sm text-neutral-600">
          {invite.has_password
            ? '임시 비밀번호를 새로 만듭니다. 이 사람의 기존 로그인은 몇 분 안에 끊기고, 다음 로그인 때 새 비밀번호를 정합니다.'
            : '임시 비밀번호를 만듭니다. 이 사람은 처음 들어올 때 새 비밀번호를 정합니다.'}
        </p>
      )}
    </EditDialog>
  )
}

function KeysCard({ report }: { report: (e: unknown, fallback: string) => void }) {
  const look = useProjectLook()
  const targets = PROJECTS.filter((p) => p.url).map((p) => p.key)
  const [rows, setRows] = useState<KeyRow[]>(() => targets.map((t) => ({ target: t, state: 'loading' })))
  const [editing, setEditing] = useState<ProjectTarget | null>(null)
  const [value, setValue] = useState('')
  const [saving, setSaving] = useState(false)
  const [saveError, setSaveError] = useState('')
  const [syncing, setSyncing] = useState<ProjectTarget | null>(null)

  const loadOne = useCallback(async (t: ProjectTarget): Promise<KeyRow> => {
    if (!getToken(t)) return { target: t, state: 'blocked', error: '들어갈 수 없음' }
    try {
      const res = await callAdmin<{ rows: StoreKeyStatus[]; authSync?: AuthSyncStatus | null }>(t, 'settings.getStoreStatus')
      return { target: t, state: 'ready', key: res.rows.find((r) => r.key === MANAGEMENT_KEY.key), sync: res.authSync ?? null }
    } catch (e: unknown) {
      if (e instanceof NotAuthenticatedError) throw e
      return { target: t, state: 'blocked', error: e instanceof Error ? e.message : '불러오지 못했습니다.' }
    }
  }, [])

  const load = useCallback(async () => {
    setRows(targets.map((t) => ({ target: t, state: 'loading' })))
    try {
      setRows(await Promise.all(targets.map((t) => loadOne(t))))
    } catch (e: unknown) {
      report(e, '관리 열쇠 상태를 불러오지 못했습니다.')
    }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [loadOne, report])

  useEffect(() => { void load() }, [load])

  const replaceRow = (row: KeyRow) => setRows((rs) => rs.map((r) => (r.target === row.target ? row : r)))

  const save = async () => {
    if (!editing) return
    setSaving(true)
    setSaveError('')
    try {
      await callAdmin(editing, 'settings.setServerConfig', { key: MANAGEMENT_KEY.key, secretValue: value })
      replaceRow(await loadOne(editing))
      setEditing(null)
      setValue('')
    } catch (e: unknown) {
      if (e instanceof NotAuthenticatedError) { report(e, ''); return }
      setSaveError(e instanceof Error ? e.message : '열쇠를 저장하지 못했습니다.')
    } finally {
      setSaving(false)
    }
  }

  const syncNow = async (t: ProjectTarget) => {
    setSyncing(t)
    try {
      await callAdmin(t, 'settings.syncAuth')
    } catch (e: unknown) {
      if (e instanceof NotAuthenticatedError) { report(e, ''); return }
      // 실패 사유는 다시 읽은 반영 상태가 보인다.
    }
    try {
      replaceRow(await loadOne(t))
    } catch (e: unknown) {
      report(e, '')
    } finally {
      setSyncing(null)
    }
  }

  return (
    <WhiteCard className="p-5 space-y-5">
      <CardHeader
        title="관리 열쇠"
        description="프로젝트마다 Supabase 로그인 설정을 바꾸는 열쇠를 넣습니다."
        actions={<RefreshButton onClick={() => void load()} loading={rows.some((r) => r.state === 'loading')} />}
      />
      <div className="overflow-hidden rounded-md border border-neutral-200">
        <DataTable
          columns={[
            {
              key: 'project', header: '프로젝트', kind: 'name', cell: (r) => (
                <>
                  <div className="text-neutral-800">{look.hidden(r.target) ? '대기 중' : look.name(r.target)}</div>
                  <div className="text-xs text-neutral-400 font-mono">{refOf(r.target)}</div>
                </>
              ),
            },
            {
              key: 'key', header: '열쇠', kind: 'status', cell: (r) =>
                r.state === 'loading' ? <Loader2 className="w-3.5 h-3.5 animate-spin text-neutral-300" />
                  : r.state === 'blocked' ? <StatusText tone="muted">{r.error}</StatusText>
                  : r.key?.status === 'ok' ? <StatusText tone="ok">등록됨</StatusText>
                  : <StatusText tone="bad">없음</StatusText>,
            },
            {
              key: 'sync', header: '마지막 반영', kind: 'text',
              full: (r) => (r.sync?.ok === false ? r.sync.message ?? '반영 실패' : ''),
              cell: (r) => {
                if (r.state !== 'ready' || r.key?.status !== 'ok') return null
                if (!r.sync || r.sync.ok === null) return <span className="text-neutral-500">아직 반영 안 함</span>
                if (r.sync.ok === false) return <span className="text-red-600">{r.sync.message ?? '반영 실패'}</span>
                return (
                  <>
                    <div className="text-neutral-500">반영됨</div>
                    {r.sync.finishedAt && <div className="text-xs text-neutral-500">{formatDateTime(r.sync.finishedAt)}</div>}
                  </>
                )
              },
            },
            {
              key: 'actions', header: '', kind: 'actions', cell: (r) => {
                if (r.state !== 'ready') return null
                const hasKey = r.key?.status === 'ok'
                return (
                  <span className="inline-flex gap-1.5">
                    <Button size="sm" variant={hasKey ? 'outline' : 'primary'} className="w-[76px]"
                      onClick={() => { setValue(''); setSaveError(''); setEditing(r.target) }}>
                      {hasKey ? <Pencil className="w-3 h-3" /> : <Plus className="w-3 h-3" />}
                      {hasKey ? '바꾸기' : '입력'}
                    </Button>
                    <Button size="sm" className="w-[84px]" onClick={() => void syncNow(r.target)} disabled={!hasKey || syncing === r.target}>
                      {syncing === r.target ? <Loader2 className="w-3 h-3 animate-spin" /> : <RefreshCw className="w-3 h-3" />}
                      다시 반영
                    </Button>
                  </span>
                )
              },
            },
          ]}
          rows={rows}
          rowKey={(r) => r.target}
          emptyText="프로젝트가 없습니다."
        />
      </div>

      {editing && (
        <EditDialog
          title={`관리 열쇠 ${rows.find((r) => r.target === editing)?.key?.status === 'ok' ? '바꾸기' : '입력'}`}
          description={look.hidden(editing) ? `대기 중 · ${refOf(editing)}` : look.name(editing)}
          busy={saving}
          onClose={() => setEditing(null)}
          error={saveError || undefined}
          footer={<>
            <Button variant="outline" onClick={() => setEditing(null)} disabled={saving}>취소</Button>
            <Button variant="primary" onClick={() => void save()} disabled={saving || value.trim() === ''}>
              {saving && <Loader2 className="w-4 h-4 animate-spin" />}
              저장
            </Button>
          </>}
        >
          <div className="space-y-1.5">
            <KeyGuideToggle guide={MANAGEMENT_KEY.guide} />
            <input
              className={`${inputCls} w-full font-mono`}
              value={value}
              onChange={(e) => setValue(e.target.value)}
              spellCheck={false}
              autoComplete="off"
            />
            <p className="text-xs text-neutral-500">{MANAGEMENT_KEY.hint}</p>
          </div>
          <p className="text-sm text-neutral-600">저장하면 그 프로젝트의 로그인 설정에 바로 반영합니다. 저장한 값은 다시 볼 수 없습니다.</p>
        </EditDialog>
      )}
    </WhiteCard>
  )
}

/** 대기 프로젝트는 이름이 없어 Supabase 프로젝트 ref 로 가른다. */
function refOf(target: ProjectTarget): string {
  try {
    return new URL(PROJECTS.find((p) => p.key === target)?.url ?? '').hostname.split('.')[0] ?? target
  } catch {
    return target
  }
}
