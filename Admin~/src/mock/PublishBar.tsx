// 가짜 데이터 모드 맨 위 띠 + "게시" 버튼. 게시는 미리보기 서버(scripts/mock-server.mjs)가 콘솔 파일만 커밋·푸시한다.
import { useCallback, useState } from 'react'
import { UploadCloud, ExternalLink } from 'lucide-react'
import { ConfirmDialog } from '../components/ui/ConfirmDialog'

type Pending = { status: string; file: string }
type PublishResult = { ok: boolean; commit?: string; message?: string; files?: number; actionsUrl?: string; error?: string }

declare global {
  interface Window { __PUBLISH_TOKEN?: string }
}

async function call<T>(path: string, method: 'GET' | 'POST'): Promise<T> {
  const res = await fetch(path, { method, headers: { 'x-publish-token': window.__PUBLISH_TOKEN ?? '' } })
  const body = (await res.json().catch(() => null)) as T | null
  if (!body) throw new Error('미리보기 서버가 응답하지 않습니다. npm run preview:mock 으로 띄웠는지 확인하세요.')
  return body
}

const STATUS_LABEL: Record<string, string> = { M: '수정', A: '추가', D: '삭제', '??': '새 파일', R: '이름 변경' }

export function PublishBar() {
  const [open, setOpen] = useState(false)
  const [files, setFiles] = useState<Pending[] | null>(null)
  const [message, setMessage] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [done, setDone] = useState<PublishResult | null>(null)

  const openDialog = useCallback(async () => {
    setError('')
    setFiles(null)
    setOpen(true)
    try {
      const s = await call<{ ok: boolean; files: Pending[]; message: string; error?: string }>('/__publish/status', 'GET')
      if (!s.ok) throw new Error(s.error ?? '게시할 내용을 읽지 못했습니다.')
      setFiles(s.files)
      setMessage(s.message)
    } catch (e) {
      setError(e instanceof Error ? e.message : '게시할 내용을 읽지 못했습니다.')
    }
  }, [])

  const publish = useCallback(async () => {
    setBusy(true)
    setError('')
    try {
      const r = await call<PublishResult>('/__publish', 'POST')
      if (!r.ok) throw new Error(r.error ?? '게시하지 못했습니다.')
      setDone(r)
      setOpen(false)
    } catch (e) {
      setError(e instanceof Error ? e.message : '게시하지 못했습니다.')
    } finally {
      setBusy(false)
    }
  }, [])

  return (
    <>
      <div className="bg-amber-400 text-amber-950 text-xs font-medium">
        <div className="max-w-6xl mx-auto px-6 py-1 flex items-center gap-3">
          <span className="truncate">가짜 데이터 모드 — 서버에 연결하지 않습니다. 숫자는 모두 만든 값입니다.</span>
          {done ? (
            <a href={done.actionsUrl} target="_blank" rel="noreferrer"
              className="ml-auto inline-flex items-center gap-1 whitespace-nowrap underline">
              게시됨({done.commit}) · 몇 분 뒤 실제 콘솔에 반영 <ExternalLink className="w-3 h-3" />
            </a>
          ) : (
            <button type="button" onClick={() => void openDialog()}
              className="ml-auto inline-flex items-center gap-1 h-6 px-2.5 rounded bg-amber-950 text-amber-50 hover:bg-amber-900 whitespace-nowrap">
              <UploadCloud className="w-3.5 h-3.5" /> 게시
            </button>
          )}
        </div>
      </div>

      <ConfirmDialog
        open={open}
        title="이 화면을 실제 콘솔에 게시할까요?"
        description="아래 콘솔 파일이 저장소에 올라가고, 몇 분 뒤 모든 게임의 실제 운영 콘솔에 반영됩니다."
        showTarget={false}
        confirmLabel="게시"
        busy={busy}
        confirmDisabled={!files || files.length === 0}
        error={error || undefined}
        onConfirm={() => void publish()}
        onCancel={() => setOpen(false)}
      >
        {files === null ? (
          !error && <p className="text-sm text-neutral-500">게시할 내용을 확인하는 중…</p>
        ) : files.length === 0 ? (
          <p className="text-sm text-neutral-600">게시할 변경이 없습니다.</p>
        ) : (
          <div className="space-y-2">
            {message && <p className="text-sm text-neutral-800">{message}</p>}
            <ul className="max-h-48 overflow-y-auto rounded border border-neutral-200 divide-y divide-neutral-100 text-xs">
              {files.map((f) => (
                <li key={f.file} className="flex gap-2 px-2 py-1">
                  <span className="w-14 shrink-0 text-neutral-500">{STATUS_LABEL[f.status] ?? f.status}</span>
                  <span className="truncate font-mono text-neutral-800">{f.file.replace(/^Admin~\//, '')}</span>
                </li>
              ))}
            </ul>
          </div>
        )}
      </ConfirmDialog>
    </>
  )
}
