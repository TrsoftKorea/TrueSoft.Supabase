import { useEffect, useRef, useState } from 'react'
import { Trash2 } from 'lucide-react'
import { callAdmin, NotAuthenticatedError } from '../lib/api'
import type { ProjectTarget } from '../lib/projectTarget'
import { useProjectLook } from '../lib/projectLook'
import { CardHeader, FormCard } from '../components/ui/Card'
import { Button } from '../components/ui/Button'
import { ConfirmDialog } from '../components/ui/ConfirmDialog'
import { InfoList } from '../components/ui/InfoList'
import { StatusText } from '../components/ui/StatusText'
import { dangerInputCls } from '../components/ui/Input'
import { formatCount, formatDateTime } from '../components/ui/format'

type Report = (e: unknown, fallback: string) => void
type ResetResult = { deleted: Record<string, number>; by: string | null; at: string }

const REASON_TEXT: Record<string, string> = {
  reset_all_requires_maintenance: '점검 중일 때만 초기화할 수 있습니다. 점검·버전 탭에서 점검을 켠 뒤 다시 시도해 주세요.',
  reset_all_confirm_mismatch: '확인 글자가 맞지 않아 초기화하지 않았습니다.',
}
const reasonText = (e: unknown) => {
  const msg = e instanceof Error ? e.message : ''
  const key = Object.keys(REASON_TEXT).find((k) => msg.includes(k))
  return key ? REASON_TEXT[key]! : msg || '초기화하지 못했습니다.'
}

/**
 * 설정 › 초기화 탭(마스터 전용). 베타를 끝내고 플레이어를 처음부터 다시 받을 때 한 번 쓴다 — 되돌릴 수 없다.
 * 서버(ts_admin_reset_all_players)가 점검 중인지 다시 본다. 화면의 점검 확인은 버튼을 막는 안내일 뿐이다.
 */
export default function ResetAllPlayersCard({ target, report, onOpenGate }: { target: ProjectTarget; report: Report; onOpenGate: () => void }) {
  const look = useProjectLook()
  const name = look.name(target)
  const [maintenance, setMaintenance] = useState<boolean | null>(null)
  const [open, setOpen] = useState(false)
  const [typed, setTyped] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [result, setResult] = useState<ResetResult | null>(null)

  useEffect(() => {
    let alive = true
    setMaintenance(null)
    callAdmin<{ maintenance_active: boolean }>(target, 'appGate.get')
      .then((g) => { if (alive) setMaintenance(g.maintenance_active) })
      .catch((e: unknown) => report(e, '점검 상태를 불러오지 못했습니다.'))
    return () => { alive = false }
  }, [target, report])

  // Enter 를 연달아 누르면 busy 가 화면에 반영되기 전에 두 번 나간다 — 두 번째가 0건으로 결과를 덮는다.
  const running = useRef(false)
  const run = async () => {
    if (typed !== name || running.current) return
    running.current = true
    setBusy(true)
    setError('')
    try {
      setResult(await callAdmin<ResetResult>(target, 'players.resetAll'))
      setOpen(false)
    } catch (e: unknown) {
      if (e instanceof NotAuthenticatedError) report(e, '')
      else setError(reasonText(e))
    } finally {
      running.current = false
      setBusy(false)
    }
  }

  // deleted 에는 지운 줄 말고도 계정 수(auth.users)·배달을 멈춘 우편 수(mail_batches.open_until)가 함께 온다.
  const NOT_ROWS = ['auth.users', 'mail_batches.open_until']
  const accounts = result?.deleted['auth.users'] ?? 0
  const stopped = result?.deleted['mail_batches.open_until'] ?? 0
  const rows = result ? Object.entries(result.deleted).filter(([k]) => !NOT_ROWS.includes(k)).reduce((s, [, n]) => s + n, 0) : 0

  return (
    <FormCard>
      <CardHeader title="전체 초기화" description="모든 플레이어 계정과 게임 데이터를 지웁니다. 되돌릴 수 없습니다." />

      <InfoList items={[
        { label: '지워지는 것', value: '계정 · 플레이어 데이터 · 우편함 · 순위 · 친구 · 채팅 · 푸시 기기', wide: true },
        { label: '남는 것', value: '콘솔 설정 · 운영 계정 · 결제 기록 · 문의 · 통계 · 쿠폰 사용 기록 · 우편 발송 내역', wide: true },
        { label: '멈추는 것', value: '나중에 들어온 플레이어에게 가던 우편 배달', wide: true },
      ]} />

      <div className="space-y-1">
        <div className="flex items-center gap-2 text-sm">
          <span className="text-neutral-500">점검</span>
          {maintenance === null ? <StatusText tone="muted">확인 중</StatusText>
            : maintenance ? <StatusText tone="ok">진행 중</StatusText>
              : <StatusText tone="bad">꺼짐</StatusText>}
          {maintenance === false && <Button onClick={onOpenGate}>점검·버전 열기</Button>}
        </div>
        <p className="text-xs text-neutral-500">점검 중일 때만 초기화할 수 있습니다.</p>
      </div>

      <div className="flex items-center gap-3">
        <Button variant="dangerOutline" disabled={!maintenance} onClick={() => { setTyped(''); setError(''); setOpen(true) }}>
          <Trash2 className="w-3.5 h-3.5" />
          전체 초기화
        </Button>
      </div>

      {result && (
        <div className="rounded-md border border-neutral-200 px-4 py-3 space-y-2">
          <p className="text-sm font-medium text-neutral-900">초기화했습니다.</p>
          <InfoList items={[
            { label: '지운 계정', value: `${formatCount(accounts)}개` },
            { label: '지운 데이터', value: `${formatCount(rows)}줄` },
            { label: '멈춘 우편 배달', value: `${formatCount(stopped)}건` },
            { label: '시각', value: formatDateTime(result.at) },
          ]} />
        </div>
      )}

      <ConfirmDialog
        open={open}
        title="전부 지울까요?"
        description="모든 계정과 플레이어 데이터가 지워집니다. 되돌릴 수 없습니다."
        confirmLabel="전체 초기화"
        danger
        busy={busy}
        confirmDisabled={typed !== name}
        error={error}
        onConfirm={() => { void run() }}
        onCancel={() => setOpen(false)}
      >
        <div>
          <label className="block text-xs text-neutral-500 mb-1">
            확인을 위해 프로젝트 이름 <span className="font-semibold text-neutral-700">{name}</span> 을 입력하세요
          </label>
          <input
            value={typed}
            onChange={(e) => setTyped(e.target.value)}
            onKeyDown={(e) => { if (e.key === 'Enter') void run() }}
            className={dangerInputCls + ' w-full'}
          />
        </div>
      </ConfirmDialog>
    </FormCard>
  )
}
