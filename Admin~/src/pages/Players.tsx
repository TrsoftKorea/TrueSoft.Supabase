import { useSearchParams } from 'react-router-dom'
import type { ProjectTarget } from '../lib/projectTarget'
import PlayerList from './players/PlayerList'
import PlayerDetail from './players/PlayerDetail'

export default function Players({
  target,
  onUnauthenticated,
}: {
  target: ProjectTarget
  onUnauthenticated: () => void
}) {
  const [params, setParams] = useSearchParams()
  const account = params.get('account')
  const name = params.get('name') ?? ''

  if (account) {
    return (
      // key 가 없으면 친구 탭에서 다른 플레이어로 넘어갈 때 화면이 새로 만들어지지 않아
      // 앞 사람의 닉네임·결제·차단 상태가 그대로 남는다 — 그 화면에서 누른 차단은 새 사람에게 걸린다.
      <PlayerDetail
        key={account}
        target={target}
        onUnauthenticated={onUnauthenticated}
        accountId={account}
        initialName={name}
        onBack={() => setParams({})}
      />
    )
  }
  return (
    <PlayerList
      target={target}
      onUnauthenticated={onUnauthenticated}
      onSelect={(id, n) => setParams({ account: id, name: n })}
    />
  )
}
