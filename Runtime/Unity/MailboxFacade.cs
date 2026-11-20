using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TrueBase.Core.Auth;
using TrueBase.Core.Common;
using TrueBase.Core.Data;

namespace TrueBase.Unity
{
    /// <summary>
    /// 로그인 세션 + <see cref="MailItemHandlerRegistry"/>를 사용하는 우편함 API.
    /// </summary>
    internal sealed class MailboxFacade
    {
        private readonly SupabaseMailboxService _mailbox;
        private readonly Func<SupabaseSession> _sessionGetter;

        /// <param name="mailbox">REST 호출을 수행할 서비스. null이면 예외.</param>
        /// <param name="sessionGetter">현재 세션 제공자. null이면 세션 없는 오버로드는 <c>auth_not_signed_in</c>으로 실패합니다.</param>
        public MailboxFacade(SupabaseMailboxService mailbox, Func<SupabaseSession> sessionGetter = null)
        {
            _mailbox = mailbox ?? throw new ArgumentNullException(nameof(mailbox));
            _sessionGetter = sessionGetter;
        }

        /// <summary>내 우편 목록을 페이지 단위로 조회합니다.</summary>
        /// <param name="limit">한 번에 가져올 최대 개수. (기본값: 50)</param>
        /// <param name="offset">건너뛸 개수. 페이지네이션에 사용합니다.</param>
        /// <param name="category">조회할 분류. null이면 전체 분류. (기본값: null)</param>
        public Task<SupabaseResult<IReadOnlyList<Mail>>> GetMailsAsync(int limit = 50, int offset = 0, string category = null) =>
            GetMailsAsync(_sessionGetter?.Invoke(), limit, offset, category);

        public async Task<SupabaseResult<IReadOnlyList<Mail>>> GetMailsAsync(
            SupabaseSession session,
            int limit = 50,
            int offset = 0,
            string category = null)
        {
            var token = RequireToken(session);
            if (token == null)
                return SupabaseResult<IReadOnlyList<Mail>>.Fail("auth_not_signed_in");

            return await _mailbox.GetMailsAsync(token, limit, offset, category);
        }

        /// <summary>우편 1건의 상세 내용을 조회합니다.</summary>
        public Task<SupabaseResult<Mail>> GetMailAsync(string mailId) =>
            GetMailAsync(_sessionGetter?.Invoke(), mailId);

        public async Task<SupabaseResult<Mail>> GetMailAsync(SupabaseSession session, string mailId)
        {
            var token = RequireToken(session);
            if (token == null)
                return SupabaseResult<Mail>.Fail("auth_not_signed_in");

            return await _mailbox.GetMailByIdAsync(token, mailId);
        }

        /// <summary>우편 설정(만료 우편 보관 일수)을 반환합니다.</summary>
        public async Task<SupabaseResult<MailSettings>> GetSettingsAsync()
        {
            var token = RequireToken(_sessionGetter?.Invoke());
            if (token == null)
                return SupabaseResult<MailSettings>.Fail("auth_not_signed_in");

            return await _mailbox.GetSettingsAsync(token);
        }

        /// <summary>미수령 개수와 분류별 세부 내역을 함께 반환합니다.</summary>
        public Task<SupabaseResult<MailInboxCounts>> GetInboxCountsAsync() =>
            GetInboxCountsAsync(_sessionGetter?.Invoke());

        public async Task<SupabaseResult<MailInboxCounts>> GetInboxCountsAsync(SupabaseSession session)
        {
            var token = RequireToken(session);
            if (token == null)
                return SupabaseResult<MailInboxCounts>.Fail("auth_not_signed_in");

            return await _mailbox.GetInboxCountsAsync(token);
        }

        /// <summary>미수령 메일 개수. <paramref name="category"/> 지정 시 그 분류만.</summary>
        public Task<SupabaseResult<int>> GetUnclaimedMailCountAsync(string category = null) =>
            GetInboxCountValueAsync(_sessionGetter?.Invoke(), category, c => c.Unclaimed, c => c.Unclaimed);

        public Task<SupabaseResult<int>> GetUnclaimedMailCountAsync(SupabaseSession session, string category = null) =>
            GetInboxCountValueAsync(session, category, c => c.Unclaimed, c => c.Unclaimed);

