// 유저 데이터(user_data 테이블) 관련 공유 로직.
// 플레이어 상세의 "유저 데이터" 탭과 데이터 관리의 "플레이어 데이터" 탭이 똑같은
// "컬럼 정의 + 현재 값 합치기"를 각자 구현하고 있었다 — 여기 하나로 모은다.
import { callAdmin } from './api'
import type { ProjectTarget } from './projectTarget'
import type { ValueType } from '../components/ui/TypedValueField'

export type UserDataCol = { column_name: string; data_type: string; is_nullable: string; column_default: string | null }
export type UserDataField = { column_name: string; data_type: string; value: unknown }

// 시스템이 관리하는 컬럼 — 게임 데이터가 아니라 편집 대상 필드 목록에서 뺀다.
export const SYSTEM_COLS = new Set(['id', 'account_id', 'user_id', 'server_id', 'created_at', 'updated_at'])

export const PG_INTEGER_TYPES = ['integer', 'smallint', 'bigint', 'int2', 'int4', 'int8']
export const PG_DECIMAL_TYPES = ['numeric', 'real', 'double precision', 'float4', 'float8']

export const isIntegerPgType = (t: string): boolean => PG_INTEGER_TYPES.includes(t)
export const isDecimalPgType = (t: string): boolean => PG_DECIMAL_TYPES.includes(t)

/** Postgres data_type 문자열을 값 편집 위젯이 아는 4가지 타입으로 매핑한다. */
export function pgTypeToValueType(dataType: string): ValueType {
  if (dataType === 'boolean') return 'boolean'
  if (dataType === 'jsonb' || dataType === 'json') return 'json'
  if (isIntegerPgType(dataType) || isDecimalPgType(dataType)) return 'number'
  return 'string'
}

/** accountId 의 필드 정의 + 현재 값을 합쳐 돌려준다. row 는 "마지막 저장" 같은 메타 표시용으로 함께 준다. */
export async function fetchUserDataFields(
  target: ProjectTarget,
  accountId: string,
): Promise<{ fields: UserDataField[]; row: Record<string, unknown> | null }> {
  const [cols, row] = await Promise.all([
    callAdmin<UserDataCol[]>(target, 'userData.columns'),
    callAdmin<Record<string, unknown> | null>(target, 'userData.get', { accountId }),
  ])
  const fields = cols
    .filter((c) => !SYSTEM_COLS.has(c.column_name))
    .map((c) => ({ column_name: c.column_name, data_type: c.data_type, value: row?.[c.column_name] ?? null }))
  return { fields, row }
}
