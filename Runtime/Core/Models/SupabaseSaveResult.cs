using TrueBase.Core.Models;

namespace TrueBase.Core.Common
{
    /// <summary>
    /// 즉시 저장 결과. 변경 없음은 규칙대로 실패로 오므로, 저장 뒤 다음 단계로 넘어갈지는 <see cref="IsSaved"/>로 가른다.
    /// </summary>
    public sealed class SupabaseSaveResult : SupabaseResult
    {
        /// <summary>서버에 보낼 변경이 남지 않았는지. 전송에 성공했거나 보낼 변경이 없었으면 true.</summary>
        public bool IsSaved => IsSuccess || Reason == SupabaseReason.UserSaveNoChanges;

        private SupabaseSaveResult(bool isSuccess, string errorCode, SupabaseBanInfo banInfo)
            : base(isSuccess, errorCode, banInfo)
        {
        }

        /// <summary>전송 결과를 저장 결과로 옮깁니다.</summary>
        public static SupabaseSaveResult From(SupabaseResult result)
            => new SupabaseSaveResult(result.IsSuccess, result.ErrorCode, result.BanInfo);

        /// <summary>실패 결과를 만듭니다.</summary>
        /// <param name="errorCode">실패 사유 원문 문자열.</param>
        /// <param name="banInfo">차단(<c>user_banned</c>) 실패인 경우의 차단 정보.</param>
        public static new SupabaseSaveResult Fail(string errorCode, SupabaseBanInfo banInfo = null)
            => new SupabaseSaveResult(false, errorCode, banInfo);

        /// <summary><see cref="SupabaseResult.IsSuccess"/>로의 암묵적 변환.</summary>
        public static implicit operator bool(SupabaseSaveResult r) => r != null && r.IsSuccess;
    }
}
