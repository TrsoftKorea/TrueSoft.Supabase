import { useCallback, useEffect, useRef, useState } from 'react'
import { callAdmin, NotAuthenticatedError } from '../lib/api'
import { getProject, PROJECTS, type ProjectTarget } from '../lib/projectTarget'
import { setProjectLook, toProjectLook, useProjectLook } from '../lib/projectLook'
import { ProjectIcon } from '../components/ui/ProjectIcon'
import { PageHeader } from '../components/ui/PageHeader'
import { ErrorBanner } from '../components/ui/ErrorBanner'
import { WhiteCard } from '../components/ui/Card'
import { ConfirmDialog } from '../components/ui/ConfirmDialog'
import { Button } from '../components/ui/Button'
import { formatDateTime } from '../components/ui/format'
import { colorDistance, contrastWithWhite } from '../lib/color'
import { forgetGameTimeZone, TIME_ZONES, type GameSettings } from '../lib/gameTime'
import { ChevronDown, ChevronUp, CircleHelp, ExternalLink, Pencil, Trash2 } from 'lucide-react'
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
/**
 * 값을 가져오는 방법. 발급처 화면은 영어라 메뉴·버튼 이름은 그 화면에 보이는 영어 그대로 따옴표로 적는다.
 * 각 단계는 2026-10-28 애플·구글 공식 도움말로 확인했다. 애플 로그인 키의 "Configure → Primary App ID" 만은
 * 그 도움말 페이지가 열리지 않아 일반 키 만들기 안내와 기억으로 적었다 — 화면이 다르면 여기를 고친다.
 */
type KeyGuide = { origin: string; site: { name: string; url: string }; steps: string[]; notes?: string[] }

