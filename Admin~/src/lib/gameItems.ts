// grant_mode — auto: 받을 때 서버가 같은 이름의 숫자 데이터 필드(또는 리스트·딕셔너리 필드의 그 원소)에 더함 · game: 게임 처리 코드가 지급 ·
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

/**
 * 코드가 리스트 원소(`칸[번호]`, 0부터)나 딕셔너리 원소(`칸.키`, 첫 점 뒤 전부)를 가리키면 그 칸 이름.
 * 서버 ts_mail_element_key 와 같은 꼴이다 — 그 칸이 리스트·딕셔너리 데이터 필드인지는 부르는 쪽이 본다.
 */
export function elementField(key: string): string | null {
  return elementKey(key)?.field ?? null
}

function elementKey(key: string): { field: string; list: boolean } | null {
  const list = key.match(/^([A-Za-z_][A-Za-z0-9_]*)\[(?:0|[1-9][0-9]{0,5})\]$/)
  if (list) return { field: list[1]!, list: true }
  const dict = key.match(/^([A-Za-z_][A-Za-z0-9_]*)\.(?:.+)$/)
  return dict ? { field: dict[1]!, list: false } : null
}

/**
 * 리스트·딕셔너리 데이터 필드에 저장된 값의 모양(서버가 표본으로 봄) — list: 숫자 리스트 · dict: 숫자 딕셔너리 ·
 * other: 객체·글자 등이라 원소 지급 불가 · null: 저장된 값이 없어 모름. 옛 서버는 칸을 안 보내 undefined.
 */
export type CollectionKind = 'list' | 'dict' | 'other' | null

/** 원소 꼴 코드가 그 필드 모양으로 지급되는가 — 서버 ts_mail_grantable_key 와 같은 규칙. 모르면(null·옛 서버) 둘 다 받는다. */
export function elementFits(key: string, kind: CollectionKind | undefined): boolean {
  const e = elementKey(key)
  if (!e || kind === 'other') return false
  return kind === 'list' ? e.list : kind === 'dict' ? !e.list : true
}

/** 운영자가 고쳐야 하는 아이템(처리 코드가 없으면 받기 실패) — 카탈로그·사이드바·아이템을 고르는 화면이 같이 쓴다. */
export const isBrokenGrant = (m: GrantMode | undefined) => m === 'missing' || m === 'blocked'

/** 고르기 목록의 한 줄 — 이름 뒤에 지급 방식. 선택지는 글자만 들어가 태그를 못 쓰니 쉼표로 가른다. */
export const itemOptionLabel = (c: CatalogItem) =>
  c.grant_mode ? `${c.display_name || c.key}, ${GRANT_NAME[c.grant_mode]}` : (c.display_name || c.key)

/** 고른 아이템 중 지급이 막힌 것(같은 코드는 한 번만). */
export function brokenItemsIn(picked: { key: string }[], catalog: CatalogItem[]): CatalogItem[] {
  const keys = new Set(picked.map((p) => p.key))
  return catalog.filter((c) => keys.has(c.key) && isBrokenGrant(c.grant_mode))
}
