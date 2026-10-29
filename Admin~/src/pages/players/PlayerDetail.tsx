import { useEffect, useState, useCallback } from 'react'
import type { ReactNode } from 'react'
import { useNavigate } from 'react-router-dom'
import { ArrowLeft, Pencil, Check, X, Loader2, Send } from 'lucide-react'
import { callAdmin, NotAuthenticatedError } from '../../lib/api'
import type { ProjectTarget } from '../../lib/projectTarget'
import { useServerName } from '../../lib/serverNames'
import { platformLabel } from '../../lib/platforms'
import { CardHeader, WhiteCard } from '../../components/ui/Card'
import { areaCls, inputCls, inputCompactCls } from '../../components/ui/Input'
import { formatDateTime, formatKRW, formatBanUntil, toDatetimeLocal } from '../../components/ui/format'
import { PageHeader } from '../../components/ui/PageHeader'
import { ConfirmDialog } from '../../components/ui/ConfirmDialog'
import { ErrorBanner } from '../../components/ui/ErrorBanner'
import { Button } from '../../components/ui/Button'
import { Switch } from '../../components/ui/Switch'
import { Tabs } from '../../components/ui/Tabs'
import UserDataTab from './UserDataTab'
import FriendsTab from './FriendsTab'
import MatchRecordsTab from './MatchRecordsTab'
import MailsTab from './MailsTab'
import CouponsTab from './CouponsTab'

type Tab = 'info' | 'userData' | 'mails' | 'coupons' | 'friends' | 'matches'

// 인증 서버는 차단 기간 상한이 있어 "영구"는 100년으로 넣는다(서버 admin-api 도 같은 상한으로 자른다).
const PERMANENT_YEARS = 100

function permanentUntil(): string {
  const d = new Date()
  d.setFullYear(d.getFullYear() + PERMANENT_YEARS)
  return d.toISOString()
}

type Profile = {
  account_id: string
  display_name: string
  is_guest: boolean | null
  platform: string | null
  signup_platform?: string | null
  country_code: string | null
  total_paid_krw: string | number | null
  refund_count: number | null
  refund_krw: string | number | null
  created_at: string | null
  last_activity_at: string | null
  last_sign_in_at: string | null
  withdrawn_at: string | null
  server_code: string | null
}

type BanInfo = {
  bannedUntil: string | null
  banMessage: string | null
  isBanned: boolean
}

