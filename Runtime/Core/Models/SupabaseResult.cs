using TrueBase.Core.Models;

namespace TrueBase.Core.Common
{
    public sealed class SupabaseResult<T>
    {
        public bool IsSuccess { get; }
        public string ErrorMessage { get; }
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

        public static SupabaseResult<T> Success(T data)
        {
            return new SupabaseResult<T>(true, data, null);
        }

        public static SupabaseResult<T> Fail(string errorMessage, SupabaseBanInfo banInfo = null)
        {
            return new SupabaseResult<T>(false, default, errorMessage, banInfo);
        }
    }
}