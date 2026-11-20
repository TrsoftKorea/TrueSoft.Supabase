using TrueBase.Core.Common;

namespace TrueBase.Core.Http
{
    /// <summary><see cref="ISupabaseHttpClient"/>가 반환하는 HTTP 응답. 전송 실패도 예외 대신 실패 인스턴스로 표현합니다.</summary>
    public sealed class SupabaseHttpResponse
    {
        /// <summary>HTTP 상태가 2xx이면 true.</summary>
        public bool IsSuccess { get; }

        /// <summary>HTTP 상태 코드. 네트워크 단계에서 실패하면 0일 수 있습니다.</summary>
        public long StatusCode { get; }

        /// <summary>응답 본문. 없으면 null.</summary>
        public string Body { get; }

        /// <summary>실패 사유 메시지. 성공이면 null.</summary>
        public string ErrorMessage { get; }

        public SupabaseHttpResponse(bool isSuccess, long statusCode, string body, string errorMessage)
        {
            IsSuccess = isSuccess;
            StatusCode = statusCode;
            Body = body;
            ErrorMessage = errorMessage;
        }

        public static SupabaseHttpResponse Success(long statusCode, string body)
        {
            return new SupabaseHttpResponse(true, statusCode, body, null);
        }

        /// <summary>서버 요청 제한에 걸린 응답이면 true. 제한은 분 단위라 곧바로 다시 보내도 소용없다.</summary>
        public bool IsRequestLimited =>
            !IsSuccess && StatusCode == 429 && Body != null && Body.Contains(SupabaseErrorCode.RequestLimited);

        /// <summary>
        /// 다른 기기에서 같은 계정으로 로그인해 서버가 거절한 응답이면 true. 요청 앞 검사(ts_request_limit_check)는 409,
        /// 세이브 저장 트리거(ts_user_data_session_guard)는 400 으로 돌려준다.
        /// </summary>
        public bool IsDuplicateLogin =>
            !IsSuccess && (StatusCode == 409 || StatusCode == 400) && Body != null && Body.Contains(SupabaseErrorCode.DuplicateLogin);

        public static SupabaseHttpResponse Fail(long statusCode, string body, string errorMessage)
        {
            var response = new SupabaseHttpResponse(false, statusCode, body, errorMessage);
            // 플랫폼이 채운 문구("Cannot resolve destination host"·"HTTP/1.1 401 Unauthorized" 꼴)는 사유 카탈로그에 없어
            // Reason 이 Unknown 이 된다. 통신 단계에서 이미 갈린 실패는 여기서 사유 코드로 바꿔 둔다 —
            // 응답을 못 받음(상태 0) = 네트워크, 401 = 토큰 거부, 429 + request_limited = 요청 제한, 다른 기기 로그인 = 중복 로그인.
            if (response.IsRequestLimited)
                return new SupabaseHttpResponse(false, statusCode, body, SupabaseErrorCode.RequestLimited);
            if (response.IsDuplicateLogin)
                return new SupabaseHttpResponse(false, statusCode, body, SupabaseErrorCode.DuplicateLogin);
            if (statusCode == 0)
                return new SupabaseHttpResponse(false, statusCode, body, SupabaseErrorCode.NetworkError);
            if (statusCode == 401)
                return new SupabaseHttpResponse(false, statusCode, body, SupabaseErrorCode.NotAuthenticated);
            return response;
        }
    }
}