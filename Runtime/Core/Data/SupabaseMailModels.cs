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

    /// <summary>메일 <c>localized</c> jsonb의 언어별 제목·본문.</summary>
    public sealed class MailLocalizedText
    {
        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("content")]
        public string Content { get; set; }
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

        /// <summary>분류(파티션 키). 지정 없이 발송된 메일은 <c>default</c>.</summary>
        public string Category { get; set; }

        /// <summary>
        /// 언어별 제목·본문 오버라이드(언어코드 → 텍스트). 발송 시 지정된 언어만 채워지며,
        /// 없는 언어는 <see cref="Title"/>·<see cref="Content"/>로 fallback. null일 수 있음.
        /// </summary>
        public IReadOnlyDictionary<string, MailLocalizedText> Localized { get; set; }

        /// <summary>지정한 언어의 제목을 반환합니다. 해당 언어가 없으면 기본 <see cref="Title"/>.</summary>
        /// <param name="lang">언어코드(예: <c>"ja"</c>, <c>"en"</c>). 게임이 명시적으로 지정합니다.</param>
        public string TitleFor(string lang)
        {
            if (!string.IsNullOrEmpty(lang) && Localized != null
                && Localized.TryGetValue(lang, out var t)
                && t != null && !string.IsNullOrEmpty(t.Title))
                return t.Title;
            return Title;
        }

        /// <summary>지정한 언어의 본문을 반환합니다. 해당 언어가 없으면 기본 <see cref="Content"/>.</summary>
        /// <param name="lang">언어코드(예: <c>"ja"</c>, <c>"en"</c>). 게임이 명시적으로 지정합니다.</param>
        public string ContentFor(string lang)
        {
            if (!string.IsNullOrEmpty(lang) && Localized != null
                && Localized.TryGetValue(lang, out var t)
                && t != null && !string.IsNullOrEmpty(t.Content))
                return t.Content;
            return Content;
        }

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
        /// <summary>이 핸들러가 처리하는 아이템 키(<c>items[].key</c>와 일치).</summary>
        string ItemKey { get; }

        /// <summary>수령 확정된 아이템 1건을 게임에 지급합니다.</summary>
        /// <param name="mailId">보상이 들어 있던 메일 UUID.</param>
        /// <param name="itemIndex"><c>items</c> 배열 내 위치(0-based).</param>
        /// <param name="itemKey">아이템 키.</param>
        /// <param name="count">지급 수량.</param>
        Task<Common.SupabaseResult<ClaimResult>> HandleAsync(
            string mailId,
            int itemIndex,
            string itemKey,
            int count);
    }

    /// <summary>한 분류의 미읽음·미수령 보상 메일 개수. <see cref="MailInboxCounts.ByCategory"/> 값.</summary>
    public sealed class MailCategoryCounts
    {
        [JsonProperty("unread")]
        public int Unread { get; set; }

        [JsonProperty("unclaimed_mails")]
        public int UnclaimedMails { get; set; }
    }

    /// <summary><c>ts_mail_inbox_counts</c> 응답. 최상위는 전체 집계, <see cref="ByCategory"/>는 분류별 세부.</summary>
    public sealed class MailInboxCounts
    {
        [JsonProperty("unread")]
        public int Unread { get; set; }

        [JsonProperty("unclaimed_mails")]
        public int UnclaimedMails { get; set; }

        /// <summary>분류별 세부 개수. 활성 메일이 있는 분류만 키로 존재.</summary>
        [JsonProperty("by_category")]
        public Dictionary<string, MailCategoryCounts> ByCategory { get; set; } =
            new Dictionary<string, MailCategoryCounts>();
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
