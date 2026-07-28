using System;
using System.Collections.Generic;
using System.Reflection;

namespace TrueBase.Core.Data
{
    /// <summary>
    /// 이 클래스가 어느 리더보드를 가리키는지 지정합니다. 클래스 생성기가 붙입니다.
    /// <para>
    /// 리더보드 API는 코드 문자열 대신 이 속성이 붙은 타입으로 대상을 지정합니다:
    /// <c>Supabase.SubmitScoreAsync&lt;ArenaLeaderboard&gt;(1250)</c>
    /// </para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class LeaderboardAttribute : Attribute
    {
        /// <param name="code">Retool 리더보드 페이지에서 정한 코드(예: <c>arena</c>).</param>
        public LeaderboardAttribute(string code) => Code = code;

        /// <summary>리더보드 코드.</summary>
        public string Code { get; }
    }

    /// <summary>
    /// 리더보드 타입 마커. 멤버가 없으며, 제네릭 제약으로 아무 클래스나 넘어오는 것을 막습니다.
    /// 생성 클래스가 <see cref="LeaderboardAttribute"/>와 함께 구현합니다.
    /// </summary>
    public interface ILeaderboard { }

    /// <summary>타입에서 리더보드 코드를 찾아 캐시합니다.</summary>
    internal static class LeaderboardMeta
    {
        private static readonly Dictionary<Type, string> _codeCache = new Dictionary<Type, string>();

        /// <summary>리더보드 타입의 코드. <see cref="LeaderboardAttribute"/>가 없으면 예외.</summary>
        public static string CodeOf(Type leaderboardType)
        {
            if (leaderboardType == null)
                throw new ArgumentNullException(nameof(leaderboardType));

            lock (_codeCache)
            {
                if (_codeCache.TryGetValue(leaderboardType, out var cached))
                    return cached;
            }

            var code = leaderboardType.GetCustomAttribute<LeaderboardAttribute>(false)?.Code;
            if (string.IsNullOrWhiteSpace(code))
                throw new InvalidOperationException(
                    $"{leaderboardType.Name}에 [Leaderboard(\"코드\")]가 없습니다. " +
                    "TrueSoft > Supabase > 클래스 생성 > 리더보드 로 클래스를 생성하세요.");

            code = code.Trim();
            lock (_codeCache) { _codeCache[leaderboardType] = code; }
            return code;
        }

        /// <summary>
        /// 행 타입이 속한 리더보드의 코드. 행 자신의 속성을 먼저 보고, 없으면 감싸는 타입에서 찾습니다
        /// (생성 클래스는 <c>XxxLeaderboard.Row</c> 형태로 중첩됩니다).
        /// </summary>
        public static string CodeOfRow(Type rowType)
        {
            if (rowType == null)
                throw new ArgumentNullException(nameof(rowType));

            lock (_codeCache)
            {
                if (_codeCache.TryGetValue(rowType, out var cached))
                    return cached;
            }

            var code = rowType.GetCustomAttribute<LeaderboardAttribute>(false)?.Code
                       ?? rowType.DeclaringType?.GetCustomAttribute<LeaderboardAttribute>(false)?.Code;

            if (string.IsNullOrWhiteSpace(code))
                throw new InvalidOperationException(
                    $"{rowType.Name}이 어느 리더보드의 행인지 알 수 없습니다. " +
                    "생성 클래스의 중첩 Row(예: ArenaLeaderboard.Row)를 넘기거나, " +
                    "행 타입에 [Leaderboard(\"코드\")]를 붙이세요.");

            code = code.Trim();
            lock (_codeCache) { _codeCache[rowType] = code; }
            return code;
        }
    }
}
