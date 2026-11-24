using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TrueBase.Core.Common;
using TrueBase.Core.Http;

namespace TrueBase.Core.Data
{
    /// <summary>
    /// 우편함 REST + RPC. 상세 <c>ts_view_mail_for_user</c>, 수령 <c>ts_claim_*</c>, 삭제 <c>ts_delete_mail_for_user</c>·<c>ts_delete_claimed_mails_for_user</c>, 카운트 <c>ts_mail_inbox_counts</c>.
    /// </summary>
    public sealed class SupabaseMailboxService
    {
        private const string MailSelectColumns =
            "id,account_id,user_id,sender_type,title,content,expires_at,created_at,items,items_claimed_at,category,localized";

        private readonly string _supabaseUrl;
        private readonly string _publishableKey;
        private readonly string _mailsTable;
        private readonly ISupabaseHttpClient _httpClient;

        public SupabaseMailboxService(
            string supabaseUrl,
            string publishableKey,
            ISupabaseHttpClient httpClient,
            string mailsTable = "mails")
        {
            _supabaseUrl = supabaseUrl?.TrimEnd('/') ?? throw new ArgumentNullException(nameof(supabaseUrl));
            _publishableKey = publishableKey ?? throw new ArgumentNullException(nameof(publishableKey));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _mailsTable = SupabaseRestTableRef.Normalize(mailsTable, nameof(mailsTable));
        }

        /// <summary>로그인한 사용자의 우편함 메일 목록을 조회합니다.</summary>
        /// <param name="accessToken">현재 로그인 세션의 액세스 토큰.</param>
        /// <param name="limit">반환할 메일 개수. 범위: 1~200 (기본값: 50).</param>
        /// <param name="offset">건너뛸 메일 개수. 0 이상. (기본값: 0).</param>
        /// <param name="category">조회할 분류. null·공백이면 전체 분류. (기본값: null).</param>
        public async Task<SupabaseResult<IReadOnlyList<Mail>>> GetMailsAsync(
            string accessToken,
            int limit = 50,
            int offset = 0,
            string category = null)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
                return SupabaseResult<IReadOnlyList<Mail>>.Fail(SupabaseErrorCode.AccessTokenEmpty);

            limit = Math.Clamp(limit, 1, 200);
            offset = Math.Max(0, offset);

            var url =
                $"{SupabaseRestTableRef.BuildTableUrl(_supabaseUrl, _mailsTable)}" +
                $"?select={Uri.EscapeDataString(MailSelectColumns)}" +
                $"&order=created_at.desc" +
                $"&limit={limit}" +
                $"&offset={offset}";

            if (!string.IsNullOrWhiteSpace(category))
                url += $"&category=eq.{Uri.EscapeDataString(category.Trim())}";

            return await FetchMailListAsync(accessToken, url);
        }

        /// <summary>메일 한 건을 조회합니다. RPC <c>ts_view_mail_for_user</c> — 보상 items가 없는 메일은 조회 시 읽음 처리됩니다.</summary>
        /// <param name="accessToken">현재 로그인 세션의 액세스 토큰. 필수.</param>
        /// <param name="mailId">조회할 메일 UUID. 필수 — 본인 소유가 아니거나 없으면 <c>mail_not_found</c> 실패.</param>
        public async Task<SupabaseResult<Mail>> GetMailByIdAsync(string accessToken, string mailId)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
                return SupabaseResult<Mail>.Fail(SupabaseErrorCode.AccessTokenEmpty);

            if (string.IsNullOrWhiteSpace(mailId))
                return SupabaseResult<Mail>.Fail("mail_id_empty");

            var url = $"{_supabaseUrl}/rest/v1/rpc/ts_view_mail_for_user";
            var bodyJson = JsonConvert.SerializeObject(new Dictionary<string, object> { ["p_mail_id"] = mailId.Trim() });

            var response = await _httpClient.SendAsync(
                method: "POST",
                url: url,
                jsonBody: bodyJson,
                headers: CreateAuthHeaders(accessToken, prefer: null));

            if (response == null)
                return SupabaseResult<Mail>.Fail(SupabaseErrorCode.NetworkError);

            if (response.IsSuccess == false)
                return SupabaseResult<Mail>.Fail(SupabaseRestHelpers.ExtractRpcErrorCode(response.Body, response.ErrorMessage, "mail_view"));

            var body = response.Body?.Trim();
            if (string.IsNullOrEmpty(body) || body == "null")
                return SupabaseResult<Mail>.Fail("mail_not_found");

