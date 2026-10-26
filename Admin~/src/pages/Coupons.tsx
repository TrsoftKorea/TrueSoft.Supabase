import { useEffect, useState, useCallback } from 'react'
import { Plus, Pencil, Trash2, X, Loader2, List, Download } from 'lucide-react'
import { callAdmin, NotAuthenticatedError } from '../lib/api'
import { downloadCsv, todayStamp } from '../lib/csv'
import type { ProjectTarget } from '../lib/projectTarget'
import { WhiteCard } from '../components/ui/Card'
import { PageHeader } from '../components/ui/PageHeader'
import { TableStatusRow } from '../components/ui/TableStatusRow'
import { ConfirmDialog } from '../components/ui/ConfirmDialog'
import { ErrorBanner } from '../components/ui/ErrorBanner'
import { formatDateTime, formatCount, toDatetimeLocal } from '../components/ui/format'
import { Button } from '../components/ui/Button'

type ItemRow = { key: string; count: number }
type GameItem = { key: string; display_name: string }
type CategoryRow = { key: string; display_name: string }

type Coupon = {
  id: string
  kind: 'normal' | 'keyword'
  title: string
  content: string
  category: string
  items: ItemRow[] | null
  expires_at: string | null
  mail_expires_days: number
  max_uses: number | null
  used_count: number
  is_active: boolean
  created_at: string
  code: string | null
  issued_count: number
}

type CodeRow = { code: string; redeemed_at: string | null; account_id: string | null; user_id: string | null }

const KEYWORD_RE = /^[A-Z0-9_-]+$/

const inputCls = 'h-9 px-2.5 rounded-md border border-neutral-300 text-sm bg-white outline-none focus:border-[#1677ff] focus:ring-1 focus:ring-[#1677ff]'

