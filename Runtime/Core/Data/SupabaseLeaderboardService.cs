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
    /// 리더보드 RPC. 목록 <c>ts_leaderboard_tables</c>, 상세 <c>ts_leaderboard_table</c>,
    /// 기록 <c>ts_leaderboard_submit_score</c>, 순위 <c>ts_leaderboard_range</c>,
    /// 플레이어 <c>ts_leaderboard_player</c>, 데이터 수정·삭제 <c>ts_leaderboard_set_player_data</c>·<c>ts_leaderboard_delete_my_score</c>.
    /// 리더보드 정의·컬럼 관리는 운영(service_role) 전용이라 여기에 없습니다.
    /// </summary>
    public sealed class SupabaseLeaderboardService
    {
        private readonly string _supabaseUrl;
        private readonly string _publishableKey;
        private readonly ISupabaseHttpClient _httpClient;

        public SupabaseLeaderboardService(
            string supabaseUrl,
            string publishableKey,
            ISupabaseHttpClient httpClient)
        {
            _supabaseUrl = (supabaseUrl ?? string.Empty).TrimEnd('/');
            _publishableKey = publishableKey ?? string.Empty;
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        /// <summary>사용 가능한 리더보드 목록. 비활성·종료된 리더보드는 제외됩니다.</summary>
        /// <param name="accessToken">현재 로그인 세션의 액세스 토큰. 필수.</param>
        public async Task<SupabaseResult<IReadOnlyList<LeaderboardTable>>> GetTablesAsync(string accessToken)
        {
            var r = await CallRpcAsync(accessToken, "ts_leaderboard_tables", "{}");
            if (!r.IsSuccess)
                return SupabaseResult<IReadOnlyList<LeaderboardTable>>.Fail(r.ErrorCode);

            try
            {
                var list = JsonConvert.DeserializeObject<List<LeaderboardTable>>(r.Data)
                           ?? new List<LeaderboardTable>();
                return SupabaseResult<IReadOnlyList<LeaderboardTable>>.Success(list);
            }
            catch (Exception e)
            {
                return SupabaseResult<IReadOnlyList<LeaderboardTable>>.Fail("leaderboard_tables_parse:" + e.Message);
            }
        }

        /// <summary>리더보드 1건의 정의·현재 회차 상태.</summary>
        public async Task<SupabaseResult<LeaderboardTable>> GetTableAsync(string accessToken, string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return SupabaseResult<LeaderboardTable>.Fail(SupabaseErrorCode.LeaderboardTableNotFound);

            var body = JsonConvert.SerializeObject(new { p_code = code.Trim() });
            var r = await CallRpcAsync(accessToken, "ts_leaderboard_table", body);
            if (!r.IsSuccess)
                return SupabaseResult<LeaderboardTable>.Fail(r.ErrorCode);

            try
            {
                var table = JsonConvert.DeserializeObject<LeaderboardTable>(r.Data);
                return table == null
                    ? SupabaseResult<LeaderboardTable>.Fail(SupabaseErrorCode.LeaderboardTableNotFound)
                    : SupabaseResult<LeaderboardTable>.Success(table);
            }
            catch (Exception e)
            {
                return SupabaseResult<LeaderboardTable>.Fail("leaderboard_table_parse:" + e.Message);
            }
        }

        /// <summary>
        /// 본인 점수를 기록합니다. 리더보드에 설정된 기록 방식(최고·최저·최신·누적)이 서버에서 적용됩니다.
        /// </summary>
        /// <param name="data">플레이어 데이터 컬럼 값. 이 리더보드에 등록된 컬럼만 허용됩니다.</param>
        public async Task<SupabaseResult<LeaderboardSubmitResult>> SubmitScoreAsync(
            string accessToken,
            string code,
            double score,
            string extraData = null,
            IReadOnlyDictionary<string, object> data = null)
        {
            if (string.IsNullOrWhiteSpace(code))
                return SupabaseResult<LeaderboardSubmitResult>.Fail(SupabaseErrorCode.LeaderboardTableNotFound);

            var body = JsonConvert.SerializeObject(new
            {
                p_code = code.Trim(),
                p_score = score,
                p_extra_data = extraData,
                p_data = data
            });

            var r = await CallRpcAsync(accessToken, "ts_leaderboard_submit_score", body);
            if (!r.IsSuccess)
                return SupabaseResult<LeaderboardSubmitResult>.Fail(r.ErrorCode);

            try
            {
                var res = JsonConvert.DeserializeObject<LeaderboardSubmitResult>(r.Data);
                return SupabaseResult<LeaderboardSubmitResult>.Success(res ?? new LeaderboardSubmitResult());
            }
            catch (Exception e)
            {
                return SupabaseResult<LeaderboardSubmitResult>.Fail("leaderboard_submit_parse:" + e.Message);
            }
        }

        /// <summary>순위 범위를 조회합니다. 한 번에 최대 100건까지 반환됩니다.</summary>
        /// <param name="start">시작 순위(1부터).</param>
        /// <param name="end">끝 순위. 시작+99를 넘으면 잘립니다.</param>
        /// <param name="rotationCount">조회할 회차. null이면 현재 회차.</param>
        public async Task<SupabaseResult<IReadOnlyList<LeaderboardEntry>>> GetRangeAsync(
            string accessToken,
            string code,
            int start = 1,
            int end = 100,
            int? rotationCount = null)
        {
            if (string.IsNullOrWhiteSpace(code))
                return SupabaseResult<IReadOnlyList<LeaderboardEntry>>.Fail(SupabaseErrorCode.LeaderboardTableNotFound);

            var body = JsonConvert.SerializeObject(new
            {
                p_code = code.Trim(),
                p_start = start,
                p_end = end,
                p_rotation_count = rotationCount
            });

            var r = await CallRpcAsync(accessToken, "ts_leaderboard_range", body);
            if (!r.IsSuccess)
                return SupabaseResult<IReadOnlyList<LeaderboardEntry>>.Fail(r.ErrorCode);

            try
            {
                var list = JsonConvert.DeserializeObject<List<LeaderboardEntry>>(r.Data)
                           ?? new List<LeaderboardEntry>();
                return SupabaseResult<IReadOnlyList<LeaderboardEntry>>.Success(list);
            }
            catch (Exception e)
            {
                return SupabaseResult<IReadOnlyList<LeaderboardEntry>>.Fail("leaderboard_range_parse:" + e.Message);
            }
        }

        /// <summary>
        /// 플레이어 1명의 순위를 조회합니다. 기록이 없으면 실패가 아니라
        /// <see cref="LeaderboardPlayerEntry.Registered"/>가 false인 결과로 성공합니다.
        /// </summary>
        /// <param name="accountId">조회할 계정. null이면 본인.</param>
        /// <param name="rotationCount">조회할 회차. null이면 현재 회차.</param>
        public async Task<SupabaseResult<LeaderboardPlayerEntry>> GetPlayerAsync(
            string accessToken,
            string code,
            string accountId = null,
            int? rotationCount = null)
        {
            if (string.IsNullOrWhiteSpace(code))
                return SupabaseResult<LeaderboardPlayerEntry>.Fail(SupabaseErrorCode.LeaderboardTableNotFound);

            var body = JsonConvert.SerializeObject(new
            {
                p_code = code.Trim(),
                p_account_id = string.IsNullOrWhiteSpace(accountId) ? null : accountId.Trim(),
                p_rotation_count = rotationCount
            });

            var r = await CallRpcAsync(accessToken, "ts_leaderboard_player", body);
            if (!r.IsSuccess)
                return SupabaseResult<LeaderboardPlayerEntry>.Fail(r.ErrorCode);

            try
            {
                var entry = JsonConvert.DeserializeObject<LeaderboardPlayerEntry>(r.Data);
                return SupabaseResult<LeaderboardPlayerEntry>.Success(entry ?? new LeaderboardPlayerEntry());
            }
            catch (Exception e)
            {
                return SupabaseResult<LeaderboardPlayerEntry>.Fail("leaderboard_player_parse:" + e.Message);
            }
        }

        /// <summary>본인 기록의 추가 데이터·등록 컬럼을 수정합니다. 점수는 바뀌지 않습니다.</summary>
        public async Task<SupabaseResult> SetPlayerDataAsync(
            string accessToken,
            string code,
            string extraData = null,
            IReadOnlyDictionary<string, object> data = null,
            int? rotationCount = null)
        {
            if (string.IsNullOrWhiteSpace(code))
                return SupabaseResult.Fail(SupabaseErrorCode.LeaderboardTableNotFound);

            var body = JsonConvert.SerializeObject(new
            {
                p_code = code.Trim(),
                p_extra_data = extraData,
                p_data = data,
                p_rotation_count = rotationCount
            });

            var r = await CallRpcAsync(accessToken, "ts_leaderboard_set_player_data", body);
            return r.IsSuccess ? SupabaseResult.Ok : SupabaseResult.Fail(r.ErrorCode);
        }

        /// <summary>본인 기록을 삭제합니다. 기록이 없으면 아무것도 하지 않고 성공합니다.</summary>
        public async Task<SupabaseResult> DeleteMyScoreAsync(
            string accessToken,
            string code,
            int? rotationCount = null)
        {
            if (string.IsNullOrWhiteSpace(code))
                return SupabaseResult.Fail(SupabaseErrorCode.LeaderboardTableNotFound);

            var body = JsonConvert.SerializeObject(new
            {
                p_code = code.Trim(),
                p_rotation_count = rotationCount
            });

            var r = await CallRpcAsync(accessToken, "ts_leaderboard_delete_my_score", body);
            return r.IsSuccess ? SupabaseResult.Ok : SupabaseResult.Fail(r.ErrorCode);
        }

        // -------------------------------------------------------------------
        // 공통 RPC 호출
        // -------------------------------------------------------------------

        /// <summary>RPC를 호출하고 응답 본문을 문자열로 반환합니다. 실패 시 서버가 던진 사유를 ErrorCode로 담습니다.</summary>
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
        /// <c>leaderboard_column_not_allowed: zz_lv</c> 처럼 상세가 붙은 경우 앞의 코드만 사용해
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