            try
            {
                var row = JsonConvert.DeserializeObject<MailRestRow>(body);
                if (row == null || string.IsNullOrWhiteSpace(row.Id))
                    return SupabaseResult<Mail>.Fail("mail_not_found");

                var mail = MapRow(row);
                return mail == null
                    ? SupabaseResult<Mail>.Fail("mail_not_found")
                    : SupabaseResult<Mail>.Success(mail);
            }
            catch (Exception e)
            {
                return SupabaseResult<Mail>.Fail("mail_detail_parse:" + e.Message);
            }
        }

        /// <summary>단일 메일의 보상을 수령 처리합니다. RPC <c>ts_claim_mail_items</c>. 보상 없음이면 빈 목록(no-op).</summary>
        /// <param name="accessToken">현재 로그인 세션의 액세스 토큰. 필수.</param>
        /// <param name="mailId">수령할 메일 UUID. 필수.</param>
        /// <param name="clientKeys">게임 처리기가 지급할 키. 그 밖의 보상은 서버가 숫자 칸이나 리스트·딕셔너리 원소에 바로 더하고, 더할 곳이 없으면 받기 전체가 실패합니다. 원소는 <paramref name="elementSpec"/> 표에 맞는 것만 서버가 지급합니다.</param>
        /// <param name="elementSpec">서버가 지급해도 되는 리스트·딕셔너리 원소 표(<c>DataSchema.GetElementGrantSpec</c>). 비어 있으면 원소 꼴 키는 지급하지 않습니다.</param>
        public async Task<SupabaseResult<IReadOnlyList<MailItemPayload>>> ClaimMailItemsRpcAsync(
            string accessToken,
            string mailId,
            IReadOnlyCollection<string> clientKeys,
            IReadOnlyDictionary<string, object> elementSpec)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
                return SupabaseResult<IReadOnlyList<MailItemPayload>>.Fail(SupabaseErrorCode.AccessTokenEmpty);

            if (string.IsNullOrWhiteSpace(mailId))
                return SupabaseResult<IReadOnlyList<MailItemPayload>>.Fail("mail_id_empty");

            var url = $"{_supabaseUrl}/rest/v1/rpc/ts_claim_mail_items";
            var bodyJson = JsonConvert.SerializeObject(new Dictionary<string, object>
            {
                ["p_mail_id"] = mailId.Trim(),
                ["p_client_keys"] = clientKeys ?? Array.Empty<string>(),
                ["p_element_spec"] = elementSpec ?? new Dictionary<string, object>()
            });

            var response = await _httpClient.SendAsync(
                method: "POST",
                url: url,
                jsonBody: bodyJson,
                headers: CreateAuthHeaders(accessToken, prefer: null));

            if (response == null)
                return SupabaseResult<IReadOnlyList<MailItemPayload>>.Fail(SupabaseErrorCode.NetworkError);

            if (response.IsSuccess == false)
                return SupabaseResult<IReadOnlyList<MailItemPayload>>.Fail(
                    ClaimErrorCode(response, "claim_mail_items"));

            return ParseClaimItemsArray(response.Body);
        }

        /// <summary>수령 가능한 모든 메일의 보상을 한 번에 수령 처리합니다. RPC <c>ts_claim_all_mail_items</c>. 메일별 보상 묶음 목록을 반환합니다.</summary>
        /// <param name="accessToken">현재 로그인 세션의 액세스 토큰. 필수.</param>
        /// <param name="category">수령 대상 분류. null·공백이면 전체 분류.</param>
        /// <param name="clientKeys">게임 처리기가 지급할 키. <see cref="ClaimMailItemsRpcAsync"/>와 같습니다.</param>
        /// <param name="elementSpec">원소 표. <see cref="ClaimMailItemsRpcAsync"/>와 같습니다. 원소 키 때문에 지급할 수 없는 우편은 서버가 건너뜁니다.</param>
        public async Task<SupabaseResult<IReadOnlyList<MailClaimBundle>>> ClaimAllMailItemsRpcAsync(
            string accessToken,
            string category,
            IReadOnlyCollection<string> clientKeys,
            IReadOnlyDictionary<string, object> elementSpec)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
                return SupabaseResult<IReadOnlyList<MailClaimBundle>>.Fail(SupabaseErrorCode.AccessTokenEmpty);

            var url = $"{_supabaseUrl}/rest/v1/rpc/ts_claim_all_mail_items";
            var bodyJson = JsonConvert.SerializeObject(new Dictionary<string, object>
            {
                ["p_category"] = string.IsNullOrWhiteSpace(category) ? null : category.Trim(),
                ["p_client_keys"] = clientKeys ?? Array.Empty<string>(),
                ["p_element_spec"] = elementSpec ?? new Dictionary<string, object>()
            });
            var response = await _httpClient.SendAsync(
                method: "POST",
                url: url,
                jsonBody: bodyJson,
                headers: CreateAuthHeaders(accessToken, prefer: null));

            if (response == null)
                return SupabaseResult<IReadOnlyList<MailClaimBundle>>.Fail(SupabaseErrorCode.NetworkError);

            if (response.IsSuccess == false)
                return SupabaseResult<IReadOnlyList<MailClaimBundle>>.Fail(
                    ClaimErrorCode(response, "claim_all_mail_items"));

            return ParseClaimAllResponse(response.Body);
        }

        /// <summary>플레이어 자신에게 우편을 보냅니다. RPC <c>ts_mail_send_to_self</c>. 성공하면 새 우편 ID.</summary>
        /// <param name="onceKey">주면 계정마다 이 키로 한 번만 보낸다. 두 번째부터 <c>mail_already_sent</c>.</param>
        public async Task<SupabaseResult<string>> SendToSelfAsync(
            string accessToken, string title, string content, IReadOnlyList<MailItemPayload> items,
            int expiresDays, string category, string onceKey)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
                return SupabaseResult<string>.Fail(SupabaseErrorCode.AccessTokenEmpty);

            var itemRows = new List<Dictionary<string, object>>();
            if (items != null)
                foreach (var it in items)
                    if (it != null)
                        itemRows.Add(new Dictionary<string, object> { ["key"] = it.Key, ["count"] = it.Count });

            var bodyJson = JsonConvert.SerializeObject(new Dictionary<string, object>
            {
                ["p_title"] = title ?? "",
                ["p_content"] = content ?? "",
                ["p_items"] = itemRows.Count > 0 ? itemRows : null,
                ["p_expires_days"] = expiresDays,
                ["p_category"] = string.IsNullOrWhiteSpace(category) ? "default" : category.Trim(),
                ["p_once_key"] = string.IsNullOrWhiteSpace(onceKey) ? null : onceKey.Trim()
            });

            var response = await _httpClient.SendAsync(
                method: "POST",
                url: $"{_supabaseUrl}/rest/v1/rpc/ts_mail_send_to_self",
                jsonBody: bodyJson,
                headers: CreateAuthHeaders(accessToken, prefer: null));

            if (response == null)
                return SupabaseResult<string>.Fail(SupabaseErrorCode.NetworkError);
            if (response.IsSuccess == false)
                return SupabaseResult<string>.Fail(SupabaseRestHelpers.ExtractRpcErrorCode(response.Body, response.ErrorMessage, "send_mail_to_self"));

            // 응답은 "uuid" 꼴의 JSON 글자다.
            var id = (response.Body ?? "").Trim().Trim('"');
            return string.IsNullOrEmpty(id)
                ? SupabaseResult<string>.Fail("send_mail_to_self_empty")
                : SupabaseResult<string>.Success(id);
        }

        /// <summary>메일 한 건을 소프트 삭제합니다. RPC <c>ts_delete_mail_for_user</c>.</summary>
        /// <param name="accessToken">현재 로그인 세션의 액세스 토큰. 필수.</param>
        /// <param name="mailId">삭제할 메일 UUID. 필수.</param>
        public async Task<SupabaseResult<bool>> DeleteMailForUserRpcAsync(string accessToken, string mailId)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
                return SupabaseResult<bool>.Fail(SupabaseErrorCode.AccessTokenEmpty);

            if (string.IsNullOrWhiteSpace(mailId))
                return SupabaseResult<bool>.Fail("mail_id_empty");

            var url = $"{_supabaseUrl}/rest/v1/rpc/ts_delete_mail_for_user";
            var bodyJson = JsonConvert.SerializeObject(new Dictionary<string, object> { ["p_mail_id"] = mailId.Trim() });

            var response = await _httpClient.SendAsync(
                method: "POST",
                url: url,
                jsonBody: bodyJson,
                headers: CreateAuthHeaders(accessToken, prefer: null));

            if (response == null)
                return SupabaseResult<bool>.Fail(SupabaseErrorCode.NetworkError);

            if (response.IsSuccess == false)
                return SupabaseResult<bool>.Fail(SupabaseRestHelpers.ExtractRpcErrorCode(response.Body, response.ErrorMessage, "delete_mail"));

            return SupabaseResult<bool>.Success(true);
        }

        /// <summary>수령한 메일만 일괄 소프트 삭제합니다. RPC <c>ts_delete_claimed_mails_for_user</c>. 반환값은 처리한 행 수.</summary>
        /// <param name="accessToken">현재 로그인 세션의 액세스 토큰. 필수.</param>
        /// <param name="category">삭제 대상 분류. null·공백이면 전체 분류. (기본값: null).</param>
        public async Task<SupabaseResult<int>> DeleteClaimedMailsForUserRpcAsync(
            string accessToken,
            string category = null)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
                return SupabaseResult<int>.Fail(SupabaseErrorCode.AccessTokenEmpty);

            var url = $"{_supabaseUrl}/rest/v1/rpc/ts_delete_claimed_mails_for_user";
            var bodyJson = JsonConvert.SerializeObject(
                new Dictionary<string, object> { ["p_category"] = string.IsNullOrWhiteSpace(category) ? null : category.Trim() });
            var response = await _httpClient.SendAsync(
                method: "POST",
                url: url,
                jsonBody: bodyJson,
                headers: CreateAuthHeaders(accessToken, prefer: null));

            if (response == null)
                return SupabaseResult<int>.Fail(SupabaseErrorCode.NetworkError);

            if (response.IsSuccess == false)
                return SupabaseResult<int>.Fail(SupabaseRestHelpers.ExtractRpcErrorCode(response.Body, response.ErrorMessage, "delete_claimed_mails"));

            var body = response.Body?.Trim();
            if (string.IsNullOrEmpty(body))
                return SupabaseResult<int>.Fail("delete_claimed_mails_empty_body");

            try
            {
                var n = JsonConvert.DeserializeObject<int>(body);
                return SupabaseResult<int>.Success(n);
            }
            catch (Exception e)
            {
                return SupabaseResult<int>.Fail("delete_claimed_mails_parse:" + e.Message);
            }
        }

        /// <summary>미수령 메일 개수를 조회합니다(JWT <c>auth.uid()</c> + 현재 프로필 서버 기준). RPC <c>ts_mail_inbox_counts</c>.</summary>
        /// <param name="accessToken">현재 로그인 세션의 액세스 토큰. 필수.</param>
        public async Task<SupabaseResult<MailInboxCounts>> GetInboxCountsAsync(string accessToken)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
                return SupabaseResult<MailInboxCounts>.Fail(SupabaseErrorCode.AccessTokenEmpty);

            var url = $"{_supabaseUrl}/rest/v1/rpc/ts_mail_inbox_counts";
            var response = await _httpClient.SendAsync(
                method: "POST",
                url: url,
                jsonBody: "{}",
                headers: CreateAuthHeaders(accessToken, prefer: null));

            if (response == null)
                return SupabaseResult<MailInboxCounts>.Fail(SupabaseErrorCode.NetworkError);

            if (response.IsSuccess == false)
                return SupabaseResult<MailInboxCounts>.Fail(
                    SupabaseRestHelpers.ExtractRpcErrorCode(response.Body, response.ErrorMessage, "mail_inbox_counts"));

            var body = response.Body?.Trim();
            if (string.IsNullOrEmpty(body) || body == "null")
                return SupabaseResult<MailInboxCounts>.Fail("mail_inbox_counts_null");

            try
            {
                var counts = JsonConvert.DeserializeObject<MailInboxCounts>(body);
                if (counts == null)
                    return SupabaseResult<MailInboxCounts>.Fail("mail_inbox_counts_parse_null");

                return SupabaseResult<MailInboxCounts>.Success(counts);
            }
            catch (Exception e)
            {
                return SupabaseResult<MailInboxCounts>.Fail("mail_inbox_counts_parse:" + e.Message);
            }
        }

        /// <summary>우편 설정(만료 우편 보관 일수)을 읽습니다. RPC <c>ts_mail_client_settings</c>.</summary>
        /// <param name="accessToken">현재 로그인 세션의 액세스 토큰. 필수.</param>
        public async Task<SupabaseResult<MailSettings>> GetSettingsAsync(string accessToken)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
                return SupabaseResult<MailSettings>.Fail(SupabaseErrorCode.AccessTokenEmpty);

            var url = $"{_supabaseUrl}/rest/v1/rpc/ts_mail_client_settings";
            var response = await _httpClient.SendAsync(
                method: "POST",
                url: url,
                jsonBody: "{}",
                headers: CreateAuthHeaders(accessToken, prefer: null));

            if (response == null)
                return SupabaseResult<MailSettings>.Fail(SupabaseErrorCode.NetworkError);

            if (response.IsSuccess == false)
                return SupabaseResult<MailSettings>.Fail(
                    SupabaseRestHelpers.ExtractRpcErrorCode(response.Body, response.ErrorMessage, "mail_settings"));

            var body = response.Body?.Trim();
            if (string.IsNullOrEmpty(body) || body == "null")
                return SupabaseResult<MailSettings>.Fail("mail_settings_null");

            try
            {
                var settings = JsonConvert.DeserializeObject<MailSettings>(body);
                if (settings == null)
                    return SupabaseResult<MailSettings>.Fail("mail_settings_parse_null");

                return SupabaseResult<MailSettings>.Success(settings);
            }
            catch (Exception e)
            {
                return SupabaseResult<MailSettings>.Fail("mail_settings_parse:" + e.Message);
            }
        }

        private async Task<SupabaseResult<IReadOnlyList<Mail>>> FetchMailListAsync(string accessToken, string url)
        {
            var response = await _httpClient.SendAsync(
                method: "GET",
                url: url,
                jsonBody: null,
                headers: CreateAuthHeaders(accessToken, prefer: null));

            if (response == null)
                return SupabaseResult<IReadOnlyList<Mail>>.Fail(SupabaseErrorCode.NetworkError);

            if (response.IsSuccess == false)
                return SupabaseResult<IReadOnlyList<Mail>>.Fail(SupabaseRestHelpers.ExtractRpcErrorCode(response.Body, response.ErrorMessage, "mail_list"));

            try
            {
                var rows = JsonConvert.DeserializeObject<List<MailRestRow>>(response.Body);
                if (rows == null)
                    return SupabaseResult<IReadOnlyList<Mail>>.Success(Array.Empty<Mail>());

                var mapped = rows.Where(r => r != null && !string.IsNullOrWhiteSpace(r.Id))
                    .Select(MapRow)
                    .ToList();
                return SupabaseResult<IReadOnlyList<Mail>>.Success(mapped);
            }
            catch (Exception e)
            {
                return SupabaseResult<IReadOnlyList<Mail>>.Fail("mail_list_parse:" + e.Message);
            }
        }

        /// <summary>
        /// 받기 실패 사유. 지급할 곳이 없는 키(<c>mail_item_handler_missing: 키</c>)는 어느 키인지 남겨
        /// <c>mail_item_handler_missing:키</c> 로 돌려준다 — 운영자가 보낸 키와 게임 처리기를 맞춰 볼 수 있게.
        /// </summary>
        private static string ClaimErrorCode(SupabaseHttpResponse response, string rpcName)
        {
            var code = SupabaseRestHelpers.ExtractRpcErrorCode(response.Body, response.ErrorMessage, rpcName);
            if (code != SupabaseErrorCode.MailItemHandlerMissing || string.IsNullOrWhiteSpace(response.Body))
                return code;

            try
            {
                var message = JObject.Parse(response.Body)["message"]?.Value<string>();
                var colon = message?.IndexOf(':') ?? -1;
                var key = colon > 0 ? message.Substring(colon + 1).Trim() : null;
                return string.IsNullOrEmpty(key) ? code : code + ":" + key;
            }
            catch
            {
                return code;
            }
        }

        private static MailItemPayload ToPayload(MailClaimLineDto x) => new MailItemPayload
        {
            Index = x.Index,
            Key = x.Key?.Trim() ?? string.Empty,
            Count = x.Count,
            GrantedByServer = x.Granted
        };

        private static SupabaseResult<IReadOnlyList<MailItemPayload>> ParseClaimItemsArray(string body)
        {
            try
            {
                var arr = JsonConvert.DeserializeObject<List<MailClaimLineDto>>(body);
                if (arr == null)
                    return SupabaseResult<IReadOnlyList<MailItemPayload>>.Success(Array.Empty<MailItemPayload>());

                var list = arr.Select(ToPayload).ToList();

                return SupabaseResult<IReadOnlyList<MailItemPayload>>.Success(list);
            }
            catch (Exception e)
            {
                return SupabaseResult<IReadOnlyList<MailItemPayload>>.Fail("claim_items_parse:" + e.Message);
            }
        }

        private static SupabaseResult<IReadOnlyList<MailClaimBundle>> ParseClaimAllResponse(string body)
        {
            try
            {
                var arr = JsonConvert.DeserializeObject<List<ClaimAllMailEntryDto>>(body);
                if (arr == null)
                    return SupabaseResult<IReadOnlyList<MailClaimBundle>>.Success(Array.Empty<MailClaimBundle>());

                var bundles = new List<MailClaimBundle>();
                foreach (var e in arr)
                {
                    var items = (e.Items ?? new List<MailClaimLineDto>()).Select(ToPayload).ToList();

                    bundles.Add(
                        new MailClaimBundle
                        {
                            MailId = e.MailId?.Trim(),
                            Items = items
                        });
                }

                return SupabaseResult<IReadOnlyList<MailClaimBundle>>.Success(bundles);
            }
            catch (Exception ex)
            {
                return SupabaseResult<IReadOnlyList<MailClaimBundle>>.Fail("claim_all_parse:" + ex.Message);
            }
        }

        private Mail MapRow(MailRestRow r)
        {
            if (r == null || string.IsNullOrWhiteSpace(r.Id))
                return null;

            var items = ParseItemsToken(r.Items);
            for (var i = 0; i < items.Count; i++)
                items[i].Index = i;

            return new Mail
            {
                Id = r.Id,
                AccountId = r.AccountId,
                UserId = r.UserId,
                SenderType = r.SenderType ?? string.Empty,
                BaseTitle = r.Title ?? string.Empty,
                BaseContent = r.Content ?? string.Empty,
                ExpiresAt = r.ExpiresAt,
                CreatedAt = r.CreatedAt,
                ItemsClaimedAt = r.ItemsClaimedAt,
                Items = items,
                Category = r.Category ?? "default",
                Localized = r.Localized
            };
        }

        private static List<MailItemPayload> ParseItemsToken(JToken tok)
        {
            var list = new List<MailItemPayload>();
            if (tok == null || tok.Type == JTokenType.Null)
                return list;

            if (tok.Type != JTokenType.Array)
                return list;

            foreach (var el in (JArray)tok)
            {
                var key = el["key"]?.Value<string>();
                var countToken = el["count"];
                var count = countToken?.Type == JTokenType.Integer
                    ? countToken.Value<int>()
                    : countToken?.Value<int?>() ?? 0;

                if (string.IsNullOrWhiteSpace(key))
                    continue;

                list.Add(new MailItemPayload { Key = key.Trim(), Count = count });
            }

            return list;
        }

        private Dictionary<string, string> CreateAuthHeaders(string accessToken, string prefer = null) =>
            SupabaseRestHelpers.AuthHeaders(_publishableKey, accessToken, prefer);

        private sealed class MailRestRow
        {
            [JsonProperty("id")]
            public string Id { get; set; }

            [JsonProperty("account_id")]
            public string AccountId { get; set; }

            [JsonProperty("user_id")]
            public string UserId { get; set; }

            [JsonProperty("sender_type")]
            public string SenderType { get; set; }

            [JsonProperty("title")]
            public string Title { get; set; }

            [JsonProperty("content")]
            public string Content { get; set; }

            [JsonProperty("expires_at")]
            public DateTimeOffset ExpiresAt { get; set; }

            [JsonProperty("created_at")]
            public DateTimeOffset CreatedAt { get; set; }

            [JsonProperty("items_claimed_at")]
            public DateTimeOffset? ItemsClaimedAt { get; set; }

            [JsonProperty("items")]
            public JToken Items { get; set; }

            [JsonProperty("category")]
            public string Category { get; set; }

            [JsonProperty("localized")]
            public Dictionary<string, MailLocalizedText> Localized { get; set; }
        }

        private sealed class MailClaimLineDto
        {
            [JsonProperty("index")]
            public int Index { get; set; }

            [JsonProperty("key")]
            public string Key { get; set; }

            [JsonProperty("count")]
            public int Count { get; set; }

            [JsonProperty("granted")]
            public bool Granted { get; set; }
        }

        private sealed class ClaimAllMailEntryDto
        {
            [JsonProperty("mail_id")]
            public string MailId { get; set; }

            [JsonProperty("items")]
            public List<MailClaimLineDto> Items { get; set; }
        }
    }
}
