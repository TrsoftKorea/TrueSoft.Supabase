import { Fragment, useEffect, useState, useCallback } from 'react'
import { Search, ChevronLeft, ChevronRight, Loader2 } from 'lucide-react'
import { callAdmin, NotAuthenticatedError } from '../lib/api'
import type { ProjectTarget } from '../lib/projectTarget'
import { WhiteCard } from '../components/ui/Card'
import { PageHeader } from '../components/ui/PageHeader'
import { TableStatusRow } from '../components/ui/TableStatusRow'
import { DateRangePicker } from '../components/ui/DateRangePicker'
import { ErrorBanner } from '../components/ui/ErrorBanner'
import { Button } from '../components/ui/Button'
import { formatDateTime, formatTotal } from '../components/ui/format'

type AuditRow = { id: number; email: string; action: string; params: Record<string, unknown>; created_at: string }
type AuditData = { rows: AuditRow[]; total: number; pageSize: number }

const CATEGORIES: { key: string; label: string }[] = [
  { key: '', label: '전체' },
  { key: 'mails', label: '우편' },
  { key: 'players', label: '플레이어' },
  { key: 'userData', label: '유저 데이터' },
  { key: 'coupons', label: '쿠폰' },
  { key: 'purchases', label: '결제' },
  { key: 'chat', label: '채팅' },
  { key: 'bannedWords', label: '금칙어' },
  { key: 'gameItems', label: '아이템 카탈로그' },
  { key: 'servers', label: '게임 서버' },
  { key: 'leaderboard', label: '리더보드' },
  { key: 'remoteConfig', label: '원격 설정' },
  { key: 'schema', label: '변경 관리' },
  { key: 'matchRewards', label: '매치 보상' },
  { key: 'friends', label: '친구 설정' },
  { key: 'matchLobby', label: '매치 로비 설정' },
  { key: 'settings', label: '설정' },
  { key: 'operators', label: '운영자' },
]

// 기록에는 서버 요청 이름이 남는다. 화면에서는 운영자가 읽을 수 있는 말로 바꿔 보여주고,
// 여기 없는 이름은 원문 그대로 둔다(새 기능이 생겨도 기록이 숨지 않게).
const ACTION_LABELS: Record<string, string> = {
  'servers.upsert': '게임 서버 추가·수정',
  'bannedWords.add': '금칙어 추가',
  'bannedWords.delete': '금칙어 삭제',
  'purchases.syncRefunds': '구글 환불 지금 확인',
  'operators.upsert': '운영자 추가·수정',
  'operators.setPassword': '운영자 비밀번호 설정',
  'operators.setDisabled': '운영자 비활성화·복구',
  'operators.delete': '운영자 삭제',
  'gameItems.upsert': '아이템 추가·수정',
  'gameItems.delete': '아이템 삭제',
  'players.ban': '플레이어 차단',
  'players.unban': '플레이어 차단 해제',
  'players.setDisplayName': '닉네임 변경',
  'userData.update': '유저 데이터 수정',
  'remoteConfig.stageNew': '원격 설정 추가(대기)',
  'remoteConfig.stageMeta': '원격 설정 수정(대기)',
  'remoteConfig.stageItem': '원격 설정 값 수정(대기)',
  'remoteConfig.stageItemDelete': '원격 설정 값 삭제(대기)',
  'remoteConfig.stageDelete': '원격 설정 삭제(대기)',
  'chat.deleteMessage': '채팅 메시지 삭제',
  'chat.mute': '채팅 금지',
  'chat.unmute': '채팅 금지 해제',
  'mails.settingsUpdate': '우편 설정 변경',
  'friends.settingsUpdate': '친구 설정 변경',
  'matchLobby.settingsUpdate': '매치 로비 설정 변경',
  'settings.setServerConfig': '스토어 연결값 변경',
  'settings.setConsoleColor': '콘솔 색 변경',
  'settings.setConsoleIdentity': '콘솔 이름·아이콘 변경',
  'matchRewards.upsert': '매치 보상 추가·수정',
  'matchRewards.delete': '매치 보상 삭제',
  'coupons.create': '쿠폰 생성',
  'coupons.update': '쿠폰 수정',
  'coupons.delete': '쿠폰 삭제',
  'mails.upsertCategory': '우편 분류 추가·수정',
  'mails.deleteCategory': '우편 분류 삭제',
  'mails.send': '우편 발송',
  'mails.createSchedule': '우편 예약 등록',
  'mails.setScheduleActive': '우편 예약 켜기·끄기',
  'mails.deleteSchedule': '우편 예약 삭제',
  'schema.discardDraft': '대기 변경 버리기',
  'schema.publish': '변경 게시',
  'schema.revertVersion': '이전 버전으로 되돌리기',
  'schema.stage': '변경 대기열에 담기',
  'leaderboard.rotate': '리더보드 회차 넘기기',
  'leaderboard.setScore': '리더보드 점수 수정',
  'leaderboard.deleteScore': '리더보드 점수 삭제',
  'leaderboard.setPlayerData': '리더보드 플레이어 데이터 수정',
}

