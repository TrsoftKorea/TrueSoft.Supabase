import { useEffect, useState } from 'react'
import { loadGameSettings } from './gameTime'
import type { ProjectTarget } from './projectTarget'

// 언어별 글(우편·푸시 문구·자주 묻는 질문)에서 고를 수 있는 언어. 고객센터 창(support/src/i18n.ts)과 같은 목록이다.
export const LANGS = ['en', 'ja', 'ko', 'zh-CN', 'zh-TW', 'de', 'fr', 'es', 'pt', 'ru', 'id', 'th', 'vi']
export const LANG_LABEL: Record<string, string> = {
  en: '영어', ja: '일본어', ko: '한국어', 'zh-CN': '중국어 간체', 'zh-TW': '중국어 번체',
  de: '독일어', fr: '프랑스어', es: '스페인어', pt: '포르투갈어', ru: '러시아어',
  id: '인도네시아어', th: '태국어', vi: '베트남어',
}
export const langLabel = (l: string) => LANG_LABEL[l] ?? l

/**
 * 이 프로젝트의 기본 언어 — 언어 표시 없는 제목·본문이 이 언어다. 받기 전이나 못 받았으면 null.
 * null 이어도 화면은 막지 않는다(이름을 안 보이고 언어 추가 목록에서 빼지 않을 뿐).
 */
export function useDefaultLanguage(target: ProjectTarget): string | null {
  const [lang, setLang] = useState<string | null>(null)
  useEffect(() => {
    let alive = true
    setLang(null)
    loadGameSettings(target).then((s) => { if (alive) setLang(s.default_language || null) }).catch(() => {})
    return () => { alive = false }
  }, [target])
  return lang
}

/** 「언어 추가」에서 고를 수 있는 언어 — 기본 언어와 다른 줄이 이미 고른 언어를 뺀다. 지금 줄의 값은 남긴다. */
export function pickableLangs(defaultLang: string | null, taken: string[], current?: string): string[] {
  return LANGS.filter((l) => l === current || (l !== defaultLang && !taken.includes(l)))
}

/** 위 칸 이름 — 기본 언어를 알면 "한국어 제목", 모르면 "제목". */
export const defaultLangField = (defaultLang: string | null, name: string) =>
  defaultLang ? `${langLabel(defaultLang)} ${name}` : name
