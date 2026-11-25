import { useCallback, useEffect, useState } from 'react'
import { ChevronDown, ChevronUp, Loader2 } from 'lucide-react'
import { callAdmin, NotAuthenticatedError } from '../../lib/api'
import type { ProjectTarget } from '../../lib/projectTarget'
import { CardHeader, WhiteCard } from '../../components/ui/Card'
import { DataTable } from '../../components/ui/DataTable'
import { InfoList } from '../../components/ui/InfoList'
import { Button } from '../../components/ui/Button'
import { formatDateTime } from '../../components/ui/format'

type Consent = {
  terms_version: number | null
  privacy_version: number | null
  age_confirmed: boolean
  agreed_at: string
  account_id: string | null
}

const version = (v: number | null) => (v === null ? null : `v${v}`)
const age = (v: boolean) => (v ? '확인함' : '확인 안 함')

/**
 * 플레이어 상세 › 기본 정보의 약관 동의. 동의는 유저 ID 로 묶여 탈퇴·전체 초기화 뒤에도 남는 증거라,
 * 예전 계정으로 남긴 동의도 함께 보인다. 맨 위 줄이 지금 적용 중인 동의다(서버가 최근 순으로 준다).
 */
export default function LegalConsentsCard({ target, accountId, onUnauthenticated }: {
  target: ProjectTarget
  accountId: string
  onUnauthenticated: () => void
}) {
  const [rows, setRows] = useState<Consent[] | null>(null)
  const [failed, setFailed] = useState(false)
  const [open, setOpen] = useState(false)

  const load = useCallback(async () => {
    setRows(null)
    setFailed(false)
    try {
      const data = await callAdmin<{ rows: Consent[] }>(target, 'legalConsents.list', { accountId })
      setRows(data.rows)
    } catch (e: unknown) {
      if (e instanceof NotAuthenticatedError) { onUnauthenticated(); return }
      setFailed(true)
    }
  }, [target, accountId, onUnauthenticated])

  useEffect(() => { void load() }, [load])

  const latest = rows?.[0]

  return (
    <WhiteCard className="p-5 space-y-3">
      <CardHeader
        title="약관 동의"
        actions={rows && rows.length > 1 ? (
          <Button size="sm" onClick={() => setOpen((v) => !v)}>
            {open ? <ChevronUp className="w-3.5 h-3.5" /> : <ChevronDown className="w-3.5 h-3.5" />}이력 {rows.length}건
          </Button>
        ) : undefined}
      />
      {failed ? (
        <p className="text-sm text-red-600">
          동의 기록을 불러오지 못했습니다.{' '}
          <button onClick={() => void load()} className="underline hover:text-red-700">다시 불러오기</button>
        </p>
      ) : !rows ? (
        <div className="py-2 flex justify-center text-neutral-400"><Loader2 className="w-5 h-5 animate-spin" /></div>
      ) : !latest ? (
        <p className="text-sm text-neutral-500">동의 기록 없음</p>
      ) : (
        <>
          <InfoList items={[
            { label: '이용약관', value: version(latest.terms_version) },
            { label: '개인정보처리방침', value: version(latest.privacy_version) },
            { label: '만 14세 이상', value: age(latest.age_confirmed) },
            { label: '동의 시각', value: formatDateTime(latest.agreed_at) },
          ]} />
          {open && (
            <div className="rounded-md border border-neutral-200 overflow-hidden">
              <DataTable
                columns={[
                  { key: 'agreed_at', header: '동의 시각', kind: 'datetime', cell: (r) => formatDateTime(r.agreed_at) },
                  { key: 'terms', header: '이용약관', kind: 'name', cell: (r) => version(r.terms_version) },
                  { key: 'privacy', header: '개인정보처리방침', kind: 'name', cell: (r) => version(r.privacy_version) },
                  { key: 'age', header: '만 14세 이상', kind: 'name', cell: (r) => age(r.age_confirmed) },
                  // 탈퇴·초기화 뒤 다시 가입했으면 같은 유저 ID 라도 계정이 다르다 — 어느 계정으로 남긴 동의인지 보인다.
                  { key: 'account', header: '계정', kind: 'name', cell: (r) => (r.account_id === accountId ? '지금 계정' : r.account_id ? '이전 계정' : '삭제된 계정') },
                ]}
                rows={rows}
                rowKey={(r, i) => `${r.agreed_at}-${i}`}
                wide
              />
            </div>
          )}
        </>
      )}
    </WhiteCard>
  )
}
