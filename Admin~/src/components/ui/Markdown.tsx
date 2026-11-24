import { Fragment, type ReactNode } from 'react'

// 약관·방침 본문(마크다운)을 그린다 — 운영 콘솔 미리보기와 고객센터 웹이 같은 부품을 쓴다(둘이 다르게 보이면 미리보기가 거짓말이 된다).
// 글을 HTML 로 해석하지 않고 React 요소로만 만든다 — 본문에 <script>·<img onerror> 를 넣어도 글자로 보인다.
// 받는 꼴: 제목(#~###), 문단(빈 줄로 나눔, 줄바꿈 유지), 목록(- · * · 1.), 가로줄(---), 표(| 칸 | 칸 | + |---|),
// 굵게(**) · 기울임(*) · 코드(`) · 링크([글](https://…), 맨 주소). 링크는 http·https·mailto 만, 새 창으로 연다.

type Block =
  | { kind: 'heading'; level: number; text: string }
  | { kind: 'para'; lines: string[] }
  | { kind: 'list'; ordered: boolean; start: number; items: string[] }
  | { kind: 'table'; head: string[]; rows: string[][] }
  | { kind: 'rule' }

// 번호는 한두 자리만 — "2026. 11. 24 시행" 같은 날짜 줄이 목록으로 먹혀 연도가 사라지지 않게.
const LIST_ITEM = /^\s*(?:[-*+]|(\d{1,2})[.)])\s+(.*)$/
const TABLE_SEP = /^\s*\|?\s*:?-{2,}:?\s*(\|\s*:?-{2,}:?\s*)*\|?\s*$/

function cells(line: string): string[] {
  return line.trim().replace(/^\|/, '').replace(/\|$/, '').split('|').map((c) => c.trim())
}

