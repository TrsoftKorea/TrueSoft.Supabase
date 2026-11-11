import { useEffect, useState, useCallback } from 'react'
import { Loader2, Plus, Pencil } from 'lucide-react'
import { callAdmin, NotAuthenticatedError } from '../lib/api'
import type { ProjectTarget } from '../lib/projectTarget'
import { PageHeader } from '../components/ui/PageHeader'
import { RefreshButton } from '../components/ui/FilterBar'
import { TableCard } from '../components/ui/DataTable'
import { StatusText, Tag } from '../components/ui/StatusText'
import { ErrorBanner } from '../components/ui/ErrorBanner'
import { EditDialog } from '../components/ui/EditDialog'
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
        description="플레이어가 속하는 서버를 관리합니다."
        actions={<>
          <RefreshButton onClick={() => void load()} loading={loading} />
          <Button variant="primary" onClick={() => setDraft({ id: null, serverCode: '', displayName: '', allowTransfers: true })}>
            <Plus className="w-4 h-4" />서버 추가
          </Button>
        </>}
      />
      <ErrorBanner message={error} onDismiss={() => setError('')} />

      <TableCard
        columns={[
          {
            key: 'code', header: '코드', kind: 'mono', cell: (s) => (
              <>
                {s.server_code}
                {s.is_default && <span className="ml-2"><Tag tone="brand" title="새로 가입한 플레이어가 들어가는 서버">기본</Tag></span>}
              </>
            ),
          },
          { key: 'name', header: '이름', kind: 'name', cell: (s) => s.display_name },
          { key: 'players', header: '플레이어', kind: 'number', cell: (s) => s.player_count.toLocaleString('ko-KR') },
          { key: 'transfer', header: '이전 받기', kind: 'status', cell: (s) => <StatusText tone={s.allow_transfers ? 'ok' : 'muted'}>{s.allow_transfers ? '허용' : '막음'}</StatusText> },
          { key: 'created', header: '등록 시각', kind: 'datetime', cell: (s) => formatDateTime(s.created_at) },
          {
            key: 'actions', header: '', kind: 'actions', cell: (s) => (
              <Button size="sm" onClick={() => setDraft({ id: s.id, serverCode: s.server_code, displayName: s.display_name, allowTransfers: s.allow_transfers })}>
                <Pencil className="w-3 h-3" />수정
              </Button>
            ),
          },
        ]}
        rows={rows}
        rowKey={(s) => s.id}
        loading={loading}
        failed={loadFailed}
        emptyText="서버가 없습니다."
      />

        {draft && (
        <EditDialog
          title={isNew ? '서버 추가' : '서버 수정'}
          busy={saving}
          onClose={() => { setDraft(null); setSaveError('') }}
          error={saveError || undefined}
          footer={<>
            <Button variant="outline" onClick={() => { setDraft(null); setSaveError('') }} disabled={saving}>취소</Button>
            <Button variant="primary" onClick={() => void save()} disabled={saving || !canSave}>
              {saving && <Loader2 className="w-4 h-4 animate-spin" />}
              {isNew ? '추가' : '저장'}
            </Button>
          </>}
        >
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
        </EditDialog>
        )}
    </div>
  )
}
