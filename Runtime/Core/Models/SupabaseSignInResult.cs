using System;
using TrueBase.Core.Data;
using TrueBase.Core.Models;

namespace TrueBase.Core.Common
{
    /// <summary>
    /// 로그인 결과. <see cref="SupabaseResult"/>를 상속하므로 성공 분기(암묵적 <c>bool</c>·<see cref="SupabaseResult.IsSuccess"/>)는
    /// 동일하고, 추가로 로그인 시 조회된 내 프로필을 <see cref="Profile"/>로 제공한다.
    /// 탈퇴 예약으로 로그인이 막힌 경우(<see cref="SupabaseFailCode.WithdrawalGateBlocked"/>)에는
    /// 삭제 예정 시각(<see cref="WithdrawnAt"/>)과 비로그인 취소 토큰(<see cref="WithdrawalCancelToken"/>)이 채워진다.
    /// <para>
    /// <c>var r = await Supabase.SignInAnonymouslyAsync(); if (r.IsSuccess) { var name = r.Profile.Name; }</c> 형태로 사용한다.
    /// </para>
    /// </summary>
    public sealed class SupabaseSignInResult : SupabaseResult
    {
        /// <summary>로그인 시 조회된 내 프로필. 실패 시에는 빈 프로필(<see cref="PublicProfile.Empty"/>)이라 null이 아니며, 안전하게 접근할 수 있다.</summary>
        public PublicProfile Profile { get; }

        /// <summary>
        /// 탈퇴 예약으로 로그인이 막힌 경우(<see cref="SupabaseFailCode.WithdrawalGateBlocked"/>)의 삭제 예정 시각. 그 외에는 null.
        /// 남은 시간은 <c>Supabase.GetServerNowAsync()</c>의 서버 시각과 비교해 계산한다.
        /// </summary>
        public DateTimeOffset? WithdrawnAt { get; }

        /// <summary>탈퇴 예약으로 로그인이 막힌 경우, 비로그인 상태에서 예약을 취소할 때 쓰는 토큰. 그 외에는 null.</summary>
        public string WithdrawalCancelToken { get; }

        private SupabaseSignInResult(
            bool isSuccess,
            PublicProfile profile,
            DateTimeOffset? withdrawnAt,
            string cancelToken,
            string errorCode,
            SupabaseBanInfo banInfo)
            : base(isSuccess, errorCode, banInfo)
        {
            Profile = profile;
            WithdrawnAt = withdrawnAt;
            WithdrawalCancelToken = cancelToken;
        }

        /// <summary>성공 결과를 만듭니다.</summary>
        /// <param name="profile">로그인 시 조회된 내 프로필.</param>
        public static SupabaseSignInResult Success(PublicProfile profile)
            => new SupabaseSignInResult(true, profile, null, null, null, null);

        /// <summary>실패 결과를 만듭니다.</summary>
        /// <param name="errorCode">실패 사유 원문 문자열.</param>
        /// <param name="banInfo">차단(<c>user_banned</c>) 실패인 경우의 차단 정보.</param>
        /// <param name="withdrawnAt">탈퇴 게이트 차단 시 삭제 예정 시각. 그 외에는 null.</param>
        /// <param name="cancelToken">탈퇴 게이트 차단 시 비로그인 취소 토큰. 그 외에는 null.</param>
        public static new SupabaseSignInResult Fail(
            string errorCode,
            SupabaseBanInfo banInfo = null,
            DateTimeOffset? withdrawnAt = null,
            string cancelToken = null)
            => new SupabaseSignInResult(false, PublicProfile.Empty, withdrawnAt, cancelToken, errorCode, banInfo);

        /// <summary><see cref="SupabaseResult.IsSuccess"/>로의 암묵적 변환.</summary>
        public static implicit operator bool(SupabaseSignInResult r) => r != null && r.IsSuccess;
    }
}