        private async Task<SupabaseResult<int>> GetInboxCountValueAsync(
            SupabaseSession session,
            string category,
            Func<MailInboxCounts, int> totalSelector,
            Func<MailCategoryCounts, int> categorySelector)
        {
            var token = RequireToken(session);
            if (token == null)
                return SupabaseResult<int>.Fail("auth_not_signed_in");

            var r = await _mailbox.GetInboxCountsAsync(token);
            if (!r.IsSuccess)
                return SupabaseResult<int>.Fail(r.ErrorCode ?? "inbox_counts_failed");

            if (string.IsNullOrWhiteSpace(category))
                return SupabaseResult<int>.Success(totalSelector(r.Data));

            var byCat = r.Data?.ByCategory;
            return byCat != null && byCat.TryGetValue(category.Trim(), out var cc)
                ? SupabaseResult<int>.Success(categorySelector(cc))
                : SupabaseResult<int>.Success(0);
        }

        /// <summary>
        /// 우편 1건의 첨부 보상을 수령합니다.
        /// 처리기가 등록된 키는 게임 처리기가, 그 밖의 키는 서버가 받기와 같은 처리 안에서 세이브의 같은 이름 숫자 칸에 지급합니다.
        /// 처리기도 없고 숫자 칸도 아닌 키가 있으면 서버가 받기 전체를 되돌립니다(<c>mail_item_handler_missing</c>).
        /// </summary>
        public Task<SupabaseResult<IReadOnlyList<ClaimResult>>> ClaimMailItemsAsync(string mailId) =>
            ClaimMailItemsAsync(_sessionGetter?.Invoke(), mailId);

        public async Task<SupabaseResult<IReadOnlyList<ClaimResult>>> ClaimMailItemsAsync(
            SupabaseSession session,
            string mailId)
        {
            var token = RequireToken(session);
            if (token == null)
                return SupabaseResult<IReadOnlyList<ClaimResult>>.Fail("auth_not_signed_in");

            var save = SupabaseSDK._userSave;
            var mark = save?.BeginServerAdd() ?? default;

            var rpc = await _mailbox.ClaimMailItemsRpcAsync(token, mailId, MailItemHandlerRegistry.Keys());
            if (!rpc.IsSuccess)
                return SupabaseResult<IReadOnlyList<ClaimResult>>.Fail(rpc.ErrorCode ?? "claim_rpc_failed");

            return await DeliverAsync(mailId, rpc.Data, save, mark, session.User?.Id);
        }

        /// <summary>
        /// 미수령 보상이 있는 모든 우편의 보상을 일괄 수령합니다. 지급 방식은 <see cref="ClaimMailItemsAsync(string)"/>와 같고,
        /// 한 우편이라도 지급할 수 없는 키가 있으면 전체가 받아지지 않습니다.
        /// </summary>
        public Task<SupabaseResult<IReadOnlyList<ClaimResult>>> ClaimAllMailItemsAsync(string category = null) =>
            ClaimAllMailItemsAsync(_sessionGetter?.Invoke(), category);

        public async Task<SupabaseResult<IReadOnlyList<ClaimResult>>> ClaimAllMailItemsAsync(
            SupabaseSession session,
            string category = null)
        {
            var token = RequireToken(session);
            if (token == null)
                return SupabaseResult<IReadOnlyList<ClaimResult>>.Fail("auth_not_signed_in");

            var save = SupabaseSDK._userSave;
            var mark = save?.BeginServerAdd() ?? default;

            var rpc = await _mailbox.ClaimAllMailItemsRpcAsync(token, category, MailItemHandlerRegistry.Keys());
            if (!rpc.IsSuccess)
                return SupabaseResult<IReadOnlyList<ClaimResult>>.Fail(rpc.ErrorCode ?? "claim_all_rpc_failed");

            var aggregated = new List<ClaimResult>();
            foreach (var bundle in rpc.Data ?? Array.Empty<MailClaimBundle>())
            {
                if (string.IsNullOrWhiteSpace(bundle.MailId))
                    continue;

                var part = await DeliverAsync(bundle.MailId, bundle.Items, save, mark, session.User?.Id);
                if (!part.IsSuccess)
                    return part;

                if (part.Data != null && part.Data.Count > 0)
                    aggregated.AddRange(part.Data);
            }

            return SupabaseResult<IReadOnlyList<ClaimResult>>.Success(aggregated);
        }

        /// <summary>우편 1건을 삭제합니다.</summary>
        public Task<SupabaseResult<bool>> DeleteMailAsync(string mailId) =>
            DeleteMailAsync(_sessionGetter?.Invoke(), mailId);

