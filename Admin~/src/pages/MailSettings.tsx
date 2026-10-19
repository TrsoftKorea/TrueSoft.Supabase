import { useCallback, useState } from 'react'
import { NotAuthenticatedError } from '../lib/api'
import type { ProjectTarget } from '../lib/projectTarget'
import { PageHeader } from '../components/ui/PageHeader'
import { ErrorBanner } from '../components/ui/ErrorBanner'
import { LimitsForm, type LimitField } from './social/LimitsForm'

const MAIL_FIELDS: LimitField[] = [
  {
    param: 'expiredRetentionDays', field: 'expired_retention_days', min: 0, max: 30,
    label: '만료 우편 보관 일수',
    hint: '기간이 지난 우편을 "기한 만료"로 남겨 두는 일수. 지나면 우편함에서 사라집니다. 0이면 기간이 지나는 즉시 사라집니다. 0~30 사이.',
  },
]

export default function MailSettings({
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
      <PageHeader title="우편 설정" description="우편함 동작 값을 조정합니다." />
      <ErrorBanner message={error} onDismiss={() => setError('')} />

      <LimitsForm
        target={target}
        report={report}
        title="만료 우편"
        getAction="mails.settingsGet"
        updateAction="mails.settingsUpdate"
        fields={MAIL_FIELDS}
        confirmTitle="우편 설정을 바꿀까요?"
      />
    </div>
  )
}