// 요청 인자 이름도 같은 원칙 — 아는 것만 바꾸고 모르는 이름은 그대로 둔다.
const PARAM_LABELS: Record<string, string> = {
  accountId: '계정 ID', accountIds: '받는 플레이어', bannedUntil: '차단 종료', banMessage: '차단 메시지',
  displayName: '이름', title: '제목', content: '내용', code: '코드', key: '키', email: '이메일',
  id: '번호', kind: '종류', words: '단어', channelId: '채널', minutes: '분', reason: '사유',
  serverCode: '서버 코드', serverId: '서버', allowTransfers: '이전 받기', disabled: '비활성화',
  category: '분류', items: '보상', expiresAt: '만료', expiresDays: '보관 일수', mailExpiresDays: '우편 보관 일수',
  isActive: '켜짐', maxUses: '최대 사용 횟수', mode: '방식', localized: '다국어',
  scheduleType: '예약 방식', scheduledAt: '예약 시각', repeatTime: '반복 시각', repeatUnit: '반복 단위',
  repeatDow: '반복 요일', repeatDom: '반복 날짜', patch: '바꾼 값', score: '점수', rotationCount: '회차',
  data: '데이터', gameCode: '게임 코드', winItems: '승리 보상', mailTitle: '우편 제목', mailContent: '우편 내용',
  createOnly: '새로 추가만', label: '메모', versionId: '버전', feature: '대상', action: '작업',
  objectName: '이름', params: '세부', itemKey: '항목 키', itemValue: '항목 값', enabled: '켜짐',
  requiresAuth: '로그인 필요', color: '색', icon: '아이콘', secretValue: '값', newPassword: '새 비밀번호',
}
const paramLabel = (k: string) => PARAM_LABELS[k] ?? k

const ISO_RE = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}/

function scalarText(v: unknown): string {
  if (typeof v === 'string' && ISO_RE.test(v)) return formatDateTime(v)
  if (typeof v === 'boolean') return v ? '예' : '아니오'
  if (v === null || v === undefined || v === '') return '—'
  return String(v)
}

/** 상세 칸 한 줄 요약. 전체 값은 행을 펼쳐서 본다. */
function paramsSummary(p: Record<string, unknown>): string {
  return Object.entries(p)
    .map(([k, v]) => {
      if (Array.isArray(v)) return `${paramLabel(k)}: ${v.length}개`
      if (v !== null && typeof v === 'object') return `${paramLabel(k)}: {…}`
      return `${paramLabel(k)}: ${scalarText(v)}`
    })
    .join(' · ')
}

