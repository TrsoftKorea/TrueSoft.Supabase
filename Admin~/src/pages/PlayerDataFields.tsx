import { useNavigate } from 'react-router-dom'
import { Database } from 'lucide-react'
import type { ProjectTarget } from '../lib/projectTarget'
import { PageHeader } from '../components/ui/PageHeader'
import { Button } from '../components/ui/Button'
import ColumnsTab from './dataManagement/ColumnsTab'

export default function PlayerDataFields({
  target,
  onUnauthenticated,
}: {
  target: ProjectTarget
  onUnauthenticated: () => void
}) {
  const navigate = useNavigate()
  return (
    <div className="space-y-4">
      <PageHeader title="플레이어 데이터 필드" description="모든 플레이어의 데이터에 담기는 필드를 관리합니다." actions={<Button onClick={() => navigate('/player-data')}><Database className="w-3.5 h-3.5" />플레이어 데이터</Button>} />
      <ColumnsTab target={target} onUnauthenticated={onUnauthenticated} />
    </div>
  )
}
