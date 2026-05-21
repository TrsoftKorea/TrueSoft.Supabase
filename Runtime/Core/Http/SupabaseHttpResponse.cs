namespace TrueBase.Core.Http
{
    public sealed class SupabaseHttpResponse
    {
        public bool IsSuccess { get; }
        public long StatusCode { get; }
        public string Body { get; }
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