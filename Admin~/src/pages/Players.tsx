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
      <PlayerDetail
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
