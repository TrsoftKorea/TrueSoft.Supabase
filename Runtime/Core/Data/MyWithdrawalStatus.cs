namespace TrueBase.Core.Data
{
    /// <summary>
    /// 로그인 사용자 본인 기준 탈퇴 예약 상태 스냅샷.
    /// RPC <c>ts_my_withdrawal_status</c> 응답을 SDK에서 다루기 쉬운 형태로 담습니다.
    /// </summary>
    public sealed class MyWithdrawalStatus
    {
        public MyWithdrawalStatus(
            string displayName,
            string withdrawnAtIso,
            string serverNowIso,
            bool isScheduled,
            long secondsRemaining)
        {
            DisplayName = displayName ?? string.Empty;
            WithdrawnAtIso = withdrawnAtIso;
            ServerNowIso = serverNowIso;
            IsScheduled = isScheduled;
            SecondsRemaining = secondsRemaining < 0 ? 0 : secondsRemaining;
        }

        /// <summary>현재 표시 이름. 없으면 빈 문자열.</summary>
        public string DisplayName { get; }

        /// <summary>예약된 탈퇴 시각(ISO 8601). 예약이 없으면 null.</summary>
        public string WithdrawnAtIso { get; }

        /// <summary>응답 시점의 서버 시각(ISO 8601). 남은 시간 계산 기준.</summary>
        public string ServerNowIso { get; }

        /// <summary>탈퇴가 예약된 상태면 true.</summary>
        public bool IsScheduled { get; }

        /// <summary>실제 탈퇴까지 남은 시간(초). 0 미만은 0으로 보정됩니다.</summary>
        public long SecondsRemaining { get; }
    }
}

