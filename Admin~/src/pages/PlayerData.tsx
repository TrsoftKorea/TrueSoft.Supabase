import { useNavigate, useSearchParams } from 'react-router-dom'
import { SlidersHorizontal } from 'lucide-react'
import type { ProjectTarget } from '../lib/projectTarget'
import { PageHeader } from '../components/ui/PageHeader'
import { Button } from '../components/ui/Button'
import PlayersTab from './dataManagement/PlayersTab'

export default function PlayerData({
  target,
  onUnauthenticated,
}: {
  target: ProjectTarget
  onUnauthenticated: () => void
}) {
  const [params] = useSearchParams()
  const navigate = useNavigate()

  return (
    <div className="space-y-4">
      <PageHeader title="플레이어 데이터" description="플레이어의 저장 데이터를 찾아 고치거나 다른 플레이어로 복사합니다." actions={<Button onClick={() => navigate('/player-data-fields')}><SlidersHorizontal className="w-3.5 h-3.5" />플레이어 데이터 필드</Button>} />
      <PlayersTab
        target={target}
        onUnauthenticated={onUnauthenticated}
        initialAccountId={params.get('account') ?? undefined}
        initialName={params.get('name') ?? undefined}
      />
    </div>
  )
}
