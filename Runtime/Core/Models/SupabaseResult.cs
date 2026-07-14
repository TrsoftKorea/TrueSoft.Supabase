using TrueBase.Core.Models;

namespace TrueBase.Core.Common
{
    /// <summary>
    /// SDK 호출 결과(데이터 없는 액션). 데이터를 돌려주는 호출은 <see cref="SupabaseResult{T}"/>를 쓴다.
    /// <c>implicit operator bool</c>을 제공하므로 <c>if (await Supabase.Xxx())</c> 형태로 바로 분기할 수 있다.
    /// </summary>
    public class SupabaseResult
    {
        /// <summary>성공 여부.</summary>
        public bool IsSuccess { get; }

        /// <summary>
        /// 실패 사유의 enum 코드(성공 시 <see cref="SupabaseFailCode.None"/>).
        /// <c>if (result.Reason == SupabaseFailCode.UserBanned)</c>처럼 타입 안전하게 분기한다.
        /// 카탈로그에 없는 동적 사유는 <see cref="SupabaseFailCode.Unknown"/>이며, 원문은 <see cref="ErrorCode"/>에서 확인한다.
        /// </summary>
        public SupabaseFailCode Reason => SupabaseFailCodeMap.FromErrorCode(ErrorCode);

        /// <summary>
        /// 실패 사유 원문 문자열(성공 시 null). 동적 사유(예외 메시지·서버 RPC reason 등)를 포함한 전체 문자열이다.
        /// 타입 안전한 분기는 <see cref="Reason"/>을, 원인 표시·로깅은 이 문자열을 사용한다.
        /// </summary>
        public string ErrorCode { get; }

        /// <summary>차단(<c>user_banned</c>) 실패 시 차단 정보. 그 외에는 null.</summary>
        public SupabaseBanInfo BanInfo { get; }

        protected SupabaseResult(bool isSuccess, string errorCode, SupabaseBanInfo banInfo)
        {
            IsSuccess = isSuccess;
            ErrorCode = errorCode;
            BanInfo = banInfo;
        }

        /// <summary>성공 결과 싱글턴.</summary>
        public static readonly SupabaseResult Ok = new SupabaseResult(true, null, null);

        /// <summary>실패 결과를 만듭니다.</summary>
        public static SupabaseResult Fail(string errorCode, SupabaseBanInfo banInfo = null)
            => new SupabaseResult(false, errorCode, banInfo);

        /// <summary><see cref="IsSuccess"/>로의 암묵적 변환.</summary>
        public static implicit operator bool(SupabaseResult r) => r != null && r.IsSuccess;
    }

    /// <summary>
    /// 데이터를 돌려주는 SDK 호출 결과. <see cref="SupabaseResult.IsSuccess"/> 확인 후 <see cref="Data"/>를 읽는다.
    /// </summary>
    public sealed class SupabaseResult<T> : SupabaseResult
    {
        /// <summary>성공 시 결과 값. 실패면 <c>default</c>.</summary>
        public T Data { get; }

        private SupabaseResult(bool isSuccess, T data, string errorCode, SupabaseBanInfo banInfo)
            : base(isSuccess, errorCode, banInfo)
        {
            Data = data;
        }

        /// <summary>성공 결과를 만듭니다.</summary>
        public static SupabaseResult<T> Success(T data)
            => new SupabaseResult<T>(true, data, null, null);

        /// <summary>실패 결과를 만듭니다.</summary>
        /// <param name="errorCode">실패 사유 원문 문자열.</param>
        /// <param name="banInfo">차단(<c>user_banned</c>) 실패인 경우의 차단 정보.</param>
        public static new SupabaseResult<T> Fail(string errorCode, SupabaseBanInfo banInfo = null)
            => new SupabaseResult<T>(false, default, errorCode, banInfo);

        /// <summary><see cref="SupabaseResult.IsSuccess"/>로의 암묵적 변환.</summary>
        public static implicit operator bool(SupabaseResult<T> r) => r != null && r.IsSuccess;
    }
}
