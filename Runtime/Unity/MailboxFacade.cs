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

        public MailboxFacade(SupabaseMailboxService mailbox, Func<SupabaseSession> sessionGetter = null)
        {
            _mailbox = mailbox ?? throw new ArgumentNullException(nameof(mailbox));
            _sessionGetter = sessionGetter;
        }

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

        public Task<SupabaseResult<bool>> DeleteMailAsync(string mailId) =>
            DeleteMailAsync(_sessionGetter?.Invoke(), mailId);

        public async Task<SupabaseResult<bool>> DeleteMailAsync(SupabaseSession session, string mailId)
        {
            var token = RequireToken(session);
            if (token == null)
                return SupabaseResult<bool>.Fail("auth_not_signed_in");

            return await _mailbox.DeleteMailForUserRpcAsync(token, mailId);
        }

        public Task<SupabaseResult<int>> DeleteReadMailsAsync() =>
            DeleteReadMailsAsync(_sessionGetter?.Invoke());

        public async Task<SupabaseResult<int>> DeleteReadMailsAsync(SupabaseSession session)
        {
            var token = RequireToken(session);
            if (token == null)
                return SupabaseResult<int>.Fail("auth_not_signed_in");

            return await _mailbox.DeleteReadMailsForUserRpcAsync(token);
        }

        private static string RequireToken(SupabaseSession session)
        {
            if (session == null || string.IsNullOrWhiteSpace(session.AccessToken))
                return null;

            return session.AccessToken;
        }

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