function CodesModal({ target, onUnauthenticated, coupon, onClose }: {
  target: ProjectTarget
  onUnauthenticated: () => void
  coupon: Coupon
  onClose: () => void
}) {
  const [rows, setRows] = useState<CodeRow[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  useEffect(() => {
    void (async () => {
      try {
        setRows(await callAdmin<CodeRow[]>(target, 'coupons.codes', { id: coupon.id }))
      } catch (e: unknown) {
        if (e instanceof NotAuthenticatedError) { onUnauthenticated(); return }
        setError(e instanceof Error ? e.message : '불러오지 못했습니다.')
      } finally { setLoading(false) }
    })()
  }, [target, coupon.id, onUnauthenticated])

  const [exporting, setExporting] = useState(false)
  // 내려받기 실패가 목록 자리를 덮지 않도록 목록 오류와 따로 둔다.
  const [exportError, setExportError] = useState('')
  const exportCsv = async () => {
    setExporting(true)
    setExportError('')
    try {
      const all = await callAdmin<CodeRow[]>(target, 'coupons.codes', { id: coupon.id, all: true })
      downloadCsv(
        `쿠폰코드_${coupon.title}_${todayStamp()}.csv`,
        ['코드', '상태', '사용 시각', '유저 ID', '계정 ID'],
        all.map((r) => [
          r.code,
          r.redeemed_at ? '사용함' : '사용하지 않음',
          r.redeemed_at ? formatDateTime(r.redeemed_at) : '',
          r.user_id,
          r.account_id,
        ]),
      )
    } catch (e: unknown) {
      if (e instanceof NotAuthenticatedError) { onUnauthenticated(); return }
      setExportError(e instanceof Error ? e.message : '내려받지 못했습니다.')
    } finally { setExporting(false) }
  }

  return (
    <div className="fixed inset-0 z-50 bg-black/40 flex items-center justify-center p-4">
      <div className="bg-white rounded-lg shadow-xl w-full max-w-md max-h-[80vh] flex flex-col">
        <div className="flex items-center justify-between px-5 py-4 border-b shrink-0">
          <h3 className="text-base font-semibold truncate">발급 코드 — {coupon.title}</h3>
          <div className="flex items-center gap-2 shrink-0">
            <Button size="sm" onClick={() => void exportCsv()} disabled={exporting || loading || rows.length === 0}>
              {exporting ? <Loader2 className="w-3 h-3 animate-spin" /> : <Download className="w-3 h-3" />}CSV 내려받기
            </Button>
            <button onClick={onClose} title="닫기" aria-label="닫기" className="text-neutral-400 hover:text-neutral-700"><X className="w-4 h-4" /></button>
          </div>
        </div>
        {exportError && (
          <div className="px-5 py-2 border-b text-xs text-red-600 shrink-0">CSV를 내려받지 못했습니다. {exportError}</div>
        )}
        <div className="overflow-auto flex-1">
          {loading ? (
            <div className="flex justify-center py-10"><Loader2 className="w-5 h-5 animate-spin text-neutral-300" /></div>
          ) : error ? (
            <div className="p-5 text-sm text-red-500">{error}</div>
          ) : rows.length === 0 ? (
            <div className="p-5 text-sm text-neutral-500 text-center">발급된 코드가 없습니다.</div>
          ) : (
            <table className="w-full text-sm">
              <thead className="bg-neutral-50 text-neutral-500 text-xs sticky top-0">
                <tr>
                  <th className="text-left px-4 py-2 font-medium">코드</th>
                  <th className="text-left px-4 py-2 font-medium">상태</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((r, i) => (
                  <tr key={`${r.code}-${i}`} className="border-t border-neutral-100">
                    <td className="px-4 py-2 font-mono">{r.code}</td>
                    <td className="px-4 py-2">
                      {r.redeemed_at ? (
                        <span className="text-xs text-neutral-500">사용됨 · {formatDateTime(r.redeemed_at)}{r.user_id ? ` · 유저 ID ${r.user_id}` : ''}</span>
                      ) : (
                        <span className="text-xs text-emerald-600">미사용</span>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>
        {coupon.issued_count > 500 && (
          <div className="border-t px-5 py-2.5 text-xs text-neutral-500 shrink-0">
            코드 {coupon.issued_count.toLocaleString('ko-KR')}개 중 앞의 500개만 보입니다. 전체는 CSV로 내려받으세요.
          </div>
        )}
      </div>
    </div>
  )
}

function CouponModal({ target, onUnauthenticated, coupon, categories, catalog, onClose, onSaved }: {
  target: ProjectTarget
  onUnauthenticated: () => void
  coupon: Coupon | null
  categories: CategoryRow[]
  catalog: GameItem[]
  onClose: () => void
  onSaved: (message: string) => void
}) {
  const isEdit = !!coupon
  const [kind, setKind] = useState<'normal' | 'keyword'>(coupon?.kind ?? 'normal')
  const [title, setTitle] = useState(coupon?.title ?? '')
  const [content, setContent] = useState(coupon?.content ?? '')
  const [category, setCategory] = useState(coupon?.category ?? 'default')
  const [items, setItems] = useState<ItemRow[]>(coupon?.items ?? [])
  const [expiresAt, setExpiresAt] = useState(toDatetimeLocal(coupon?.expires_at))
  const [mailExpiresDays, setMailExpiresDays] = useState(coupon?.mail_expires_days ?? 7)
  const [isActive, setIsActive] = useState(coupon?.is_active ?? true)
  const [maxUses, setMaxUses] = useState(coupon?.max_uses != null ? String(coupon.max_uses) : '')
  const [code, setCode] = useState(coupon?.code ?? '')
  const [prefix, setPrefix] = useState('')
  const [randomLen, setRandomLen] = useState(6)
  const [quantity, setQuantity] = useState(1)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)
  const [issuedMsg, setIssuedMsg] = useState('')

  const addItem = () => setItems((x) => [...x, { key: catalog[0]?.key ?? '', count: 1 }])
  const setItem = (i: number, patch: Partial<ItemRow>) => setItems((x) => x.map((r, j) => (j === i ? { ...r, ...patch } : r)))
  const delItem = (i: number) => setItems((x) => x.filter((_, j) => j !== i))

  // 수정 화면의 코드는 잠겨 있어 검사하지 않는다(이미 만들어진 코드).
  const codeInvalid = kind === 'keyword' && !isEdit && code.trim() !== '' && !KEYWORD_RE.test(code.trim())
  const canSubmit = title.trim() !== '' && mailExpiresDays > 0 &&
    (kind === 'normal' || (kind === 'keyword' && code.trim() !== '' && !codeInvalid))

  const submit = async () => {
    if (!canSubmit || loading || issuedMsg) return
    setError('')
    let maxUsesValue: number | null = null
    if (maxUses.trim() !== '') {
      maxUsesValue = Number(maxUses)
      if (Number.isNaN(maxUsesValue)) {
        setError('최대 사용 횟수는 숫자여야 합니다.')
        return
      }
    }
    setLoading(true); setIssuedMsg('')
    const common = {
      title: title.trim(),
      content,
      category: category.trim() || 'default',
      items: items.map((it) => ({ key: it.key, count: Number(it.count) })),
      expiresAt: expiresAt ? new Date(expiresAt).toISOString() : null,
      mailExpiresDays,
      isActive,
      maxUses: maxUsesValue,
    }
    try {
      if (isEdit) {
        await callAdmin(target, 'coupons.update', { id: coupon.id, ...common })
        onSaved(`"${common.title}" 쿠폰을 저장했습니다.`)
      } else {
        const res = await callAdmin<{ code?: string; issued: number }>(target, 'coupons.create', {
          ...common,
          kind,
          code: kind === 'keyword' ? code.trim().toUpperCase() : null,
          prefix,
          randomLen,
          quantity,
        })
        const msg = kind === 'keyword' ? `발급 완료 — 코드 ${res.code}` : `발급 완료 — ${formatCount(res.issued)}개`
        // 이 메시지가 서 있는 동안 발급 버튼은 계속 잠근다 — 닫히기 전 다시 누르면 한 번 더 발급된다.
        setIssuedMsg(msg)
        setTimeout(() => onSaved(`"${common.title}" 쿠폰 ${msg}`), 900)
      }
    } catch (e: unknown) {
      if (e instanceof NotAuthenticatedError) { onUnauthenticated(); return }
      setError(e instanceof Error ? e.message : '실패했습니다.')
    } finally { setLoading(false) }
  }

  return (
    <div className="fixed inset-0 z-50 bg-black/40 flex items-center justify-center p-4 overflow-auto">
      <div className="bg-white rounded-lg shadow-xl w-full max-w-lg my-6">
        <div className="flex items-center justify-between px-5 py-4 border-b">
          <h3 className="text-base font-semibold">{isEdit ? '쿠폰 수정' : '쿠폰 추가'}</h3>
          <button onClick={onClose} title="닫기" aria-label="닫기" className="text-neutral-400 hover:text-neutral-700"><X className="w-4 h-4" /></button>
        </div>
        <div className="p-5 space-y-4">
          {!isEdit && (
            <div>
              <label className="block text-xs text-neutral-500 mb-1">종류</label>
              <div className="inline-flex rounded-lg bg-neutral-100 p-0.5">
                {(['normal', 'keyword'] as const).map((k) => (
                  <button key={k} type="button" onClick={() => setKind(k)}
                    className={['px-4 py-1.5 rounded-md text-sm transition-colors', kind === k ? 'bg-white shadow-sm text-[#1677ff] font-medium' : 'text-neutral-600 hover:text-neutral-800'].join(' ')}>
                    {k === 'normal' ? '코드 발급 (1회용 여러 개)' : '키워드 (공용 코드)'}
                  </button>
                ))}
              </div>
            </div>
          )}

          <div>
            <label className="block text-xs text-neutral-500 mb-1">분류</label>
            <select value={category} onChange={(e) => setCategory(e.target.value)} className={inputCls + ' w-full'}>
              {!categories.some((c) => c.key === 'default') && <option value="default">기본</option>}
              {categories.map((c) => (<option key={c.key} value={c.key}>{c.display_name || c.key}</option>))}
            </select>
          </div>
          <div>
            <label className="block text-xs text-neutral-500 mb-1">제목</label>
            <input value={title} onChange={(e) => setTitle(e.target.value)} className={inputCls + ' w-full'} />
          </div>
          <div>
            <label className="block text-xs text-neutral-500 mb-1">본문</label>
            <textarea value={content} onChange={(e) => setContent(e.target.value)} rows={3} className={inputCls + ' w-full h-auto py-2'} />
          </div>

          <div className="space-y-2">
            <div className="flex items-center justify-between">
              <span className="text-xs text-neutral-500">보상 아이템</span>
              <button onClick={addItem} className="inline-flex items-center gap-1 text-xs text-[#1677ff]"><Plus className="w-3 h-3" />추가</button>
            </div>
            {items.map((it, i) => (
              <div key={i} className="flex items-center gap-2">
                <select value={it.key} onChange={(e) => setItem(i, { key: e.target.value })} className={inputCls + ' flex-1'}>
                  {catalog.map((c) => (<option key={c.key} value={c.key}>{c.display_name} ({c.key})</option>))}
                </select>
                <input type="number" min={1} value={it.count} onChange={(e) => setItem(i, { count: Number(e.target.value) })} className={inputCls + ' w-24'} />
                <button onClick={() => delItem(i)} title="이 보상 삭제" aria-label="이 보상 삭제" className="text-neutral-400 hover:text-red-500 shrink-0"><X className="w-4 h-4" /></button>
              </div>
            ))}
          </div>

          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="block text-xs text-neutral-500 mb-1">사용 마감</label>
              <input type="datetime-local" value={expiresAt} onChange={(e) => setExpiresAt(e.target.value)} className={inputCls + ' w-full'} />
            </div>
            <div>
              <label className="block text-xs text-neutral-500 mb-1">우편 수령 기한(일)</label>
              <input type="number" min={1} value={mailExpiresDays} onChange={(e) => setMailExpiresDays(Number(e.target.value))} className={inputCls + ' w-full'} />
            </div>
          </div>

          {kind === 'keyword' && (
            <div className="grid grid-cols-2 gap-3">
              <div>
                <label className="block text-xs text-neutral-500 mb-1">코드</label>
                <input value={code} onChange={(e) => setCode(e.target.value.toUpperCase())} disabled={isEdit} className={inputCls + ' w-full font-mono disabled:bg-neutral-50 disabled:text-neutral-400' + (codeInvalid ? ' border-red-400' : '')} />
                {codeInvalid && <p className="mt-1 text-xs text-red-600">영문·숫자·밑줄(_)·하이픈(-)만 쓸 수 있습니다. 띄어쓰기는 안 됩니다.</p>}
              </div>
              <div>
                <label className="block text-xs text-neutral-500 mb-1">최대 사용 횟수(빈칸=무제한)</label>
                <input type="number" min={1} value={maxUses} onChange={(e) => setMaxUses(e.target.value)} className={inputCls + ' w-full'} />
              </div>
            </div>
          )}

          {!isEdit && kind === 'normal' && (
            <div className="grid grid-cols-3 gap-3">
              <div>
                <label className="block text-xs text-neutral-500 mb-1">접두어(선택)</label>
                <input value={prefix} onChange={(e) => setPrefix(e.target.value.toUpperCase())} className={inputCls + ' w-full font-mono'} />
              </div>
              <div>
                <label className="block text-xs text-neutral-500 mb-1">코드 길이</label>
                <input type="number" min={1} value={randomLen} onChange={(e) => setRandomLen(Number(e.target.value))} className={inputCls + ' w-full'} />
              </div>
              <div>
                <label className="block text-xs text-neutral-500 mb-1">발급 수량</label>
                <input type="number" min={1} value={quantity} onChange={(e) => setQuantity(Number(e.target.value))} className={inputCls + ' w-full'} />
              </div>
            </div>
          )}

          <label className="flex items-center gap-2 text-sm cursor-pointer">
            <input type="checkbox" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} className="rounded" />
            활성
          </label>

          {issuedMsg && <div className="text-sm text-emerald-600">{issuedMsg}</div>}
          {error && <div className="text-sm text-red-500">{error}</div>}
        </div>
        <div className="flex justify-end gap-2 px-5 py-3 border-t bg-neutral-50">
          <Button variant="outline" onClick={onClose}>취소</Button>
          <Button variant="primary" onClick={() => { void submit() }} disabled={loading || !canSubmit || !!issuedMsg}>
            {loading ? '저장 중…' : isEdit ? '저장' : '발급'}
          </Button>
        </div>
      </div>
    </div>
  )
}

export default function Coupons({
  target,
  onUnauthenticated,
}: {
  target: ProjectTarget
  onUnauthenticated: () => void
}) {
  const [rows, setRows] = useState<Coupon[]>([])
  const [loading, setLoading] = useState(true)
  const [categories, setCategories] = useState<CategoryRow[]>([])
  const [catalog, setCatalog] = useState<GameItem[]>([])
  const [error, setError] = useState('')
  const [failed, setFailed] = useState(false)
  const [notice, setNotice] = useState('')
  const [showModal, setShowModal] = useState(false)
  const [editTarget, setEditTarget] = useState<Coupon | null>(null)
  const [codesTarget, setCodesTarget] = useState<Coupon | null>(null)
  const [delTarget, setDelTarget] = useState<Coupon | null>(null)
  const [deleting, setDeleting] = useState(false)
  const [delError, setDelError] = useState('')

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
      setRows(await callAdmin<Coupon[]>(target, 'coupons.list'))
      setFailed(false)
    } catch (e: unknown) {
      report(e, '쿠폰 목록을 불러오지 못했습니다.')
      setFailed(true)
    } finally {
      setLoading(false)
    }
  }, [target, report])

  useEffect(() => {
    void load()
    void (async () => {
      try {
        const [cat, items] = await Promise.all([
          callAdmin<{ rows: CategoryRow[] }>(target, 'mails.getCategories'),
          callAdmin<GameItem[]>(target, 'gameItems.list'),
        ])
        setCategories(cat.rows)
        setCatalog(items)
      } catch (e: unknown) {
        report(e, '불러오지 못했습니다.')
      }
    })()
  }, [target, load, report])

  const confirmDelete = async () => {
    if (!delTarget) return
    setDeleting(true)
    setDelError('')
    setNotice('')
    try {
      await callAdmin(target, 'coupons.delete', { id: delTarget.id })
      setDelTarget(null)
      await load()
    } catch (e: unknown) {
      if (e instanceof NotAuthenticatedError) { onUnauthenticated(); return }
      setDelError(e instanceof Error ? e.message : '삭제하지 못했습니다.')
    } finally {
      setDeleting(false)
    }
  }

  return (
    <div className="space-y-4">
      <PageHeader title="쿠폰" description="코드를 입력하면 보상을 지급하는 쿠폰을 관리합니다." />
      <ErrorBanner message={error} onDismiss={() => setError('')} />
      {notice && <div className="rounded-md border border-emerald-200 bg-emerald-50 px-4 py-2.5 text-sm text-emerald-700">{notice}</div>}

      <div className="flex justify-end">
        <Button variant="primary" size="compact" onClick={() => { setNotice(''); setShowModal(true) }}>
          <Plus className="w-4 h-4" />쿠폰 추가
        </Button>
      </div>

      <WhiteCard>
        <div className="overflow-auto">
          <table className="w-full text-sm">
            <thead className="bg-neutral-50 text-neutral-500 text-xs">
              <tr>
                <th className="text-left px-5 py-2.5 font-medium">제목</th>
                <th className="text-left px-4 py-2.5 font-medium w-24">종류</th>
                <th className="text-left px-4 py-2.5 font-medium">코드</th>
                <th className="text-left px-4 py-2.5 font-medium w-28">사용</th>
                <th className="text-left px-4 py-2.5 font-medium w-24">상태</th>
                <th className="text-left px-4 py-2.5 font-medium w-40">사용 마감</th>
                <th className="px-3 py-2.5 w-28" />
              </tr>
            </thead>
            <tbody>
              {loading || rows.length === 0 ? (
                <TableStatusRow loading={loading} empty={rows.length === 0} failed={failed} colSpan={7} emptyText="쿠폰이 없습니다." />
              ) : (
                rows.map((c) => (
                  <tr key={c.id} className="border-t border-neutral-100">
                    <td className="px-5 py-3">{c.title}</td>
                    <td className="px-4 py-3 text-neutral-600">{c.kind === 'normal' ? '코드 발급' : '키워드'}</td>
                    <td className="px-4 py-3 font-mono text-xs">
                      {c.kind === 'keyword' ? c.code : `발급 ${formatCount(c.issued_count)}개`}
                    </td>
                    <td className="px-4 py-3 text-neutral-600 whitespace-nowrap">
                      {formatCount(c.used_count)}{c.kind === 'keyword' && c.max_uses != null ? ` / ${formatCount(c.max_uses)}` : ''}
                    </td>
                    <td className="px-4 py-3">
                      <span className={`px-2 py-0.5 text-xs rounded ${c.is_active ? 'bg-emerald-100 text-emerald-700' : 'bg-neutral-100 text-neutral-600'}`}>
                        {c.is_active ? '활성' : '비활성'}
                      </span>
                    </td>
                    <td className="px-4 py-3 text-neutral-600 whitespace-nowrap">{c.expires_at ? formatDateTime(c.expires_at) : '무기한'}</td>
                    <td className="px-3 py-3">
                      <div className="flex items-center justify-end gap-1">
                        {c.kind === 'normal' && (
                          <button onClick={() => setCodesTarget(c)} title="코드 보기" aria-label="코드 보기" className="w-7 h-7 inline-flex items-center justify-center rounded hover:bg-neutral-100 text-neutral-400 hover:text-neutral-700">
                            <List className="w-3.5 h-3.5" />
                          </button>
                        )}
                        <button onClick={() => { setNotice(''); setEditTarget(c) }} title="수정" aria-label="수정" className="w-7 h-7 inline-flex items-center justify-center rounded hover:bg-neutral-100 text-neutral-400 hover:text-neutral-700">
                          <Pencil className="w-3.5 h-3.5" />
                        </button>
                        <button onClick={() => { setDelError(''); setDelTarget(c) }} title="삭제" aria-label="삭제" className="w-7 h-7 inline-flex items-center justify-center rounded hover:bg-red-50 text-neutral-400 hover:text-red-500">
                          <Trash2 className="w-3.5 h-3.5" />
                        </button>
                      </div>
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      </WhiteCard>

      {showModal && (
        <CouponModal target={target} onUnauthenticated={onUnauthenticated} coupon={null} categories={categories} catalog={catalog}
          onClose={() => setShowModal(false)} onSaved={(msg) => { setShowModal(false); setNotice(msg); void load() }} />
      )}
      {editTarget && (
        <CouponModal target={target} onUnauthenticated={onUnauthenticated} coupon={editTarget} categories={categories} catalog={catalog}
          onClose={() => setEditTarget(null)} onSaved={(msg) => { setEditTarget(null); setNotice(msg); void load() }} />
      )}
      {codesTarget && (
        <CodesModal target={target} onUnauthenticated={onUnauthenticated} coupon={codesTarget} onClose={() => setCodesTarget(null)} />
      )}

      <ConfirmDialog
        open={!!delTarget}
        title="쿠폰 삭제"
        confirmLabel="삭제"
        danger
        busy={deleting}
        error={delError || undefined}
        onConfirm={() => void confirmDelete()}
        onCancel={() => setDelTarget(null)}
        description="발급된 코드와 사용 이력이 함께 삭제됩니다. 이미 지급된 보상은 그대로 남습니다."
      >
        {delTarget && (
          <div className="rounded-md bg-neutral-50 border border-neutral-200 px-3 py-2 text-sm">{delTarget.title}</div>
        )}
      </ConfirmDialog>
    </div>
  )
}