function parse(src: string): Block[] {
  const lines = src.replace(/\r\n?/g, '\n').split('\n')
  const blocks: Block[] = []
  let i = 0
  while (i < lines.length) {
    const line = lines[i]!
    if (!line.trim()) { i++; continue }
    const h = /^(#{1,6})\s+(.*)$/.exec(line)
    if (h) { blocks.push({ kind: 'heading', level: h[1]!.length, text: h[2]!.trim() }); i++; continue }
    if (/^\s*([-*_])\s*(\1\s*){2,}$/.test(line)) { blocks.push({ kind: 'rule' }); i++; continue }
    if (line.includes('|') && TABLE_SEP.test(lines[i + 1] ?? '')) {
      const head = cells(line)
      const rows: string[][] = []
      i += 2
      while (i < lines.length && lines[i]!.includes('|') && lines[i]!.trim()) rows.push(cells(lines[i++]!))
      blocks.push({ kind: 'table', head, rows })
      continue
    }
    const li = LIST_ITEM.exec(line)
    if (li) {
      const ordered = !!li[1]
      const items: string[] = []
      while (i < lines.length) {
        const m = LIST_ITEM.exec(lines[i]!)
        if (m && !!m[1] === ordered) { items.push(m[2]!); i++; continue }
        // 들여 쓴 다음 줄은 앞 항목에 잇는다.
        if (items.length && /^\s{2,}\S/.test(lines[i]!)) { items[items.length - 1] += `\n${lines[i]!.trim()}`; i++; continue }
        break
      }
      // 항목 사이에 문단이 끼면 목록이 갈라진다 — 첫 번호를 이어 받아 "1. … 2. …" 가 둘 다 1 로 보이지 않게.
      blocks.push({ kind: 'list', ordered, start: ordered ? Number(li[1]) : 1, items })
      continue
    }
    const para: string[] = []
    while (i < lines.length && lines[i]!.trim() && !/^#{1,6}\s/.test(lines[i]!) && !LIST_ITEM.exec(lines[i]!)
      && !(lines[i]!.includes('|') && TABLE_SEP.test(lines[i + 1] ?? ''))) {
      para.push(lines[i++]!)
    }
    blocks.push({ kind: 'para', lines: para })
  }
  return blocks
}

const SAFE_URL = /^(https?:\/\/|mailto:)/i
const INLINE = /(\*\*([^*]+)\*\*)|(`([^`]+)`)|(\[([^\]]+)\]\(([^)\s]+)\))|(\*([^*\s][^*]*)\*)|(https?:\/\/[^\s<>()]+[^\s<>().,;:!?'"])/g

// inLink — 링크 글자 안. 그 안의 링크·맨 주소는 글자로 둔다(<a> 안에 <a> 가 들어가지 않게).
function inline(text: string, key: string, inLink = false): ReactNode[] {
  const out: ReactNode[] = []
  let last = 0
  let n = 0
  for (const m of text.matchAll(INLINE)) {
    if (m.index > last) out.push(text.slice(last, m.index))
    const k = `${key}-${n++}`
    if (m[2] != null) out.push(<strong key={k} className="font-semibold">{inline(m[2], k, inLink)}</strong>)
    else if (m[4] != null) out.push(<code key={k} className="font-mono rounded bg-neutral-100 px-1">{m[4]}</code>)
    else if (m[6] != null) {
      const href = m[7]!
      out.push(!inLink && SAFE_URL.test(href)
        ? <a key={k} href={href} target="_blank" rel="noopener noreferrer" className="underline break-all" style={{ color: 'var(--accent, #1677ff)' }}>{inline(m[6], k, true)}</a>
        : m[0])
    } else if (m[9] != null) out.push(<em key={k}>{inline(m[9], k, inLink)}</em>)
    else if (m[10] != null) {
      out.push(inLink
        ? m[10]
        : <a key={k} href={m[10]} target="_blank" rel="noopener noreferrer" className="underline break-all" style={{ color: 'var(--accent, #1677ff)' }}>{m[10]}</a>)
    }
    last = m.index + m[0].length
  }
  if (last < text.length) out.push(text.slice(last))
  return out
}

function lines(text: string, key: string): ReactNode {
  return text.split('\n').map((l, i) => <Fragment key={i}>{i > 0 && <br />}{inline(l, `${key}-${i}`)}</Fragment>)
}

export function Markdown({ source, className = '' }: { source: string; className?: string }) {
  return (
    // 한글은 띄어쓰기에서만 줄을 바꾼다(고객센터 웹은 콘솔의 전역 설정이 없어 여기서 정한다). 긴 주소는 링크가 따로 자른다.
    <div className={`space-y-3 text-sm leading-relaxed text-neutral-800 break-keep [overflow-wrap:anywhere] ${className}`}>
      {parse(source).map((b, i) => {
        const k = String(i)
        switch (b.kind) {
          case 'heading':
            return b.level <= 2
              ? <h2 key={k} className="pt-2 text-base font-semibold text-neutral-900">{inline(b.text, k)}</h2>
              : <h3 key={k} className="pt-1 text-sm font-semibold text-neutral-900">{inline(b.text, k)}</h3>
          case 'para':
            return <p key={k}>{lines(b.lines.join('\n'), k)}</p>
          case 'rule':
            return <hr key={k} className="border-neutral-200" />
          case 'list': {
            const Tag = b.ordered ? 'ol' : 'ul'
            return (
              <Tag key={k} {...(b.ordered ? { start: b.start } : {})} className={`${b.ordered ? 'list-decimal' : 'list-disc'} pl-5 space-y-1`}>
                {b.items.map((it, j) => <li key={j}>{lines(it, `${k}-${j}`)}</li>)}
              </Tag>
            )
          }
          case 'table':
            return (
              <div key={k} className="overflow-x-auto">
                <table className="w-full border-collapse text-sm">
                  <thead>
                    <tr>{b.head.map((c, j) => <th key={j} className="border border-neutral-200 bg-neutral-50 px-2 py-1.5 text-left font-semibold">{inline(c, `${k}-h${j}`)}</th>)}</tr>
                  </thead>
                  <tbody>
                    {b.rows.map((r, ri) => (
                      <tr key={ri}>{b.head.map((_, j) => <td key={j} className="border border-neutral-200 px-2 py-1.5 align-top">{inline(r[j] ?? '', `${k}-${ri}-${j}`)}</td>)}</tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )
        }
      })}
    </div>
  )
}
