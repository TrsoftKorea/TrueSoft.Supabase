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
export const opLabel = (feature: string, action: string) =>
  `${FEATURE_LABEL[feature] ?? feature} ${ACTION_LABEL[action] ?? action}`
