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
    public sealed class MailboxFacade
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
        public Task<SupabaseResult<IReadOnlyList<Mail>>> GetMyMailsAsync(int limit = 50, int offset = 0) =>
            GetMyMailsAsync(_sessionGetter?.Invoke(), limit, offset);

        public async Task<SupabaseResult<IReadOnlyList<Mail>>> GetMyMailsAsync(
            SupabaseSession session,
            int limit = 50,
            int offset = 0)
        {
            var token = RequireToken(session);
            if (token == null)
                return SupabaseResult<IReadOnlyList<Mail>>.Fail("auth_not_signed_in");

            return await _mailbox.GetMailsAsync(token, limit, offset);
        }

        /// <summary>우편 1건의 상세 내용을 조회합니다.</summary>
        public Task<SupabaseResult<Mail>> GetMailDetailAsync(string mailId) =>
            GetMailDetailAsync(_sessionGetter?.Invoke(), mailId);

        public async Task<SupabaseResult<Mail>> GetMailDetailAsync(SupabaseSession session, string mailId)
        {
            var token = RequireToken(session);
            if (token == null)
                return SupabaseResult<Mail>.Fail("auth_not_signed_in");

            return await _mailbox.GetMailByIdAsync(token, mailId);
        }

        /// <summary>미읽음 수. <paramref name="userId"/>는 계약 호환용(무시).</summary>
        public Task<SupabaseResult<int>> GetUnreadCountAsync(string userId = null) =>
            GetInboxCountValueAsync(_sessionGetter?.Invoke(), c => c.Unread);

        public Task<SupabaseResult<int>> GetUnreadCountAsync(SupabaseSession session, string userId = null) =>
            GetInboxCountValueAsync(session, c => c.Unread);

        /// <summary>미수령 보상이 있는 메일 개수. <paramref name="userId"/>는 계약 호환용(무시).</summary>
        public Task<SupabaseResult<int>> GetUnclaimedItemMailCountAsync(string userId = null) =>
            GetInboxCountValueAsync(_sessionGetter?.Invoke(), c => c.UnclaimedMails);

        public Task<SupabaseResult<int>> GetUnclaimedItemMailCountAsync(SupabaseSession session, string userId = null) =>
            GetInboxCountValueAsync(session, c => c.UnclaimedMails);

        private async Task<SupabaseResult<int>> GetInboxCountValueAsync(
            SupabaseSession session,
            Func<MailInboxCounts, int> selector)
        {
            var token = RequireToken(session);
            if (token == null)
                return SupabaseResult<int>.Fail("auth_not_signed_in");

            var r = await _mailbox.GetInboxCountsAsync(token);
            if (!r.IsSuccess)
                return SupabaseResult<int>.Fail(r.ErrorMessage ?? "inbox_counts_failed");

            return SupabaseResult<int>.Success(selector(r.Data));
        }

        /// <summary>
        /// 우편 1건의 첨부 보상을 수령합니다.
        /// RPC 호출 전에 모든 아이템 key의 핸들러가 <see cref="MailItemHandlerRegistry"/>에 등록돼 있는지 먼저 검증합니다
        /// (수령 처리 후 핸들러 누락으로 아이템이 유실되는 것을 방지).
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

            var detail = await _mailbox.GetMailByIdAsync(token, mailId);
            if (!detail.IsSuccess)
                return SupabaseResult<IReadOnlyList<ClaimResult>>.Fail(detail.ErrorMessage ?? "mail_load_failed");

            var mail = detail.Data;
            if (mail == null)
                return SupabaseResult<IReadOnlyList<ClaimResult>>.Fail("mail_not_found");

            if (mail.IsExpired)
                return SupabaseResult<IReadOnlyList<ClaimResult>>.Fail("mail_expired");

            if (mail.HasUnclaimedItems)
            {
                var pre = ValidateHandlers(mail.Items);
                if (!pre.IsSuccess)
                    return pre;
            }

            var rpc = await _mailbox.ClaimMailItemsRpcAsync(token, mailId);
            if (!rpc.IsSuccess)
                return SupabaseResult<IReadOnlyList<ClaimResult>>.Fail(rpc.ErrorMessage ?? "claim_rpc_failed");

            return await RunHandlersAsync(mailId, rpc.Data);
        }

        /// <summary>
        /// 미수령 보상이 있는 모든 우편의 보상을 일괄 수령합니다.
        /// RPC 호출 전에 모든 아이템 key의 핸들러 등록 여부를 먼저 검증합니다.
        /// </summary>
        public Task<SupabaseResult<IReadOnlyList<ClaimResult>>> ClaimAllMailItemsAsync() =>
            ClaimAllMailItemsAsync(_sessionGetter?.Invoke());

        public async Task<SupabaseResult<IReadOnlyList<ClaimResult>>> ClaimAllMailItemsAsync(SupabaseSession session)
        {
            var token = RequireToken(session);
            if (token == null)
                return SupabaseResult<IReadOnlyList<ClaimResult>>.Fail("auth_not_signed_in");

            var list = await _mailbox.GetMailsAsync(token, limit: 200, offset: 0);
            if (!list.IsSuccess)
                return SupabaseResult<IReadOnlyList<ClaimResult>>.Fail(list.ErrorMessage ?? "mail_list_failed");

            foreach (var m in list.Data ?? Array.Empty<Mail>())
            {
                if (!m.HasUnclaimedItems)
                    continue;

                var pre = ValidateHandlers(m.Items);
                if (!pre.IsSuccess)
                    return pre;
            }

            var rpc = await _mailbox.ClaimAllMailItemsRpcAsync(token);
            if (!rpc.IsSuccess)
                return SupabaseResult<IReadOnlyList<ClaimResult>>.Fail(rpc.ErrorMessage ?? "claim_all_rpc_failed");

            var aggregated = new List<ClaimResult>();
            foreach (var bundle in rpc.Data ?? Array.Empty<MailClaimBundle>())
            {
                if (string.IsNullOrWhiteSpace(bundle.MailId))
                    continue;

                var part = await RunHandlersAsync(bundle.MailId, bundle.Items);
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

        /// <summary>읽은 우편을 일괄 삭제합니다. 성공 시 삭제된 개수를 반환합니다.</summary>
        public Task<SupabaseResult<int>> DeleteReadMailsAsync() =>
            DeleteReadMailsAsync(_sessionGetter?.Invoke());

        public async Task<SupabaseResult<int>> DeleteReadMailsAsync(SupabaseSession session)
        {
            var token = RequireToken(session);
            if (token == null)
                return SupabaseResult<int>.Fail("auth_not_signed_in");

            return await _mailbox.DeleteReadMailsForUserRpcAsync(token);
        }

        /// <summary>세션에서 액세스 토큰을 추출합니다. 세션이 null이거나 토큰이 비어 있으면 null.</summary>
        private static string RequireToken(SupabaseSession session)
        {
            if (session == null || string.IsNullOrWhiteSpace(session.AccessToken))
                return null;

            return session.AccessToken;
        }

        /// <summary>
        /// 모든 아이템 key의 핸들러가 <see cref="MailItemHandlerRegistry"/>에 등록돼 있는지 검증합니다.
        /// RPC로 수령 처리하기 전에 호출해, 핸들러 누락으로 이미 수령 처리된 아이템이 유실되는 것을 막습니다.
        /// </summary>
        /// <param name="items">검증할 첨부 아이템 목록. null 또는 빈 목록이면 성공으로 간주합니다.</param>
        private static SupabaseResult<IReadOnlyList<ClaimResult>> ValidateHandlers(IReadOnlyList<MailItemPayload> items)
        {
            if (items == null || items.Count == 0)
                return SupabaseResult<IReadOnlyList<ClaimResult>>.Success(Array.Empty<ClaimResult>());

            foreach (var it in items)
            {
                if (it == null || string.IsNullOrWhiteSpace(it.Key))
                    return SupabaseResult<IReadOnlyList<ClaimResult>>.Fail("mail_item_key_empty");

                if (!MailItemHandlerRegistry.TryGetHandler(it.Key, out _))
                    return SupabaseResult<IReadOnlyList<ClaimResult>>.Fail("mail_item_handler_missing:" + it.Key);
            }

            return SupabaseResult<IReadOnlyList<ClaimResult>>.Success(Array.Empty<ClaimResult>());
        }

        /// <summary>
        /// RPC 응답의 아이템들을 <c>Index</c> 오름차순으로 핸들러에 전달해 지급 처리합니다.
        /// 중간에 하나라도 실패하면 즉시 실패를 반환합니다(이후 아이템은 처리하지 않음).
        /// </summary>
        /// <param name="mailId">핸들러에 전달할 우편 ID.</param>
        /// <param name="lines">지급할 아이템 목록. null 또는 빈 목록이면 빈 결과로 성공합니다.</param>
        private static async Task<SupabaseResult<IReadOnlyList<ClaimResult>>> RunHandlersAsync(
            string mailId,
            IReadOnlyList<MailItemPayload> lines)
        {
            if (lines == null || lines.Count == 0)
                return SupabaseResult<IReadOnlyList<ClaimResult>>.Success(Array.Empty<ClaimResult>());

            var ordered = lines.OrderBy(x => x.Index).ToList();
            var results = new List<ClaimResult>();

            foreach (var line in ordered)
            {
                if (line == null || string.IsNullOrWhiteSpace(line.Key))
                    return SupabaseResult<IReadOnlyList<ClaimResult>>.Fail("mail_item_key_empty");

                if (!MailItemHandlerRegistry.TryGetHandler(line.Key, out var handler))
                    return SupabaseResult<IReadOnlyList<ClaimResult>>.Fail("mail_item_handler_missing:" + line.Key);

                var r = await handler.HandleAsync(mailId, line.Index, line.Key, line.Count);
                if (!r.IsSuccess)
                    return SupabaseResult<IReadOnlyList<ClaimResult>>.Fail(r.ErrorMessage ?? "mail_handler_failed");

                if (r.Data != null)
                    results.Add(r.Data);
            }

            return SupabaseResult<IReadOnlyList<ClaimResult>>.Success(results);
        }
    }
}
