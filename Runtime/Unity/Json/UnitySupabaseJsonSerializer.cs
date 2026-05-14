using System;
using Newtonsoft.Json;

namespace Truesoft.Supabase.Unity
{
    public sealed class UnitySupabaseJsonSerializer : ISupabaseJsonSerializer
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
