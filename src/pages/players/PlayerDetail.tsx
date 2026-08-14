import { useEffect, useState, useCallback } from 'react'
import type { ReactNode } from 'react'
import { ArrowLeft, Pencil, Check, X, Loader2 } from 'lucide-react'
import { callAdmin, NotAuthenticatedError } from '../../lib/api'
import type { ProjectTarget } from '../../lib/projectTarget'
import { WhiteCard } from '../../components/ui/Card'
import { formatDateTime, formatKRW, formatBanUntil } from '../../components/ui/format'
import { ConfirmDialog } from '../../components/ui/ConfirmDialog'
import { ErrorBanner } from '../../components/ui/ErrorBanner'
import UserDataTab from './UserDataTab'

type Tab = 'info' | 'userData'

const PERMANENT_UNTIL = '2999-12-31T23:59:59.000Z'

type Profile = {
  account_id: string
  display_name: string
  is_guest: boolean | null
  platform: string | null
  country_code: string | null
  total_paid_krw: string | number | null
  created_at: string | null
  last_activity_at: string | null
  last_sign_in_at: string | null
}

type BanInfo = {
  bannedUntil: string | null
  banMessage: string | null
  isBanned: boolean
}

function isPermanent(iso: string | null): boolean {
  if (!iso) return false
  const d = new Date(iso)
  return !Number.isNaN(d.getTime()) && d.getFullYear() >= 2900
}

