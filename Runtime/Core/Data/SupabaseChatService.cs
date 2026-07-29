using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TrueBase.Core.Common;
using TrueBase.Core.Http;

namespace TrueBase.Core.Data
{
    /// <summary>
    /// 채팅 RPC. 채널 목록 <c>ts_chat_channels</c>, 발송 <c>ts_chat_send</c>,
    /// 조회 <c>ts_chat_fetch_many</c>.
    /// 채널 생성·삭제와 메시지 숨김·차단은 운영(service_role) 전용이라 여기에 없습니다.
    /// </summary>
    public sealed class SupabaseChatService
    {
        private readonly string _supabaseUrl;
        private readonly string _publishableKey;
        private readonly ISupabaseHttpClient _httpClient;

        public SupabaseChatService(
            string supabaseUrl,
            string publishableKey,
            ISupabaseHttpClient httpClient)
        {
            _supabaseUrl = (supabaseUrl ?? string.Empty).TrimEnd('/');
            _publishableKey = publishableKey ?? string.Empty;
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        /// <summary>사용 가능한 채널 목록. 비활성 채널은 빠집니다.</summary>
        public async Task<SupabaseResult<IReadOnlyList<ChatChannelInfo>>> GetChannelsAsync(string accessToken)
        {
            var r = await CallRpcAsync(accessToken, "ts_chat_channels", "{}");
            if (!r.IsSuccess)
                return SupabaseResult<IReadOnlyList<ChatChannelInfo>>.Fail(r.ErrorCode);

            try
            {
                var list = JsonConvert.DeserializeObject<List<ChatChannelInfo>>(r.Data)
                           ?? new List<ChatChannelInfo>();
                return SupabaseResult<IReadOnlyList<ChatChannelInfo>>.Success(list);
            }
            catch (Exception e)
            {
                return SupabaseResult<IReadOnlyList<ChatChannelInfo>>.Fail("chat_channels_parse:" + e.Message);
            }
        }

        /// <summary>메시지를 보냅니다. 길이·차단·연속 채팅 검사는 서버가 합니다.</summary>
        public async Task<SupabaseResult<ChatSendResult>> SendAsync(string accessToken, string channelCode, string content)
        {
            if (string.IsNullOrWhiteSpace(channelCode))
                return SupabaseResult<ChatSendResult>.Fail(SupabaseErrorCode.ChatChannelNotFound);

            var body = JsonConvert.SerializeObject(new
            {
                p_code = channelCode.Trim(),
                p_content = content ?? string.Empty
            });

            var r = await CallRpcAsync(accessToken, "ts_chat_send", body);
            if (!r.IsSuccess)
                return SupabaseResult<ChatSendResult>.Fail(r.ErrorCode);

            try
            {
                var res = JsonConvert.DeserializeObject<ChatSendResult>(r.Data);
                return SupabaseResult<ChatSendResult>.Success(res ?? new ChatSendResult());
            }
            catch (Exception e)
            {
                return SupabaseResult<ChatSendResult>.Fail("chat_send_parse:" + e.Message);
            }
        }

        /// <summary>
        /// 여러 채널의 커서 이후 메시지를 한 번에 가져옵니다.
        /// 채널마다 따로 호출하면 폴링 요청이 채널 수만큼 늘어나므로 항상 묶어서 부릅니다.
        /// </summary>
        /// <param name="cursors">채널 코드 → 마지막으로 받은 메시지 id. 0이면 최근 <paramref name="limit"/>개.</param>
        public async Task<SupabaseResult<IReadOnlyDictionary<string, IReadOnlyList<ChatMessage>>>> FetchManyAsync(
            string accessToken,
            IReadOnlyDictionary<string, long> cursors,
            int limit = 50)
        {
            if (cursors == null || cursors.Count == 0)
                return SupabaseResult<IReadOnlyDictionary<string, IReadOnlyList<ChatMessage>>>.Fail(
                    SupabaseErrorCode.ChatChannelsEmpty);

            var body = JsonConvert.SerializeObject(new { p_cursors = cursors, p_limit = limit });

            var r = await CallRpcAsync(accessToken, "ts_chat_fetch_many", body);
            if (!r.IsSuccess)
                return SupabaseResult<IReadOnlyDictionary<string, IReadOnlyList<ChatMessage>>>.Fail(r.ErrorCode);

            try
            {
                var raw = JsonConvert.DeserializeObject<Dictionary<string, List<ChatMessage>>>(r.Data)
                          ?? new Dictionary<string, List<ChatMessage>>();

                var map = new Dictionary<string, IReadOnlyList<ChatMessage>>(raw.Count);
                foreach (var pair in raw)
                {
                    var list = pair.Value ?? new List<ChatMessage>();
                    // 응답이 채널 코드로 키잉되어 있어 메시지 자체에는 채널이 없다. 여기서 채워 준다.
                    foreach (var m in list)
                        m.ChannelCode = pair.Key;

                    map[pair.Key] = list;
                }

                return SupabaseResult<IReadOnlyDictionary<string, IReadOnlyList<ChatMessage>>>.Success(map);
            }
            catch (Exception e)
            {
                return SupabaseResult<IReadOnlyDictionary<string, IReadOnlyList<ChatMessage>>>.Fail(
                    "chat_fetch_parse:" + e.Message);
            }
        }

        // -------------------------------------------------------------------
        // 공통 RPC 호출
        // -------------------------------------------------------------------

        private async Task<SupabaseResult<string>> CallRpcAsync(string accessToken, string rpcName, string bodyJson)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
                return SupabaseResult<string>.Fail(SupabaseErrorCode.AccessTokenEmpty);

            var response = await _httpClient.SendAsync(
                method: "POST",
                url: $"{_supabaseUrl}/rest/v1/rpc/{rpcName}",
                jsonBody: bodyJson,
                headers: CreateAuthHeaders(accessToken));

            if (response == null)
                return SupabaseResult<string>.Fail(SupabaseErrorCode.NetworkError);

            if (response.IsSuccess == false)
                return SupabaseResult<string>.Fail(ExtractErrorCode(response.Body, response.ErrorMessage, rpcName));

            var body = response.Body?.Trim();
            return string.IsNullOrEmpty(body)
                ? SupabaseResult<string>.Fail(rpcName + "_empty_body")
                : SupabaseResult<string>.Success(body);
        }

