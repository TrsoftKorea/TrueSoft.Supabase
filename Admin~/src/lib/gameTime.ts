import { useEffect, useState } from 'react'
import { callAdmin } from './api'
import type { ProjectTarget } from './projectTarget'

// 게임 시간대 — 통계의 "하루", 탈퇴 삭제 자정, 새 우편 예약·리더보드 회차의 기준. 게임마다 "설정 › 시간대"에서 정한다.
// 운영자 PC 시간대와 무관하게 이 시간대로 읽고 써야 서버와 어긋나지 않는다.

export const DEFAULT_TIME_ZONE = 'Asia/Seoul'

// 고를 수 있는 시간대. DB 가 아는 이름(pg_timezone_names)만 받는다 — 목록 밖 값이 이미 저장돼 있으면 그것도 보인다.
// 이름 뒤 시차는 괄호로 감싸지 않는다 — 항목 이름에 괄호 부연 금지(.claude/rules/console.md 「글」).
export const TIME_ZONES: readonly (readonly [string, string])[] = [
  ['Asia/Seoul', '한국 UTC+9'],
  ['Asia/Tokyo', '일본 UTC+9'],
  ['Asia/Shanghai', '중국 UTC+8'],
  ['Asia/Taipei', '대만 UTC+8'],
  ['Asia/Singapore', '싱가포르 UTC+8'],
  ['Asia/Bangkok', '태국 UTC+7'],
  ['Asia/Jakarta', '인도네시아 서부 UTC+7'],
  ['Asia/Kolkata', '인도 UTC+5:30'],
  ['Europe/Moscow', '러시아 모스크바 UTC+3'],
  ['Europe/Berlin', '중부 유럽 UTC+1 서머타임'],
  ['Europe/London', '영국 UTC+0 서머타임'],
  ['UTC', '협정 세계시 UTC'],
  ['America/Sao_Paulo', '브라질 UTC-3'],
  ['America/New_York', '미국 동부 UTC-5 서머타임'],
  ['America/Chicago', '미국 중부 UTC-6 서머타임'],
  ['America/Los_Angeles', '미국 서부 UTC-8 서머타임'],
  ['Australia/Sydney', '호주 동부 UTC+10 서머타임'],
]

/** 화면 문구용 이름. "한국 UTC+9" → "한국 시간". 목록 밖이면 시간대 이름 그대로. */
export function timeZoneName(tz: string): string {
  const label = TIME_ZONES.find(([v]) => v === tz)?.[1]
  return label ? `${label.replace(/\s+UTC.*$/, '')} 시간` : tz
}

export type GameSettings = { time_zone: string; updated_at: string | null; updated_by: string | null; example_deadline: string | null }

const cache = new Map<string, Promise<string>>()

/** 설정을 바꾼 화면이 부른다 — 다른 화면이 옛 시간대를 계속 쓰지 않게. */
export function forgetGameTimeZone(target: ProjectTarget) {
  cache.delete(target)
}

/** 이 게임의 시간대. 받기 전에는 null — 시각을 서버에 보내는 버튼은 그동안 막는다. */
export function useGameTimeZone(target: ProjectTarget): string | null {
  const [tz, setTz] = useState<string | null>(null)
  useEffect(() => {
    let alive = true
    let p = cache.get(target)
    if (!p) {
      p = callAdmin<GameSettings>(target, 'game.settingsGet').then((s) => s.time_zone || DEFAULT_TIME_ZONE)
      p.catch(() => cache.delete(target))
      cache.set(target, p)
    }
    p.then((v) => { if (alive) setTz(v) }).catch(() => {})
    return () => { alive = false }
  }, [target])
  return tz
}

const pad2 = (n: number) => String(n).padStart(2, '0')

/** instant 를 tz 의 벽시계 구성요소로. */
export function zonedParts(date: Date, tz: string) {
  const parts = new Intl.DateTimeFormat('en-US', {
    timeZone: tz, hour12: false, weekday: 'short',
    year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', second: '2-digit',
  }).formatToParts(date)
  const get = (t: string) => parts.find((p) => p.type === t)?.value ?? ''
  const dowMap: Record<string, number> = { Sun: 0, Mon: 1, Tue: 2, Wed: 3, Thu: 4, Fri: 5, Sat: 6 }
  let hh = Number(get('hour'))
  if (hh === 24) hh = 0 // 일부 환경에서 자정을 24로 반환
  return {
    y: Number(get('year')), mo: Number(get('month')), d: Number(get('day')),
    hh, mm: Number(get('minute')), ss: Number(get('second')), dow: dowMap[get('weekday')] ?? 1,
  }
}

/** tz 의 벽시계(연·월·일·시·분)가 가리키는 instant(ISO). 서머타임 경계도 맞춘다. */
export function wallClockToIso(tz: string, y: number, mo: number, d: number, hh: number, mm: number): string {
  const target = Date.UTC(y, mo - 1, d, hh, mm)
  let t = target
  // 그 시각의 시차를 재고 빼기를 두 번 — 서머타임 경계 근처에서 한 번으로는 한 시간 어긋날 수 있다.
  for (let i = 0; i < 2; i++) {
    const p = zonedParts(new Date(t), tz)
    t += target - Date.UTC(p.y, p.mo - 1, p.d, p.hh, p.mm, p.ss)
  }
  return new Date(t).toISOString()
}

/** datetime-local 값("YYYY-MM-DDTHH:mm", 초가 붙기도 함)을 tz 시각으로 읽어 ISO 로. */
export function localInputToIso(local: string, tz: string): string {
  const [date = '', time = '00:00'] = local.split('T')
  const [y, mo, d] = date.split('-').map(Number)
  const [hh, mm] = time.split(':').map(Number)
  return wallClockToIso(tz, y!, mo!, d!, hh ?? 0, mm ?? 0)
}

/** ISO 를 tz 기준 datetime-local 값('YYYY-MM-DDTHH:MM')으로. */
export function isoToLocalInput(iso: string | null | undefined, tz: string): string {
  if (!iso) return ''
  const date = new Date(iso)
  if (Number.isNaN(date.getTime())) return ''
  const p = zonedParts(date, tz)
  return `${p.y}-${pad2(p.mo)}-${pad2(p.d)}T${pad2(p.hh)}:${pad2(p.mm)}`
}
