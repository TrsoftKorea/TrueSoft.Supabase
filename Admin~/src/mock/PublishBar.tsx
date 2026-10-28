// 가짜 데이터 모드 맨 위 띠 + "게시" 버튼 + 실제 반영 상태.
// 게시는 미리보기 서버(scripts/mock-server.mjs)가 콘솔 화면과 SDK·DB 변경을 커밋 두 개로 나눠 한 번에 푸시한다.
import { useCallback, useEffect, useState, type ReactNode } from 'react'
import { UploadCloud, ExternalLink, ChevronDown, ChevronUp, RefreshCw } from 'lucide-react'
import { ConfirmDialog } from '../components/ui/ConfirmDialog'

type Pending = { status: string; file: string; orig?: string; hash: string }
type Group = { files: Pending[]; message: string; lines: { added: number; removed: number } }
type PublishStatus = {
  ok: boolean; error?: string
  console: Group & { deploys: boolean }
  sdk: Group
  unpushed: { hash: string; subject: string }[]
  blocked: string[]
}
type PublishResult = { ok: boolean; commits?: { kind: string; hash: string }[]; alsoPushed?: number; error?: string }
type Phase = 'unpushed' | 'starting' | 'running' | 'failed' | 'propagating' | 'live' | 'unknown'
type Deploy = {
  ok: boolean; error?: string; checkedAt: string; phase: Phase
  console: { sha: string; committedAt: string; subject: string; pushed: boolean }
  run: {
    status?: string; conclusion?: string | null; url?: string; createdAt?: string; updatedAt?: string; error?: boolean
    steps?: { done: number; total: number; current: string | null } | null
  } | null
  live: { sha?: string; builtAt?: string | null; current?: boolean; error?: boolean } | null
  waiting: { console: number; sdk: number; unpushed: number }
  liveUrl: string; actionsUrl: string
}

declare global {
  interface Window { __PUBLISH_TOKEN?: string }
}

async function call<T>(path: string, method: 'GET' | 'POST', payload?: unknown): Promise<T> {
  const res = await fetch(path, {
    method,
    headers: { 'x-publish-token': window.__PUBLISH_TOKEN ?? '', ...(payload ? { 'Content-Type': 'application/json' } : {}) },
    ...(payload ? { body: JSON.stringify(payload) } : {}),
  })
  const body = (await res.json().catch(() => null)) as T | null
  if (!body) throw new Error('미리보기 서버가 응답하지 않습니다. npm run preview:mock 으로 띄웠는지 확인하세요.')
  return body
}

const STATUS_LABEL: Record<string, string> = { M: '수정', A: '추가', D: '삭제', '??': '새 파일', R: '이름 변경' }

const timeText = (iso: string | null | undefined) => {
  if (!iso) return '-'
  const d = new Date(iso)
  return Number.isNaN(d.getTime()) ? '-' : d.toLocaleString('ko-KR', { month: 'numeric', day: 'numeric', hour: '2-digit', minute: '2-digit', hour12: false })
}

type Summary = { text: string; tone: 'ok' | 'busy' | 'warn' | 'bad' }

/** 몇 초·몇 분 걸렸는지. */
const durationText = (ms: number) => {
  const s = Math.max(0, Math.round(ms / 1000))
  return s < 60 ? `${s}초` : `${Math.floor(s / 60)}분 ${s % 60}초`
}

const BUSY: Phase[] = ['starting', 'running', 'propagating']

