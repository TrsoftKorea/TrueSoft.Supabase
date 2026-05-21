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

        public string DisplayName { get; }
        public string WithdrawnAtIso { get; }
        public string ServerNowIso { get; }
        public bool IsScheduled { get; }
        public long SecondsRemaining { get; }
    }
}

