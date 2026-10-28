import { useState } from 'react'
import type { ProjectTarget } from '../lib/projectTarget'
import { PageHeader } from '../components/ui/PageHeader'
import { Tabs } from '../components/ui/Tabs'
import LeaderboardsTab from './leaderboard/LeaderboardsTab'
import ColumnsTab from './leaderboard/ColumnsTab'
import ScoresTab from './leaderboard/ScoresTab'

type Tab = 'boards' | 'columns' | 'scores'

export default function Leaderboard({
  target,
  onUnauthenticated,
}: {
  target: ProjectTarget
  onUnauthenticated: () => void
}) {
  const [tab, setTab] = useState<Tab>('boards')
  const [selectedCode, setSelectedCode] = useState('')

  return (
    <div className="space-y-4">
      <PageHeader
        title="리더보드"
        description="게임에서 사용할 랭킹을 만들고 기록을 관리합니다."
      />

      <Tabs value={tab} onChange={setTab} tabs={[['boards', '리더보드'], ['columns', '필드'], ['scores', '기록']]} />

      {tab === 'boards' && (
        <LeaderboardsTab target={target} onUnauthenticated={onUnauthenticated} selectedCode={selectedCode} onSelect={setSelectedCode} />
      )}
      {tab === 'columns' && (
        <ColumnsTab target={target} onUnauthenticated={onUnauthenticated} selectedCode={selectedCode} onSelect={setSelectedCode} />
      )}
      {tab === 'scores' && (
        <ScoresTab target={target} onUnauthenticated={onUnauthenticated} selectedCode={selectedCode} onSelect={setSelectedCode} />
      )}
    </div>
  )
}
