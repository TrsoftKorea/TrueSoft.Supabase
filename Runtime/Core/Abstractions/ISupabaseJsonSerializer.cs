namespace TrueBase
{
    public interface ISupabaseJsonSerializer
    {
        string ToJson<T>(T value);
        T FromJson<T>(string json);
        T[] FromJsonArray<T>(string json);
    }
}