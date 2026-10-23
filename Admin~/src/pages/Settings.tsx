import { useCallback, useEffect, useState } from 'react'
import { callAdmin, NotAuthenticatedError } from '../lib/api'
import type { ProjectTarget } from '../lib/projectTarget'
import { PageHeader } from '../components/ui/PageHeader'
import { ErrorBanner } from '../components/ui/ErrorBanner'
import { WhiteCard } from '../components/ui/Card'
import { ConfirmDialog } from '../components/ui/ConfirmDialog'
import { Button } from '../components/ui/Button'
import { formatDateTime } from '../components/ui/format'
import { Pencil, Trash2 } from 'lucide-react'
import { LimitsForm, inputCls, type LimitField } from './social/LimitsForm'

type StoreKeyStatus = {
  key: string
  source: 'console' | 'supabase' | null
  status: 'ok' | 'missing' | 'invalid'
  updatedAt: string | null
  updatedBy: string | null
}

// 화면에는 이 값이 없으면 무엇이 안 되는지만 적는다. 키 목록은 admin-api SERVER_CONFIG_VALID ·
// install.sql ts_server_config_keys() 와 같아야 한다(SdkAudit R16 이 대조).
const STORE_KEYS: { key: string; label: string; needFor: string; multiline?: boolean; hint: string }[] = [
  {
    key: 'APPLE_BUNDLE_ID', label: 'iOS 앱 번들 ID', needFor: 'iOS 결제 확인 · 애플 환불 기록',
    hint: '앱이 여럿이면 쉼표로 나눠 적습니다.',
  },
  {
    key: 'APPLE_SHARED_SECRET', label: 'App Store 앱 전용 공유 암호', needFor: 'iOS 옛 방식 결제 확인',
    hint: 'App Store Connect 앱 정보의 "앱 전용 공유 암호"입니다.',
  },
  {
    key: 'GOOGLE_SERVICE_ACCOUNT_JSON', label: 'Google Play 서비스 계정', needFor: 'Android 결제 확인 · 구글 환불 기록',
    multiline: true, hint: '서비스 계정 키 JSON 파일 내용 전체를 붙여 넣습니다.',
  },
  {
    key: 'APPLE_TEAM_ID', label: 'Apple 팀 ID', needFor: '탈퇴 시 애플 로그인 연결 해제',
    hint: '영문 대문자·숫자 10자입니다.',
  },
  {
    key: 'APPLE_KEY_ID', label: 'Sign in with Apple 키 ID', needFor: '탈퇴 시 애플 로그인 연결 해제',
    hint: '영문 대문자·숫자 10자입니다.',
  },
  {
    key: 'APPLE_PRIVATE_KEY', label: 'Sign in with Apple 키 파일', needFor: '탈퇴 시 애플 로그인 연결 해제',
    multiline: true, hint: '.p8 파일 내용 전체(-----BEGIN PRIVATE KEY----- 줄 포함)를 붙여 넣습니다.',
  },
]

const STATUS_VIEW: Record<StoreKeyStatus['status'], { text: string; cls: string }> = {
  ok: { text: '등록됨', cls: 'bg-green-50 text-green-700' },
  missing: { text: '없음', cls: 'bg-red-50 text-red-600' },
  invalid: { text: '형식 오류', cls: 'bg-amber-50 text-amber-700' },
}