export default function PlayerDetail({
  target,
  onUnauthenticated,
  accountId,
  initialName,
  onBack,
}: {
  target: ProjectTarget
  onUnauthenticated: () => void
  accountId: string
  initialName: string
  onBack: () => void
}) {
  const [tab, setTab] = useState<Tab>('info')
  const [profile, setProfile] = useState<Profile | null>(null)
  const [profileLoading, setProfileLoading] = useState(true)
  const [ban, setBan] = useState<BanInfo | null>(null)
  const [banLoading, setBanLoading] = useState(false)

  const [editingName, setEditingName] = useState(false)
  const [nameDraft, setNameDraft] = useState(initialName)
  const [savingName, setSavingName] = useState(false)
  const [banMessage, setBanMessage] = useState('')
  const [banUntilInput, setBanUntilInput] = useState('')
  const [permanent, setPermanent] = useState(false)
  const [applying, setApplying] = useState(false)
  const [askUnban, setAskUnban] = useState(false)
  const [unbanLoading, setUnbanLoading] = useState(false)
  const [error, setError] = useState('')

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

  const reloadProfile = useCallback(async () => {
    setProfileLoading(true)
    try {
      setProfile(await callAdmin<Profile>(target, 'players.profile', { accountId }))
    } catch (e: unknown) {
      report(e, '프로필을 불러오지 못했습니다.')
    } finally {
      setProfileLoading(false)
    }
  }, [target, accountId, report])

  const reloadBan = useCallback(async () => {
    setBanLoading(true)
    try {
      setBan(await callAdmin<BanInfo>(target, 'players.banInfo', { accountId }))
    } catch (e: unknown) {
      report(e, '차단 정보를 불러오지 못했습니다.')
    } finally {
      setBanLoading(false)
    }
  }, [target, accountId, report])

  useEffect(() => {
    void reloadProfile()
    void reloadBan()
  }, [reloadProfile, reloadBan])

  useEffect(() => {
    if (profile?.display_name) setNameDraft(profile.display_name)
  }, [profile?.display_name])

  const handleNameSave = async () => {
    if (!nameDraft.trim() || savingName) return
    setSavingName(true)
    setError('')
    try {
      await callAdmin(target, 'players.setDisplayName', { accountId, displayName: nameDraft.trim() })
      setEditingName(false)
      await reloadProfile()
    } catch (e: unknown) {
      report(e, '닉네임 변경에 실패했습니다.')
    } finally {
      setSavingName(false)
    }
  }

  const handleBan = async () => {
    setError('')
    const until = permanent
      ? PERMANENT_UNTIL
      : banUntilInput
        ? new Date(banUntilInput).toISOString()
        : ''
    if (!until) {
      setError('종료 기한을 입력하거나 영구 차단을 선택하세요.')
      return
    }
    setApplying(true)
    try {
      await callAdmin(target, 'players.ban', { accountId, bannedUntil: until, banMessage })
      setBanMessage('')
      setBanUntilInput('')
      setPermanent(false)
      await reloadBan()
    } catch (e: unknown) {
      report(e, '차단에 실패했습니다.')
    } finally {
      setApplying(false)
    }
  }

  const confirmUnban = async () => {
    if (applying || unbanLoading) return
    setApplying(true)
    setUnbanLoading(true)
    try {
      await callAdmin(target, 'players.unban', { accountId })
      setAskUnban(false)
      await reloadBan()
    } catch (e: unknown) {
      report(e, '차단 해제에 실패했습니다.')
    } finally {
      setApplying(false)
      setUnbanLoading(false)
    }
  }

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h2 className="text-2xl font-semibold text-neutral-900">플레이어</h2>
        <button
          onClick={onBack}
          className="inline-flex items-center gap-1.5 h-9 px-3 rounded-md border border-neutral-300 bg-white text-sm hover:bg-neutral-50"
        >
          <ArrowLeft className="w-3.5 h-3.5" />
          플레이어 목록
        </button>
      </div>

      <div className="border-b border-neutral-200 flex gap-1">
        {(
          [
            ['info', '기본 정보'],
            ['userData', '유저 데이터'],
          ] as const
        ).map(([key, label]) => (
          <button
            key={key}
            onClick={() => setTab(key)}
            className={[
              'h-10 px-4 text-sm border-b-2 -mb-px transition-colors',
              tab === key
                ? 'border-[#1677ff] text-[#1677ff] font-medium'
                : 'border-transparent text-neutral-600 hover:text-neutral-900',
            ].join(' ')}
          >
            {label}
          </button>
        ))}
      </div>

      {tab === 'userData' ? (
        <UserDataTab target={target} onUnauthenticated={onUnauthenticated} accountId={accountId} />
      ) : (
      <>
      <ErrorBanner message={error} onDismiss={() => setError('')} />

      {profileLoading && !profile ? (
        <WhiteCard className="p-10 text-center">
          <Loader2 className="w-6 h-6 animate-spin text-neutral-300 mx-auto" />
        </WhiteCard>
      ) : (
      <>
      <WhiteCard className="p-5 space-y-3">
        <h3 className="text-base font-semibold mb-2">기본 정보</h3>
        <InfoRow label="ID">
          <span className="font-mono text-xs">{accountId}</span>
        </InfoRow>
        <InfoRow label="닉네임">
          {editingName ? (
            <div className="flex items-center gap-2">
              <input
                value={nameDraft}
                onChange={(e) => setNameDraft(e.target.value)}
                className="h-8 px-2 rounded border border-neutral-300 text-sm focus:outline-none focus:ring-2 focus:ring-[#1677ff]/30 focus:border-[#1677ff]"
              />
              <button
                onClick={handleNameSave}
                disabled={savingName}
                className="inline-flex items-center justify-center w-7 h-7 rounded bg-[#1677ff] text-white hover:bg-[#1677ff]/90 disabled:opacity-60"
                aria-label="확인"
              >
                <Check className="w-3.5 h-3.5" />
              </button>
              <button
                onClick={() => {
                  setEditingName(false)
                  setNameDraft(profile?.display_name ?? initialName)
                }}
                className="inline-flex items-center justify-center w-7 h-7 rounded border border-neutral-300 text-neutral-500 hover:bg-neutral-50"
                aria-label="취소"
              >
                <X className="w-3.5 h-3.5" />
              </button>
            </div>
          ) : (
            <div className="flex items-center gap-2">
              <span>{profile?.display_name ?? initialName}</span>
              <button
                onClick={() => setEditingName(true)}
                className="text-neutral-400 hover:text-[#1677ff]"
                aria-label="닉네임 수정"
              >
                <Pencil className="w-3.5 h-3.5" />
              </button>
            </div>
          )}
        </InfoRow>
        <InfoRow label="플랫폼">{profile?.platform ?? '-'}</InfoRow>
        <InfoRow label="국가">{profile?.country_code ?? '-'}</InfoRow>
        <InfoRow label="총 결제금액">{formatKRW(profile?.total_paid_krw ?? null)}</InfoRow>
        <InfoRow label="마지막 로그인">{formatDateTime(profile?.last_sign_in_at ?? null)}</InfoRow>
        <InfoRow label="마지막 활동">{formatDateTime(profile?.last_activity_at ?? null)}</InfoRow>
      </WhiteCard>

      <WhiteCard className="relative overflow-hidden p-5 space-y-3">
        <div className="flex items-center justify-between">
          <h3 className="text-base font-semibold">차단 정보</h3>
          {ban?.isBanned ? (
            <button
              onClick={() => setAskUnban(true)}
              disabled={applying || unbanLoading}
              className="h-9 px-4 rounded-md bg-[#1677ff] text-white text-sm hover:bg-[#1677ff]/90 disabled:opacity-60"
            >
              {applying || unbanLoading ? '처리 중…' : '차단 해제'}
            </button>
          ) : (
            <button
              onClick={handleBan}
              disabled={applying}
              className="h-9 px-4 rounded-md bg-[#1677ff] text-white text-sm hover:bg-[#1677ff]/90 disabled:opacity-60"
            >
              {applying ? '처리 중…' : '차단'}
            </button>
          )}
        </div>

        <div className="text-sm">
          현재 상태:{' '}
          {banLoading && !ban ? (
            <span className="text-neutral-400">확인 중…</span>
          ) : ban?.isBanned ? (
            <span className="text-red-600 font-medium">
              차단{isPermanent(ban.bannedUntil) ? ' (영구)' : ban.bannedUntil ? ` (${formatBanUntil(ban.bannedUntil)}까지)` : ''}
            </span>
          ) : (
            <span className="text-emerald-600 font-medium">정상</span>
          )}
        </div>

        {!ban?.isBanned && (
          <>
            <div className="flex flex-wrap items-end gap-6">
              <div className="w-64">
                <label className="block text-xs text-neutral-500 mb-1">종료 기한</label>
                <input
                  type="datetime-local"
                  value={banUntilInput}
                  onChange={(e) => setBanUntilInput(e.target.value)}
                  disabled={permanent}
                  className="w-full h-9 px-3 rounded-md border border-neutral-300 text-sm disabled:bg-neutral-100 disabled:text-neutral-400 focus:outline-none focus:ring-2 focus:ring-[#1677ff]/30 focus:border-[#1677ff]"
                />
              </div>
              <div className="flex items-center gap-2 pb-1.5">
                <span className="text-xs text-neutral-500">영구 차단</span>
                <button
                  type="button"
                  role="switch"
                  aria-checked={permanent}
                  onClick={() => setPermanent((v) => !v)}
                  className={`relative inline-flex h-5 w-9 items-center rounded-full transition-colors ${permanent ? 'bg-[#1677ff]' : 'bg-neutral-300'}`}
                >
                  <span className={`inline-block h-4 w-4 transform rounded-full bg-white transition-transform ${permanent ? 'translate-x-4' : 'translate-x-0.5'}`} />
                </button>
              </div>
            </div>

            <div>
              <label className="block text-xs text-neutral-500 mb-1">차단 사유/메시지</label>
              <textarea
                value={banMessage}
                onChange={(e) => setBanMessage(e.target.value)}
                rows={4}
                placeholder="차단된 유저에게 보여줄 메시지를 입력하세요"
                className="w-full px-3 py-2 rounded-md border border-neutral-300 text-sm focus:outline-none focus:ring-2 focus:ring-[#1677ff]/30 focus:border-[#1677ff]"
              />
            </div>
          </>
        )}

        {applying && (
          <div className="absolute inset-0 z-10 flex items-center justify-center bg-white/70 backdrop-blur-[1px] transition-opacity">
            <div className="flex items-center gap-2 text-sm font-medium text-neutral-600">
              <Loader2 className="w-4 h-4 animate-spin" />
              적용 중…
            </div>
          </div>
        )}
      </WhiteCard>
      </>
      )}
      </>
      )}

      <ConfirmDialog
        open={askUnban}
        title="차단 풀기"
        confirmLabel="차단 풀기"
        busy={applying || unbanLoading}
        onConfirm={confirmUnban}
        onCancel={() => setAskUnban(false)}
        description="바로 다시 접속할 수 있게 됩니다. 필요하면 다시 차단할 수 있습니다."
      >
        <div className="rounded-md bg-neutral-50 border border-neutral-200 px-3 py-2 text-sm">
          <div className="text-neutral-800">{initialName}</div>
          <div className="text-xs text-neutral-500 mt-1 font-mono">{accountId.slice(0, 8)}</div>
        </div>
      </ConfirmDialog>
    </div>
  )
}

function InfoRow({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="flex items-center justify-between border-b border-neutral-100 pb-2 last:border-b-0 last:pb-0">
      <span className="text-xs text-neutral-500">{label}</span>
      <span className="text-sm text-neutral-800">{children}</span>
    </div>
  )
}
