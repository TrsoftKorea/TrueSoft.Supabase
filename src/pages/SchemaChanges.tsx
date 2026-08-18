import { useState, useEffect, useCallback } from 'react'
import { RotateCcw, Trash2, Send, Loader2, AlertTriangle, X } from 'lucide-react'
import { callAdmin, NotAuthenticatedError } from '../lib/api'
import type { ProjectTarget } from '../lib/projectTarget'
import { usePendingChanges } from '../lib/pendingChanges'
import { opLabel } from '../lib/schemaLabels'
import { PageHeader } from '../components/ui/PageHeader'
import { WhiteCard } from '../components/ui/Card'
import { TableStatusRow } from '../components/ui/TableStatusRow'
import { formatDateTime } from '../components/ui/format'
import { ConfirmDialog } from '../components/ui/ConfirmDialog'
import { ErrorBanner } from '../components/ui/ErrorBanner'

type VersionRow = {
  id: number
  published_at: string
  operator: string | null
  label: string | null
  reversible: boolean
  status: string
  reverted_at: string | null
  reverted_by: string | null
  op_count: number
  ops: Array<{ feature: string; action: string; object_name: string; reversible: boolean }>
}

const isDestructive = (feature: string, action: string) =>
  action === 'drop' || (feature === 'leaderboard_table' && action === 'delete')

// 대기 중 변경의 params(내부 payload)를 운영자가 읽기 쉬운 한글 요약으로 바꾼다.
// 작업·대상 열에 이미 나오는 값은 빼고, 그 작업에서 실제로 의미 있는 것만 남긴다.
const SCOPE_TEXT: Record<string, string> = { global: '전체 통합', server: '서버별' }
const RECORD_TEXT: Record<string, string> = { highest: '최고', lowest: '최저', last: '최신', sum: '누적' }
const ROTATION_TEXT: Record<string, string> = {
  none: '초기화 없음', daily: '일간 초기화', weekly: '주간 초기화',
  monthly: '월간 초기화', custom: '주기 직접 지정',
}

const str = (v: unknown) => (v === null || v === undefined ? '' : String(v))

// 필드 추가·수정 — 타입과 기본값만 보여주면 충분하다.
function fieldSummary(params: Record<string, unknown>, action: string): string {
  const def = params['default_sql']
  const defText = def === null || def === undefined || def === '' ? '기본값 없음' : `기본값 ${str(def)}`
  if (action === 'update') return `${defText}으로 변경`

  const parts: string[] = []
  if (params['coltype']) parts.push(`${str(params['coltype'])} 타입`)
  parts.push(defText)
  return parts.join(' · ')
}

// 리더보드 생성·수정 — 이름과 핵심 설정만.
function tableSummary(params: Record<string, unknown>): string {
  const parts: string[] = []
  if (params['display_name']) parts.push(str(params['display_name']))
  if (params['scope']) parts.push(SCOPE_TEXT[str(params['scope'])] ?? str(params['scope']))
  if (params['record_type']) parts.push(RECORD_TEXT[str(params['record_type'])] ?? str(params['record_type']))
  if (params['rotation']) parts.push(ROTATION_TEXT[str(params['rotation'])] ?? str(params['rotation']))
  if (params['is_active'] === false) parts.push('사용 중지')
  return parts.join(' · ')
}

// 아직 전용 요약이 없는 기능(원격 설정 등)은 대상과 겹치는 키만 걸러 남긴다.
function genericSummary(params: Record<string, unknown>): string {
  const skip = new Set(['colname', 'code', 'key', 'name'])
  const entries = Object.entries(params ?? {}).filter(
    ([k, v]) => !skip.has(k) && v !== null && v !== undefined && v !== '',
  )
  if (entries.length === 0) return '—'
  return entries
    .map(([k, v]) => `${k}: ${typeof v === 'object' ? JSON.stringify(v) : String(v)}`)
    .join(' · ')
}

// 원격 설정 — 스테이징이 키 단위 스냅샷이라 개별 항목 대신 게시 후 상태를 요약한다.
function configSummary(params: Record<string, unknown>): string {
  const vj = (params['value_json'] ?? {}) as Record<string, unknown>
  const itemCount = Object.keys(vj).filter((k) => k !== '__meta').length
  return [
    itemCount === 0 ? '필드 없음' : `필드 ${itemCount}개`,
    params['enabled'] === false ? '비활성' : '활성',
    params['requires_auth'] ? '인증 필요' : '인증 불필요',
  ].join(' · ')
}

