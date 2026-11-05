import { useCallback, useState, type ReactNode } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { Check, Copy, X } from 'lucide-react'
import { copyToClipboard } from '../../lib/clipboard'

/**
 * 플레이어는 어느 화면에서나 같은 꼴이다 — 닉네임, 눌러서 상세, 닉네임이 없으면 계정 ID 앞부분.
 * 화면마다 짜면 어떤 곳은 유저 ID 를, 어떤 곳은 눌리지 않는 글자를 보이게 된다(.claude/rules/console.md 검사 U12).
 */
export const withPlayer = (path: string, accountId: string, name?: string | null) =>
  `${path}?account=${encodeURIComponent(accountId)}&name=${encodeURIComponent(name ?? '')}`

export const playerPath = (accountId: string, name?: string | null) => withPlayer('/players', accountId, name)

/** 글자로만 쓸 때(창 설명·안내 문구). */
export const playerLabel = (name: string | null | undefined, accountId?: string | null, userId?: string | null) =>
  name || accountId?.slice(0, 8) || userId || '-'

export function PlayerLink({
  accountId,
  name,
  userId,
  className = '',
  children,
}: {
  accountId: string | null | undefined
  name?: string | null | undefined
  /** 계정이 사라진 기록(탈퇴 등)은 유저 ID 만 남는다. 눌러서 갈 곳이 없어 회색 글자로 보인다. */
  userId?: string | null | undefined
  className?: string
  children?: ReactNode
}) {
  if (!accountId) {
    return (
      <span className={`text-neutral-400 ${className}`} title={userId ?? undefined}>
        {children ?? (name || (userId ? <span className="font-mono text-xs">{userId}</span> : '-'))}
      </span>
    )
  }
  return (
    <Link
      to={playerPath(accountId, name)}
      // 줄 전체를 누르면 열리는 표(고객센터 등)에서도 이 글자는 상세로 간다.
      onClick={(e) => e.stopPropagation()}
      className={`text-[#1677ff] hover:underline ${className}`}
      title={accountId}
    >
      {children ?? (name || <span className="font-mono text-xs">{accountId.slice(0, 8)}</span>)}
    </Link>
  )
}

/** 계정 ID 칸 — 표에서는 줄여 보이고 복사로 전체를 가져간다. */
export function AccountIdCell({ accountId }: { accountId: string }) {
  const [copied, setCopied] = useState(false)
  const copy = async (e: React.MouseEvent) => {
    e.stopPropagation()
    if (await copyToClipboard(accountId)) {
      setCopied(true)
      setTimeout(() => setCopied(false), 1500)
    }
  }
  return (
    <div className="flex items-center gap-1.5 font-mono text-xs text-neutral-600">
      <span className="truncate max-w-[140px]" title={accountId}>{accountId}</span>
      <button
        type="button"
        onClick={(e) => void copy(e)}
        className={`flex-shrink-0 transition-colors ${copied ? 'text-emerald-500' : 'text-neutral-400 hover:text-neutral-700'}`}
        aria-label="계정 ID 복사"
        title="계정 ID 복사"
      >
        {copied ? <Check className="w-3 h-3" /> : <Copy className="w-3 h-3" />}
      </button>
    </div>
  )
}

/** 앞 게임에서만 뜻이 있는 주소 값을 지운다(게임을 바꿀 때). 플레이어·검색어·쪽 번호가 새 게임에 남으면 "기록 없음"처럼 보인다. */
export function dropGameParams(p: URLSearchParams): URLSearchParams {
  for (const k of ['account', 'name', 'q', 'page']) p.delete(k)
  return p
}

/** 값이 바뀌면 그 렌더에서 곧바로 fn 을 부른다 — 효과(useEffect)로 하면 옛 값으로 요청이 한 번 먼저 나간다. */
export function useOnChange<T>(value: T, fn: () => void) {
  const [prev, setPrev] = useState(value)
  if (prev !== value) {
    setPrev(value)
    fn()
  }
}

/** 주소에 플레이어를 실어 열린 화면(`?account=&name=`) — 그 플레이어의 기록만 본다. */
export function useFilterPlayer() {
  const [params, setParams] = useSearchParams()
  const accountId = params.get('account') ?? ''
  const name = params.get('name') ?? ''
  const clear = useCallback(() => {
    setParams((p) => {
      p.delete('account')
      p.delete('name')
      return p
    }, { replace: true })
  }, [setParams])
  return { accountId, name, clear }
}

/** 거르고 있는 플레이어 표시. 검색칸과 높이가 같다. */
export function PlayerFilterChip({ accountId, name, onClear }: { accountId: string; name: string; onClear: () => void }) {
  return (
    <span className="inline-flex h-9 items-center gap-1.5 rounded-md border border-neutral-300 bg-neutral-50 px-3 text-sm whitespace-nowrap">
      <PlayerLink accountId={accountId} name={name} />
      <button type="button" onClick={onClear} className="text-neutral-400 hover:text-neutral-700" aria-label="플레이어 거르기 해제" title="플레이어 거르기 해제">
        <X className="w-3.5 h-3.5" />
      </button>
    </span>
  )
}