function isPermanent(iso: string | null): boolean {
  if (!iso) return false
  const d = new Date(iso)
  // 영구 차단은 지금부터 100년이라 시간이 지나면 남은 기간이 줄어든다 — 50년 넘게 남았으면 영구로 본다.
  return !Number.isNaN(d.getTime()) && d.getFullYear() >= new Date().getFullYear() + 50
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
  const navigate = useNavigate()
  const serverName = useServerName(target)
  const [tab, setTab] = useState<Tab>('info')
  const [profile, setProfile] = useState<Profile | null>(null)
  const [profileLoading, setProfileLoading] = useState(true)
  const [ban, setBan] = useState<BanInfo | null>(null)
  // 첫 조회 전에도 "불러오는 중"이어야 한다 — false 로 시작하면 잠깐 "정상"이 보인다.
  const [banLoading, setBanLoading] = useState(true)
  // 차단 정보를 못 읽었으면 "정상"으로 보이면 안 된다 — 차단 중인 플레이어를 정상으로 믿고 다시 차단하게 된다.
  const [banFailed, setBanFailed] = useState(false)

  const [editingName, setEditingName] = useState(false)
  const [nameDraft, setNameDraft] = useState(initialName)
  const [savingName, setSavingName] = useState(false)
  const [banMessage, setBanMessage] = useState('')
  const [banUntilInput, setBanUntilInput] = useState('')
  const [permanent, setPermanent] = useState(false)
  const [applying, setApplying] = useState(false)
  const [askUnban, setAskUnban] = useState(false)
  const [askBan, setAskBan] = useState(false)
  const [unbanLoading, setUnbanLoading] = useState(false)
  const [error, setError] = useState('')
  const [banInputError, setBanInputError] = useState('')
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
    setBanFailed(false)
    try {
      setBan(await callAdmin<BanInfo>(target, 'players.banInfo', { accountId }))
    } catch (e: unknown) {
      setBan(null)
      setBanFailed(true)
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

  // 손으로 친 다섯 자리 연도처럼 날짜로 못 바꾸는 값은 toISOString 이 예외를 던져 화면이 깨진다 — 빈 값으로 받고 입력 오류로 알린다.
  const typedUntil = banUntilInput ? new Date(banUntilInput) : null
  const typedValid = !!typedUntil && !Number.isNaN(typedUntil.getTime()) && typedUntil.getFullYear() <= 9999
  const banUntil = permanent ? permanentUntil() : typedValid ? typedUntil.toISOString() : ''

  const requestBan = () => {
    setError('')
    setBanInputError('')
    if (!permanent && banUntilInput && !typedValid) {
      setBanInputError('종료 기한을 날짜로 읽을 수 없습니다. 다시 입력하세요.')
      return
    }
    if (!banUntil) {
      setBanInputError('종료 기한을 입력하거나 영구 차단을 선택하세요.')
      return
    }
    // 지난 시각은 서버가 "차단 없음"으로 받아 성공으로 끝난다 — 차단했다고 믿게 되므로 보내기 전에 막는다.
    if (!permanent && new Date(banUntil).getTime() <= Date.now()) {
      setBanInputError('종료 기한이 이미 지났습니다. 지금 이후 시각을 넣으세요.')
      return
    }
    setDialogError('')
    setAskBan(true)
  }

  const handleBan = async () => {
    if (!banUntil || applying) return
    setApplying(true)
    setDialogError('')
    try {
      await callAdmin(target, 'players.ban', { accountId, bannedUntil: banUntil, banMessage })
      setAskBan(false)
      setBanMessage('')
      setBanUntilInput('')
      setPermanent(false)
      await reloadBan()
    } catch (e: unknown) {
      if (e instanceof NotAuthenticatedError) { onUnauthenticated(); return }
      setDialogError(e instanceof Error ? e.message : '차단에 실패했습니다.')
    } finally {
      setApplying(false)
    }
  }

  const confirmUnban = async () => {
    if (applying || unbanLoading) return
    setApplying(true)
    setUnbanLoading(true)
    setDialogError('')
    try {
      await callAdmin(target, 'players.unban', { accountId })
      setAskUnban(false)
      await reloadBan()
    } catch (e: unknown) {
      if (e instanceof NotAuthenticatedError) { onUnauthenticated(); return }
      setDialogError(e instanceof Error ? e.message : '차단 해제에 실패했습니다.')
    } finally {
      setApplying(false)
      setUnbanLoading(false)
    }
  }

  const shownName = profile?.display_name || initialName
  const cancelNameEdit = () => {
    setEditingName(false)
    setNameDraft(profile?.display_name ?? initialName)
  }

  return (
    <div className="space-y-4">
      <PageHeader
        title={shownName ? `플레이어 · ${shownName}` : '플레이어'}
        actions={
          <div className="flex items-center gap-2">
            <Button
              onClick={() => {
                navigate(`/mails/send?account=${encodeURIComponent(accountId)}&name=${encodeURIComponent(shownName)}`)
              }}
            >
              <Send className="w-3.5 h-3.5" />
              우편 보내기
            </Button>
            <Button onClick={onBack}>
              <ArrowLeft className="w-3.5 h-3.5" />
              플레이어 목록
            </Button>
          </div>
        }
      />

      <Tabs
        value={tab}
        onChange={setTab}
        tabs={[
          ['info', '기본 정보'],
          ['userData', '유저 데이터'],
          ['mails', '받은 우편'],
          ['coupons', '쿠폰 사용'],
          ['friends', '친구'],
          ['matches', '대전 기록'],
        ]}
      />

      {tab === 'userData' ? (
        <UserDataTab
          target={target}
          onUnauthenticated={onUnauthenticated}
          accountId={accountId}
          displayName={profile?.display_name ?? initialName}
        />
      ) : tab === 'mails' ? (
        <MailsTab target={target} onUnauthenticated={onUnauthenticated} accountId={accountId} />
      ) : tab === 'coupons' ? (
        <CouponsTab target={target} onUnauthenticated={onUnauthenticated} accountId={accountId} />
      ) : tab === 'friends' ? (
        <FriendsTab
          target={target}
          onUnauthenticated={onUnauthenticated}
          accountId={accountId}
        />
      ) : tab === 'matches' ? (
        <MatchRecordsTab
          target={target}
          onUnauthenticated={onUnauthenticated}
          accountId={accountId}
        />
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
        <CardHeader title="기본 정보" description="계정 · 결제 · 접속 기록 요약" />
        <InfoRow label="ID">
          <span className="font-mono text-xs">{accountId}</span>
        </InfoRow>
        <InfoRow label="닉네임">
          {editingName ? (
            <div className="flex items-center gap-2">
              <input
                value={nameDraft}
                onChange={(e) => setNameDraft(e.target.value)}
                onKeyDown={(e) => {
                  if (e.key === 'Enter') void handleNameSave()
                  else if (e.key === 'Escape') cancelNameEdit()
                }}
                autoFocus
                className={inputCompactCls}
              />
              {/* 줄 안 편집 확인·취소 — 아이템 카탈로그·우편 분류와 같은 꼴 */}
              <button
                onClick={() => void handleNameSave()}
                disabled={savingName || !nameDraft.trim()}
                className="text-emerald-600 hover:text-emerald-700 disabled:opacity-50"
                aria-label="저장"
                title="저장"
              >
                {savingName ? <Loader2 className="w-4 h-4 animate-spin" /> : <Check className="w-4 h-4" />}
              </button>
              <button
                onClick={cancelNameEdit}
                disabled={savingName}
                className="text-neutral-400 hover:text-neutral-700 disabled:opacity-50"
                aria-label="취소"
                title="취소"
              >
                <X className="w-4 h-4" />
              </button>
            </div>
          ) : (
            <div className="flex items-center gap-2">
              <span>{profile?.display_name ?? initialName}</span>
              <button
                onClick={() => setEditingName(true)}
                className="text-neutral-400 hover:text-[#1677ff]"
                aria-label="닉네임 수정"
                title="닉네임 수정"
              >
                <Pencil className="w-3.5 h-3.5" />
              </button>
            </div>
          )}
        </InfoRow>
        <InfoRow label="서버">{serverName(profile?.server_code)}</InfoRow>
        <InfoRow label="탈퇴">{withdrawalText(profile?.withdrawn_at ?? null)}</InfoRow>
        <InfoRow label="가입 플랫폼">{!profile ? '-' : platformLabel(profile.signup_platform) || '알 수 없음'}</InfoRow>
        <InfoRow label="최근 플랫폼">{platformLabel(profile?.platform) || '-'}</InfoRow>
        <InfoRow label="국가">{profile?.country_code ?? '-'}</InfoRow>
        <InfoRow label="총 결제금액">{profile ? formatKRW(profile.total_paid_krw ?? null) : '-'}</InfoRow>
        <InfoRow label="환불">
          {!profile
            ? '-'
            : profile.refund_count
              ? <span className="text-red-600">{profile.refund_count}건 · {formatKRW(profile.refund_krw ?? null)}</span>
              : '없음'}
        </InfoRow>
        <InfoRow label="마지막 로그인">{formatDateTime(profile?.last_sign_in_at ?? null)}</InfoRow>
        <InfoRow label="마지막 활동">{formatDateTime(profile?.last_activity_at ?? null)}</InfoRow>
      </WhiteCard>

      <WhiteCard className="relative overflow-hidden p-5 space-y-3">
        <CardHeader
          title="차단 정보"
          description="게임 접속 차단 여부와 차단 메시지"
          actions={!ban ? null : ban.isBanned ? (
            <Button variant="primary" onClick={() => { setDialogError(''); setAskUnban(true) }} disabled={applying || unbanLoading}>
              {applying || unbanLoading ? '처리 중…' : '차단 해제'}
            </Button>
          ) : (
            <Button variant="primary" onClick={requestBan} disabled={applying}>
              {applying ? '처리 중…' : '차단'}
            </Button>
          )}
        />

        <div className="text-sm">
          현재 상태:{' '}
          {banLoading && !ban ? (
            <span className="text-neutral-400">확인 중…</span>
          ) : banFailed || !ban ? (
            <span className="text-red-600">
              차단 상태를 확인하지 못했습니다.{' '}
              <button onClick={() => void reloadBan()} className="underline hover:text-red-700">다시 확인</button>
            </span>
          ) : ban.isBanned ? (
            <span className="text-red-600 font-medium">
              {isPermanent(ban.bannedUntil) ? '영구 차단' : ban.bannedUntil ? `${formatBanUntil(ban.bannedUntil)}까지 차단` : '차단'}
            </span>
          ) : (
            <span className="text-emerald-600 font-medium">정상</span>
          )}
        </div>

        {ban?.isBanned && (
          <div className="text-sm text-neutral-700 whitespace-pre-wrap">
            차단 메시지: {ban.banMessage?.trim() ? ban.banMessage : <span className="text-neutral-400">없음</span>}
          </div>
        )}

        {ban && !ban.isBanned && (
          <>
            <div className="flex flex-wrap items-end gap-6">
              <div className="w-64">
                <label className="block text-xs text-neutral-500 mb-1">종료 기한</label>
                <input
                  type="datetime-local"
                  value={banUntilInput}
                  min={toDatetimeLocal(new Date().toISOString())}
                  max="9999-12-31T23:59"
                  onChange={(e) => { setBanUntilInput(e.target.value); setBanInputError('') }}
                  disabled={permanent}
                  className={inputCls + ' w-full disabled:bg-neutral-100 disabled:text-neutral-400'}
                />
              </div>
              <div className="flex items-center gap-2 pb-1.5">
                <span className="text-xs text-neutral-500">영구 차단</span>
                <Switch value={permanent} onChange={(v) => { setPermanent(v); setBanInputError('') }} />
              </div>
            </div>
            {banInputError && <p className="text-xs text-red-600">{banInputError}</p>}

            <div>
              <label className="block text-xs text-neutral-500 mb-1">차단 메시지</label>
              <textarea
                value={banMessage}
                onChange={(e) => setBanMessage(e.target.value)}
                rows={4}
                className={areaCls + ' w-full'}
              />
              <p className="mt-1 text-xs text-neutral-400">차단된 플레이어가 게임에서 보게 됩니다.</p>
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
        open={askBan}
        title="플레이어 차단"
        confirmLabel="차단"
        danger
        busy={applying}
        error={askBan ? dialogError : ''}
        onConfirm={() => void handleBan()}
        onCancel={() => setAskBan(false)}
        description="차단하면 바로 접속이 끊기고 기한까지 다시 들어올 수 없습니다."
      >
        <div className="rounded-md bg-neutral-50 border border-neutral-200 px-3 py-2 text-sm space-y-1">
          <div className="text-neutral-800">{shownName}</div>
          <div className="text-xs text-neutral-500 font-mono">{accountId.slice(0, 8)}</div>
          <div className="text-xs text-neutral-500">
            {permanent ? '영구 차단' : `${formatBanUntil(banUntil)}까지`}
          </div>
        </div>
      </ConfirmDialog>

      <ConfirmDialog
        open={askUnban}
        title="차단 해제"
        confirmLabel="차단 해제"
        busy={applying || unbanLoading}
        error={askUnban ? dialogError : ''}
        onConfirm={() => void confirmUnban()}
        onCancel={() => setAskUnban(false)}
        description="바로 다시 접속할 수 있게 됩니다. 필요하면 다시 차단할 수 있습니다."
      >
        <div className="rounded-md bg-neutral-50 border border-neutral-200 px-3 py-2 text-sm">
          <div className="text-neutral-800">{shownName}</div>
          <div className="text-xs text-neutral-500 mt-1 font-mono">{accountId.slice(0, 8)}</div>
        </div>
      </ConfirmDialog>
    </div>
  )
}

function withdrawalText(withdrawnAt: string | null): ReactNode {
  if (!withdrawnAt) return '-'
  return new Date(withdrawnAt).getTime() > Date.now()
    ? <span className="text-red-600 font-medium">탈퇴 예약 · {formatDateTime(withdrawnAt)}에 삭제</span>
    : <span className="text-red-600 font-medium">탈퇴 처리 대기 · 곧 삭제</span>
}

function InfoRow({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="flex items-center justify-between border-b border-neutral-100 pb-2 last:border-b-0 last:pb-0">
      <span className="text-xs text-neutral-500">{label}</span>
      <span className="text-sm text-neutral-800">{children}</span>
    </div>
  )
}
