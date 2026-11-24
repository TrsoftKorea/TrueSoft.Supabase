import { useCallback, useEffect, useRef, useState } from 'react'
import { Check, Copy, Eye, Loader2, Pencil, Plus, Send, Trash2, X } from 'lucide-react'
import { callAdmin, NotAuthenticatedError } from '../lib/api'
import { copyToClipboard } from '../lib/clipboard'
import { supportPageUrl, type ProjectTarget } from '../lib/projectTarget'
import { defaultLangField, langLabel, pickableLangs, useDefaultLanguage } from '../lib/languages'
import { Segmented } from '../components/ui/Segmented'
import { TableCard } from '../components/ui/DataTable'
import { StatusText, type Tone } from '../components/ui/StatusText'
import { EditDialog } from '../components/ui/EditDialog'
import { ConfirmDialog } from '../components/ui/ConfirmDialog'
import { Button } from '../components/ui/Button'
import { Markdown } from '../components/ui/Markdown'
import { areaCls, inputCls } from '../components/ui/Input'
import { formatDateTime } from '../components/ui/format'

type Kind = 'privacy' | 'terms'
export type LegalDoc = {
  id: number
  version: number
  effective_date: string
  body: string
  localized: Record<string, string>
  published_at: string | null
  in_force: boolean
  updated_at: string | null
  updated_by: string | null
}
type Draft = { id: number | null; effectiveDate: string; body: string; langs: { lang: string; body: string }[] }
type State = 'draft' | 'upcoming' | 'current' | 'past'

const KINDS: { v: Kind; label: string }[] = [
  { v: 'privacy', label: '개인정보처리방침' },
  { v: 'terms', label: '이용약관' },
]
const STATE_TEXT: Record<State, [string, Tone]> = {
  draft: ['작성 중', 'warn'],
  upcoming: ['시행 예정', 'plain'],
  current: ['시행 중', 'ok'],
  past: ['지난 버전', 'muted'],
}
const REASON_TEXT: Record<string, string> = {
  legal_not_editable: '이미 게시한 버전은 고칠 수 없습니다.',
  legal_body_empty: '본문을 넣어 주세요.',
  legal_effective_date_before_published: '이미 게시한 버전보다 시행일이 앞설 수 없습니다.',
  legal_kind_invalid: '문서 종류가 올바르지 않습니다.',
  legal_effective_date_missing: '시행일을 넣어 주세요.',
  legal_localized_invalid: '언어별 본문이 올바르지 않습니다.',
}
const reasonText = (e: unknown, fallback: string) => {
  const msg = e instanceof Error ? e.message : ''
  const key = Object.keys(REASON_TEXT).find((k) => msg.includes(k))
  return key ? REASON_TEXT[key]! : msg || fallback
}

/** 버전의 상태 — 시행 중인 버전은 시행일이 지난 게시 버전 중 가장 새 버전 하나다. */
function stateOf(d: LegalDoc, rows: LegalDoc[]): State {
  if (!d.published_at) return 'draft'
  if (!d.in_force) return 'upcoming'
  const top = Math.max(...rows.filter((r) => r.in_force).map((r) => r.version))
  return d.version === top ? 'current' : 'past'
}

