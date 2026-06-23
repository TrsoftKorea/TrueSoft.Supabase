# 플레이나누 이관

플레이나누와 SDK를 동시에 운영하면서 단계적으로 SDK로 전환할 때 사용합니다.  
`SupabaseRuntime` 대신 `PlayNanooRuntime`을 씬에 배치하면, 게임 코드의 `Supabase.*` 호출이 자동으로 플레이나누를 경유합니다.

**지원 기능:**
- 게스트·Google·Apple 로그인
- 익명 계정 → Google·Apple 연동
- 로그아웃, 탈퇴 예약
- 탈퇴 복구 (`OnWithdrawalPending` · `OnWithdrawalRestored` 이벤트)
- `updated_at` 기반 PlayNANOO ↔ SDK 데이터 동기화

자세한 사용법은 [플레이나누 이관](/guide/migration/how-it-works)을 참고하세요.