        public async Task<SupabaseResult<bool>> DeleteMailAsync(SupabaseSession session, string mailId)
        {
            var token = RequireToken(session);
            if (token == null)
                return SupabaseResult<bool>.Fail("auth_not_signed_in");

            return await _mailbox.DeleteMailForUserRpcAsync(token, mailId);
        }

        /// <summary>수령한 우편을 일괄 삭제합니다. 성공 시 삭제된 개수를 반환합니다.</summary>
        /// <param name="category">삭제 대상 분류. null이면 전체 분류. (기본값: null)</param>
        public Task<SupabaseResult<int>> DeleteClaimedMailsAsync(string category = null) =>
            DeleteClaimedMailsAsync(_sessionGetter?.Invoke(), category);

        public async Task<SupabaseResult<int>> DeleteClaimedMailsAsync(SupabaseSession session, string category = null)
        {
            var token = RequireToken(session);
            if (token == null)
                return SupabaseResult<int>.Fail("auth_not_signed_in");

            return await _mailbox.DeleteClaimedMailsForUserRpcAsync(token, category);
        }

        /// <summary>세션에서 액세스 토큰을 추출합니다. 세션이 null이거나 토큰이 비어 있으면 null.</summary>
        private static string RequireToken(SupabaseSession session)
        {
            if (session == null || string.IsNullOrWhiteSpace(session.AccessToken))
                return null;

            return session.AccessToken;
        }

        /// <summary>
        /// 받은 보상을 <c>Index</c> 오름차순으로 게임에 넘깁니다. 서버가 이미 세이브 칸에 더한 보상은 로컬 세이브에 맞추고,
        /// 나머지는 처리기에 넘깁니다. 처리기가 하나라도 실패하면 즉시 실패를 반환합니다(이후 아이템은 처리하지 않음).
        /// </summary>
        /// <param name="mailId">처리기에 넘길 우편 ID.</param>
        /// <param name="lines">받은 보상 목록. null 또는 빈 목록이면 빈 결과로 성공합니다.</param>
        /// <param name="save">로컬 세이브. 없으면 서버 지급분은 다음 로드에서 들어옵니다.</param>
        /// <param name="mark">받기 요청을 보내기 직전의 저장 상태.</param>
        /// <param name="accountAtCall">받기를 요청한 계정.</param>
        private static async Task<SupabaseResult<IReadOnlyList<ClaimResult>>> DeliverAsync(
            string mailId,
            IReadOnlyList<MailItemPayload> lines,
            IUserSaveOperations save,
            ServerAddMark mark,
            string accountAtCall)
        {
            if (lines == null || lines.Count == 0)
                return SupabaseResult<IReadOnlyList<ClaimResult>>.Success(Array.Empty<ClaimResult>());

            var ordered = lines.OrderBy(x => x.Index).ToList();
            var results = new List<ClaimResult>();

            foreach (var line in ordered)
            {
                if (line == null || string.IsNullOrWhiteSpace(line.Key))
                    return SupabaseResult<IReadOnlyList<ClaimResult>>.Fail("mail_item_key_empty");

                if (line.GrantedByServer)
                {
                    // 받는 사이 다른 계정으로 바뀌었으면 그 세이브에 더하지 않는다 — 다음 로드가 서버 값을 가져온다.
                    if (save != null && accountAtCall != null && accountAtCall == SupabaseSDK.CurrentAccountId)
                        save.ApplyServerAdd(line.Key, line.Count, mark);

                    results.Add(new ClaimResult { MailId = mailId, ItemIndex = line.Index, ItemKey = line.Key, Count = line.Count });
                    continue;
                }

                if (!MailItemHandlerRegistry.TryGetHandler(line.Key, out var handler))
                    return SupabaseResult<IReadOnlyList<ClaimResult>>.Fail(SupabaseErrorCode.MailItemHandlerMissing + ":" + line.Key);

                var r = await handler.HandleAsync(mailId, line.Index, line.Key, line.Count);
                if (!r.IsSuccess)
                    return SupabaseResult<IReadOnlyList<ClaimResult>>.Fail(r.ErrorCode ?? "mail_handler_failed");

                if (r.Data != null)
                    results.Add(r.Data);
            }

            return SupabaseResult<IReadOnlyList<ClaimResult>>.Success(results);
        }
    }
}
