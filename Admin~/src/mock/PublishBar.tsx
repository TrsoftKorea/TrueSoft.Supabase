// 가짜 데이터 모드 맨 위 띠 + "게시" 버튼 + 실제 반영 상태.
// 게시는 미리보기 서버(scripts/mock-server.mjs)가 콘솔 화면과 SDK·DB 변경을 커밋 두 개로 나눠 한 번에 푸시한다.
import { useCallback, useEffect, useState, type ReactNode } from 'react'
import { UploadCloud, ExternalLink, ChevronDown, ChevronUp, RefreshCw, Check, X, Loader2, CheckCircle2, XCircle, AlertCircle } from 'lucide-react'
import { ConfirmDialog } from '../components/ui/ConfirmDialog'

type Pending = { status: string; file: string; orig?: string; hash: string }
type Group = { files: Pending[]; message: string; lines: { added: number; removed: number } }
type PublishStatus = {
  ok: boolean; error?: string
  console: Group & { deploys: boolean }
  sdk: Group
  unpushed: { hash: string; full: string; subject: string }[]
  messages: string
  blocked: string[]
  unplanned: string[]
}
type PublishResult = { ok: boolean; commits?: { kind: string; hash: string }[]; alsoPushed?: number; error?: string }
type Phase = 'unpushed' | 'starting' | 'running' | 'failed' | 'propagating' | 'live' | 'elsewhere' | 'unknown'
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

/** 띠의 상태 표시. label 은 지금 상태 한 마디, detail 은 진행 단계, time 은 걸린·지난 시간. */
type Summary = { label: string; tone: 'ok' | 'busy' | 'warn' | 'bad'; detail?: string; time?: string }

/** 몇 초·몇 분 걸렸는지. */
const durationText = (ms: number) => {
  const s = Math.max(0, Math.round(ms / 1000))
  return s < 60 ? `${s}초` : `${Math.floor(s / 60)}분 ${s % 60}초`
}

const BUSY: Phase[] = ['starting', 'running', 'propagating']

/** 띠에 보일 실제 반영 상태. 올릴 변경 수는 따로 보인다. elapsed 는 배포를 시작한 지 얼마나 됐는지(진행 중일 때만). */
function summarize(d: Deploy | null, failed: string, elapsed: string, finished: string): Summary {
  if (failed) return { label: '상태 확인 실패', tone: 'warn' }
  if (!d) return { label: '확인 중', tone: 'busy' }
  const since = elapsed ? `${elapsed}째` : undefined
  const at = (s: Summary) => (since ? { ...s, time: since } : s)
  switch (d.phase) {
    case 'starting': return at({ label: '배포 대기', tone: 'busy' })
    case 'running': {
      const st = d.run?.steps
      return at({ label: '배포 중', tone: 'busy', ...(st ? { detail: `${st.done + 1}/${st.total}${st.current ? ` ${st.current}` : ''}` } : {}) })
    }
    case 'propagating': return at({ label: '반영 대기', tone: 'busy', detail: '배포 끝' })
    case 'failed': return { label: '배포 실패', tone: 'bad' }
    case 'live': return finished ? { label: '반영 완료', tone: 'ok', time: finished } : { label: '실제 콘솔 최신', tone: 'ok' }
    case 'unpushed': return { label: 'GitHub 에 안 올라감', tone: 'warn' }
    case 'elsewhere': return { label: '다른 PC 에서 올린 판', tone: 'warn' }
    default: return { label: '판 확인 불가', tone: 'warn' }
  }
}

