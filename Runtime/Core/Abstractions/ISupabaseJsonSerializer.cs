namespace TrueBase
{
    /// <summary>
    /// Core 서비스가 사용하는 JSON 직렬화 추상화. Unity 레이어의 <c>UnitySupabaseJsonSerializer</c>(Newtonsoft.Json)가 구현합니다.
    /// </summary>
    public interface ISupabaseJsonSerializer
    {
        /// <summary>객체를 JSON 문자열로 직렬화합니다.</summary>
        string ToJson<T>(T value);

        /// <summary>객체 루트(<c>{...}</c>) JSON을 역직렬화합니다.</summary>
        T FromJson<T>(string json);

        /// <summary>배열 루트(<c>[...]</c>) JSON을 역직렬화합니다.</summary>
        T[] FromJsonArray<T>(string json);
    }
}