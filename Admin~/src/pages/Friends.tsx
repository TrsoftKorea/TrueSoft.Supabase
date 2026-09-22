import { useCallback, useState } from 'react'
import { NotAuthenticatedError } from '../lib/api'
import type { ProjectTarget } from '../lib/projectTarget'
import { PageHeader } from '../components/ui/PageHeader'
import { ErrorBanner } from '../components/ui/ErrorBanner'
import { LimitsForm, type LimitField } from './social/LimitsForm'

const FRIEND_FIELDS: LimitField[] = [
  {
    param: 'maxFriends', field: 'max_friends', min: 1, max: 1000,
    label: '친구 수 상한',
    hint: '한 계정이 가질 수 있는 친구의 최대 수. 상한에 닿으면 요청도 수락도 막힙니다. 1~1000 사이.',
  },
  {
    param: 'maxPendingSent', field: 'max_pending_sent', min: 1, max: 500,
    label: '보낸 요청 대기 상한',
    hint: '아직 상대가 응답하지 않은 요청의 최대 개수. 무차별 요청을 막습니다. 1~500 사이.',
  },
  {
    param: 'requestCooldownSeconds', field: 'request_cooldown_seconds', min: 0, max: 300,
    label: '연속 요청 최소 간격',
    hint: '같은 사람이 요청을 연달아 보낼 때 기다려야 하는 시간. 초 단위로 최대 300까지, 0이면 제한 없음.',
  },
]

export default function Friends({
  target,
  onUnauthenticated,
}: {
  target: ProjectTarget
  onUnauthenticated: () => void
}) {
  const [error, setError] = useState('')

  const report = useCallback((e: unknown, fallback: string) => {
    if (e instanceof NotAuthenticatedError) { onUnauthenticated(); return }
    setError(e instanceof Error ? e.message : fallback)
  }, [onUnauthenticated])

  return (
    <div className="space-y-4">
      <PageHeader title="친구" description="친구 기능의 제한값을 조정합니다." />
      <ErrorBanner message={error} onDismiss={() => setError('')} />

      <LimitsForm
        target={target}
        report={report}
        title="친구 제한값"
        getAction="friends.settingsGet"
        updateAction="friends.settingsUpdate"
        fields={FRIEND_FIELDS}
      />
    </div>
  )
}