function StoreStatusCard({
  target,
  report,
}: {
  target: ProjectTarget
  report: (e: unknown, fallback: string) => void
}) {
  const [rows, setRows] = useState<StoreKeyStatus[] | null>(null)
  const [editing, setEditing] = useState<{ key: string; clear: boolean } | null>(null)
  const [value, setValue] = useState('')
  const [saving, setSaving] = useState(false)
  const [savedKey, setSavedKey] = useState('')

  const load = useCallback(async () => {
    try {
      const res = await callAdmin<{ rows: StoreKeyStatus[] }>(target, 'settings.getStoreStatus', {})
      setRows(res.rows)
    } catch (e: unknown) {
      report(e, '스토어 연결 상태를 불러오지 못했습니다.')
    }
  }, [target, report])

  useEffect(() => {
    setRows(null)
    setSavedKey('')
    void load()
  }, [load])

  const byKey = new Map((rows ?? []).map((r) => [r.key, r]))
  const editingDef = STORE_KEYS.find((k) => k.key === editing?.key)

  const open = (key: string, clear: boolean) => {
    setValue('')
    setSavedKey('')
    setEditing({ key, clear })
  }

  const save = async () => {
    if (!editing) return
    setSaving(true)
    try {
      await callAdmin(target, 'settings.setServerConfig', { key: editing.key, secretValue: editing.clear ? '' : value })
      setSavedKey(editing.key)
      setEditing(null)
      setValue('')
      await load()
    } catch (e: unknown) {
      report(e, '값을 저장하지 못했습니다.')
    } finally {
      setSaving(false)
    }
  }

  return (
    <WhiteCard className="p-5 space-y-3">
      <div>
        <h2 className="text-sm font-medium text-neutral-900">스토어 연결</h2>
        <p className="mt-1 text-xs text-neutral-500">
          저장한 값은 다시 보이지 않습니다. 바꾸면 1분 안에 결제 확인·환불 기록·탈퇴 처리에 적용됩니다.
        </p>
      </div>
      <table className="w-full text-sm">
        <tbody>
          {STORE_KEYS.map((k) => {
            const row = byKey.get(k.key)
            const view = row ? STATUS_VIEW[row.status] : null
            return (
              <tr key={k.key} className="border-t border-neutral-100">
                <td className="py-2 pr-4">
                  <div className="text-neutral-800">{k.label}</div>
                  <div className="text-xs text-neutral-500">{k.needFor}</div>
                </td>
                <td className="py-2 pr-4 text-xs text-neutral-400">
                  {row?.source === 'console' && row.updatedAt && (
                    <span>수정 {formatDateTime(row.updatedAt)}{row.updatedBy ? ` · ${row.updatedBy}` : ''}</span>
                  )}
                  {row?.source === 'supabase' && <span>Supabase 에 등록된 값을 쓰는 중</span>}
                  {savedKey === k.key && <span className="ml-2 text-green-600 whitespace-nowrap">저장했습니다.</span>}
                </td>
                <td className="py-2 pr-3 text-right">
                  {view
                    ? <span className={`inline-flex rounded px-2 py-0.5 text-xs whitespace-nowrap ${view.cls}`}>{view.text}</span>
                    : <span className="text-xs text-neutral-400">-</span>}
                </td>
                <td className="py-2 text-right whitespace-nowrap">
                  <div className="inline-flex gap-1.5">
                    <Button size="sm" onClick={() => open(k.key, false)} disabled={!rows}>
                      <Pencil className="w-3 h-3" />
                      {row?.status === 'missing' ? '입력' : '바꾸기'}
                    </Button>
                    {row?.source === 'console' && (
                      <Button size="sm" variant="danger" onClick={() => open(k.key, true)}>
                        <Trash2 className="w-3 h-3" />
                        지우기
                      </Button>
                    )}
                  </div>
                </td>
              </tr>
            )
          })}
        </tbody>
      </table>

      <ConfirmDialog
        open={!!editing}
        title={editing?.clear ? `${editingDef?.label} 지우기` : `${editingDef?.label} ${byKey.get(editing?.key ?? '')?.status === 'missing' ? '입력' : '바꾸기'}`}
        description={editing?.clear
          ? '콘솔에 넣은 값을 지웁니다. Supabase 에 따로 등록된 값이 있으면 1분 안에 그 값으로 돌아가고, 없으면 이 값이 필요한 기능이 멈춥니다.'
          : '저장하면 1분 안에 적용됩니다. 저장한 값은 다시 볼 수 없습니다.'}
        confirmLabel={editing?.clear ? '지우기' : '저장'}
        danger={!!editing?.clear}
        busy={saving}
        confirmDisabled={!editing?.clear && value.trim() === ''}
        onConfirm={() => void save()}
        onCancel={() => setEditing(null)}
      >
        {editing && !editing.clear && editingDef && (
          <div className="space-y-1">
            {editingDef.multiline
              ? (
                <textarea
                  className="w-full h-40 px-3 py-2 rounded-md border border-neutral-200 font-mono text-xs outline-none focus:ring-2 focus:ring-[#1677ff]/30 focus:border-[#1677ff]"
                  value={value}
                  onChange={(e) => setValue(e.target.value)}
                  spellCheck={false}
                  autoComplete="off"
                />
              )
              : (
                <input
                  className={`${inputCls} w-full font-mono`}
                  value={value}
                  onChange={(e) => setValue(e.target.value)}
                  spellCheck={false}
                  autoComplete="off"
                />
              )}
            <p className="text-xs text-neutral-500">{editingDef.hint}</p>
          </div>
        )}
      </ConfirmDialog>
    </WhiteCard>
  )
}

const MAIL_FIELDS: LimitField[] = [
  {
    param: 'expiredRetentionDays', field: 'expired_retention_days', min: 0, max: 30,
    label: '만료 우편 보관 일수',
    hint: '기간이 지난 우편을 "기한 만료"로 남겨 두는 일수. 지나면 우편함에서 사라집니다. 0이면 기간이 지나는 즉시 사라집니다. 0~30 사이.',
  },
]

