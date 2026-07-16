using TrueBase.Core.Data;
using TrueBase.Core.Models;

namespace TrueBase.Core.Common
{
    /// <summary>
    /// 로그인 결과. <see cref="SupabaseResult"/>를 상속하므로 성공 분기(암묵적 <c>bool</c>·<see cref="SupabaseResult.IsSuccess"/>)는
    /// 동일하고, 추가로 로그인 시 조회된 내 프로필(닉네임·서버 코드·탈퇴 상태 등)을 <see cref="Profile"/>로 제공한다.
    /// <para>
    /// <c>var r = await Supabase.SignInAnonymouslyAsync(); if (r.IsSuccess) { var name = r.Profile.DisplayName; }</c> 형태로 사용한다.
    /// </para>
    /// </summary>
    public sealed class SupabaseSignInResult : SupabaseResult
    {
        /// <summary>로그인 시 조회된 내 프로필. 실패 시 null.</summary>
        public PublicProfile Profile { get; }

        private SupabaseSignInResult(bool isSuccess, PublicProfile profile, string errorCode, SupabaseBanInfo banInfo)
            : base(isSuccess, errorCode, banInfo)
        {
            Profile = profile;
        }

        /// <summary>성공 결과를 만듭니다.</summary>
        /// <param name="profile">로그인 시 조회된 내 프로필.</param>
        public static SupabaseSignInResult Success(PublicProfile profile)
            => new SupabaseSignInResult(true, profile, null, null);

        /// <summary>실패 결과를 만듭니다.</summary>
        /// <param name="errorCode">실패 사유 원문 문자열.</param>
        /// <param name="banInfo">차단(<c>user_banned</c>) 실패인 경우의 차단 정보.</param>
        public static new SupabaseSignInResult Fail(string errorCode, SupabaseBanInfo banInfo = null)
            => new SupabaseSignInResult(false, null, errorCode, banInfo);

        /// <summary><see cref="SupabaseResult.IsSuccess"/>로의 암묵적 변환.</summary>
        public static implicit operator bool(SupabaseSignInResult r) => r != null && r.IsSuccess;
    }
}
