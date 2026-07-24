using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace TrueBase.Core.Data
{
    /// <summary>리더보드 점수 기록 방식. 운영이 리더보드마다 지정합니다.</summary>
    public enum LeaderboardRecordType
    {
        /// <summary>최고 기록만 남깁니다.</summary>
        Highest,

        /// <summary>최저 기록만 남깁니다(기록 시간 등).</summary>
        Lowest,

        /// <summary>제출할 때마다 덮어씁니다.</summary>
        Last,

        /// <summary>제출값을 계속 더합니다.</summary>
        Sum
    }

    /// <summary>리더보드 정렬 방향. 기록 방식과 독립적으로 지정합니다.</summary>
    public enum LeaderboardSortType
    {
        /// <summary>높은 점수가 상위.</summary>
        Desc,

        /// <summary>낮은 점수가 상위.</summary>
        Asc
    }

    /// <summary>리더보드 초기화 주기. 종료 시각(<c>EndsAt</c>)과는 독립된 축입니다.</summary>
    public enum LeaderboardRotation
    {
        /// <summary>초기화하지 않고 계속 누적합니다.</summary>
        None,
        Daily,
        Weekly,
        Monthly,

        /// <summary>지정한 초 단위 주기로 초기화합니다.</summary>
        Custom
    }

    /// <summary>리더보드 정의와 현재 회차 상태.</summary>
    public sealed class LeaderboardTable
    {
        [JsonProperty("code")]
        public string Code { get; set; }

        [JsonProperty("display_name")]
        public string DisplayName { get; set; }

        /// <summary><c>global</c> 이면 전체 통합, <c>server</c> 이면 현재 접속 서버별 집계.</summary>
        [JsonProperty("scope")]
        public string Scope { get; set; }

        [JsonProperty("record_type")]
        public string RecordTypeRaw { get; set; }

        [JsonProperty("sort_type")]
        public string SortTypeRaw { get; set; }

        [JsonProperty("rotation")]
        public string RotationRaw { get; set; }

        /// <summary>현재 회차 번호(1부터).</summary>
        [JsonProperty("rotation_count")]
        public int RotationCount { get; set; }

        [JsonProperty("next_rotation_at")]
        public DateTimeOffset? NextRotationAt { get; set; }

        /// <summary>다음 회차 전환까지 남은 초. 주기가 없으면 null.</summary>
        [JsonProperty("rotation_time_left")]
        public int? RotationTimeLeft { get; set; }

        /// <summary>종료 예약 시각. null이면 무기한.</summary>
        [JsonProperty("ends_at")]
        public DateTimeOffset? EndsAt { get; set; }

        /// <summary>종료·비활성 여부. true면 기록은 거부되고 순위 조회만 됩니다.</summary>
        [JsonProperty("is_ended")]
        public bool IsEnded { get; set; }

        /// <summary>현재 회차 참여자 수.</summary>
        [JsonProperty("total_ids")]
        public int TotalIds { get; set; }

        /// <summary>이 리더보드에 등록된 플레이어 데이터 컬럼 이름.</summary>
        [JsonProperty("columns")]
        public IReadOnlyList<string> Columns { get; set; }

        public LeaderboardRecordType RecordType => ParseRecordType(RecordTypeRaw);
        public LeaderboardSortType   SortType   => ParseSortType(SortTypeRaw);
        public LeaderboardRotation   Rotation   => ParseRotation(RotationRaw);

        internal static LeaderboardRecordType ParseRecordType(string v) => v switch
        {
            "lowest" => LeaderboardRecordType.Lowest,
            "last"   => LeaderboardRecordType.Last,
            "sum"    => LeaderboardRecordType.Sum,
            _        => LeaderboardRecordType.Highest
        };

        internal static LeaderboardSortType ParseSortType(string v) =>
            v == "asc" ? LeaderboardSortType.Asc : LeaderboardSortType.Desc;

        internal static LeaderboardRotation ParseRotation(string v) => v switch
        {
            "daily"   => LeaderboardRotation.Daily,
            "weekly"  => LeaderboardRotation.Weekly,
            "monthly" => LeaderboardRotation.Monthly,
            "custom"  => LeaderboardRotation.Custom,
            _         => LeaderboardRotation.None
        };
    }

    /// <summary>순위 목록의 한 줄.</summary>
    public sealed class LeaderboardEntry
    {
        /// <summary>순위(1부터). 동점이면 그 점수를 먼저 기록한 쪽이 위입니다.</summary>
        [JsonProperty("rank")]
        public int Rank { get; set; }

        [JsonProperty("account_id")]
        public string AccountId { get; set; }

        [JsonProperty("user_id")]
        public string UserId { get; set; }

        /// <summary>현재 닉네임. 등록된 닉네임이 없으면 null.</summary>
        [JsonProperty("display_name")]
        public string DisplayName { get; set; }

        [JsonProperty("score")]
        public double Score { get; set; }

        [JsonProperty("rotation_count")]
        public int RotationCount { get; set; }

        /// <summary>이 리더보드에 등록된 플레이어 데이터 컬럼 값(컬럼명 → 값).</summary>
        [JsonProperty("data")]
        public Dictionary<string, object> Data { get; set; } = new Dictionary<string, object>();
    }

    /// <summary>플레이어 1명의 리더보드 기록.</summary>
    public sealed class LeaderboardPlayerEntry
    {
        /// <summary>이 회차에 기록이 있는지. false면 나머지 값은 비어 있습니다.</summary>
        [JsonProperty("registered")]
        public bool Registered { get; set; }

        [JsonProperty("rank")]
        public int Rank { get; set; }

        [JsonProperty("account_id")]
        public string AccountId { get; set; }

        [JsonProperty("user_id")]
        public string UserId { get; set; }

        [JsonProperty("display_name")]
        public string DisplayName { get; set; }

        [JsonProperty("score")]
        public double Score { get; set; }

        [JsonProperty("rotation_count")]
        public int RotationCount { get; set; }

        [JsonProperty("data")]
        public Dictionary<string, object> Data { get; set; } = new Dictionary<string, object>();
    }

    /// <summary>점수 기록 결과.</summary>
    public sealed class LeaderboardSubmitResult
    {
        /// <summary>기록 방식이 적용된 뒤의 최종 점수.</summary>
        [JsonProperty("score")]
        public double Score { get; set; }

        [JsonProperty("rotation_count")]
        public int RotationCount { get; set; }
    }
}
