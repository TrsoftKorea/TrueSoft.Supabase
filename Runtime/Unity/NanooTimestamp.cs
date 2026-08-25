using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace TrueBase.Unity
{
    /// <summary>
    /// 나누(PlayNANOO) 동기화 비교 필드 값을 <see cref="DateTimeOffset"/>으로 파싱합니다.
    /// <see cref="StaticUserSave{TRow}"/>(SDK 쪽 필드)와 PlayNanooRuntimeBase(나누 JSON 쪽)가 함께 씁니다.
    /// </summary>
    public static class NanooTimestamp
    {
        private static readonly Regex HasOffsetSuffix = new Regex(@"(Z|[+-]\d{2}:?\d{2})$", RegexOptions.Compiled);

        /// <summary>
        /// 문자열에 시간대 정보(<c>Z</c>·오프셋)가 있으면 그대로 파싱합니다.
        /// 없으면 시각·날짜만 읽어 <paramref name="fallbackUtcOffsetHours"/>를 오프셋으로 적용합니다
        /// — 기기 시간대에 휘둘리지 않도록, 값 자체에 오프셋이 없을 때만 이 기본값을 씁니다.
        /// 파싱에 실패하면 <see cref="DateTimeOffset.MinValue"/>.
        /// </summary>
        public static DateTimeOffset Parse(string raw, double fallbackUtcOffsetHours)
        {
            if (string.IsNullOrEmpty(raw)) return DateTimeOffset.MinValue;

            var trimmed = raw.Trim();
            if (HasOffsetSuffix.IsMatch(trimmed) &&
                DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out var withOffset))
                return withOffset;

            if (DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out var naive))
                return new DateTimeOffset(DateTime.SpecifyKind(naive, DateTimeKind.Unspecified), TimeSpan.FromHours(fallbackUtcOffsetHours));

            return DateTimeOffset.MinValue;
        }
    }
}
