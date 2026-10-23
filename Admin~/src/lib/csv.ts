type Cell = string | number | null | undefined

function escapeCell(v: Cell): string {
  const s = v == null ? '' : String(v)
  // 엑셀이 =·+·-·@ 로 시작하는 칸을 수식으로 실행하므로 앞에 ' 를 붙여 글자로 둔다(닉네임·코드는 사용자가 정한 값이다).
  const safe = /^[=+\-@\t\r]/.test(s) ? `'${s}` : s
  return /[",\r\n]/.test(safe) ? `"${safe.replace(/"/g, '""')}"` : safe
}

/**
 * CSV 파일을 내려받게 한다. 엑셀이 한글을 깨뜨리지 않도록 UTF-8 BOM 을 붙인다.
 */
export function downloadCsv(filename: string, headers: string[], rows: Cell[][]): void {
  const text = [headers, ...rows].map((r) => r.map(escapeCell).join(',')).join('\r\n')
  const blob = new Blob(['﻿' + text], { type: 'text/csv;charset=utf-8' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = filename
  document.body.appendChild(a)
  a.click()
  a.remove()
  URL.revokeObjectURL(url)
}

/** 파일 이름에 넣을 오늘 날짜(YYYYMMDD, 현지 시각). */
export function todayStamp(): string {
  const d = new Date()
  return `${d.getFullYear()}${String(d.getMonth() + 1).padStart(2, '0')}${String(d.getDate()).padStart(2, '0')}`
}