const STORE_KEYS: { key: string; label: string; needFor: string; multiline?: boolean; hint: string; guide: KeyGuide }[] = [
  {
    key: 'APPLE_BUNDLE_ID', label: 'iOS 앱 번들 ID', needFor: 'iOS 결제 확인 · 애플 환불 기록',
    hint: '앱이 여럿이면 쉼표로 나눠 적습니다.',
    guide: {
      origin: 'Bundle ID',
      site: { name: 'App Store Connect', url: 'https://appstoreconnect.apple.com/apps' },
      steps: [
        '"Apps"에서 게임 앱을 고릅니다.',
        '왼쪽 "General" 아래 "App Information"을 엽니다.',
        '"Bundle ID"에 보이는 값을 복사합니다. 예: com.회사이름.게임이름',
      ],
      notes: ['유니티 Player Settings의 "Bundle Identifier"와 같은 값입니다.'],
    },
  },
  {
    key: 'APPLE_SHARED_SECRET', label: 'App Store 앱 전용 공유 암호', needFor: 'iOS 옛 방식 결제 확인',
    hint: '복사한 값을 그대로 붙여 넣습니다.',
    guide: {
      origin: 'App-Specific Shared Secret',
      site: { name: 'App Store Connect', url: 'https://appstoreconnect.apple.com/apps' },
      steps: [
        '"Apps"에서 게임 앱을 고릅니다.',
        '왼쪽 "General" 아래 "App Information"을 엽니다.',
        '"App-Specific Shared Secret" 칸의 "Manage"를 누릅니다.',
        '처음이면 "Generate"를 누르고, 이미 있으면 보이는 값을 그대로 복사합니다.',
        '"Done"을 눌러 닫습니다.',
      ],
      notes: [
        '"Regenerate"는 누르지 마세요. 누르는 순간 예전 값이 바로 못 쓰게 됩니다.',
        'App Store Connect 계정 소유자(Account Holder)나 관리자(Admin)만 볼 수 있습니다.',
        '새로 만든 값은 적용까지 최대 24시간 걸릴 수 있습니다.',
      ],
    },
  },
  {
    key: 'GOOGLE_SERVICE_ACCOUNT_JSON', label: 'Google Play 서비스 계정', needFor: 'Android 결제 확인 · 구글 환불 기록',
    multiline: true, hint: '서비스 계정 키 JSON 파일 내용 전체를 붙여 넣습니다.',
    guide: {
      origin: 'Service account key (JSON)',
      site: { name: 'Google Cloud Console', url: 'https://console.cloud.google.com/iam-admin/serviceaccounts' },
      steps: [
        'Google Cloud Console에서 프로젝트를 고르고, "Google Play Android Developer API"를 찾아 "Enable"을 누릅니다.',
        '"Service accounts"에서 "Create service account"를 눌러 계정을 만듭니다.',
        '만든 계정의 이메일을 누르고 "Keys" 탭 → "Add key" → "Create new key" → "JSON" → "Create". JSON 파일이 내려받아집니다.',
        'Google Play Console의 "Users and permissions"에서 "Invite new users"를 누르고 그 서비스 계정 이메일을 넣습니다.',
        '권한은 "View financial data, orders, and cancellation survey responses"와 "Manage orders and subscriptions"를 켜고 "Invite user"를 누릅니다.',
        '내려받은 JSON 파일을 메모장으로 열어 내용 전체를 여기 붙여 넣습니다.',
      ],
      notes: [
        'JSON 파일은 한 번만 내려받을 수 있습니다. 잃어버리면 새 키를 만듭니다.',
        '"Create new key"가 막혀 있으면 회사 Google Cloud 조직 정책이 키 만들기를 막은 것입니다. 조직 관리자에게 이 프로젝트만 풀어 달라고 요청합니다.',
      ],
    },
  },
  {
    key: 'APPLE_TEAM_ID', label: 'Apple 팀 ID', needFor: '탈퇴 시 애플 로그인 연결 해제',
    hint: '영문 대문자·숫자 10자입니다.',
    guide: {
      origin: 'Team ID',
      site: { name: 'Apple Developer', url: 'https://developer.apple.com/account' },
      steps: [
        'Apple Developer 계정에 로그인합니다.',
        '"Membership details"에 보이는 "Team ID"를 복사합니다.',
      ],
      notes: ['유료 개발자 프로그램에 가입한 계정에서만 보입니다.'],
    },
  },
  {
    key: 'APPLE_KEY_ID', label: 'Sign in with Apple 키 ID', needFor: '탈퇴 시 애플 로그인 연결 해제',
    hint: '영문 대문자·숫자 10자입니다.',
    guide: {
      origin: 'Key ID',
      site: { name: 'Apple Developer › Keys', url: 'https://developer.apple.com/account/resources/authkeys/list' },
      steps: [
        '"Certificates, Identifiers & Profiles"의 "Keys"에서 왼쪽 위 "+"를 누릅니다.',
        '"Key Name"에 알아볼 이름을 넣고 "Sign in with Apple"에 체크한 뒤, 옆 "Configure"에서 게임 앱("Primary App ID")을 고르고 "Save"를 누릅니다.',
        '"Continue" → 내용을 확인하고 "Confirm"을 누릅니다.',
        '만든 키를 누르면 키 이름 아래에 보이는 값이 키 ID입니다.',
      ],
      notes: ['이미 만든 애플 로그인 키가 있으면 "Keys" 목록에서 그 키를 눌러 확인합니다. 키 파일과 같은 키여야 합니다.'],
    },
  },
  {
    key: 'APPLE_PRIVATE_KEY', label: 'Sign in with Apple 키 파일', needFor: '탈퇴 시 애플 로그인 연결 해제',
    multiline: true, hint: '.p8 파일 내용 전체(-----BEGIN PRIVATE KEY----- 줄 포함)를 붙여 넣습니다.',
    guide: {
      origin: 'Private key (.p8)',
      site: { name: 'Apple Developer › Keys', url: 'https://developer.apple.com/account/resources/authkeys/list' },
      steps: [
        '"Sign in with Apple 키 ID"를 만들 때 마지막 화면에서 "Download"를 누르면 .p8 파일이 내려받아집니다.',
        '그 파일을 메모장으로 열어 "-----BEGIN PRIVATE KEY-----"부터 "-----END PRIVATE KEY-----"까지 전부 복사해 붙여 넣습니다.',
      ],
      notes: [
        '.p8 파일은 한 번만 내려받을 수 있습니다. "Download"가 회색이면 이미 받은 것입니다. 파일을 잃어버렸으면 새 키를 만들고, 키 ID도 새 키 것으로 바꿉니다.',
      ],
    },
  },
]

