import { useSearchParams } from 'react-router-dom'
import type { ProjectTarget } from '../lib/projectTarget'
import { PageHeader } from '../components/ui/PageHeader'
import { Tabs } from '../components/ui/Tabs'
import ColumnsTab from './dataManagement/ColumnsTab'
import PlayersTab from './dataManagement/PlayersTab'

type Tab = 'players' | 'columns'

export default function DataManagement({
  target,
  onUnauthenticated,
}: {
  target: ProjectTarget
  onUnauthenticated: () => void
}) {
  const [params, setParams] = useSearchParams()
  const tab: Tab = params.get('tab') === 'columns' ? 'columns' : 'players'
  const account = params.get('account') ?? undefined
  const name = params.get('name') ?? undefined

  const switchTab = (t: Tab) => setParams(t === 'players' ? {} : { tab: t })

  return (
    <div className="space-y-4">
      <PageHeader title="데이터 관리" description="플레이어의 저장 데이터와 필드를 관리합니다." />
      <Tabs value={tab} onChange={switchTab} tabs={[['players', '플레이어 데이터'], ['columns', '필드 관리']]} />

      {tab === 'players' ? (
        <PlayersTab target={target} onUnauthenticated={onUnauthenticated} initialAccountId={account} initialName={name} />
      ) : (
        <ColumnsTab target={target} onUnauthenticated={onUnauthenticated} />
      )}
    </div>
  )
}