const FRIEND_FIELDS: LimitField[] = [
  {
    param: 'maxFriends', field: 'max_friends', min: 1, max: 1000,
    label: '친구 수 상한',
    hint: '한 계정이 가질 수 있는 친구의 최대 수. 상한에 닿으면 요청도 수락도 막힙니다. 1~1000 사이.',
  },
  {
    param: 'maxPendingSent', field: 'max_pending_sent', min: 1, max: 500,
    label: '보낸 요청 대기 상한',
    hint: '아직 상대가 응답하지 않은 요청의 최대 개수. 무차별 요청을 막습니다. 1~500 사이.',
  },
  {
    param: 'requestCooldownSeconds', field: 'request_cooldown_seconds', min: 0, max: 300,
    label: '연속 요청 최소 간격',
    hint: '같은 사람이 요청을 연달아 보낼 때 기다려야 하는 시간. 초 단위로 최대 300까지, 0이면 제한 없음.',
  },
]

const LOBBY_FIELDS: LimitField[] = [
  {
    param: 'maxPendingInvitesReceived', field: 'max_pending_invites_received', min: 1, max: 200,
    label: '받는 쪽 대기 초대 상한',
    hint: '한 사람이 동시에 받아 둘 수 있는 초대의 최대 개수. 넘으면 그 사람에게는 초대가 들어가지 않습니다. 1~200 사이.',
  },
  {
    param: 'inviteCooldownSeconds', field: 'invite_cooldown_seconds', min: 0, max: 300,
    label: '연속 초대 최소 간격',
    hint: '같은 사람이 초대를 연달아 보낼 때 기다려야 하는 시간. 초 단위로 최대 300까지, 0이면 제한 없음.',
  },
  {
    param: 'lobbyExpireMinutes', field: 'lobby_expire_minutes', min: 1, max: 1440,
    label: '방이 닫히기까지의 시간',
    hint: '아무도 시작하지 않은 방이 저절로 닫히기까지의 시간. 분 단위로 최대 1440까지.',
  },
]

type Tab = 'store' | 'mail' | 'friend' | 'lobby'

const TABS: readonly (readonly [Tab, string])[] = [
  ['store', '스토어 연결'],
  ['mail', '우편'],
  ['friend', '친구'],
  ['lobby', '매치 로비'],
]

export default function Settings({
  target,
  onUnauthenticated,
}: {
  target: ProjectTarget
  onUnauthenticated: () => void
}) {
  const [error, setError] = useState('')
  const [tab, setTab] = useState<Tab>('store')

  const report = useCallback((e: unknown, fallback: string) => {
    if (e instanceof NotAuthenticatedError) { onUnauthenticated(); return }
    setError(e instanceof Error ? e.message : fallback)
  }, [onUnauthenticated])

  return (
    <div className="space-y-4">
      <PageHeader title="설정" description="게임 기능의 제한값과 동작 값을 조정합니다." />
      <ErrorBanner message={error} onDismiss={() => setError('')} />

      <div className="border-b border-neutral-200 flex gap-1">
        {TABS.map(([key, label]) => (
          <button
            key={key}
            onClick={() => setTab(key)}
            className={[
              'h-10 px-4 text-sm border-b-2 -mb-px transition-colors whitespace-nowrap',
              tab === key
                ? 'border-[#1677ff] text-[#1677ff] font-medium'
                : 'border-transparent text-neutral-600 hover:text-neutral-900',
            ].join(' ')}
          >
            {label}
          </button>
        ))}
      </div>

      {tab === 'store' && <StoreStatusCard target={target} report={report} />}
      {tab === 'mail' && (
        <LimitsForm
          target={target}
          report={report}
          title="우편"
          getAction="mails.settingsGet"
          updateAction="mails.settingsUpdate"
          fields={MAIL_FIELDS}
          confirmTitle="우편 설정을 바꿀까요?"
        />
      )}
      {tab === 'friend' && (
        <LimitsForm
          target={target}
          report={report}
          title="친구"
          getAction="friends.settingsGet"
          updateAction="friends.settingsUpdate"
          fields={FRIEND_FIELDS}
          confirmTitle="친구 설정을 바꿀까요?"
        />
      )}
      {tab === 'lobby' && (
        <LimitsForm
          target={target}
          report={report}
          title="매치 로비"
          getAction="matchLobby.settingsGet"
          updateAction="matchLobby.settingsUpdate"
          fields={LOBBY_FIELDS}
          confirmTitle="매치 로비 설정을 바꿀까요?"
        />
      )}
    </div>
  )
}