// 상태 아이콘 색. 진행 중이면 돈다.
function StatusIcon({ tone }: { tone: Summary['tone'] }) {
  if (tone === 'ok') return <CheckCircle2 className="w-3.5 h-3.5 text-[#047857]" />
  if (tone === 'busy') return <Loader2 className="w-3.5 h-3.5 text-[#1d4ed8] animate-spin" />
  if (tone === 'bad') return <XCircle className="w-3.5 h-3.5 text-[#dc2626]" />
  return <AlertCircle className="w-3.5 h-3.5 opacity-70" />
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

type StepState = 'done' | 'busy' | 'todo' | 'bad'

function StepMark({ state }: { state: StepState }) {
  if (state === 'done') return <span className="w-5 h-5 rounded-full bg-emerald-600 text-white grid place-items-center shrink-0"><Check className="w-3 h-3" strokeWidth={3} /></span>
  if (state === 'bad') return <span className="w-5 h-5 rounded-full bg-red-600 text-white grid place-items-center shrink-0"><X className="w-3 h-3" strokeWidth={3} /></span>
  if (state === 'busy') return <span className="w-5 h-5 rounded-full border-2 border-blue-500 grid place-items-center shrink-0"><Loader2 className="w-3 h-3 text-blue-600 animate-spin" /></span>
  return <span className="w-5 h-5 rounded-full border-2 border-neutral-300 shrink-0" />
}

/** 진행 표시의 한 칸 — 동그라미 + 이름 + 한 줄 설명. 마지막 칸이 아니면 오른쪽으로 이음선. */
function Step({ label, state, last, children }: { label: string; state: StepState; last?: boolean; children?: ReactNode }) {
  return (
    <li className="flex-1 min-w-0">
      <div className="flex items-center gap-2">
        <StepMark state={state} />
        <span className="text-[13px] font-medium text-neutral-900 whitespace-nowrap">{label}</span>
        {!last && <span className={`flex-1 h-px ${state === 'done' ? 'bg-emerald-500' : 'bg-neutral-200'}`} />}
      </div>
      <div className="pl-7 pr-3 mt-0.5 text-xs text-neutral-500 truncate">{children}</div>
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
      const r = await call<PublishResult>('/__publish', 'POST', { files: seen, unpushed: status.unpushed.map((c) => c.full), messages: status.messages })
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
  const waitingCount = deploy ? deploy.waiting.console + deploy.waiting.sdk + deploy.waiting.unpushed : 0
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
      {/* 띠는 화면 모드를 따르지 않는다 — 색 이름(amber 등)은 어두운 화면에서 값이 바뀌어 글자만 뒤집히므로 띠 안은 고정 색으로 쓴다. */}
      <div className="bg-[#fbbf24] text-[#451a03] text-xs">
        <div className="max-w-6xl mx-auto px-6 h-8 flex items-center gap-3">
          <span className="font-semibold whitespace-nowrap">미리보기</span>
          <span className="text-[#451a03]/70 truncate hidden sm:inline">가짜 데이터로 보는 화면 · 실제 서버와 연결되지 않습니다</span>
          <button type="button" onClick={() => setExpanded((v) => !v)}
            title={deploy ? `마지막 확인 ${timeText(deploy.checkedAt)} · 눌러서 자세히` : undefined}
            className="ml-auto inline-flex items-center gap-1.5 h-6 rounded-full hover:bg-[#451a03]/10 pr-1 whitespace-nowrap">
            {/* 지금 상태 — 아이콘 · 한 마디 · 진행 단계 · 시간 */}
            <span className="inline-flex items-center gap-1.5 h-6 pl-2 pr-2.5 rounded-full bg-[#451a03]/10 font-semibold">
              <StatusIcon tone={summary.tone} />
              {summary.label}
              {summary.detail && <span className="font-normal opacity-70">{summary.detail}</span>}
              {summary.time && <span className="font-normal tabular-nums opacity-70">{summary.time}</span>}
            </span>
            {/* 올릴 변경 수 — 있을 때만 */}
            {waitingCount > 0 && (
              <span className="inline-flex items-center gap-1 h-6 pl-2 pr-1 rounded-full bg-[#451a03]/10">
                올릴 변경
                <span className="min-w-[18px] h-[18px] px-1 rounded-full bg-[#451a03] text-[#fffbeb] text-[11px] font-semibold grid place-items-center tabular-nums">{waitingCount}</span>
              </span>
            )}
            {expanded ? <ChevronUp className="w-3.5 h-3.5 opacity-60" /> : <ChevronDown className="w-3.5 h-3.5 opacity-60" />}
          </button>
          <button type="button" onClick={() => void openDialog()}
            className="inline-flex items-center gap-1 h-6 px-3 rounded-full bg-[#451a03] text-[#fffbeb] hover:bg-[#78350f] whitespace-nowrap font-medium">
            <UploadCloud className="w-3.5 h-3.5" /> 게시
          </button>
        </div>
      </div>

      {expanded && (
        <div className="bg-white border-b border-neutral-200">
          <div className="max-w-6xl mx-auto px-6 py-3 space-y-3">
            {deployError && <p className="text-xs text-red-600">{deployError}</p>}
            {deploy && (
              <ol className="flex gap-2">
                <Step label="GitHub 에 올림" state={deploy.console.pushed ? 'done' : 'todo'}>
                  {deploy.console.pushed ? `${deploy.console.sha} · ${timeText(deploy.console.committedAt)}` : '아직 안 올라감'}
                </Step>
                <Step label="배포" state={runState}>{runText}</Step>
                <Step label="실제 콘솔 반영" state={liveState} last>
                  {deploy.live?.error ? '판 정보를 읽지 못했습니다'
                    : deploy.live?.current ? `반영됨 · ${timeText(deploy.live.builtAt)}`
                    : phase === 'propagating' ? '새 판이 퍼지는 중'
                    : phase === 'elsewhere' ? `다른 PC 에서 올린 판 (${deploy.live?.sha ?? '-'})`
                    : `이전 판 (${deploy.live?.sha ?? '-'})`}
                </Step>
              </ol>
            )}
            <div className="flex items-center gap-4 text-xs text-neutral-500 border-t border-neutral-100 pt-2">
              {deploy && (
                <span className="min-w-0 flex-1 truncate">
                  마지막 변경 <span className="text-neutral-700">{deploy.console.subject}</span>
                </span>
              )}
              {deploy && (
                <span className="whitespace-nowrap">
                  올릴 변경 {deploy.waiting.console + deploy.waiting.sdk + deploy.waiting.unpushed
                    ? `${deploy.waiting.console + deploy.waiting.sdk + deploy.waiting.unpushed}개`
                    : '없음'}
                </span>
              )}
              <span className="inline-flex items-center gap-3 whitespace-nowrap">
                {run?.url && (
                  <a href={run.url} target="_blank" rel="noreferrer" className="inline-flex items-center gap-0.5 hover:text-neutral-800">
                    배포 기록 <ExternalLink className="w-3 h-3" />
                  </a>
                )}
                {deploy && (
                  <a href={deploy.liveUrl} target="_blank" rel="noreferrer" className="inline-flex items-center gap-0.5 hover:text-neutral-800">
                    실제 콘솔 <ExternalLink className="w-3 h-3" />
                  </a>
                )}
                <button type="button" onClick={() => void refresh()} className="inline-flex items-center gap-1 hover:text-neutral-800">
                  <RefreshCw className="w-3 h-3" /> {timeText(deploy?.checkedAt)} 확인
                </button>
              </span>
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
        confirmDisabled={!status || empty || status.blocked.length > 0 || status.unplanned.length > 0}
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
            {status.unplanned.length > 0 && (
              <p className="text-sm text-red-600">
                이번 요약에 없는 파일이 섞여 있어 게시할 수 없습니다(다른 작업일 수 있습니다): {status.unplanned.join(', ')}
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