/** 입력칸 옆 "가져오는 방법" — 누르면 발급처·원래 이름·단계가 펼쳐진다. */
function KeyGuideToggle({ guide }: { guide: KeyGuide }) {
  const [open, setOpen] = useState(false)
  return (
    <div>
      <button type="button" onClick={() => setOpen((v) => !v)}
        className="inline-flex items-center gap-1 text-xs text-[#1677ff] hover:underline">
        <CircleHelp className="w-3.5 h-3.5" />
        가져오는 방법
        {open ? <ChevronUp className="w-3 h-3" /> : <ChevronDown className="w-3 h-3" />}
      </button>
      {open && (
        <div className="mt-2 rounded-md border border-neutral-200 bg-neutral-50 px-3 py-2.5 space-y-2 text-xs">
          <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
            <span className="text-neutral-500">발급처의 이름 <span className="font-mono text-neutral-800">{guide.origin}</span></span>
            <a href={guide.site.url} target="_blank" rel="noreferrer" className="inline-flex items-center gap-0.5 text-[#1677ff] hover:underline">
              {guide.site.name} 열기 <ExternalLink className="w-3 h-3" />
            </a>
          </div>
          <ol className="list-decimal pl-4 space-y-1 leading-relaxed text-neutral-700">
            {guide.steps.map((s, i) => <li key={i}>{s}</li>)}
          </ol>
          {guide.notes && guide.notes.length > 0 && (
            <ul className="space-y-1 leading-relaxed text-neutral-500">
              {guide.notes.map((s, i) => <li key={i}>※ {s}</li>)}
            </ul>
          )}
        </div>
      )}
    </div>
  )
}

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
  const [saveError, setSaveError] = useState('')

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
    setSaveError('')
    setEditing({ key, clear })
  }

  const save = async () => {
    if (!editing) return
    setSaving(true)
    setSaveError('')
    try {
      await callAdmin(target, 'settings.setServerConfig', { key: editing.key, secretValue: editing.clear ? '' : value })
      setSavedKey(editing.key)
      setEditing(null)
      setValue('')
      await load()
    } catch (e: unknown) {
      if (e instanceof NotAuthenticatedError) report(e, '')
      else setSaveError(e instanceof Error ? e.message : '값을 저장하지 못했습니다.')
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
                  {row?.source === 'supabase' && <span>기본 설정 값을 쓰는 중</span>}
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
                        삭제
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
        title={editing?.clear ? `${editingDef?.label} 삭제` : `${editingDef?.label} ${byKey.get(editing?.key ?? '')?.status === 'missing' ? '입력' : '바꾸기'}`}
        description={editing?.clear
          ? '삭제하면 1분 안에 기본 설정 값으로 돌아갑니다. 기본 설정 값도 없으면 이 기능이 멈춥니다.'
          : '저장하면 1분 안에 적용됩니다. 저장한 값은 다시 볼 수 없습니다.'}
        confirmLabel={editing?.clear ? '삭제' : '저장'}
        danger={!!editing?.clear}
        busy={saving}
        error={saveError}
        confirmDisabled={!editing?.clear && value.trim() === ''}
        onConfirm={() => void save()}
        onCancel={() => setEditing(null)}
      >
        {editing && !editing.clear && editingDef && (
          <div className="space-y-1.5">
            <KeyGuideToggle key={editingDef.key} guide={editingDef.guide} />
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

// 흰 글자가 얹히는 색이라 전부 흰색과 4.5:1 이상 대비가 나는 진한 색으로 골랐다.
const CONSOLE_PALETTE: { name: string; hex: string }[] = [
  { name: '보라', hex: '#7c3aed' },
  { name: '남색', hex: '#4338ca' },
  { name: '파랑', hex: '#1d4ed8' },
  { name: '청록', hex: '#0f766e' },
  { name: '초록', hex: '#15803d' },
  { name: '주황', hex: '#c2410c' },
  { name: '빨강', hex: '#b91c1c' },
  { name: '분홍', hex: '#be185d' },
  { name: '갈색', hex: '#92400e' },
  { name: '회색', hex: '#475569' },
]

const HEX_RE = /^#[0-9a-f]{6}$/

type ConsoleSettings = {
  color: string | null
  display_name: string | null
  icon: string | null
  updated_at: string | null
  updated_by: string | null
}

const ICON_PX = 128
const ICON_MAX_CHARS = 120_000 // install.sql ts_admin_console_settings.icon 검사와 같은 값

/** 올린 그림을 가운데 기준 정사각형으로 잘라 128px 로 줄인다. 투명 배경을 살리려 WebP, 안 되는 브라우저는 PNG. */
function shrinkIcon(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const url = URL.createObjectURL(file)
    const img = new Image()
    img.onload = () => {
      URL.revokeObjectURL(url)
      const side = Math.min(img.naturalWidth, img.naturalHeight)
      const canvas = document.createElement('canvas')
      canvas.width = ICON_PX
      canvas.height = ICON_PX
      const ctx = canvas.getContext('2d')
      if (!ctx) { reject(new Error('그림을 처리하지 못했습니다.')); return }
      ctx.imageSmoothingQuality = 'high'
      ctx.drawImage(img, (img.naturalWidth - side) / 2, (img.naturalHeight - side) / 2, side, side, 0, 0, ICON_PX, ICON_PX)
      let out = canvas.toDataURL('image/webp', 0.9)
      if (!out.startsWith('data:image/webp')) out = canvas.toDataURL('image/png')
      resolve(out)
    }
    img.onerror = () => { URL.revokeObjectURL(url); reject(new Error('그림 파일을 열지 못했습니다. PNG·JPG·WebP 파일을 골라 주세요.')) }
    img.src = url
  })
}

/** 콘솔에 보이는 게임 이름과 아이콘. */
function ConsoleIdentityCard({
  target,
  current,
  onSaved,
  report,
}: {
  target: ProjectTarget
  current: ConsoleSettings | null
  onSaved: (s: ConsoleSettings) => void
  report: (e: unknown, fallback: string) => void
}) {
  const [name, setName] = useState('')
  const [icon, setIcon] = useState<string | null>(null)
  const [iconError, setIconError] = useState('')
  const [saving, setSaving] = useState(false)
  const [saved, setSaved] = useState(false)
  const filled = useRef(false)

  // 서버 값은 처음 받을 때만 채운다(게임을 바꾸면 current 가 null 로 돌아갔다가 다시 온다).
  // current 가 바뀔 때마다 채우면 색 카드에서 저장할 때 여기서 고치던 이름·아이콘이 서버 값으로 돌아간다.
  useEffect(() => {
    if (!current) { filled.current = false; setSaved(false); return }
    if (filled.current) return
    filled.current = true
    setName(current.display_name ?? '')
    setIcon(current.icon)
    setIconError('')
  }, [current])

  const defaultName = getProject(target).label
  const trimmed = name.trim()
  const tooLong = trimmed.length > 40
  const changed = !!current && (trimmed !== (current.display_name ?? '') || icon !== current.icon)

  const pick = async (file: File | undefined) => {
    setIconError('')
    setSaved(false)
    if (!file) return
    try {
      const data = await shrinkIcon(file)
      if (data.length > ICON_MAX_CHARS) { setIconError('그림이 너무 복잡해 줄여도 큽니다. 다른 그림을 골라 주세요.'); return }
      setIcon(data)
    } catch (e: unknown) {
      setIconError(e instanceof Error ? e.message : '그림을 처리하지 못했습니다.')
    }
  }

  const save = async () => {
    setSaving(true)
    setSaved(false)
    try {
      const s = await callAdmin<ConsoleSettings>(target, 'settings.setConsoleIdentity', { displayName: trimmed, icon: icon ?? '' })
      onSaved(s)
      setSaved(true)
    } catch (e: unknown) {
      report(e, '이름·아이콘을 저장하지 못했습니다.')
    } finally {
      setSaving(false)
    }
  }

  return (
    <WhiteCard className="p-5 space-y-5 max-w-2xl">
      <div>
        <h2 className="text-sm font-medium text-neutral-900">게임 이름 · 아이콘</h2>
        <p className="mt-1 text-xs text-neutral-500">게임 목록·메뉴·상단 띠·확인 창에 보입니다. 모든 운영자에게 같게 보입니다.</p>
      </div>

      <div className="space-y-1">
        <label htmlFor="console-name" className="block text-sm font-medium text-neutral-800">게임 이름</label>
        <input
          id="console-name"
          className={`${inputCls} w-72 ${tooLong ? 'border-red-400 focus:border-red-400 focus:ring-red-200' : ''}`}
          value={name}
          disabled={!current}
          onChange={(e) => { setName(e.target.value); setSaved(false) }}
          autoComplete="off"
        />
        <p className={`text-xs ${tooLong ? 'text-red-600' : 'text-neutral-500'}`}>
          {tooLong ? '40자 이내로 적어 주세요.' : `비워 두면 "${defaultName}"으로 보입니다.`}
        </p>
      </div>

      <div className="space-y-2">
        <span className="block text-sm font-medium text-neutral-800">아이콘</span>
        <div className="flex flex-wrap items-center gap-3">
          {icon
            ? <img src={icon} alt="" className="w-14 h-14 rounded-xl object-cover border border-neutral-200" />
            : <ProjectIcon target={target} size={56} />}
          <label className={`inline-flex items-center h-9 px-3 rounded-md border border-neutral-300 text-sm text-neutral-700 hover:bg-neutral-50 whitespace-nowrap ${current ? 'cursor-pointer' : 'opacity-40 pointer-events-none'}`}>
            그림 고르기
            <input
              type="file"
              accept="image/png,image/jpeg,image/webp"
              className="sr-only"
              disabled={!current}
              onChange={(e) => { void pick(e.target.files?.[0]); e.target.value = '' }}
            />
          </label>
          {icon && (
            <Button onClick={() => { setIcon(null); setSaved(false) }} disabled={saving}>
              그림 빼기
            </Button>
          )}
        </div>
        <p className={`text-xs ${iconError ? 'text-red-600' : 'text-neutral-500'}`}>
          {iconError || '정사각형 그림이 좋습니다. 가운데를 기준으로 잘라 작게 줄여 저장합니다. 그림이 없으면 게임 색 바탕에 이름 첫 글자가 보입니다.'}
        </p>
      </div>

      <div className="flex flex-wrap items-center gap-3">
        <Button variant="primary" onClick={() => void save()} disabled={saving || !changed || tooLong}>
          저장
        </Button>
        {saved && <span className="text-sm text-green-600 whitespace-nowrap">저장했습니다.</span>}
      </div>
    </WhiteCard>
  )
}

function ConsoleCards({ target, report }: { target: ProjectTarget; report: (e: unknown, fallback: string) => void }) {
  const [current, setCurrent] = useState<ConsoleSettings | null>(null)

  useEffect(() => {
    setCurrent(null)
    callAdmin<ConsoleSettings>(target, 'settings.getConsole')
      .then(setCurrent)
      .catch((e: unknown) => report(e, '콘솔 표시 설정을 불러오지 못했습니다.'))
  }, [target, report])

  const onSaved = (s: ConsoleSettings) => {
    setCurrent(s)
    setProjectLook(target, toProjectLook(s))
  }

  return (
    <div className="space-y-4">
      <ConsoleIdentityCard target={target} current={current} onSaved={onSaved} report={report} />
      <ConsoleColorCard target={target} current={current} onSaved={onSaved} report={report} />
    </div>
  )
}

function ConsoleColorCard({
  target,
  current,
  onSaved,
  report,
}: {
  target: ProjectTarget
  current: ConsoleSettings | null
  onSaved: (s: ConsoleSettings) => void
  report: (e: unknown, fallback: string) => void
}) {
  const look = useProjectLook()
  // 다른 게임과 비슷한지는 고른 원래 색끼리 잰다(화면에 보이는 어두운 화면용 색이 아니라).
  const colorOf = look.baseColor
  const [value, setValue] = useState('')
  const [saving, setSaving] = useState(false)
  const [saved, setSaved] = useState(false)
  const filled = useRef(false)

  // 이름·아이콘 카드와 같은 이유로 처음 받을 때만 채우고, 저장 표시도 그때만 끈다.
  useEffect(() => {
    if (!current) { filled.current = false; setSaved(false); return }
    if (filled.current) return
    filled.current = true
    setValue(current.color ?? getProject(target).color)
  }, [target, current])

  const hex = value.trim().toLowerCase()
  const valid = HEX_RE.test(hex)
  const readable = valid && contrastWithWhite(hex) >= 4.5
  const similar = valid
    ? PROJECTS.filter((p) => p.key !== target && colorDistance(colorOf(p.key), hex) < 90)
    : []
  const changed = !!current && valid && hex !== (current.color ?? getProject(target).color)

  const save = async (color: string | null) => {
    setSaving(true)
    setSaved(false)
    try {
      const s = await callAdmin<ConsoleSettings>(target, 'settings.setConsoleColor', { color: color ?? '' })
      onSaved(s)
      setValue(s.color ?? getProject(target).color)
      setSaved(true)
    } catch (e: unknown) {
      report(e, '색을 저장하지 못했습니다.')
    } finally {
      setSaving(false)
    }
  }

  return (
    <WhiteCard className="p-5 space-y-5 max-w-2xl">
      <div>
        <h2 className="text-sm font-medium text-neutral-900">게임 색</h2>
        <p className="mt-1 text-xs text-neutral-500">
          {look.name(target)}을 고르면 메뉴·상단 띠·확인 창에 이 색이 쓰입니다. 모든 운영자에게 같은 색으로 보입니다.
        </p>
      </div>

      <div className="flex flex-wrap gap-2.5" role="group" aria-label="색 고르기">
        {CONSOLE_PALETTE.map((c) => {
          const on = c.hex === hex
          return (
            <button
              key={c.hex}
              type="button"
              aria-label={c.name}
              aria-pressed={on}
              disabled={!current}
              onClick={() => { setValue(c.hex); setSaved(false) }}
              className="w-11 h-11 rounded-xl border-[3px] border-white disabled:opacity-40"
              // 테두리 색은 neutral 변수로 — 고정 색이면 어두운 화면에서 선택 표시가 안 보인다.
              style={{
                backgroundColor: c.hex,
                boxShadow: on ? '0 0 0 2px rgb(var(--neutral-900))' : '0 0 0 1px rgb(var(--neutral-300))',
              }}
            />
          )
        })}
      </div>

      <div className="space-y-1">
        <label htmlFor="console-color" className="block text-sm font-medium text-neutral-800">직접 입력</label>
        <input
          id="console-color"
          className={`${inputCls} w-40 font-mono ${valid && readable ? '' : 'border-red-400 focus:border-red-400 focus:ring-red-200'}`}
          value={value}
          disabled={!current}
          onChange={(e) => { setValue(e.target.value); setSaved(false) }}
          spellCheck={false}
          autoComplete="off"
        />
        <p className={`text-xs ${valid && readable ? 'text-neutral-500' : 'text-red-600'}`}>
          {!valid
            ? '#과 16진수 6자리로 적어 주세요.'
            : !readable
              ? '너무 밝아 흰 글자가 잘 안 보입니다. 더 진한 색을 골라 주세요.'
              : '#과 16진수 6자리.'}
        </p>
      </div>

      {similar.length > 0 && (
        <div className="rounded-md bg-amber-50 px-3 py-2 text-sm text-amber-800">
          {similar.map((p) => look.name(p.key)).join(', ')}와(과) 색이 비슷합니다. 엉뚱한 게임에서 작업하지 않도록 확실히 다른 색을 고르세요.
        </div>
      )}

      <div className="rounded-lg overflow-hidden border border-neutral-200">
        <div className="px-4 py-3 text-white font-bold" style={{ backgroundColor: valid ? look.display(hex) : '#9ca3af' }}>
          {look.name(target)}
        </div>
        <div className="px-4 py-2 text-xs text-neutral-500">미리보기</div>
      </div>

      <div className="flex flex-wrap items-center gap-3">
        <Button variant="primary" onClick={() => void save(hex)} disabled={saving || !changed || !readable}>
          저장
        </Button>
        {current?.color && (
          <Button onClick={() => void save(null)} disabled={saving}>
            기본색으로
          </Button>
        )}
        {saved && <span className="text-sm text-green-600 whitespace-nowrap">저장했습니다.</span>}
        {current?.updated_at && current.color && (
          <span className="text-xs text-neutral-400">
            마지막 수정 {formatDateTime(current.updated_at)}{current.updated_by ? ` · ${current.updated_by}` : ''}
          </span>
        )}
      </div>
    </WhiteCard>
  )
}

function zonedText(iso: string | null, tz: string): string {
  if (!iso) return '-'
  try {
    return new Intl.DateTimeFormat('ko-KR', {
      timeZone: tz, year: 'numeric', month: 'long', day: 'numeric', weekday: 'short', hour: '2-digit', minute: '2-digit', hour12: false,
    }).format(new Date(iso))
  } catch {
    return formatDateTime(iso)
  }
}

function TimeZoneCard({ target, report }: { target: ProjectTarget; report: (e: unknown, fallback: string) => void }) {
  const [current, setCurrent] = useState<GameSettings | null>(null)
  const [tz, setTz] = useState('')
  const [asking, setAsking] = useState(false)
  const [busy, setBusy] = useState(false)
  const [saveError, setSaveError] = useState('')

  useEffect(() => {
    void (async () => {
      try {
        const s = await callAdmin<GameSettings>(target, 'game.settingsGet')
        setCurrent(s)
        setTz(s.time_zone)
      } catch (e: unknown) {
        report(e, '시간대 설정을 불러오지 못했습니다.')
      }
    })()
  }, [target, report])

  const save = async () => {
    setBusy(true); setSaveError('')
    try {
      const s = await callAdmin<GameSettings>(target, 'game.settingsUpdate', { timeZone: tz })
      forgetGameTimeZone(target)
      setCurrent(s); setTz(s.time_zone); setAsking(false)
    } catch (e: unknown) {
      if (e instanceof NotAuthenticatedError) { report(e, ''); return }
      setSaveError(e instanceof Error ? e.message : '저장하지 못했습니다.')
    } finally {
      setBusy(false)
    }
  }

  const options = current && !TIME_ZONES.some(([v]) => v === current.time_zone)
    ? [[current.time_zone, current.time_zone] as const, ...TIME_ZONES]
    : TIME_ZONES
  const label = (v: string) => options.find(([k]) => k === v)?.[1] ?? v

  return (
    <WhiteCard className="p-5 space-y-5 max-w-2xl">
      <div>
        <h3 className="text-sm font-semibold text-neutral-900">게임 시간대</h3>
        <p className="text-xs text-neutral-500 mt-1">
          통계의 하루, 대시보드의 오늘, 날짜 검색, 탈퇴 계정이 삭제되는 자정, 새로 만드는 우편 예약·리더보드 회차를 이 시간대로 셉니다.
        </p>
      </div>
      <div className="space-y-1.5">
        <label className="block text-sm font-medium text-neutral-700">시간대</label>
        <select value={tz} onChange={(e) => setTz(e.target.value)} className={inputCls + ' max-w-sm'} disabled={!current}>
          {options.map(([v, l]) => (<option key={v} value={v}>{l}</option>))}
        </select>
        {current && (
          <p className="text-xs text-neutral-500">
            지금 7일 유예로 탈퇴하면 <span className="font-medium text-neutral-700">{zonedText(current.example_deadline, current.time_zone)}</span>({label(current.time_zone)})에 삭제됩니다.
          </p>
        )}
        <p className="text-xs text-neutral-400">
          바꾸면 그 뒤로 쌓이는 기록부터 새 시간대로 셉니다. 지난 날짜별 통계, 이미 예약된 탈퇴, 이미 만든 우편 예약과 리더보드는 그대로입니다.
        </p>
      </div>
      <div className="flex items-center gap-3">
        <Button variant="primary" onClick={() => { setSaveError(''); setAsking(true) }} disabled={!current || tz === current.time_zone}>
          저장
        </Button>
        {current?.updated_at && (
          <span className="text-xs text-neutral-400">
            마지막 수정 {formatDateTime(current.updated_at)}{current.updated_by ? ` · ${current.updated_by}` : ''}
          </span>
        )}
      </div>
      <ConfirmDialog
        open={asking}
        title="게임 시간대를 바꿀까요?"
        confirmLabel="바꾸기"
        busy={busy}
        error={saveError || undefined}
        onConfirm={() => { void save() }}
        onCancel={() => setAsking(false)}
        description="통계의 하루가 바뀌는 시각이 달라져, 바꾼 날의 숫자는 두 기준이 섞입니다. 지난 기록은 다시 세지 않습니다."
      >
        <div className="rounded-md bg-neutral-50 border border-neutral-200 px-3 py-2 text-sm">
          {current ? label(current.time_zone) : ''} → <span className="font-medium">{label(tz)}</span>
        </div>
      </ConfirmDialog>
    </WhiteCard>
  )
}

type Tab = 'store' | 'time' | 'mail' | 'friend' | 'lobby' | 'console'

const TABS: readonly (readonly [Tab, string])[] = [
  ['store', '스토어 연결'],
  ['time', '시간대'],
  ['mail', '우편'],
  ['friend', '친구'],
  ['lobby', '매치 로비'],
  ['console', '콘솔 표시'],
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
      {tab === 'console' && <ConsoleCards target={target} report={report} />}
      {tab === 'time' && <TimeZoneCard target={target} report={report} />}
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