export default function AuditLogs({
  target,
  onUnauthenticated,
}: {
  target: ProjectTarget
  onUnauthenticated: () => void
}) {
  const [searchInput, setSearchInput] = useState('')
  const [search, setSearch] = useState('')
  const [category, setCategory] = useState('')
  const [dateFrom, setDateFrom] = useState('')
  const [dateTo, setDateTo] = useState('')
  const [page, setPage] = useState(1)
  const [data, setData] = useState<AuditData | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [failed, setFailed] = useState(false)
  const [openId, setOpenId] = useState<number | null>(null)

  const report = useCallback(
    (e: unknown, fallback: string) => {
      if (e instanceof NotAuthenticatedError) { onUnauthenticated(); return }
      setError(e instanceof Error ? e.message : fallback)
    },
    [onUnauthenticated],
  )

  const load = useCallback(async () => {
    setLoading(true)
    setFailed(false)
    try {
      setData(await callAdmin<AuditData>(target, 'audit.list', {
        search, category, startDate: dateFrom || null, endDate: dateTo || null, page,
      }))
    } catch (e: unknown) {
      report(e, '운영 기록을 불러오지 못했습니다.')
      setData(null)
      setFailed(true)
    } finally {
      setLoading(false)
    }
  }, [target, search, category, dateFrom, dateTo, page, report])

  useEffect(() => { void load() }, [load])

  const rows = data?.rows ?? []
  const total = data?.total ?? 0
  const pageSize = data?.pageSize ?? 50
  const totalPages = Math.max(1, Math.ceil(total / pageSize))

  const submitSearch = () => { setSearch(searchInput.trim()); setPage(1) }

  return (
    <div className="space-y-4">
      <PageHeader title="운영 기록" description="운영 도구에서 누가 언제 무엇을 바꿨는지 봅니다." />
      <ErrorBanner message={error} onDismiss={() => setError('')} />

      <WhiteCard className="p-4">
        <div className="flex flex-wrap items-center gap-3">
          <span className="text-xs text-neutral-500">시각</span>
          <DateRangePicker start={dateFrom || null} end={dateTo || null} onApply={(s, e) => { setDateFrom(s ?? ''); setDateTo(e ?? ''); setPage(1) }} />
          <span className="text-xs text-neutral-500">종류</span>
          <select
            value={category}
            onChange={(e) => { setCategory(e.target.value); setPage(1) }}
            className="h-9 px-2 rounded-md border border-neutral-300 text-sm bg-white"
          >
            {CATEGORIES.map((c) => <option key={c.key} value={c.key}>{c.label}</option>)}
          </select>
          <span className="text-xs text-neutral-500">운영자·내용</span>
          <input
            value={searchInput}
            onChange={(e) => setSearchInput(e.target.value)}
            onKeyDown={(e) => e.key === 'Enter' && submitSearch()}
            className="h-9 px-3 rounded-md border border-neutral-300 text-sm focus:outline-none focus:ring-2 focus:ring-[#1677ff]/30 focus:border-[#1677ff] w-56"
          />
          <Button variant="primary" onClick={submitSearch} disabled={loading}>
            {loading ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Search className="w-3.5 h-3.5" />}검색
          </Button>
        </div>
      </WhiteCard>

      <WhiteCard>
        <div className="overflow-auto">
          <table className="w-full text-sm">
            <thead className="bg-neutral-50 text-neutral-500 text-xs">
              <tr>
                <th className="text-left px-4 py-2.5 font-medium w-40">시각</th>
                <th className="text-left px-4 py-2.5 font-medium w-52">운영자</th>
                <th className="text-left px-4 py-2.5 font-medium w-48">한 일</th>
                <th className="text-left px-4 py-2.5 font-medium">내용</th>
              </tr>
            </thead>
            <tbody>
              {loading || rows.length === 0 ? (
                <TableStatusRow loading={loading} empty={rows.length === 0} failed={failed} colSpan={4} emptyText="운영 기록이 없습니다." />
              ) : (
                rows.map((r) => (
                  <Fragment key={r.id}>
                    <tr
                      onClick={() => setOpenId((o) => (o === r.id ? null : r.id))}
                      className="border-t border-neutral-100 hover:bg-neutral-50/50 cursor-pointer"
                    >
                      <td className="px-4 py-3 text-neutral-600 whitespace-nowrap">{formatDateTime(r.created_at)}</td>
                      <td className="px-4 py-3 text-neutral-700 truncate max-w-[13rem]" title={r.email}>{r.email}</td>
                      <td className="px-4 py-3 text-neutral-800 whitespace-nowrap" title={r.action}>{ACTION_LABELS[r.action] ?? r.action}</td>
                      <td className="px-4 py-3 text-neutral-600 max-w-[420px] truncate">{paramsSummary(r.params)}</td>
                    </tr>
                    {openId === r.id && (
                      <tr className="bg-neutral-50/60">
                        <td colSpan={4} className="px-4 py-3">
                          <dl className="grid grid-cols-[max-content_1fr] gap-x-4 gap-y-1 text-xs">
                            {Object.entries(r.params).map(([k, v]) => (
                              <Fragment key={k}>
                                <dt className="text-neutral-500 whitespace-nowrap" title={k}>{paramLabel(k)}</dt>
                                <dd className="text-neutral-700 break-all">
                                  {v !== null && typeof v === 'object'
                                    ? <pre className="whitespace-pre-wrap font-mono">{JSON.stringify(v, null, 2)}</pre>
                                    : scalarText(v)}
                                </dd>
                              </Fragment>
                            ))}
                          </dl>
                        </td>
                      </tr>
                    )}
                  </Fragment>
                ))
              )}
            </tbody>
          </table>
        </div>
        <div className="flex items-center justify-between border-t border-neutral-100 px-5 py-2.5 text-xs text-neutral-500">
          <span>{formatTotal(total)}</span>
          <div className="flex items-center gap-1">
            <button disabled={page <= 1} onClick={() => setPage((p) => Math.max(1, p - 1))} className="w-7 h-7 inline-flex items-center justify-center rounded border border-neutral-200 disabled:opacity-40 hover:bg-neutral-50"><ChevronLeft className="w-3.5 h-3.5" /></button>
            <span className="px-2">{page} / {totalPages}</span>
            <button disabled={page >= totalPages} onClick={() => setPage((p) => Math.min(totalPages, p + 1))} className="w-7 h-7 inline-flex items-center justify-center rounded border border-neutral-200 disabled:opacity-40 hover:bg-neutral-50"><ChevronRight className="w-3.5 h-3.5" /></button>
          </div>
        </div>
      </WhiteCard>
    </div>
  )
}
