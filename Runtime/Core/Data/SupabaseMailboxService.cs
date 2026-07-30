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
            var bodyJson = JsonConvert.SerializeObject(new { p_mail_id = mailId.Trim() });

            var response = await _httpClient.SendAsync(
                method: "POST",
                url: url,
                jsonBody: bodyJson,
                headers: CreateAuthHeaders(accessToken, prefer: null));

            if (response == null)
                return SupabaseResult<Mail>.Fail(SupabaseErrorCode.NetworkError);

            if (response.IsSuccess == false)
                return SupabaseResult<Mail>.Fail(response.ErrorMessage ?? response.Body ?? "mail_view_failed");

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
        public async Task<SupabaseResult<IReadOnlyList<MailItemPayload>>> ClaimMailItemsRpcAsync(
            string accessToken,
            string mailId)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
                return SupabaseResult<IReadOnlyList<MailItemPayload>>.Fail(SupabaseErrorCode.AccessTokenEmpty);

            if (string.IsNullOrWhiteSpace(mailId))
                return SupabaseResult<IReadOnlyList<MailItemPayload>>.Fail("mail_id_empty");

            var url = $"{_supabaseUrl}/rest/v1/rpc/ts_claim_mail_items";
            var bodyJson = JsonConvert.SerializeObject(new { p_mail_id = mailId.Trim() });

            var response = await _httpClient.SendAsync(
                method: "POST",
                url: url,
                jsonBody: bodyJson,
                headers: CreateAuthHeaders(accessToken, prefer: null));

            if (response == null)
                return SupabaseResult<IReadOnlyList<MailItemPayload>>.Fail(SupabaseErrorCode.NetworkError);

            if (response.IsSuccess == false)
                return SupabaseResult<IReadOnlyList<MailItemPayload>>.Fail(
                    response.ErrorMessage ?? response.Body ?? "claim_mail_items_failed");

            return ParseClaimItemsArray(response.Body);
        }

        /// <summary>수령 가능한 모든 메일의 보상을 한 번에 수령 처리합니다. RPC <c>ts_claim_all_mail_items</c>. 메일별 보상 묶음 목록을 반환합니다.</summary>
        /// <param name="accessToken">현재 로그인 세션의 액세스 토큰. 필수.</param>
        /// <param name="category">수령 대상 분류. null·공백이면 전체 분류. (기본값: null).</param>
        public async Task<SupabaseResult<IReadOnlyList<MailClaimBundle>>> ClaimAllMailItemsRpcAsync(
            string accessToken,
            string category = null)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
                return SupabaseResult<IReadOnlyList<MailClaimBundle>>.Fail(SupabaseErrorCode.AccessTokenEmpty);

            var url = $"{_supabaseUrl}/rest/v1/rpc/ts_claim_all_mail_items";
            var bodyJson = JsonConvert.SerializeObject(
                new { p_category = string.IsNullOrWhiteSpace(category) ? null : category.Trim() });
            var response = await _httpClient.SendAsync(
                method: "POST",
                url: url,
                jsonBody: bodyJson,
                headers: CreateAuthHeaders(accessToken, prefer: null));

            if (response == null)
                return SupabaseResult<IReadOnlyList<MailClaimBundle>>.Fail(SupabaseErrorCode.NetworkError);

            if (response.IsSuccess == false)
                return SupabaseResult<IReadOnlyList<MailClaimBundle>>.Fail(
                    response.ErrorMessage ?? response.Body ?? "claim_all_mail_items_failed");

            return ParseClaimAllResponse(response.Body);
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
            var bodyJson = JsonConvert.SerializeObject(new { p_mail_id = mailId.Trim() });

            var response = await _httpClient.SendAsync(
                method: "POST",
                url: url,
                jsonBody: bodyJson,
                headers: CreateAuthHeaders(accessToken, prefer: null));

            if (response == null)
                return SupabaseResult<bool>.Fail(SupabaseErrorCode.NetworkError);

            if (response.IsSuccess == false)
                return SupabaseResult<bool>.Fail(response.ErrorMessage ?? response.Body ?? "delete_mail_failed");

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
                new { p_category = string.IsNullOrWhiteSpace(category) ? null : category.Trim() });
            var response = await _httpClient.SendAsync(
                method: "POST",
                url: url,
                jsonBody: bodyJson,
                headers: CreateAuthHeaders(accessToken, prefer: null));

            if (response == null)
                return SupabaseResult<int>.Fail(SupabaseErrorCode.NetworkError);

            if (response.IsSuccess == false)
                return SupabaseResult<int>.Fail(response.ErrorMessage ?? response.Body ?? "delete_claimed_mails_failed");

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
                    response.ErrorMessage ?? response.Body ?? "mail_inbox_counts_failed");

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
                return SupabaseResult<IReadOnlyList<Mail>>.Fail(response.ErrorMessage ?? response.Body ?? "mail_list_failed");

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

        private static SupabaseResult<IReadOnlyList<MailItemPayload>> ParseClaimItemsArray(string body)
        {
            try
            {
                var arr = JsonConvert.DeserializeObject<List<MailClaimLineDto>>(body);
                if (arr == null)
                    return SupabaseResult<IReadOnlyList<MailItemPayload>>.Success(Array.Empty<MailItemPayload>());

                var list = arr
                    .Select(
                        x => new MailItemPayload
                        {
                            Index = x.Index,
                            Key = x.Key?.Trim() ?? string.Empty,
                            Count = x.Count
                        })
                    .ToList();

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
                    var items = (e.Items ?? new List<MailClaimLineDto>())
                        .Select(
                            x => new MailItemPayload
                            {
                                Index = x.Index,
                                Key = x.Key?.Trim() ?? string.Empty,
                                Count = x.Count
                            })
                        .ToList();

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
                Title = r.Title ?? string.Empty,
                Content = r.Content ?? string.Empty,
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
