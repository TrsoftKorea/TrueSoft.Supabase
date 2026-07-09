using TrueBase.Core.Models;

namespace TrueBase.Core.Common
{
    /// <summary>
    /// SDK 데이터 API의 성공/실패 결과. <see cref="IsSuccess"/> 확인 후 <see cref="Data"/> 또는 <see cref="ErrorMessage"/>를 읽습니다.
    /// </summary>
    public sealed class SupabaseResult<T>
    {
        /// <summary>호출이 성공했으면 true.</summary>
        public bool IsSuccess { get; }

        /// <summary>실패 사유 문자열. 성공이면 null.</summary>
        public string ErrorMessage { get; }

        /// <summary>성공 시 결과 값. 실패면 <c>default</c>.</summary>
        public T Data { get; }

        /// <summary>
        /// 로그인이 차단(<c>user_banned</c>)된 경우 차단 정보. 차단이 아니면 <see langword="null"/>.
        /// </summary>
        public SupabaseBanInfo BanInfo { get; }

        private SupabaseResult(bool isSuccess, T data, string errorMessage, SupabaseBanInfo banInfo = null)
        {
            IsSuccess = isSuccess;
            Data = data;
            ErrorMessage = errorMessage;
            BanInfo = banInfo;
        }

        /// <summary>성공 결과를 만듭니다.</summary>
        public static SupabaseResult<T> Success(T data)
        {
            return new SupabaseResult<T>(true, data, null);
        }

        /// <summary>실패 결과를 만듭니다.</summary>
        /// <param name="errorMessage">실패 사유.</param>
        /// <param name="banInfo">차단(<c>user_banned</c>) 실패인 경우의 차단 정보(기본값: null).</param>
        public static SupabaseResult<T> Fail(string errorMessage, SupabaseBanInfo banInfo = null)
        {
            return new SupabaseResult<T>(false, default, errorMessage, banInfo);
        }
    }
}