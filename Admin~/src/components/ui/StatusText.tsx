import type { ReactNode } from 'react'

/**
 * 상태 색. 정상 초록 · 주의 주황 · 문제 빨강 · 해당 없음 회색, 그리고 문제도 정상도 아닌 보통 상태(미수령 등)는 색 없이.
 * 색을 더 늘리지 않는다 — 색이 많으면 진짜 문제가 묻힌다.
 */
export type Tone = 'ok' | 'warn' | 'bad' | 'muted' | 'plain'

export const TONE_TEXT: Record<Tone, string> = {
  ok: 'text-emerald-600',
  warn: 'text-amber-700',
  bad: 'text-red-600',
  muted: 'text-neutral-400',
  plain: 'text-neutral-900',
}

/** 상태 글자. 정상·보통은 글자색만, 문제(bad)일 때만 옅은 바탕을 깔아 한눈에 띄게 한다. */
export function StatusText({ tone, children }: { tone: Tone; children: ReactNode }) {
  return tone === 'bad'
    // 위아래 여백 없이 줄 높이만 — 바탕을 깐 줄만 표 줄이 높아지지 않게.
    ? <span className="inline-flex items-center rounded bg-red-50 px-1.5 leading-5 text-red-600 font-medium whitespace-nowrap">{children}</span>
    : <span className={`${TONE_TEXT[tone]} whitespace-nowrap`}>{children}</span>
}

export type TagTone = 'neutral' | 'brand' | 'bad'

const TAG_CLS: Record<TagTone, string> = {
  neutral: 'bg-neutral-100 text-neutral-600',
  brand: 'bg-brand-soft text-[#1677ff]',
  bad: 'bg-red-50 text-red-600',
}

/**
 * 글자 옆에 붙는 작은 표시(기본·테스트·환불 등). 12px — 화면마다 11px·10px 로 따로 짜면 크기가 갈라진다.
 * 상태 칸 하나를 차지하는 상태는 StatusText, 다른 값 옆에 덧붙는 표시는 이것.
 */
export function Tag({ tone = 'neutral', title, children }: { tone?: TagTone; title?: string; children: ReactNode }) {
  return (
    <span title={title} className={`inline-flex items-center rounded px-1.5 text-xs leading-5 font-sans font-medium whitespace-nowrap ${TAG_CLS[tone]}`}>
      {children}
    </span>
  )
}
