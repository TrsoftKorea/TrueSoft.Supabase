import { useEffect, useState, useCallback } from 'react'
import { Plus, Pencil } from 'lucide-react'
import { callAdmin, NotAuthenticatedError } from '../lib/api'
import type { ProjectTarget } from '../lib/projectTarget'
import { WhiteCard } from '../components/ui/Card'
import { PageHeader } from '../components/ui/PageHeader'
import { TableStatusRow } from '../components/ui/TableStatusRow'
import { ErrorBanner } from '../components/ui/ErrorBanner'
import { ConfirmDialog } from '../components/ui/ConfirmDialog'
import { Button } from '../components/ui/Button'
import { formatDateTime } from '../components/ui/format'
import { inputCls } from '../components/ui/Input'

type ServerRow = {
  id: string
  server_code: string
  display_name: string
  allow_transfers: boolean
  is_default: boolean
  player_count: number
  created_at: string
}

type Draft = { id: string | null; serverCode: string; displayName: string; allowTransfers: boolean }


export default function GameServers({
  target,
  onUnauthenticated,
}: {
  target: ProjectTarget
  onUnauthenticated: () => void
}) {
  const [rows, setRows] = useState<ServerRow[]>([])
  const [loading, setLoading] = useState(true)
  const [loadFailed, setLoadFailed] = useState(false)
  const [error, setError] = useState('')
  const [draft, setDraft] = useState<Draft | null>(null)
  const [saving, setSaving] = useState(false)
  const [saveError, setSaveError] = useState('')

  const report = useCallback(
    (e: unknown, fallback: string) => {
      if (e instanceof NotAuthenticatedError) { onUnauthenticated(); return }
      setError(e instanceof Error ? e.message : fallback)
    },
    [onUnauthenticated],
  )

  const load = useCallback(async () => {
    setLoading(true)
    try {
      setRows(await callAdmin<ServerRow[]>(target, 'servers.list'))
      setLoadFailed(false)
    } catch (e: unknown) {
      setLoadFailed(true)
      report(e, '서버 목록을 불러오지 못했습니다.')
    } finally {
      setLoading(false)
    }
  }, [target, report])

  useEffect(() => { void load() }, [load])

  const save = async () => {
    if (!draft) return
    setSaving(true)
    setSaveError('')
    try {
      await callAdmin(target, 'servers.upsert', draft)
      setDraft(null)
      await load()
    } catch (e: unknown) {
      // 창은 열어 둔다 — 닫으면 입력이 사라지고 실패 사유도 배경에 가려 안 보인다.
      if (e instanceof NotAuthenticatedError) { setDraft(null); onUnauthenticated(); return }
      setSaveError(e instanceof Error ? e.message : '저장하지 못했습니다.')
    } finally {
      setSaving(false)
    }
  }

  const isNew = draft?.id === null
  const canSave = !!draft && draft.displayName.trim() !== '' && (!isNew || draft.serverCode.trim() !== '')

  return (
    <div className="space-y-4">
      <PageHeader
        title="게임 서버"
        description="플레이어가 속하는 서버를 추가하고 이름·이전 허용 여부를 정합니다."
        actions={
          <Button variant="primary" onClick={() => setDraft({ id: null, serverCode: '', displayName: '', allowTransfers: true })}>
            <Plus className="w-4 h-4" />서버 추가
          </Button>
        }
      />
      <ErrorBanner message={error} onDismiss={() => setError('')} />

      <WhiteCard>
        <div className="overflow-auto">
          <table className="w-full text-sm">
            <thead className="bg-neutral-50 text-neutral-500 text-xs">
              <tr>
                <th className="text-left px-4 py-2.5 font-medium">코드</th>
                <th className="text-left px-4 py-2.5 font-medium">이름</th>
                <th className="text-right px-4 py-2.5 font-medium">플레이어</th>
                <th className="text-left px-4 py-2.5 font-medium">이전 받기</th>
                <th className="text-left px-4 py-2.5 font-medium">추가한 날</th>
                <th className="px-4 py-2.5" />
              </tr>
            </thead>
            <tbody>
              {loading || loadFailed || rows.length === 0 ? (
                <TableStatusRow loading={loading} failed={loadFailed} empty={rows.length === 0} colSpan={6} emptyText="서버가 없습니다." />
              ) : (
                rows.map((s) => (
                  <tr key={s.id} className="border-t border-neutral-100">
                    <td className="px-4 py-3 font-mono text-neutral-800 whitespace-nowrap">
                      {s.server_code}
                      {s.is_default && (
                        <span className="ml-2 rounded bg-brand-soft px-1.5 py-0.5 text-[11px] font-sans text-[#1677ff]">기본</span>
                      )}
                    </td>
                    <td className="px-4 py-3 text-neutral-800">{s.display_name}</td>
                    <td className="px-4 py-3 text-right text-neutral-700">{s.player_count.toLocaleString('ko-KR')}</td>
                    <td className="px-4 py-3 whitespace-nowrap">
                      {s.allow_transfers
                        ? <span className="text-emerald-600">허용</span>
                        : <span className="text-neutral-400">막음</span>}
                    </td>
                    <td className="px-4 py-3 text-neutral-600 whitespace-nowrap">{formatDateTime(s.created_at)}</td>
                    <td className="px-4 py-3 text-right">
                      <Button
                        size="sm"
                        onClick={() => setDraft({ id: s.id, serverCode: s.server_code, displayName: s.display_name, allowTransfers: s.allow_transfers })}
                      >
                        <Pencil className="w-3 h-3" />수정
                      </Button>
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      </WhiteCard>

      <p className="text-xs text-neutral-500">
        새로 가입한 플레이어는 "기본" 표시가 붙은 서버에 들어갑니다. 서버 코드는 게임이 서버를 고를 때 쓰므로 추가한 뒤에는 바꿀 수 없습니다.
      </p>

      <ConfirmDialog
        open={!!draft}
        title={isNew ? '서버 추가' : '서버 수정'}
        confirmLabel={isNew ? '추가' : '저장'}
        busy={saving}
        confirmDisabled={!canSave}
        error={saveError}
        onConfirm={() => void save()}
        onCancel={() => { setDraft(null); setSaveError('') }}
      >
        {draft && (
          <div className="space-y-3">
            <div>
              <label className="block text-xs font-medium text-neutral-500 mb-1">서버 코드</label>
              <input
                value={draft.serverCode}
                disabled={!isNew}
                onChange={(e) => setDraft({ ...draft, serverCode: e.target.value })}
                className={inputCls + ' w-full font-mono disabled:bg-neutral-50 disabled:text-neutral-500'}
              />
              {isNew && <p className="mt-1 text-xs text-neutral-400">영문·숫자·_·- 로 32자 이내. 추가한 뒤에는 바꿀 수 없습니다.</p>}
            </div>
            <div>
              <label className="block text-xs font-medium text-neutral-500 mb-1">이름</label>
              <input
                value={draft.displayName}
                onChange={(e) => setDraft({ ...draft, displayName: e.target.value })}
                className={inputCls + ' w-full'}
              />
            </div>
            <label className="flex items-center gap-2 text-sm text-neutral-700">
              <input
                type="checkbox"
                checked={draft.allowTransfers}
                onChange={(e) => setDraft({ ...draft, allowTransfers: e.target.checked })}
              />
              다른 서버에서 이전해 오는 것을 허용
            </label>
          </div>
        )}
      </ConfirmDialog>
    </div>
  )
}
