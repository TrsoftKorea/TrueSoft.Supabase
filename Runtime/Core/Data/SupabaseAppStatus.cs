using System;

namespace TrueBase.Core.Data
{
    /// <summary>앱 판·점검 상태. <c>Supabase.GetAppStatusAsync</c> 가 돌려준다.</summary>
    public sealed class SupabaseAppStatus
    {
        /// <summary>이 앱의 판이 운영 콘솔에서 정한 최소 판보다 낮으면 true.</summary>
        public bool UpdateRequired { get; internal set; }

        /// <summary>이 플랫폼의 최소 판. 정하지 않았으면 null.</summary>
        public string MinVersion { get; internal set; }

        /// <summary>업데이트 안내에서 열 스토어 주소. 모르면 null.</summary>
        public string StoreUrl { get; internal set; }

        /// <summary>지금 점검 중이고 이 계정이 점검 중 통과 계정이 아니면 true.</summary>
        public bool InMaintenance { get; internal set; }

        /// <summary>점검 중 통과 계정으로 로그인해 있으면 true. 로그인 전에는 늘 false.</summary>
        public bool MaintenanceAllowed { get; internal set; }

        /// <summary>점검 안내 문구. 점검을 켜 두지 않았으면 null.</summary>
        public string MaintenanceMessage { get; internal set; }

        /// <summary>점검 시작 시각. 켜 두었는데 비어 있으면 저장한 때부터. 지금보다 뒤면 예정된 점검이다.</summary>
        public DateTimeOffset? MaintenanceStartsAt { get; internal set; }

        /// <summary>점검 예상 종료 시각(안내용). 지나도 운영자가 끌 때까지 점검이다.</summary>
        public DateTimeOffset? MaintenanceEndsAt { get; internal set; }
    }
}
