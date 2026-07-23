using TrueBase.Core.Models;

namespace TrueBase.Core.Common
{
    /// <summary>
    /// 유저 세이브 로드 결과. <see cref="SupabaseResult"/>를 상속하므로 성공 분기(암묵적 <c>bool</c>·<see cref="SupabaseResult.IsSuccess"/>)는
    /// 동일하고, 추가로 이 로드가 <b>신규 유저</b>(DB에 본인 행이 없던 최초 로드)였는지를 <see cref="IsNewUser"/>로 제공한다.
    /// <para>
    /// <c>var r = await Supabase.LoadUserSaveAsync(); if (r.IsNewUser) { ... }</c> 형태로 사용한다.
    /// 값이 호출에 묶여 불변이므로, 이후 재로드해도 이 결과의 <see cref="IsNewUser"/>는 바뀌지 않는다.
    /// </para>
    /// </summary>
    public sealed class SupabaseLoadResult : SupabaseResult
    {
        /// <summary>이 로드가 DB에 본인 행이 없던 <b>신규 유저</b>였는지. 실패 시 false.</summary>
        public bool IsNewUser { get; }

        private SupabaseLoadResult(bool isSuccess, bool isNewUser, string errorCode, SupabaseBanInfo banInfo)
            : base(isSuccess, errorCode, banInfo)
        {
            IsNewUser = isNewUser;
        }

        /// <summary>성공 결과를 만듭니다.</summary>
        /// <param name="isNewUser">이 로드가 신규 유저(DB 행 없던 최초 로드)였는지.</param>
        public static SupabaseLoadResult Success(bool isNewUser)
            => new SupabaseLoadResult(true, isNewUser, null, null);

        /// <summary>실패 결과를 만듭니다.</summary>
        /// <param name="errorCode">실패 사유 원문 문자열.</param>
        /// <param name="banInfo">차단(<c>user_banned</c>) 실패인 경우의 차단 정보.</param>
        public static new SupabaseLoadResult Fail(string errorCode, SupabaseBanInfo banInfo = null)
            => new SupabaseLoadResult(false, false, errorCode, banInfo);

        /// <summary><see cref="SupabaseResult.IsSuccess"/>로의 암묵적 변환.</summary>
        public static implicit operator bool(SupabaseLoadResult r) => r != null && r.IsSuccess;
    }
}
