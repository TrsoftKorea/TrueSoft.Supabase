using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace TrueBase.Core.Data
{
    /// <summary>우편 <c>items</c> JSON 배열 원소 (<c>key</c>, <c>count</c>).</summary>
    public sealed class MailItemPayload
    {
        [JsonProperty("key")]
        public string Key { get; set; }

        [JsonProperty("count")]
        public int Count { get; set; }

        /// <summary>배열 순서(0-based). DB에 없고 클라이언트/수령 RPC 응답에서 채움.</summary>
        [JsonIgnore]
        public int Index { get; set; }
    }

    /// <summary>플레이어 우편함에 표시할 메일 모델.</summary>
    public sealed class Mail
    {
        public string Id { get; set; }
        public string AccountId { get; set; }
        public string UserId { get; set; }
        public string SenderType { get; set; }
        public string SenderName { get; set; }
        public string Title { get; set; }
        public string Content { get; set; }
        public bool IsRead { get; set; }
        public DateTime ExpiresAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ItemsClaimedAt { get; set; }
        public IReadOnlyList<MailItemPayload> Items { get; set; }

        public bool IsExpired => DateTime.UtcNow > ExpiresAt;

        public bool HasUnclaimedItems =>
            ItemsClaimedAt == null
            && Items != null
            && Items.Count > 0
            && !IsExpired;

        public bool CanDeleteFromInbox => !HasUnclaimedItems;
    }

    /// <summary>메일 보상 수령 핸들러가 반환하는 한 줄 결과(로그·UI용).</summary>
    public sealed class ClaimResult
    {
        public string MailId { get; set; }
        public int ItemIndex { get; set; }
        public string ItemKey { get; set; }
        public int Count { get; set; }
    }

    /// <summary>
    /// <c>items</c>의 <c>key</c>마다 게임 쪽 지급 로직을 등록합니다.
    /// 수령 RPC 성공 후 클라이언트에서 순서대로 호출됩니다.
    /// </summary>
    public interface IMailItemHandler
    {
        string ItemKey { get; }

        Task<Common.SupabaseResult<ClaimResult>> HandleAsync(
            string mailId,
            int itemIndex,
            string itemKey,
            int count);
    }

    /// <summary><c>ts_mail_inbox_counts</c> 응답.</summary>
    public sealed class MailInboxCounts
    {
        [JsonProperty("unread")]
        public int Unread { get; set; }

        [JsonProperty("unclaimed_mails")]
        public int UnclaimedMails { get; set; }
    }

    /// <summary><c>ts_claim_all_mail_items</c> 한 메일 분.</summary>
    public sealed class MailClaimBundle
    {
        public string MailId { get; set; }
        public IReadOnlyList<MailItemPayload> Items { get; set; }
    }

    /// <summary>전역 핸들러 레지스트리(게임 시작 시 <see cref="Register"/>).</summary>
    public static class MailItemHandlerRegistry
    {
        private static readonly Dictionary<string, IMailItemHandler> Map =
            new Dictionary<string, IMailItemHandler>(StringComparer.Ordinal);

        private static readonly object Gate = new object();

        public static void Register(IMailItemHandler handler)
        {
            if (handler == null || string.IsNullOrWhiteSpace(handler.ItemKey))
                return;

            lock (Gate)
            {
                Map[handler.ItemKey.Trim()] = handler;
            }
        }

        public static void Unregister(string itemKey)
        {
            if (string.IsNullOrWhiteSpace(itemKey))
                return;

            lock (Gate)
            {
                Map.Remove(itemKey.Trim());
            }
        }

        public static void Clear()
        {
            lock (Gate)
            {
                Map.Clear();
            }
        }

        public static bool TryGetHandler(string itemKey, out IMailItemHandler handler)
        {
            handler = null;
            if (string.IsNullOrWhiteSpace(itemKey))
                return false;

            lock (Gate)
            {
                return Map.TryGetValue(itemKey.Trim(), out handler);
            }
        }
    }
}
