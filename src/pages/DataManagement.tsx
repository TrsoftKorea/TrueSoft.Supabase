import { useSearchParams } from 'react-router-dom'
import type { ProjectTarget } from '../lib/projectTarget'
import { PageHeader } from '../components/ui/PageHeader'
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
      <div className="border-b border-neutral-200 flex gap-1">
        {(
          [
            ['players', '플레이어 데이터'],
            ['columns', '필드 관리'],
          ] as const
        ).map(([key, label]) => (
          <button
            key={key}
            onClick={() => switchTab(key)}
            className={[
              'h-10 px-4 text-sm border-b-2 -mb-px transition-colors',
              tab === key ? 'border-[#1677ff] text-[#1677ff] font-medium' : 'border-transparent text-neutral-600 hover:text-neutral-900',
            ].join(' ')}
          >
            {label}
          </button>
        ))}
      </div>

      {tab === 'players' ? (
        <PlayersTab target={target} onUnauthenticated={onUnauthenticated} initialAccountId={account} initialName={name} />
      ) : (
        <ColumnsTab target={target} onUnauthenticated={onUnauthenticated} />
      )}
    </div>
  )
}
