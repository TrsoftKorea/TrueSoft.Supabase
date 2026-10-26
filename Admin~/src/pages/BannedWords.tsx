import { useEffect, useState, useCallback } from 'react'
import { Plus, X, Loader2 } from 'lucide-react'
import { callAdmin, NotAuthenticatedError } from '../lib/api'
import type { ProjectTarget } from '../lib/projectTarget'
import { WhiteCard } from '../components/ui/Card'
import { PageHeader } from '../components/ui/PageHeader'
import { ErrorBanner } from '../components/ui/ErrorBanner'
import { ConfirmDialog } from '../components/ui/ConfirmDialog'
import { Button } from '../components/ui/Button'

type Kind = 'word' | 'name'
type Row = { kind: Kind; word: string; created_at: string; created_by: string | null }

const SECTIONS: { kind: Kind; title: string; hint: string }[] = [
  {
    kind: 'word',
    title: '금칙어',
    hint: '채팅에 들어 있으면 그 부분이 ***로 가려집니다. 닉네임에 들어 있으면 그 닉네임을 쓸 수 없습니다. 띄어쓰기·기호를 끼워 넣어도 걸립니다.',
  },
  {
    kind: 'name',
    title: '금지 닉네임',
    hint: '닉네임 전체가 이것과 같으면 쓸 수 없습니다. "운영자"·"GM"처럼 사칭에 쓰일 이름을 넣습니다.',
  },
]

/** 줄바꿈·쉼표로 나눠 받는다 — 목록을 붙여 넣기 쉽게. */
function splitWords(text: string): string[] {
  return text.split(/[\n,]/).map((w) => w.trim()).filter(Boolean)
}

export default function BannedWords({
  target,
  onUnauthenticated,
}: {
  target: ProjectTarget
  onUnauthenticated: () => void
}) {
  const [rows, setRows] = useState<Row[]>([])
  const [loading, setLoading] = useState(true)
  const [loadFailed, setLoadFailed] = useState(false)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [drafts, setDrafts] = useState<Record<Kind, string>>({ word: '', name: '' })
  const [adding, setAdding] = useState<Kind | null>(null)
  const [toDelete, setToDelete] = useState<Row | null>(null)
  const [deleting, setDeleting] = useState(false)

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
      setRows(await callAdmin<Row[]>(target, 'bannedWords.list'))
      setLoadFailed(false)
    } catch (e: unknown) {
      setLoadFailed(true)
      report(e, '금칙어를 불러오지 못했습니다.')
    } finally {
      setLoading(false)
    }
  }, [target, report])

  useEffect(() => { void load() }, [load])

  const add = async (kind: Kind) => {
    const words = splitWords(drafts[kind])
    if (words.length === 0 || adding) return
    setAdding(kind)
    setNotice('')
    try {
      const res = await callAdmin<{ added: number }>(target, 'bannedWords.add', { kind, words })
      const skipped = words.length - res.added
      setNotice(`${res.added}개를 추가했습니다.${skipped > 0 ? ` 이미 있거나 비어 있는 ${skipped}개는 건너뛰었습니다.` : ''}`)
      setDrafts((d) => ({ ...d, [kind]: '' }))
      await load()
    } catch (e: unknown) {
      report(e, '추가하지 못했습니다.')
    } finally {
      setAdding(null)
    }
  }

  const remove = async () => {
    if (!toDelete) return
    setDeleting(true)
    setNotice('')
    try {
      await callAdmin(target, 'bannedWords.delete', { kind: toDelete.kind, words: [toDelete.word] })
      setToDelete(null)
      await load()
    } catch (e: unknown) {
      setToDelete(null)
      report(e, '삭제하지 못했습니다.')
    } finally {
      setDeleting(false)
    }
  }

  return (
    <div className="space-y-4">
      <PageHeader title="금칙어" description="채팅과 닉네임에 쓸 수 없는 말을 정합니다." />
      <ErrorBanner message={error} onDismiss={() => setError('')} />
      {notice && <div className="rounded-md border border-emerald-200 bg-emerald-50 px-4 py-2.5 text-sm text-emerald-700">{notice}</div>}

      {SECTIONS.map((s) => {
        const list = rows.filter((r) => r.kind === s.kind)
        return (
          <WhiteCard key={s.kind} className="p-5 space-y-3">
            <div>
              <h3 className="text-base font-semibold text-neutral-900">
                {s.title} {!loadFailed && <span className="ml-1 text-sm font-normal text-neutral-400">{list.length}개</span>}
              </h3>
              <p className="mt-1 text-xs text-neutral-500">{s.hint}</p>
            </div>

            <div className="flex items-start gap-2">
              <textarea
                value={drafts[s.kind]}
                onChange={(e) => setDrafts((d) => ({ ...d, [s.kind]: e.target.value }))}
                rows={2}
                className="flex-1 px-2.5 py-2 rounded-md border border-neutral-300 text-sm bg-white outline-none focus:border-[#1677ff] focus:ring-1 focus:ring-[#1677ff]"
              />
              <Button
                variant="primary"
                onClick={() => void add(s.kind)}
                disabled={adding !== null || splitWords(drafts[s.kind]).length === 0}
              >
                {adding === s.kind ? <Loader2 className="w-4 h-4 animate-spin" /> : <Plus className="w-4 h-4" />}추가
              </Button>
            </div>
            <p className="text-xs text-neutral-400">여러 개는 줄을 바꾸거나 쉼표로 나눠 한 번에 넣을 수 있습니다.</p>

            {loading && rows.length === 0 ? (
              <div className="py-6 text-center"><Loader2 className="w-5 h-5 animate-spin text-neutral-300 mx-auto" /></div>
            ) : loadFailed && !loading ? (
              <div className="py-4 text-center text-sm text-red-600">목록을 불러오지 못했습니다. 새로고침해 주세요.</div>
            ) : list.length === 0 ? (
              <div className="py-4 text-center text-sm text-neutral-400">아직 없습니다.</div>
            ) : (
              <div className="flex flex-wrap gap-1.5">
                {list.map((r) => (
                  <span
                    key={r.word}
                    title={r.created_by ? `${r.created_by} 추가` : undefined}
                    className="inline-flex items-center gap-1 rounded bg-neutral-100 px-2 py-1 text-sm text-neutral-800"
                  >
                    {r.word}
                    <button onClick={() => setToDelete(r)} className="text-neutral-400 hover:text-red-500" aria-label={`${r.word} 삭제`}>
                      <X className="w-3.5 h-3.5" />
                    </button>
                  </span>
                ))}
              </div>
            )}
          </WhiteCard>
        )
      })}

      <ConfirmDialog
        open={!!toDelete}
        title={toDelete?.kind === 'name' ? '금지 닉네임 삭제' : '금칙어 삭제'}
        confirmLabel="삭제"
        danger
        busy={deleting}
        onConfirm={() => void remove()}
        onCancel={() => setToDelete(null)}
        description="삭제하면 바로 다시 쓸 수 있게 됩니다."
      >
        <div className="rounded-md bg-neutral-50 border border-neutral-200 px-3 py-2 text-sm text-neutral-800">{toDelete?.word}</div>
      </ConfirmDialog>
    </div>
  )
}