/** 띠에 한 줄로 보일 실제 반영 상태. elapsed 는 배포를 시작한 지 얼마나 됐는지(진행 중일 때만). */
function summarize(d: Deploy | null, failed: string, elapsed: string, finished: string): Summary {
  if (failed) return { text: '반영 상태를 확인하지 못했습니다', tone: 'warn' }
  if (!d) return { text: '실제 반영 확인 중…', tone: 'busy' }
  const waiting = d.waiting.console + d.waiting.sdk + d.waiting.unpushed
  const tail = waiting ? ` · 올릴 변경 ${waiting}개` : ''
  const since = elapsed ? ` · ${elapsed}째` : ''
  switch (d.phase) {
    case 'starting': return { text: `배포 시작 기다리는 중…${since}`, tone: 'busy' }
    case 'running': {
      const st = d.run?.steps
      return { text: `배포 중${st ? ` · ${st.done + 1}/${st.total} 단계${st.current ? ` ${st.current}` : ''}` : ''}${since}`, tone: 'busy' }
    }
    case 'propagating': return { text: `배포 끝 · 실제 콘솔 반영 기다리는 중…${since}`, tone: 'busy' }
    case 'failed': return { text: `실제 콘솔 배포 실패${tail}`, tone: 'bad' }
    case 'live':
      if (finished) return { text: `실제 콘솔 반영 완료 (${finished})${tail}`, tone: 'ok' }
      return { text: waiting ? `실제 콘솔은 최신${tail}` : '실제 콘솔 최신 · 올릴 변경 없음', tone: waiting ? 'warn' : 'ok' }
    case 'unpushed': return { text: `콘솔 화면이 아직 GitHub 에 안 올라감${tail}`, tone: 'warn' }
    default: return { text: `실제 콘솔 판 확인 불가${tail}`, tone: 'warn' }
  }
}

const TONE: Record<Summary['tone'], string> = {
  ok: 'bg-emerald-600 text-white',
  busy: 'bg-amber-950/80 text-amber-50',
  warn: 'bg-amber-950 text-amber-50',
  bad: 'bg-red-600 text-white',
}

function FileGroup({ title, note, group }: { title: string; note?: string | undefined; group: Group }) {
  // 요약 첫 줄(제목)만 늘 보이고, 나머지 줄과 파일 목록은 눌러서 펼친다 — 다 펼쳐 두면 확인 창이 화면보다 길어진다.
  const [open, setOpen] = useState(false)
  if (group.files.length === 0) return null
  // 요약 글: 첫 줄은 제목, 나머지 "- " 줄은 항목.
  const [headline = '', ...rest] = group.message.split('\n').map((l) => l.trim()).filter(Boolean)
  const details = rest.map((l) => l.replace(/^[-•]\s*/, ''))
  return (
    <div className="rounded-lg border border-neutral-200">
      <button type="button" onClick={() => setOpen((v) => !v)}
        className="w-full flex items-start gap-3 px-3.5 py-3 text-left hover:bg-neutral-50 rounded-lg">
        <span className="min-w-0 flex-1 space-y-1.5">
          <span className="flex items-center gap-2 text-xs">
            <span className="px-1.5 py-0.5 rounded bg-neutral-100 text-neutral-600 font-medium whitespace-nowrap">{title}</span>
            <span className="text-neutral-500 whitespace-nowrap">
              파일 {group.files.length}개 · <span className="text-emerald-700">+{group.lines.added}</span> <span className="text-red-600">−{group.lines.removed}</span>줄
            </span>
          </span>
          <span className="block text-[15px] font-semibold leading-snug text-neutral-900">{headline || '요약이 없습니다'}</span>
          {note && <span className="block text-xs text-neutral-500">{note}</span>}
        </span>
        {open ? <ChevronUp className="w-4 h-4 mt-1 shrink-0 text-neutral-400" /> : <ChevronDown className="w-4 h-4 mt-1 shrink-0 text-neutral-400" />}
      </button>
      {open && (
        <div className="border-t border-neutral-100 px-3.5 py-3 space-y-3">
          {details.length > 0 && (
            <ul className="list-disc pl-4 space-y-1.5 text-[13px] leading-relaxed text-neutral-700 marker:text-neutral-400">
              {details.map((d, i) => <li key={i}>{d}</li>)}
            </ul>
          )}
          <p className="text-xs font-medium text-neutral-500">바뀐 파일</p>
          <ul className="max-h-32 overflow-y-auto rounded border border-neutral-200 divide-y divide-neutral-100 text-xs">
            {group.files.map((f) => (
              <li key={f.file} className="flex gap-2 px-2 py-1">
                <span className="w-14 shrink-0 text-neutral-500">{STATUS_LABEL[f.status] ?? f.status}</span>
                <span className="truncate font-mono text-neutral-800">{f.orig ? `${f.orig} → ${f.file}` : f.file}</span>
              </li>
            ))}
          </ul>
        </div>
      )}
    </div>
  )
}