/** 고객센터 › 약관·방침 — 버전을 써서 게시한다. 게시한 버전은 고칠 수 없고, 시행일이 되면 고객센터 웹에 보인다. */
export function LegalTab({ target, report }: { target: ProjectTarget; report: (e: unknown, fallback: string) => void }) {
  const [kind, setKind] = useState<Kind>('privacy')
  const [rows, setRows] = useState<LegalDoc[] | null>(null)
  const [failed, setFailed] = useState(false)
  const [draft, setDraft] = useState<Draft | null>(null)
  const [preview, setPreview] = useState(false)
  const [saving, setSaving] = useState(false)
  const [saveError, setSaveError] = useState('')
  const [viewing, setViewing] = useState<LegalDoc | null>(null)
  const [publishing, setPublishing] = useState<LegalDoc | null>(null)
  const [deleting, setDeleting] = useState<LegalDoc | null>(null)
  const [actionBusy, setActionBusy] = useState(false)
  const [actionError, setActionError] = useState('')
  const [copied, setCopied] = useState(false)
  const defaultLang = useDefaultLanguage(target)
  const base = supportPageUrl(target)
  const pageUrl = base ? `${base}&view=${kind}` : null

  // 종류를 빨리 바꾸면 앞 종류의 늦은 응답이 뒤에 올 수 있다 — 마지막 요청의 응답만 받는다.
  // (안 그러면 방침 목록이 약관 탭에 뜨고, 그 상태로 「새 버전」이 방침 본문을 약관으로 복사한다.)
  const reqRef = useRef(0)
  const load = useCallback(async () => {
    const req = ++reqRef.current
    setFailed(false)
    setRows(null)
    try {
      const data = await callAdmin<LegalDoc[]>(target, 'legal.list', { kind })
      if (req === reqRef.current) setRows(data)
    } catch (e: unknown) {
      if (req !== reqRef.current) return
      setRows([])
      setFailed(true)
      report(e, '약관·방침을 불러오지 못했습니다.')
    }
  }, [target, kind, report])

  useEffect(() => { void load() }, [load])

  const hasDraft = !!rows?.some((r) => !r.published_at)
  // 새 버전은 지금 시행 중인 버전(없으면 가장 새 게시 버전)을 복사해 시작한다 — 고친 곳만 바꾸면 된다.
  const startNew = () => {
    const source = rows?.find((r) => rows && stateOf(r, rows) === 'current') ?? rows?.find((r) => r.published_at)
    setSaveError('')
    setPreview(false)
    setDraft({
      id: null, effectiveDate: '', body: source?.body ?? '',
      langs: Object.entries(source?.localized ?? {}).map(([lang, body]) => ({ lang, body })),
    })
  }
  const openEdit = (d: LegalDoc) => {
    setSaveError('')
    setPreview(false)
    setDraft({ id: d.id, effectiveDate: d.effective_date, body: d.body, langs: Object.entries(d.localized ?? {}).map(([lang, body]) => ({ lang, body })) })
  }

  const save = async () => {
    if (!draft) return
    setSaving(true)
    setSaveError('')
    try {
      await callAdmin(target, 'legal.save', {
        id: draft.id, kind, effectiveDate: draft.effectiveDate, body: draft.body,
        localized: Object.fromEntries(draft.langs.filter((l) => l.lang && l.body.trim()).map((l) => [l.lang, l.body])),
      })
      setDraft(null)
      await load()
    } catch (e: unknown) {
      if (e instanceof NotAuthenticatedError) { report(e, ''); return }
      setSaveError(reasonText(e, '저장하지 못했습니다.'))
    } finally {
      setSaving(false)
    }
  }

  const act = async (action: 'legal.publish' | 'legal.delete', d: LegalDoc, done: () => void) => {
    setActionBusy(true)
    setActionError('')
    try {
      await callAdmin(target, action, { id: d.id })
      done()
      await load()
    } catch (e: unknown) {
      if (e instanceof NotAuthenticatedError) { report(e, ''); return }
      setActionError(reasonText(e, '처리하지 못했습니다.'))
    } finally {
      setActionBusy(false)
    }
  }

  const copyUrl = async () => {
    if (!pageUrl) return
    if (await copyToClipboard(pageUrl)) { setCopied(true); setTimeout(() => setCopied(false), 1500) }
    else report(new Error('주소를 복사하지 못했습니다.'), '')
  }

  const draftOk = !!draft && /^\d{4}-\d{2}-\d{2}$/.test(draft.effectiveDate) && !!draft.body.trim()
    && draft.langs.every((l) => l.lang && l.body.trim())
  const taken = draft?.langs.map((l) => l.lang) ?? []

  return (
    <div className="space-y-4">
      <Segmented value={kind} onChange={setKind} options={KINDS} />
      <TableCard
        title="버전"
        description="게시한 버전은 고칠 수 없고, 시행일부터 고객센터 웹에 보입니다."
        actions={
          <span className="inline-flex flex-wrap gap-1.5">
            {pageUrl && (
              <Button size="compact" onClick={() => void copyUrl()} title={pageUrl}>
                {copied ? <Check className="w-3.5 h-3.5 text-green-600" /> : <Copy className="w-3.5 h-3.5" />}
                {copied ? '복사했습니다' : '주소 복사'}
              </Button>
            )}
            <Button variant="primary" size="compact" onClick={startNew} disabled={rows === null || hasDraft}
              title={hasDraft ? '작성 중인 버전이 있습니다' : undefined}>
              <Plus className="w-3.5 h-3.5" />새 버전
            </Button>
          </span>
        }
        columns={[
          { key: 'version', header: '버전', kind: 'number', cell: (d) => d.version },
          { key: 'date', header: '시행일', kind: 'datetime', cell: (d) => d.effective_date },
          {
            key: 'state', header: '상태', kind: 'status', cell: (d) => {
              const [text, tone] = STATE_TEXT[stateOf(d, rows ?? [])]
              return <StatusText tone={tone}>{text}</StatusText>
            },
          },
          { key: 'langs', header: '다른 언어', kind: 'name', cell: (d) => Object.keys(d.localized ?? {}).map(langLabel).join(' · ') || null },
          { key: 'updated', header: '마지막 수정', kind: 'datetime', cell: (d) => (d.updated_at ? formatDateTime(d.updated_at) : null) },
          {
            key: 'actions', header: '', kind: 'actions', cell: (d) => d.published_at
              ? <Button size="sm" onClick={() => setViewing(d)}><Eye className="w-3 h-3" />보기</Button>
              : (
                <span className="inline-flex gap-1.5">
                  <Button size="sm" onClick={() => openEdit(d)}><Pencil className="w-3 h-3" />수정</Button>
                  <Button size="sm" variant="primary" onClick={() => { setActionError(''); setPublishing(d) }}><Send className="w-3 h-3" />게시</Button>
                  <Button size="sm" variant="dangerOutline" onClick={() => { setActionError(''); setDeleting(d) }}><Trash2 className="w-3 h-3" />지우기</Button>
                </span>
              ),
          },
        ]}
        rows={rows ?? []}
        rowKey={(d) => String(d.id)}
        loading={rows === null}
        failed={failed}
        emptyText="아직 쓴 버전이 없습니다."
      />

      {draft && (
        <EditDialog
          size="xl"
          title={draft.id == null ? '새 버전' : '버전 수정'}
          description={KINDS.find((k) => k.v === kind)!.label}
          busy={saving}
          error={saveError || undefined}
          onClose={() => setDraft(null)}
          actions={<Segmented value={preview ? 'preview' : 'edit'} onChange={(v) => setPreview(v === 'preview')}
            options={[{ v: 'edit', label: '편집' }, { v: 'preview', label: '미리보기' }]} />}
          footer={
            <>
              <Button onClick={() => setDraft(null)} disabled={saving}>취소</Button>
              <Button variant="primary" onClick={() => void save()} disabled={saving || !draftOk}>
                {saving && <Loader2 className="w-3.5 h-3.5 animate-spin" />}저장
              </Button>
            </>
          }
        >
          {preview ? (
            <div className="space-y-6">
              <Markdown source={draft.body} />
              {draft.langs.filter((l) => l.lang && l.body.trim()).map((l) => (
                <div key={l.lang} className="space-y-2 border-t border-neutral-200 pt-4">
                  <div className="text-xs font-medium text-neutral-500">{langLabel(l.lang)}</div>
                  <Markdown source={l.body} />
                </div>
              ))}
            </div>
          ) : (
            <div className="space-y-4">
              <div className="space-y-1">
                <label className="block text-sm font-medium text-neutral-800">시행일</label>
                <input type="date" className={`${inputCls} w-44`} value={draft.effectiveDate}
                  onChange={(e) => setDraft({ ...draft, effectiveDate: e.target.value })} />
                <p className="text-xs text-neutral-500">이 날짜(프로젝트 시간대)부터 고객센터 웹에 보이는 날짜를 넣습니다.</p>
              </div>
              <div className="space-y-1">
                <label className="block text-sm font-medium text-neutral-800">{defaultLangField(defaultLang, '본문')}</label>
                <textarea className={`${areaCls} w-full font-mono`} rows={14} value={draft.body}
                  onChange={(e) => setDraft({ ...draft, body: e.target.value })} />
                <p className="text-xs text-neutral-500">마크다운으로 넣습니다. 제목은 #, 목록은 -, 표는 | 칸 | 칸 |, 링크는 [글](주소).</p>
              </div>
              <div className="flex items-center justify-between">
                <span className="text-xs font-medium text-neutral-500">다른 언어</span>
                <button type="button" disabled={pickableLangs(defaultLang, taken).length === 0}
                  onClick={() => setDraft({ ...draft, langs: [...draft.langs, { lang: pickableLangs(defaultLang, taken)[0] ?? '', body: '' }] })}
                  className="inline-flex items-center gap-1 text-xs text-[#1677ff] disabled:opacity-40 disabled:cursor-not-allowed">
                  <Plus className="w-3 h-3" />언어 추가
                </button>
              </div>
              {draft.langs.map((l, i) => (
                <div key={i} className="border border-neutral-200 rounded-md p-2.5 space-y-2 bg-neutral-50">
                  <div className="flex items-center gap-2">
                    <select value={l.lang} className={`${inputCls} w-36`}
                      onChange={(e) => setDraft({ ...draft, langs: draft.langs.map((x, j) => (j === i ? { ...x, lang: e.target.value } : x)) })}>
                      {pickableLangs(defaultLang, taken.filter((_, j) => j !== i), l.lang).map((x) => <option key={x} value={x}>{langLabel(x)}</option>)}
                    </select>
                    <span className="flex-1" />
                    <button type="button" title="이 언어 삭제" aria-label="이 언어 삭제" className="text-neutral-400 hover:text-red-500"
                      onClick={() => setDraft({ ...draft, langs: draft.langs.filter((_, j) => j !== i) })}><X className="w-4 h-4" /></button>
                  </div>
                  <textarea className={`${areaCls} w-full font-mono`} rows={8} value={l.body}
                    onChange={(e) => setDraft({ ...draft, langs: draft.langs.map((x, j) => (j === i ? { ...x, body: e.target.value } : x)) })} />
                </div>
              ))}
            </div>
          )}
        </EditDialog>
      )}

      {viewing && (
        <EditDialog size="xl" title={`버전 ${viewing.version}`} description={`시행일 ${viewing.effective_date}`} onClose={() => setViewing(null)}
          footer={<Button onClick={() => setViewing(null)}>닫기</Button>}>
          <div className="space-y-6">
            <Markdown source={viewing.body} />
            {Object.entries(viewing.localized ?? {}).map(([lang, body]) => (
              <div key={lang} className="space-y-2 border-t border-neutral-200 pt-4">
                <div className="text-xs font-medium text-neutral-500">{langLabel(lang)}</div>
                <Markdown source={body} />
              </div>
            ))}
          </div>
        </EditDialog>
      )}

      <ConfirmDialog
        open={publishing !== null}
        title="게시할까요?"
        description={publishing ? `게시하면 고칠 수 없습니다. ${publishing.effective_date}부터 고객센터 웹에 보입니다.` : undefined}
        confirmLabel="게시"
        busy={actionBusy}
        error={actionError || undefined}
        onConfirm={() => { if (publishing) void act('legal.publish', publishing, () => setPublishing(null)) }}
        onCancel={() => setPublishing(null)}
      />
      <ConfirmDialog
        open={deleting !== null}
        title="버전을 지울까요?"
        description={deleting ? `작성 중인 버전 ${deleting.version}` : undefined}
        confirmLabel="지우기"
        danger
        busy={actionBusy}
        error={actionError || undefined}
        onConfirm={() => { if (deleting) void act('legal.delete', deleting, () => setDeleting(null)) }}
        onCancel={() => setDeleting(null)}
      />
    </div>
  )
}
