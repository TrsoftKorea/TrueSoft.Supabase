import { GRANT_NAME, isBrokenGrant, type CatalogItem, type GrantMode } from '../lib/gameItems'
import { Tag } from './ui/StatusText'

const CANNOT_RECEIVE = '게임에 처리 코드가 없으면 받을 수 없습니다.'

/** 고른 아이템 줄 바로 아래 — 지급이 막힌 아이템일 때만 보인다. 막지는 않는다(게임에 처리 코드가 있을 수 있다). */
export function BrokenItemNote({ mode }: { mode: GrantMode | undefined }) {
  if (!mode || !isBrokenGrant(mode)) return null
  return <p className="text-xs text-red-600">{GRANT_NAME[mode]} — {CANNOT_RECEIVE}</p>
}

/** 보내기·저장 확인 창 — 지급이 막힌 아이템을 한 번 더 보여 준다. */
export function BrokenItemsConfirm({ items }: { items: CatalogItem[] }) {
  if (items.length === 0) return null
  return (
    <div className="rounded-md border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700 space-y-1">
      {items.map((c) => (
        <p key={c.key}>
          <span className="font-medium">{c.display_name || c.key}</span>
          <span className="font-mono text-xs"> {c.key}</span>
          {c.grant_mode && <span className="ml-2"><Tag tone="bad">{GRANT_NAME[c.grant_mode]}</Tag></span>}
        </p>
      ))}
      <p className="text-red-600">{CANNOT_RECEIVE}</p>
    </div>
  )
}