function Step({ label, state, children }: { label: string; state: 'done' | 'busy' | 'todo' | 'bad'; children?: ReactNode }) {
  const mark = state === 'done' ? '✓' : state === 'busy' ? '…' : state === 'bad' ? '✕' : '·'
  const cls = state === 'done' ? 'text-emerald-700' : state === 'bad' ? 'text-red-600' : state === 'busy' ? 'text-amber-700' : 'text-neutral-400'
  return (
    <li className="flex gap-2">
      <span className={`w-4 shrink-0 font-semibold ${cls}`}>{mark}</span>
      <span className="text-neutral-800 whitespace-nowrap">{label}</span>
      <span className="text-neutral-500 min-w-0">{children}</span>
    </li>
  )
}

export function PublishBar() {
  const [open, setOpen] = useState(false)
  const [status, setStatus] = useState<PublishStatus | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [done, setDone] = useState<PublishResult | null>(null)
  const [deploy, setDeploy] = useState<Deploy | null>(null)
  const [deployError, setDeployError] = useState('')
  const [expanded, setExpanded] = useState(false)

  const refresh = useCallback(async () => {
    try {
      const d = await call<Deploy>('/__publish/deploy', 'GET')
      if (!d.ok) throw new Error(d.error ?? '')
      setDeploy(d); setDeployError('')
    } catch (e) {
      setDeployError(e instanceof Error ? e.message : '확인하지 못했습니다.')
    }
  }, [])

  // 배포가 진행 중이거나 방금 게시해 아직 반영 전이면 3초마다, 아니면 1분마다 다시 본다.
  const phase = deploy?.phase
  const watching = (!!phase && BUSY.includes(phase)) || (!!done && phase !== 'live' && phase !== 'failed')
  useEffect(() => {
    void refresh()
    const id = window.setInterval(() => void refresh(), watching ? 3_000 : 60_000)
    return () => window.clearInterval(id)
  }, [refresh, watching])

  // 지난 시간 표시를 1초마다 다시 그린다(진행 중일 때만).
  const [now, setNow] = useState(() => Date.now())
  useEffect(() => {
    if (!watching) return
    const id = window.setInterval(() => setNow(Date.now()), 1_000)
    return () => window.clearInterval(id)
  }, [watching])

  // 게시한 순간부터 실제 콘솔에 반영될 때까지 걸린 시간.
  const [publishedAt, setPublishedAt] = useState<number | null>(null)
  const [finished, setFinished] = useState('')
  useEffect(() => {
    if (publishedAt && phase === 'live' && !finished) setFinished(durationText(Date.now() - publishedAt))
  }, [publishedAt, phase, finished])
  const startedAt = publishedAt ?? (deploy?.run?.createdAt ? Date.parse(deploy.run.createdAt) : null)
  const elapsed = watching && startedAt ? durationText(now - startedAt) : ''

  const openDialog = useCallback(async () => {
    setError('')
    setStatus(null)
    setOpen(true)
    try {
      const s = await call<PublishStatus>('/__publish/status', 'GET')
      if (!s.ok) throw new Error(s.error ?? '게시할 내용을 읽지 못했습니다.')
      setStatus(s)
    } catch (e) {
      setError(e instanceof Error ? e.message : '게시할 내용을 읽지 못했습니다.')
    }
  }, [])

  const publish = useCallback(async () => {
    if (!status) return
    setBusy(true)
    setError('')
    try {
      // 이 창에서 본 목록·내용만 올린다 — 그사이 바뀌었으면 서버가 거절한다.
      const seen = [...status.sdk.files, ...status.console.files].map((f) => ({ file: f.file, hash: f.hash }))
      const r = await call<PublishResult>('/__publish', 'POST', { files: seen, unpushed: status.unpushed.length })
      if (!r.ok) throw new Error(r.error ?? '게시하지 못했습니다.')
      setDone(r)
      setPublishedAt(Date.now())
      setFinished('')
      setOpen(false)
      setExpanded(true)
      void refresh()
    } catch (e) {
      setError(e instanceof Error ? e.message : '게시하지 못했습니다.')
    } finally {
      setBusy(false)
    }
  }, [refresh, status])

  const summary = summarize(deploy, deployError, elapsed, finished)
  const empty = !!status && status.console.files.length === 0 && status.sdk.files.length === 0 && status.unpushed.length === 0
  const run = deploy?.run
  const runState: 'done' | 'busy' | 'todo' | 'bad' = phase === 'starting' || phase === 'running' ? 'busy'
    : phase === 'failed' ? 'bad'
    : run?.conclusion === 'success' ? 'done' : 'todo'
  const runText = phase === 'starting' ? '시작 기다리는 중'
    : phase === 'running'
      ? `진행 중${run?.steps ? ` · ${run.steps.done + 1}/${run.steps.total} 단계${run.steps.current ? ` ${run.steps.current}` : ''}` : ''}`
    : phase === 'failed' ? '실패'
    : run?.conclusion === 'success'
      ? `성공${run.createdAt && run.updatedAt ? ` · ${durationText(Date.parse(run.updatedAt) - Date.parse(run.createdAt))} 걸림` : ''}`
    : run?.error ? '배포 기록을 읽지 못했습니다' : '배포 기록 없음'
  const liveState: 'done' | 'busy' | 'todo' = deploy?.live?.current ? 'done' : phase === 'propagating' ? 'busy' : 'todo'

  return (
    <>
      <div className="bg-amber-400 text-amber-950 text-xs font-medium">
        <div className="max-w-6xl mx-auto px-6 py-1 flex items-center gap-3">
          <span className="truncate">가짜 데이터 모드 — 서버에 연결하지 않습니다. 숫자는 모두 만든 값입니다.</span>
          <button type="button" onClick={() => setExpanded((v) => !v)}
            title={deploy ? `마지막 확인 ${timeText(deploy.checkedAt)}` : undefined}
            className={`ml-auto inline-flex items-center gap-1 h-6 px-2.5 rounded whitespace-nowrap ${TONE[summary.tone]}`}>
            {summary.text}
            {expanded ? <ChevronUp className="w-3.5 h-3.5" /> : <ChevronDown className="w-3.5 h-3.5" />}
          </button>
          <button type="button" onClick={() => void openDialog()}
            className="inline-flex items-center gap-1 h-6 px-2.5 rounded bg-amber-950 text-amber-50 hover:bg-amber-900 whitespace-nowrap">
            <UploadCloud className="w-3.5 h-3.5" /> 게시
          </button>
        </div>
      </div>

      {expanded && (
        <div className="bg-amber-50 border-b border-amber-200 text-xs">
          <div className="max-w-6xl mx-auto px-6 py-2.5 space-y-2">
            {done?.commits && (
              <p className="text-neutral-800">
                방금 게시함 — 커밋 {done.commits.map((c) => `${c.hash}(${c.kind === 'console' ? '콘솔 화면' : 'SDK·DB'})`).join(', ')}
                {done.alsoPushed ? ` · 앞서 쌓여 있던 커밋 ${done.alsoPushed}개도 함께 올림` : ''}
              </p>
            )}
            {deployError && <p className="text-red-600">{deployError}</p>}
            {deploy && (
              <ul className="space-y-1">
                <Step label="콘솔 화면 마지막 변경" state="done">
                  {deploy.console.sha} · {deploy.console.subject} · {timeText(deploy.console.committedAt)}
                </Step>
                <Step label="GitHub 에 올라감" state={deploy.console.pushed ? 'done' : 'todo'}>
                  {deploy.console.pushed ? '' : '아직 안 올라감'}
                </Step>
                <Step label="실제 콘솔 배포" state={runState}>
                  {runText}
                  {run?.url && (
                    <a href={run.url} target="_blank" rel="noreferrer" className="ml-1 inline-flex items-center gap-0.5 underline">
                      기록 보기 <ExternalLink className="w-3 h-3" />
                    </a>
                  )}
                </Step>
                <Step label="실제 콘솔이 이 판인지" state={liveState}>
                  {deploy.live?.error ? '실제 콘솔의 판 정보를 읽지 못했습니다'
                    : deploy.live?.current ? `맞습니다 (${deploy.live.sha} · ${timeText(deploy.live.builtAt)} 빌드)`
                    : phase === 'propagating' ? `새 판이 퍼지는 중 (지금 ${deploy.live?.sha ?? '-'})`
                    : `아직 이전 판입니다 (${deploy.live?.sha ?? '-'})`}
                  <a href={deploy.liveUrl} target="_blank" rel="noreferrer" className="ml-1 inline-flex items-center gap-0.5 underline">
                    실제 콘솔 열기 <ExternalLink className="w-3 h-3" />
                  </a>
                </Step>
                <Step label="올릴 변경" state={deploy.waiting.console + deploy.waiting.sdk + deploy.waiting.unpushed ? 'todo' : 'done'}>
                  {deploy.waiting.console + deploy.waiting.sdk + deploy.waiting.unpushed
                    ? `콘솔 화면 ${deploy.waiting.console}개 · SDK·DB ${deploy.waiting.sdk}개${deploy.waiting.unpushed ? ` · 안 올린 커밋 ${deploy.waiting.unpushed}개` : ''}`
                    : '없음'}
                </Step>
              </ul>
            )}
            <div className="flex items-center gap-3 text-neutral-500">
              <span>마지막 확인 {timeText(deploy?.checkedAt)}</span>
              <button type="button" onClick={() => void refresh()} className="inline-flex items-center gap-1 underline">
                <RefreshCw className="w-3 h-3" /> 지금 확인
              </button>
            </div>
          </div>
        </div>
      )}

      <ConfirmDialog
        open={open}
        title="변경을 게시할까요?"
        description="콘솔 화면은 몇 분 뒤 모든 게임의 실제 운영 콘솔에 반영됩니다. SDK 변경은 GitHub 에 올라가 게임이 패키지를 새로 받을 때 반영됩니다."
        showTarget={false}
        confirmLabel="게시"
        busy={busy}
        confirmDisabled={!status || empty || status.blocked.length > 0}
        error={error || undefined}
        onConfirm={() => void publish()}
        onCancel={() => setOpen(false)}
      >
        {status === null ? (
          !error && <p className="text-sm text-neutral-500">게시할 내용을 확인하는 중…</p>
        ) : empty ? (
          <p className="text-sm text-neutral-600">게시할 변경이 없습니다.</p>
        ) : (
          <div className="space-y-2">
            {status.blocked.length > 0 && (
              <p className="text-sm text-red-600">
                비밀값이 들었을 수 있는 새 파일이 있어 게시할 수 없습니다: {status.blocked.join(', ')}
              </p>
            )}
            <FileGroup title="콘솔 화면" group={status.console}
              note={status.console.deploys ? undefined : '화면 코드는 그대로라 실제 콘솔은 다시 배포되지 않습니다.'} />
            <FileGroup title="SDK·DB·문서" group={status.sdk} />
            {status.unpushed.length > 0 && (
              <div className="space-y-1">
                <span className="text-sm font-semibold text-neutral-900">앞서 만들어 둔 커밋 {status.unpushed.length}개도 함께 올라갑니다</span>
                <ul className="text-xs text-neutral-700">
                  {status.unpushed.map((c) => <li key={c.hash} className="font-mono">{c.hash} {c.subject}</li>)}
                </ul>
              </div>
            )}
          </div>
        )}
      </ConfirmDialog>
    </>
  )
}