function formatSummary(feature: string, action: string, params: Record<string, unknown>): string {
  const p = params ?? {}
  if (feature === 'remote_config') {
    if (action === 'delete') return '설정과 모든 필드 삭제'
    return configSummary(p)
  }
  if (feature === 'leaderboard_field') {
    if (action === 'attach') return `${str(p['code'])} 리더보드에서 사용으로 변경`
    if (action === 'detach') return `${str(p['code'])} 리더보드에서 사용 안 함으로 변경`
    if (action === 'drop') return '필드 목록에서 삭제'
    return fieldSummary(p, action)
  }
  if (feature === 'user_data_field') {
    if (action === 'drop') return '필드 목록에서 삭제'
    return fieldSummary(p, action)
  }
  if (feature === 'leaderboard_table') {
    if (action === 'delete') return '리더보드와 기록 전체 삭제'
    return tableSummary(p)
  }
  return genericSummary(p)
}

export default function SchemaChanges({
  target,
  onUnauthenticated,
}: {
  target: ProjectTarget
  onUnauthenticated: () => void
}) {
  const { drafts: draftsOrNull, reload: reloadDrafts } = usePendingChanges()
  const drafts = draftsOrNull ?? []
  const draftLoading = draftsOrNull === null
  const [versions, setVersions] = useState<VersionRow[]>([])
  const [versionsLoading, setVersionsLoading] = useState(true)

  const [label, setLabel] = useState('')
  const [busy, setBusy] = useState(false)
  const [showPublish, setShowPublish] = useState(false)
  const [askDiscardAll, setAskDiscardAll] = useState(false)
  const [revertTarget, setRevertTarget] = useState<VersionRow | null>(null)
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

  const reloadVersions = useCallback(async () => {
    setVersionsLoading(true)
    try {
      const v = await callAdmin<{ rows: VersionRow[] }>(target, 'schema.getVersions')
      setVersions(v.rows)
    } catch (e: unknown) {
      report(e, '불러오지 못했습니다.')
    } finally {
      setVersionsLoading(false)
    }
  }, [target, report])

  useEffect(() => { void reloadVersions() }, [reloadVersions])

  const hasDestructiveDraft = drafts.some((d) => isDestructive(d.feature, d.action))
  // 이름을 비우고 게시하면 이력에 이 작업 요약이 이름으로 자동 표시된다.
  const draftSummary = Array.from(new Set(drafts.map((d) => opLabel(d.feature, d.action)))).join(', ')

  const doPublish = async () => {
    if (drafts.length === 0) return
    setBusy(true)
    try {
      await callAdmin(target, 'schema.publish', { label: label.trim() || null })
      setLabel('')
      setShowPublish(false)
      await Promise.all([reloadDrafts(), reloadVersions()])
    } catch (e: unknown) {
      report(e, '게시에 실패했습니다.')
    } finally { setBusy(false) }
  }

  const discardOne = async (id: number) => {
    setBusy(true)
    try {
      await callAdmin(target, 'schema.discardDraft', { id })
      await reloadDrafts()
    } catch (e: unknown) {
      report(e, '실패했습니다.')
    } finally { setBusy(false) }
  }

  const confirmDiscardAll = async () => {
    setBusy(true)
    try {
      await callAdmin(target, 'schema.discardDraft', {})
      setAskDiscardAll(false)
      await reloadDrafts()
    } catch (e: unknown) {
      report(e, '실패했습니다.')
    } finally { setBusy(false) }
  }

  const confirmRevert = async () => {
    if (!revertTarget) return
    setBusy(true)
    try {
      await callAdmin(target, 'schema.revertVersion', { versionId: revertTarget.id })
      setRevertTarget(null)
      await Promise.all([reloadDrafts(), reloadVersions()])
    } catch (e: unknown) {
      report(e, '되돌리기에 실패했습니다.')
    } finally { setBusy(false) }
  }

  return (
    <div className="space-y-4">
      <PageHeader
        title="변경 관리"
        description="스키마 변경을 모아 검토한 뒤 한 번에 게시하고, 버전 단위로 되돌립니다."
      />

      <ErrorBanner message={error} onDismiss={() => setError('')} />

      <WhiteCard>
        <div className="flex flex-wrap items-center gap-2 border-b border-neutral-100 px-5 py-3">
          <span className="text-sm font-medium text-neutral-800">대기 중 변경</span>
          <span className="text-xs text-neutral-400">{drafts.length}건</span>
          <div className="ml-auto flex items-center gap-2">
            <button
              onClick={() => setAskDiscardAll(true)}
              disabled={busy || drafts.length === 0}
              className="inline-flex items-center gap-1.5 h-9 px-3 rounded-md border border-neutral-200 text-sm text-neutral-600 hover:bg-neutral-50 disabled:opacity-50"
            >
              전체 취소
            </button>
            <button
              onClick={() => setShowPublish(true)}
              disabled={busy || drafts.length === 0}
              className="inline-flex items-center gap-1.5 h-9 px-3 rounded-md bg-[#1677ff] text-white text-sm disabled:opacity-50"
            >
              <Send className="w-3.5 h-3.5" />
              게시
            </button>
          </div>
        </div>
        {hasDestructiveDraft && (
          <div className="flex items-center gap-2 px-5 py-2 bg-amber-50 border-b border-amber-100 text-xs text-amber-700">
            <AlertTriangle className="w-3.5 h-3.5" />
            삭제 작업이 포함돼 있습니다. 게시 후에는 자동 되돌리기가 불가하며 데이터가 사라집니다.
          </div>
        )}
        <div className="overflow-auto">
          <table className="w-full text-sm">
            <thead className="bg-neutral-50 text-neutral-500 text-xs">
              <tr>
                <th className="text-left px-5 py-2.5 font-medium">작업</th>
                <th className="text-left px-4 py-2.5 font-medium">대상</th>
                <th className="text-left px-4 py-2.5 font-medium">내용</th>
                <th className="px-4 py-2.5 w-16"></th>
              </tr>
            </thead>
            <tbody>
              {draftLoading || drafts.length === 0 ? (
                <TableStatusRow
                  loading={draftLoading}
                  empty={drafts.length === 0}
                  colSpan={4}
                  emptyText="대기 중 변경이 없습니다. 각 관리 화면에서 변경을 담으세요."
                />
              ) : (
                drafts.map((d) => (
                  <tr key={d.id} className="border-t border-neutral-100">
                    <td className="px-5 py-3">
                      <span className={isDestructive(d.feature, d.action) ? 'text-red-600' : ''}>
                        {opLabel(d.feature, d.action)}
                      </span>
                    </td>
                    <td className="px-4 py-3 font-mono text-xs">{d.object_name}</td>
                    <td className="px-4 py-3 text-xs text-neutral-500 max-w-[360px] truncate" title={formatSummary(d.feature, d.action, d.params)}>
                      {formatSummary(d.feature, d.action, d.params)}
                    </td>
                    <td className="px-4 py-3 text-right">
                      <button
                        onClick={() => void discardOne(d.id)}
                        disabled={busy}
                        title="취소"
                        className="p-1.5 rounded hover:bg-neutral-100 text-neutral-500 hover:text-red-600"
                      >
                        <Trash2 className="w-3.5 h-3.5" />
                      </button>
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      </WhiteCard>

      <WhiteCard>
        <div className="border-b border-neutral-100 px-5 py-3">
          <span className="text-sm font-medium text-neutral-800">게시 이력</span>
        </div>
        <div className="overflow-auto">
          <table className="w-full text-sm">
            <thead className="bg-neutral-50 text-neutral-500 text-xs">
              <tr>
                <th className="text-left px-5 py-2.5 font-medium">버전</th>
                <th className="text-left px-4 py-2.5 font-medium">게시 시각</th>
                <th className="text-left px-4 py-2.5 font-medium">이름</th>
                <th className="text-right px-4 py-2.5 font-medium">변경 수</th>
                <th className="text-left px-4 py-2.5 font-medium">상태</th>
                <th className="px-4 py-2.5 w-28"></th>
              </tr>
            </thead>
            <tbody>
              {versionsLoading || versions.length === 0 ? (
                <TableStatusRow
                  loading={versionsLoading}
                  empty={versions.length === 0}
                  colSpan={6}
                  emptyText="게시된 버전이 없습니다."
                />
              ) : (
                versions.map((v) => {
                  const opsSummary = (v.ops ?? []).map((o) => opLabel(o.feature, o.action)).join(', ')
                  const reverted = v.status === 'reverted'
                  return (
                    <tr key={v.id} className="border-t border-neutral-100">
                      <td className="px-5 py-3">#{v.id}</td>
                      <td className="px-4 py-3 whitespace-nowrap">{formatDateTime(v.published_at)}</td>
                      <td className="px-4 py-3 max-w-[220px] truncate" title={v.label || opsSummary}>
                        {v.label || opsSummary || <span className="text-neutral-400">(이름 없음)</span>}
                      </td>
                      <td className="px-4 py-3 text-right">{v.op_count}</td>
                      <td className="px-4 py-3">
                        {reverted ? (
                          <span className="text-xs text-neutral-500">되돌림</span>
                        ) : v.reversible ? (
                          <span className="text-xs text-emerald-600">게시됨</span>
                        ) : (
                          <span className="text-xs text-amber-600">게시됨 · 되돌리기 불가</span>
                        )}
                      </td>
                      <td className="px-4 py-3 text-right">
                        <button
                          onClick={() => setRevertTarget(v)}
                          disabled={busy || reverted || !v.reversible}
                          title={!v.reversible ? '삭제 작업이 포함돼 되돌릴 수 없습니다' : reverted ? '이미 되돌림' : '되돌리기'}
                          className="inline-flex items-center gap-1 px-2 py-1 rounded text-xs border border-neutral-200 text-neutral-600 hover:bg-neutral-50 disabled:opacity-40 whitespace-nowrap"
                        >
                          <RotateCcw className="w-3 h-3" /> 되돌리기
                        </button>
                      </td>
                    </tr>
                  )
                })
              )}
            </tbody>
          </table>
        </div>
      </WhiteCard>

      {/* 게시 확인 모달 — 이름은 여기서 선택 입력 */}
      {showPublish && (
        <div
          className="fixed inset-0 z-50 bg-black/40 flex items-center justify-center p-4"
          onClick={() => { if (!busy) setShowPublish(false) }}
        >
          <div className="bg-white rounded-lg shadow-xl w-full max-w-md" onClick={(e) => e.stopPropagation()}>
            <div className="flex items-center justify-between px-5 py-4 border-b">
              <h3 className="text-base font-semibold">변경 게시</h3>
              <button
                onClick={() => setShowPublish(false)}
                disabled={busy}
                className="text-neutral-400 hover:text-neutral-700 disabled:opacity-50"
              >
                <X className="w-4 h-4" />
              </button>
            </div>
            <div className="p-5 space-y-4">
              <p className="text-sm text-neutral-700">
                대기 중 변경 <b>{drafts.length}건</b>을 라이브에 적용합니다.
              </p>
              {hasDestructiveDraft && (
                <div className="flex items-start gap-2 rounded-md bg-amber-50 border border-amber-200 px-3 py-2 text-xs text-amber-700">
                  <AlertTriangle className="w-3.5 h-3.5 mt-0.5 shrink-0" />
                  <span>삭제 작업이 포함돼 있습니다. 게시 후에는 자동 되돌리기가 불가하며 데이터가 사라집니다.</span>
                </div>
              )}
              <div>
                <label className="block text-xs text-neutral-500 mb-1">게시 이름</label>
                <input
                  value={label}
                  onChange={(e) => setLabel(e.target.value)}
                  placeholder={draftSummary}
                  autoFocus
                  className="w-full h-9 px-3 rounded-md border border-neutral-300 text-sm focus:outline-none focus:ring-2 focus:ring-[#1677ff]/30 focus:border-[#1677ff]"
                />
                <p className="mt-1 text-xs text-neutral-400">비우면 회색으로 표시된 이름이 이력에 자동으로 기록됩니다.</p>
              </div>
            </div>
            <div className="flex justify-end gap-2 px-5 py-3 border-t bg-neutral-50">
              <button
                onClick={() => setShowPublish(false)}
                disabled={busy}
                className="h-9 px-4 rounded-md border border-neutral-300 text-sm hover:bg-white disabled:opacity-50"
              >
                취소
              </button>
              <button
                onClick={() => { void doPublish() }}
                disabled={busy}
                className="inline-flex items-center gap-1.5 h-9 px-4 rounded-md bg-[#1677ff] text-white text-sm disabled:opacity-50"
              >
                {busy ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Send className="w-3.5 h-3.5" />}
                게시
              </button>
            </div>
          </div>
        </div>
      )}

      <ConfirmDialog
        open={askDiscardAll}
        title="대기 중 변경 모두 취소"
        confirmLabel="모두 취소"
        danger
        busy={busy}
        onConfirm={() => void confirmDiscardAll()}
        onCancel={() => setAskDiscardAll(false)}
        description={`아직 게시하지 않은 변경 ${drafts.length}건이 사라집니다. 이미 게시된 것은 그대로입니다.`}
      >
        {draftSummary && (
          <div className="rounded-md bg-neutral-50 border border-neutral-200 px-3 py-2 text-sm text-neutral-700">
            {draftSummary}
          </div>
        )}
      </ConfirmDialog>

      <ConfirmDialog
        open={!!revertTarget}
        title="버전 되돌리기"
        confirmLabel="되돌리기"
        danger
        busy={busy}
        onConfirm={() => void confirmRevert()}
        onCancel={() => setRevertTarget(null)}
        description="이 버전에서 적용한 변경을 되돌립니다. 되돌린 뒤에는 다시 게시해야 원래대로 돌아갑니다."
      >
        {revertTarget && (
          <div className="rounded-md bg-neutral-50 border border-neutral-200 px-3 py-2 text-sm">
            <div className="text-neutral-800">
              버전 #{revertTarget.id}{revertTarget.label ? ` · ${revertTarget.label}` : ''}
            </div>
            <div className="text-xs text-neutral-500 mt-1">
              {formatDateTime(revertTarget.published_at)} · 변경 {revertTarget.op_count}건
            </div>
          </div>
        )}
      </ConfirmDialog>
    </div>
  )
}
