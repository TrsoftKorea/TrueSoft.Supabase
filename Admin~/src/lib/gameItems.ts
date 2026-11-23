// grant_mode — auto: 받을 때 서버가 같은 이름의 숫자 데이터 필드에 더함 · game: 게임 처리 코드가 지급 ·
// missing: 데이터 필드에서 골랐는데 그 필드가 사라짐 · blocked: 코드가 숫자가 아닌 데이터 필드와 같음(등록 뒤 생긴 필드).
export type GrantMode = 'auto' | 'game' | 'missing' | 'blocked'

/** gameItems.list 한 줄. grant_mode 가 없으면 옛 서버다. */
export type CatalogItem = { key: string; display_name: string; grant_mode?: GrantMode }

/** 화면에 쓰는 지급 방식 이름 — 화면마다 따로 적지 않는다(명칭 통일, 화면 검사 U20). */
export const GRANT_NAME: Record<GrantMode, string> = {
  auto: '자동 지급',
  game: '게임 처리',
  missing: '필드 없음',
  blocked: '형식 다름',
}

/** 운영자가 고쳐야 하는 아이템(처리 코드가 없으면 받기 실패) — 카탈로그·사이드바·아이템을 고르는 화면이 같이 쓴다. */
export const isBrokenGrant = (m: GrantMode | undefined) => m === 'missing' || m === 'blocked'

/** 고르기 목록의 한 줄 — 이름 뒤에 지급 방식. */
export const itemOptionLabel = (c: CatalogItem) =>
  c.grant_mode ? `${c.display_name || c.key} · ${GRANT_NAME[c.grant_mode]}` : (c.display_name || c.key)

/** 고른 아이템 중 지급이 막힌 것(같은 코드는 한 번만). */
export function brokenItemsIn(picked: { key: string }[], catalog: CatalogItem[]): CatalogItem[] {
  const keys = new Set(picked.map((p) => p.key))
  return catalog.filter((c) => keys.has(c.key) && isBrokenGrant(c.grant_mode))
}
