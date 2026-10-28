import { useEffect, useState } from 'react'
import { callAdmin } from './api'
import type { ProjectTarget } from './projectTarget'

type ServerRow = { server_code: string; display_name: string | null }

/**
 * 서버 코드를 화면에 보일 이름으로 바꾸는 함수. 목록을 못 받았거나 이름이 없는 서버는 코드 그대로 보인다 —
 * 코드도 맞는 정보라 이름 때문에 화면을 막지 않는다.
 */
export function useServerName(target: ProjectTarget): (code: string | null | undefined) => string {
  const [names, setNames] = useState<Map<string, string>>(new Map())

  useEffect(() => {
    let alive = true
    callAdmin<{ rows: ServerRow[] }>(target, 'mails.getServers')
      .then((r) => {
        if (alive) setNames(new Map(r.rows.filter((s) => s.display_name).map((s) => [s.server_code, s.display_name!])))
      })
      .catch(() => {})
    return () => {
      alive = false
    }
  }, [target])

  return (code) => (code ? names.get(code) ?? code : '-')
}

/** 우편 대상 칸의 "Global 서버" — 이름이 이미 "서버"로 끝나면("1서버") 다시 붙이지 않는다. */
export function serverTargetLabel(name: string): string {
  return name.endsWith('서버') ? name : `${name} 서버`
}
