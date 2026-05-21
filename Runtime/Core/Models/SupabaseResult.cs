namespace TrueBase.Core.Common
{
    public sealed class SupabaseResult<T>
    {
        public bool IsSuccess { get; }
        public string ErrorMessage { get; }
        public T Data { get; }

        private SupabaseResult(bool isSuccess, T data, string errorMessage)
        {
            IsSuccess = isSuccess;
            Data = data;
            ErrorMessage = errorMessage;
        }

        public static SupabaseResult<T> Success(T data)
        {
            return new SupabaseResult<T>(true, data, null);
        }

        public static SupabaseResult<T> Fail(string errorMessage)
        {
            return new SupabaseResult<T>(false, default, errorMessage);
        }
    }
}