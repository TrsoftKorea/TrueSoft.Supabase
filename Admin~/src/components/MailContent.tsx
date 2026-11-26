import { InfoList, type InfoItem } from './ui/InfoList'
import { itemLabels } from './ui/format'
import { Tags } from './ui/StatusText'
import { defaultLangField, langLabel } from '../lib/languages'

export type MailLocalized = Record<string, { title?: string | null; content?: string | null }> | null | undefined

/** 보낸 우편의 전문 — 기본 언어 제목·본문, 언어별 제목·본문, 아이템. 칸 이름은 우편 발송 화면과 같다("한국어 제목"). */
export function MailContent({ title, content, localized, items, catalog, defaultLang }: {
  title: string | null | undefined
  content: string | null | undefined
  localized: MailLocalized
  items: { key: string; count: number }[] | null | undefined
  catalog: { key: string; display_name: string }[]
  defaultLang: string | null
}) {
  const text = (v: string | null | undefined) => (v ? <span className="whitespace-pre-wrap">{v}</span> : null)
  const groups: InfoItem[][] = [[
    { label: defaultLangField(defaultLang, '제목'), value: text(title), wide: true },
    { label: defaultLangField(defaultLang, '본문'), value: text(content), wide: true },
  ]]
  for (const [lang, v] of Object.entries(localized ?? {})) {
    groups.push([
      { label: `${langLabel(lang)} 제목`, value: text(v?.title), wide: true },
      { label: `${langLabel(lang)} 본문`, value: text(v?.content), wide: true },
    ])
  }
  groups.push([{ label: '아이템', value: items?.length ? <Tags wrap items={itemLabels(items, catalog)} /> : null, wide: true }])
  // 언어마다 줄로 갈라 둔다 — 이어 붙이면 어느 본문이 어느 언어인지 칸 이름을 하나하나 읽어야 갈린다.
  return (
    // 폭을 묶는다 — 표 안에서 펼치면 표 전체 폭까지 늘어나 긴 본문 한 줄이 화면 밖으로 나간다.
    <div className="max-w-2xl divide-y divide-neutral-200">
      {groups.map((g, i) => <InfoList key={i} items={g} className="py-3 first:pt-0 last:pb-0" />)}
    </div>
  )
}
