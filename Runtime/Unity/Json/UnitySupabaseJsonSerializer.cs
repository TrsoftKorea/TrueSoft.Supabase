using System;
using Newtonsoft.Json;

namespace TrueBase.Unity
{
    /// <summary>
    /// <see cref="ISupabaseJsonSerializer"/>의 Newtonsoft.Json 구현입니다.
    /// 역직렬화 입력이 null/공백이면 예외 대신 <c>default</c>(배열은 빈 배열)를 반환합니다.
    /// </summary>
    internal sealed class UnitySupabaseJsonSerializer : ISupabaseJsonSerializer
    {
        public string ToJson<T>(T value)
        {
            return JsonConvert.SerializeObject(value);
        }

        public T FromJson<T>(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return default;

            return JsonConvert.DeserializeObject<T>(json);
        }

        public T[] FromJsonArray<T>(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return Array.Empty<T>();

            return JsonConvert.DeserializeObject<T[]>(json) ?? Array.Empty<T>();
        }
    }
}
