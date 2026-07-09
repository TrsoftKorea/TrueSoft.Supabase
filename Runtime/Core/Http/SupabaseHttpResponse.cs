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

        public static SupabaseHttpResponse Fail(long statusCode, string body, string errorMessage)
        {
            return new SupabaseHttpResponse(false, statusCode, body, errorMessage);
        }
    }
}