// grant_mode — auto: 받을 때 서버가 같은 이름의 숫자 데이터 필드에 더함 · game: 게임 처리 코드가 지급 ·
// missing: 데이터 필드에서 골랐는데 그 필드가 사라짐 · blocked: 코드가 숫자가 아닌 데이터 필드와 같음(등록 뒤 생긴 필드).
export type GrantMode = 'auto' | 'game' | 'missing' | 'blocked'

/** 운영자가 고쳐야 하는 아이템(처리 코드가 없으면 받기 실패) — 아이템 카탈로그 화면과 사이드바 빨간 표시가 같이 쓴다. */
export const isBrokenGrant = (m: GrantMode | undefined) => m === 'missing' || m === 'blocked'
