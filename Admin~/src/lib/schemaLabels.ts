// 변경 관리·미게시 패널이 공유하는 작업 라벨.
export const FEATURE_LABEL: Record<string, string> = {
  leaderboard_field: '리더보드 필드',
  leaderboard_table: '리더보드',
  user_data_field: '유저 데이터 필드',
  remote_config: '원격 설정',
}
export const ACTION_LABEL: Record<string, string> = {
  add: '추가', update: '수정', drop: '삭제',
  attach: '등록', detach: '해제', create: '생성', delete: '삭제',
}
// 필드 타입 표시 이름. 게임 코드의 타입과 맞춰야 하므로 원래 이름은 괄호로 남긴다.
const FIELD_TYPE_KO: Record<string, string> = {
  string: '글자', short: '작은 정수', int: '정수', long: '큰 정수',
  float: '소수', double: '소수(정밀)', decimal: '소수(금액)', bool: '예/아니오',
  DateTimeOffset: '날짜·시각', DateTime: '날짜·시각(시간대 없음)', DateOnly: '날짜', TimeOnly: '시각',
  JSON: '묶음 데이터',
}
/** "정수 (int)" 꼴. 모르는 타입은 원래 이름 그대로. */
export const fieldTypeLabel = (t: string) => (FIELD_TYPE_KO[t] ? `${FIELD_TYPE_KO[t]} (${t})` : t)

// 표시 전용. 필드 화면들의 PG_TO_CSHARP 는 저장 로직에 쓰여 타입이 좁고 각자 둔다.
const PG_TO_CSHARP_NAME: Record<string, string> = {
  text: 'string', 'character varying': 'string', varchar: 'string',
  int2: 'short', smallint: 'short', int4: 'int', integer: 'int', int8: 'long', bigint: 'long',
  float4: 'float', real: 'float', float8: 'double', 'double precision': 'double', numeric: 'decimal',
  boolean: 'bool', bool: 'bool',
  timestamptz: 'DateTimeOffset', 'timestamp with time zone': 'DateTimeOffset',
  timestamp: 'DateTime', 'timestamp without time zone': 'DateTime',
  date: 'DateOnly', time: 'TimeOnly', 'time without time zone': 'TimeOnly',
  jsonb: 'JSON', json: 'JSON',
}
/** DB 타입 이름("integer"·"int4" 등)을 "정수 (int)" 꼴로. 이미 게임 코드 타입 이름이면 그대로 받는다. */
export const pgTypeLabel = (pg: string) => fieldTypeLabel(PG_TO_CSHARP_NAME[pg] ?? pg)

export const opLabel =(feature: string, action: string) =>
  `${FEATURE_LABEL[feature] ?? feature} ${ACTION_LABEL[action] ?? action}`
