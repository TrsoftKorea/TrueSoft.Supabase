using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TrueBase.Core.Common;
using TrueBase.Core.Http;

namespace TrueBase.Core.Data
{
    /// <summary>
    /// Supabase remote_config 테이블을 통해 원격 설정을 조회합니다.
    /// 스키마: <c>Samples~/DatabaseSetup/SQL/player/08_remote_config.sql</c> (key, value_json, updated_at, version, 메타데이터 컬럼).
    /// 설계: 1키 = 1설정묶음(JSON) = 1폴링주기 (category 없음)
    /// </summary>
    public sealed class SupabaseRemoteConfigService
    {
        private const string SelectColumns =
            "key,value_json,updated_at,version,enabled,description,requires_auth";

        private readonly string _supabaseUrl;
        private readonly string _publishableKey;
        private readonly string _remoteConfigTable;
        private readonly ISupabaseHttpClient _httpClient;

        public SupabaseRemoteConfigService(
            string supabaseUrl,
            string publishableKey,
            ISupabaseHttpClient httpClient,
            string remoteConfigTable = "remote_config")
        {
            _supabaseUrl = supabaseUrl?.TrimEnd('/') ?? throw new ArgumentNullException(nameof(supabaseUrl));
            _publishableKey = publishableKey ?? throw new ArgumentNullException(nameof(publishableKey));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _remoteConfigTable = SupabaseRestTableRef.Normalize(remoteConfigTable, nameof(remoteConfigTable));
        }

        /// <summary>특정 키 목록만 조회합니다(Cold Start 시 첫 조회용).</summary>
        /// <param name="keys">조회할 Remote Config 키 목록. null·빈 목록이면 요청 없이 빈 배열을 반환합니다.</param>
        /// <param name="accessToken">로그인 세션의 액세스 토큰. null이면 공개(Publishable 키) 액세스 — <c>requires_auth</c> 행은 RLS로 제외됩니다(기본값: null).</param>
        /// <returns>요청한 키에 해당하는 Remote Config 행 배열. 미존재 키는 제외됩니다.</returns>
        public async Task<SupabaseResult<RemoteConfigRow[]>> GetByKeysAsync(
            IReadOnlyList<string> keys,
            string accessToken = null)
        {
            if (keys == null || keys.Count == 0)
                return SupabaseResult<RemoteConfigRow[]>.Success(Array.Empty<RemoteConfigRow>());

            var url = BuildBaseUrl() + AppendInFilter("key", keys);
            var response = await _httpClient.SendAsync("GET", url, null, CreateHeaders(accessToken));
            return ParseRows(response);
        }

        private string BuildBaseUrl()
        {
            return $"{SupabaseRestTableRef.BuildTableUrl(_supabaseUrl, _remoteConfigTable)}?select={SelectColumns}";
        }

        private static string AppendInFilter(string column, IReadOnlyList<string> values)
        {
            var sb = new StringBuilder();
            var first = true;
            foreach (var v in values)
            {
                if (string.IsNullOrWhiteSpace(v))
                    continue;
                if (first)
                {
                    sb.Append('&').Append(column).Append("=in.(");
                    first = false;
                }
                else
                    sb.Append(',');

                sb.Append(Uri.EscapeDataString(v.Trim()));
            }

            if (first)
                return string.Empty;

            sb.Append(')');
            return sb.ToString();
        }

        private SupabaseResult<RemoteConfigRow[]> ParseRows(SupabaseHttpResponse response)
        {
            if (response == null)
                return SupabaseResult<RemoteConfigRow[]>.Fail(SupabaseErrorCode.NetworkError);

            if (response.IsSuccess == false)
                return SupabaseResult<RemoteConfigRow[]>.Fail(response.ErrorMessage ?? response.Body ?? "remote_config_fetch_failed");

            try
            {
                var rows = DeserializeRemoteConfigRows(response.Body);
                return SupabaseResult<RemoteConfigRow[]>.Success(rows);
            }
            catch (Exception e)
            {
                return SupabaseResult<RemoteConfigRow[]>.Fail("remote_config_parse_failed:" + e.Message);
            }
        }

        /// <summary>
        /// PostgREST는 <c>value_json</c>이 DB에서 <c>text</c>이면 JSON 문자열, <c>jsonb</c>이면 객체 리터럴로 보냅니다.
        /// 두 형태 모두 문자열로 정규화하기 위해 JToken 기반으로 직접 파싱합니다.
        /// </summary>
        private static RemoteConfigRow[] DeserializeRemoteConfigRows(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return Array.Empty<RemoteConfigRow>();

            var arr = JArray.Parse(body);
            var list = new List<RemoteConfigRow>(arr.Count);
            foreach (var token in arr)
            {
                if (token is not JObject o)
                    continue;

                var row = new RemoteConfigRow
                {
                    key = o["key"]?.Type == JTokenType.String ? o["key"]!.Value<string>() : o["key"]?.ToString(),
                    value_json = ExtractValueJsonString(o["value_json"]),
                    updated_at = o["updated_at"]?.Type == JTokenType.String
                        ? o["updated_at"]!.Value<string>()
                        : o["updated_at"]?.ToString(),
                    version = o["version"]?.Value<int?>() ?? 0,
                    enabled = o["enabled"]?.Value<bool?>() ?? true,
                    description = o["description"]?.Value<string>(),
                    requires_auth = o["requires_auth"]?.Value<bool?>() ?? false,
                };
                list.Add(row);
            }

            return list.ToArray();
        }

        private static string ExtractValueJsonString(JToken vj)
        {
            if (vj == null || vj.Type == JTokenType.Null)
                return string.Empty;

            string raw;
            if (vj.Type == JTokenType.String)
                raw = vj.Value<string>() ?? string.Empty;
            else
                raw = vj.ToString(Formatting.None);

            return StripMetaFromJson(raw);
        }

        /// <summary>
        /// <c>value_json</c>에서 <c>__meta</c> 키를 제거합니다.
        /// <c>__meta</c>는 Retool에서 각 항목의 C# 타입 힌트를 저장하는 데 사용되며,
        /// 게임 런타임 역직렬화에는 포함되지 않아야 합니다.
        /// </summary>
        private static string StripMetaFromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return json;

            try
            {
                var obj = JObject.Parse(json);
                if (obj.ContainsKey("__meta"))
                {
                    obj.Remove("__meta");
                    return obj.ToString(Formatting.None);
                }

                return json;
            }
            catch
            {
                return json;
            }
        }

        private Dictionary<string, string> CreateHeaders(string accessToken) =>
            SupabaseCoreHelper.BuildApiHeaders(_publishableKey, accessToken);

        [Serializable]
        public sealed class RemoteConfigRow
        {
            public string key;
            /// <summary>설정 JSON 본문. API는 <c>text</c> 컬럼이면 문자열, <c>jsonb</c> 컬럼이면 중첩 객체로 줄 수 있음(클라이언트는 문자열로 정규화).</summary>
            public string value_json;
            public string updated_at;
            public int version;
            public bool enabled;
            public string description;
            public bool requires_auth;
        }
    }
}