        /// <summary>
        /// PostgREST 오류 본문에서 서버가 <c>raise exception</c>으로 던진 사유를 뽑아냅니다.
        /// <c>chat_channel_not_found: shout</c> 처럼 상세가 붙은 경우 앞의 코드만 사용해
        /// <c>SupabaseReason</c> 매핑이 동작하게 합니다.
        /// </summary>
        private static string ExtractErrorCode(string body, string fallbackMessage, string rpcName)
        {
            if (!string.IsNullOrWhiteSpace(body))
            {
                try
                {
                    var message = JObject.Parse(body)["message"]?.Value<string>();
                    if (!string.IsNullOrWhiteSpace(message))
                    {
                        var colon = message.IndexOf(':');
                        return colon > 0 ? message.Substring(0, colon).Trim() : message.Trim();
                    }
                }
                catch
                {
                    // JSON이 아니면 아래 폴백 사용
                }
            }

            if (!string.IsNullOrWhiteSpace(fallbackMessage))
                return fallbackMessage;

            return string.IsNullOrWhiteSpace(body) ? rpcName + "_failed" : body;
        }

        private Dictionary<string, string> CreateAuthHeaders(string accessToken)
        {
            return new Dictionary<string, string>
            {
                { "apikey", _publishableKey },
                { "Authorization", "Bearer " + accessToken },
                { "Content-Type", "application/json" }
            };
        }
    }
}
