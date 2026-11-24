using System;
using System.Collections.Generic;
using System.Linq;
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

        /// <summary>받기 응답에서만 채운다 — 서버가 세이브 칸에 이미 더했으면 true(게임 처리기를 부르지 않는다).</summary>
        [JsonIgnore]
        internal bool GrantedByServer { get; set; }
    }

    /// <summary>
    /// 우편 다국어 글의 언어 고르기. 찾는 순서는 푸시(push-send 의 pick)와 같다 — 같은 코드 → 앞 두 글자 → 앞 두 글자가 같은 코드.
    /// 기기 언어와 게임이 넘긴 코드 정리는 Unity 계층이 꽂는다(Core 는 기기 언어를 모른다).
    /// </summary>
    internal static class MailLanguage
    {
        /// <summary>기기 언어 코드. 지원하지 않는 언어면 null → 기본 언어 글.</summary>
        internal static string Device;

        /// <summary>게임이 넘긴 값("Korean"·"zh_Hant" 등)을 코드로. 못 읽으면 null(넘긴 값 그대로 찾는다).</summary>
        internal static Func<string, string> Normalize = raw => null;

        internal static T Pick<T>(IReadOnlyDictionary<string, T> localized, string lang) where T : class
        {
            if (localized == null || localized.Count == 0) return null;
            var code = string.IsNullOrWhiteSpace(lang) ? Device : (Normalize(lang) ?? lang.Trim().Replace('_', '-'));
            if (string.IsNullOrEmpty(code)) return null;

            if (localized.TryGetValue(code, out var exact) && exact != null) return exact;

            // Title·Content 는 목록 화면이 매 프레임 읽을 수 있어 문자열을 잘라 만들지 않고 앞부분 길이로 비교한다.
            var headLen = HeadLength(code);
            T byCase = null, byHead = null, sameHead = null;
            void Visit(string key, T value)
            {
                if (value == null || key == null) return;
                if (byCase == null && string.Equals(key, code, StringComparison.OrdinalIgnoreCase)) byCase = value;
                if (HeadLength(key) != headLen || string.Compare(key, 0, code, 0, headLen, StringComparison.OrdinalIgnoreCase) != 0) return;
                if (key.Length == headLen) byHead ??= value;
                else sameHead ??= value;
            }
            // 인터페이스로 돌면 열거자가 상자에 담겨 할당이 생긴다 — 서비스가 채우는 Dictionary 는 그대로 돈다.
            if (localized is Dictionary<string, T> dict)
                foreach (var kv in dict) Visit(kv.Key, kv.Value);
            else
                foreach (var kv in localized) Visit(kv.Key, kv.Value);
            return byCase ?? byHead ?? sameHead;
        }

        private static int HeadLength(string code)
        {
            for (var i = 0; i < code.Length; i++)
                if (code[i] == '-' || code[i] == '_') return i;
            return code.Length;
        }
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

        /// <summary>기기 언어의 제목. 그 언어로 쓴 제목이 없으면 기본 언어 제목.</summary>
        public string Title => TitleFor(null);

        /// <summary>기기 언어의 본문. 그 언어로 쓴 본문이 없으면 기본 언어 본문.</summary>
        public string Content => ContentFor(null);

        /// <summary>기본 언어(운영 콘솔 "설정"에서 정한 언어)로 쓴 제목·본문 — 발송 화면의 위 칸.</summary>
        internal string BaseTitle { get; set; }
        internal string BaseContent { get; set; }

        public DateTimeOffset ExpiresAt { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? ItemsClaimedAt { get; set; }
        public IReadOnlyList<MailItemPayload> Items { get; set; }

        /// <summary>분류(파티션 키). 지정 없이 발송된 메일은 <c>default</c>.</summary>
        public string Category { get; set; }

        /// <summary>
        /// 기본 언어 밖의 언어별 제목·본문(언어코드 → 텍스트). 발송 때 추가한 언어만 채워진다. null일 수 있음.
        /// </summary>
        public IReadOnlyDictionary<string, MailLocalizedText> Localized { get; set; }

        /// <summary>지정한 언어의 제목. 그 언어로 쓴 제목이 없으면 기본 언어 제목.</summary>
        /// <param name="lang">언어코드(예: <c>"ja"</c>, <c>"zh-TW"</c>). 비우면 기기 언어.</param>
        public string TitleFor(string lang)
        {
            var t = MailLanguage.Pick(Localized, lang)?.Title;
            return string.IsNullOrEmpty(t) ? BaseTitle ?? string.Empty : t;
        }

        /// <summary>지정한 언어의 본문. 그 언어로 쓴 본문이 없으면 기본 언어 본문.</summary>
        /// <param name="lang">언어코드(예: <c>"ja"</c>, <c>"zh-TW"</c>). 비우면 기기 언어.</param>
        public string ContentFor(string lang)
        {
            var c = MailLanguage.Pick(Localized, lang)?.Content;
            return string.IsNullOrEmpty(c) ? BaseContent ?? string.Empty : c;
        }

        /// <summary>
        /// 기한이 지났는지. 만료 우편도 보관 기간(<see cref="MailSettings.ExpiredRetentionDays"/>) 동안 목록에 오므로
        /// 이 값으로 "기한 만료" 표시와 받기 버튼을 가른다. 서버 시각을 받아 둔 뒤면 서버 시각으로, 그 전엔 기기 시계로 판정한다.
        /// </summary>
        public bool IsExpired => MailClock.UtcNow() > ExpiresAt;

        /// <summary>수령(첨부 없는 텍스트는 열람, 보상은 수령) 완료 여부. <c>items_claimed_at</c>이 채워졌으면 true.</summary>
        public bool IsClaimed => ItemsClaimedAt != null;

        public bool HasUnclaimedItems =>
            ItemsClaimedAt == null
            && Items != null
            && Items.Count > 0
            && !IsExpired;

        public bool CanDeleteFromInbox => !HasUnclaimedItems;
    }

    /// <summary>
    /// <see cref="Mail.IsExpired"/> 가 쓰는 시계. Unity 계층이 서버 시각 기준점으로 바꿔 끼운다 —
    /// 기기 시계를 그대로 쓰면 시간을 바꾼 기기에서 만료 표시와 서버 판정(<c>mail_expired</c>)이 어긋난다.
    /// </summary>
    internal static class MailClock
    {
        internal static Func<DateTimeOffset> UtcNow = () => DateTimeOffset.UtcNow;
    }

    /// <summary>우편 설정(<c>ts_mail_client_settings</c>). 운영 콘솔 "우편 설정"에서 바꾼다.</summary>
    public sealed class MailSettings
    {
        /// <summary>기한이 지난 우편을 목록에 남겨 두는 일수. 지나면 목록에서 빠지고 서버가 지운다. 0이면 만료 즉시 빠진다.</summary>
        [JsonProperty("expired_retention_days")]
        public int ExpiredRetentionDays { get; set; }
    }

    /// <summary>받은 보상 한 줄(로그·UI용).</summary>
    public sealed class ClaimResult
    {
        public string MailId { get; set; }
        public int ItemIndex { get; set; }
        public string ItemKey { get; set; }
        public int Count { get; set; }
    }

    /// <summary>
    /// 우편 보상 키마다 지급 함수를 모아 <c>Supabase.RegisterMailItemHandlers</c>에 넘기는 목록입니다.
    /// <c>{ "키", 함수 }</c>로 적고, 함수는 지급 수량을 받습니다. 기다려야 하는 지급은 <c>async</c> 함수로 씁니다.
    /// </summary>
    public sealed class MailItemHandlers : IEnumerable<KeyValuePair<string, Func<int, Task>>>
    {
        private readonly List<KeyValuePair<string, Func<int, Task>>> _items = new List<KeyValuePair<string, Func<int, Task>>>();

        /// <summary>바로 끝나는 지급 함수를 더합니다.</summary>
        public void Add(string itemKey, Action<int> grant) =>
            _items.Add(new KeyValuePair<string, Func<int, Task>>(itemKey, grant == null ? null : (Func<int, Task>)(count =>
            {
                grant(count);
                return Task.CompletedTask;
            })));

        /// <summary>기다려야 하는 지급 함수를 더합니다.</summary>
        public void Add(string itemKey, Func<int, Task> grant) =>
            _items.Add(new KeyValuePair<string, Func<int, Task>>(itemKey, grant));

        IEnumerator<KeyValuePair<string, Func<int, Task>>> IEnumerable<KeyValuePair<string, Func<int, Task>>>.GetEnumerator() => _items.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _items.GetEnumerator();
    }

    /// <summary>한 분류의 미수령 메일 개수. <see cref="MailInboxCounts.ByCategory"/> 값.</summary>
    public sealed class MailCategoryCounts
    {
        [JsonProperty("unclaimed")]
        public int Unclaimed { get; set; }
    }

    /// <summary><c>ts_mail_inbox_counts</c> 응답. 최상위는 전체 집계, <see cref="ByCategory"/>는 분류별 세부.</summary>
    public sealed class MailInboxCounts
    {
        /// <summary>미수령 메일 수(items_claimed_at 미설정 — 미열람 텍스트 + 미수령 보상).</summary>
        [JsonProperty("unclaimed")]
        public int Unclaimed { get; set; }

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

    /// <summary>전역 지급 함수 레지스트리. 게임은 <c>Supabase.RegisterMailItemHandlers</c>로 등록한다.</summary>
    internal static class MailItemHandlerRegistry
    {
        private static readonly Dictionary<string, Func<int, Task>> Map =
            new Dictionary<string, Func<int, Task>>(StringComparer.Ordinal);

        private static readonly object Gate = new object();

        public static void Register(string itemKey, Func<int, Task> grant)
        {
            if (grant == null || string.IsNullOrWhiteSpace(itemKey))
                return;

            lock (Gate)
            {
                Map[itemKey.Trim()] = grant;
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

        public static bool TryGetHandler(string itemKey, out Func<int, Task> handler)
        {
            handler = null;
            if (string.IsNullOrWhiteSpace(itemKey))
                return false;

            lock (Gate)
            {
                return Map.TryGetValue(itemKey.Trim(), out handler);
            }
        }

        /// <summary>지금 등록된 처리기 키. 받기 요청에 실어, 서버가 이 키는 지급하지 않고 게임 처리기에 넘기게 한다.</summary>
        public static string[] Keys()
        {
            lock (Gate)
            {
                return Map.Keys.ToArray();
            }
        }
    }
}
