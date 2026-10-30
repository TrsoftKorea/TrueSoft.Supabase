using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TrueBase.Core.Common;
using TrueBase.Core.Http;
using TrueBase.Core.Models;

namespace TrueBase.Core.Data
{
    /// <summary>
    /// 플레이어의 푸시 수신 설정. 행이 없으면 알림 켬 · 광고 미동의 · 야간 미동의.
    /// 동의·철회 시각은 게임이 "처리 결과"를 알릴 때 쓴다(광고 수신 동의는 처리 결과를 알려야 한다).
    /// </summary>
    public sealed class SupabasePushPreferences
    {
        /// <summary>알림을 받는지. 끄면 아래 상황별 값과 상관없이 모든 푸시가 오지 않는다.</summary>
        public bool Enabled { get; set; }

        /// <summary>우편 도착 알림을 받는지.</summary>
        public bool Mail { get; set; }

        /// <summary>고객센터 답변 알림을 받는지.</summary>
        public bool SupportReply { get; set; }

        /// <summary>친구 요청 알림을 받는지.</summary>
        public bool FriendRequest { get; set; }

        /// <summary>매치 로비 초대 알림을 받는지.</summary>
        public bool LobbyInvite { get; set; }

        /// <summary>광고성 알림 수신 동의.</summary>
        public bool AdConsent { get; set; }

        /// <summary>광고 수신 동의·철회가 마지막으로 바뀐 시각. 한 번도 안 바꿨으면 null.</summary>
        public DateTimeOffset? AdConsentAt { get; set; }

        /// <summary>밤(21~8시) 광고성 알림 수신 동의. 광고 동의가 없으면 항상 false.</summary>
        public bool NightConsent { get; set; }

        /// <summary>야간 수신 동의·철회가 마지막으로 바뀐 시각. 한 번도 안 바꿨으면 null.</summary>
        public DateTimeOffset? NightConsentAt { get; set; }
    }

    /// <summary>푸시 알림이 어떤 상황에서 왔는지.</summary>
    public enum SupabasePushKind
    {
        /// <summary>SDK 가 모르는 상황 — 서버에 새 상황이 생겼는데 SDK 가 아직 옛 판일 때.</summary>
        Unknown,
        /// <summary>우편 도착.</summary>
        Mail,
        /// <summary>고객센터 답변.</summary>
        SupportReply,
        /// <summary>친구 요청.</summary>
        FriendRequest,
        /// <summary>매치 로비 초대.</summary>
        LobbyInvite
    }

    /// <summary>게임이 화면에 떠 있는 동안 온 푸시 알림. 기기에는 띄우지 않고 게임에만 넘긴다.</summary>
    public sealed class SupabasePushMessage
    {
        /// <summary>어떤 상황의 알림인지.</summary>
        public SupabasePushKind Kind { get; }

        /// <summary>알림 제목. 플레이어 기기 언어로 고른 문구.</summary>
        public string Title { get; }

        /// <summary>알림 본문.</summary>
        public string Body { get; }

        /// <summary>
        /// 상황별 값. 우편은 <c>batch_id</c>·<c>category</c>, 고객센터 답변은 <c>ticket_id</c>,
        /// 친구 요청은 <c>request_id</c>, 로비 초대는 <c>lobby_id</c>. 상황 이름은 <c>type</c>.
        /// </summary>
        public IReadOnlyDictionary<string, string> Data { get; }

        /// <summary>기기가 받은 시각(기기 시계, 유닉스 밀리초). 모르면 0.</summary>
        internal long ReceivedAtMs { get; }

        internal SupabasePushMessage(SupabasePushKind kind, string title, string body, IReadOnlyDictionary<string, string> data, long receivedAtMs = 0)
        {
            Kind = kind;
            Title = title ?? "";
            Body = body ?? "";
            Data = data;
            ReceivedAtMs = receivedAtMs;
        }

        /// <summary>기기 쪽이 넘긴 <c>{"title","body","data":{...}}</c> 를 읽는다. 꼴이 틀리면 null.</summary>
        internal static SupabasePushMessage FromNativeJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                var o = JObject.Parse(json);
                var data = new Dictionary<string, string>();
                if (o["data"] is JObject d)
                    foreach (var p in d.Properties())
                        data[p.Name] = p.Value.Type == JTokenType.String ? p.Value.Value<string>() : p.Value.ToString(Formatting.None);
                // 상황 이름은 대기열에 넣을 때 DB(ts_push_enqueue)가 type 으로 넣는다.
                data.TryGetValue("type", out var trigger);
                return new SupabasePushMessage(KindOf(trigger), o.Value<string>("title"), o.Value<string>("body"), data,
                                               o.Value<long?>("at") ?? 0);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        // 서버 push_triggers.code 와 같은 이름.
        private static SupabasePushKind KindOf(string trigger) => trigger switch
        {
            "mail"           => SupabasePushKind.Mail,
            "support_reply"  => SupabasePushKind.SupportReply,
            "friend_request" => SupabasePushKind.FriendRequest,
            "lobby_invite"   => SupabasePushKind.LobbyInvite,
            _                => SupabasePushKind.Unknown
        };
    }

    /// <summary>
    /// 푸시 알림 — 기기 토큰 등록·해제(RPC <c>ts_push_register</c>·<c>ts_push_unregister</c>)와
    /// 수신 설정(<c>ts_push_preferences_get</c>·<c>ts_push_preferences_set</c>). 보내기는 서버가 한다.
    /// </summary>
    public sealed class SupabasePushService
    {
        // 서버 응답 꼴이 틀린 경우 — 카탈로그 밖 저수준 사유.
        private const string InvalidResponse = "push_invalid_response";

        private readonly string _supabaseUrl;
        private readonly string _publishableKey;
        private readonly ISupabaseHttpClient _httpClient;

        public SupabasePushService(string supabaseUrl, string publishableKey, ISupabaseHttpClient httpClient)
        {
            _supabaseUrl = (supabaseUrl ?? string.Empty).TrimEnd('/');
            _publishableKey = publishableKey ?? string.Empty;
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        /// <summary>내 기기 토큰을 등록합니다. <paramref name="platform"/> 은 <c>android</c>·<c>ios</c>.</summary>
        public async Task<SupabaseResult> RegisterAsync(string accessToken, string token, string platform, string language, string appVersion)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
                return SupabaseResult.Fail(SupabaseErrorCode.NotSignedIn);
            var body = JsonConvert.SerializeObject(new Dictionary<string, object>
            {
                ["p_token"] = token,
                ["p_platform"] = platform,
                ["p_language"] = language,
                ["p_app_version"] = appVersion,
            });
            // returns void — 성공해도 본문이 비어 온다.
            var r = await SupabaseRestHelpers.CallRpcAsync(_httpClient, _supabaseUrl, _publishableKey, accessToken, "ts_push_register", body, allowEmptyBody: true);
            return r.IsSuccess ? SupabaseResult.Ok : SupabaseResult.Fail(r.ErrorCode);
        }

        /// <summary>내 기기 토큰을 해제합니다(로그아웃 때).</summary>
        public async Task<SupabaseResult> UnregisterAsync(string accessToken, string token)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
                return SupabaseResult.Fail(SupabaseErrorCode.NotSignedIn);
            var body = JsonConvert.SerializeObject(new Dictionary<string, object> { ["p_token"] = token });
            var r = await SupabaseRestHelpers.CallRpcAsync(_httpClient, _supabaseUrl, _publishableKey, accessToken, "ts_push_unregister", body, allowEmptyBody: true);
            return r.IsSuccess ? SupabaseResult.Ok : SupabaseResult.Fail(r.ErrorCode);
        }

        /// <summary>내 수신 설정을 읽습니다.</summary>
        public async Task<SupabaseResult<SupabasePushPreferences>> GetPreferencesAsync(string accessToken)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
                return SupabaseResult<SupabasePushPreferences>.Fail(SupabaseErrorCode.NotSignedIn);
            var r = await SupabaseRestHelpers.CallRpcAsync(_httpClient, _supabaseUrl, _publishableKey, accessToken, "ts_push_preferences_get", "{}");
            return r.IsSuccess ? Parse(r.Data) : SupabaseResult<SupabasePushPreferences>.Fail(r.ErrorCode);
        }

        /// <summary>내 수신 설정을 바꿉니다. null 인 값은 그대로 둡니다. 광고 동의를 철회하면 야간 동의도 철회됩니다.</summary>
        public async Task<SupabaseResult<SupabasePushPreferences>> SetPreferencesAsync(
            string accessToken, bool? enabled, bool? adConsent, bool? nightConsent,
            bool? mail, bool? supportReply, bool? friendRequest, bool? lobbyInvite)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
                return SupabaseResult<SupabasePushPreferences>.Fail(SupabaseErrorCode.NotSignedIn);
            var body = JsonConvert.SerializeObject(new Dictionary<string, object>
            {
                ["p_enabled"] = enabled,
                ["p_ad_consent"] = adConsent,
                ["p_night_consent"] = nightConsent,
                ["p_mail"] = mail,
                ["p_support_reply"] = supportReply,
                ["p_friend_request"] = friendRequest,
                ["p_lobby_invite"] = lobbyInvite,
            });
            var r = await SupabaseRestHelpers.CallRpcAsync(_httpClient, _supabaseUrl, _publishableKey, accessToken, "ts_push_preferences_set", body);
            return r.IsSuccess ? Parse(r.Data) : SupabaseResult<SupabasePushPreferences>.Fail(r.ErrorCode);
        }

        private static SupabaseResult<SupabasePushPreferences> Parse(string json)
        {
            try
            {
                // 시각은 글자로 받아 직접 읽는다 — 기본 날짜 해석은 시간대를 기기 시간대로 바꿔 버린다.
                var o = JsonConvert.DeserializeObject<JObject>(json ?? "", new JsonSerializerSettings { DateParseHandling = DateParseHandling.None });
                if (o == null) return SupabaseResult<SupabasePushPreferences>.Fail(InvalidResponse);
                return SupabaseResult<SupabasePushPreferences>.Success(new SupabasePushPreferences
                {
                    Enabled = o.Value<bool?>("enabled") ?? true,
                    Mail = o.Value<bool?>("mail") ?? true,
                    SupportReply = o.Value<bool?>("support_reply") ?? true,
                    FriendRequest = o.Value<bool?>("friend_request") ?? true,
                    LobbyInvite = o.Value<bool?>("lobby_invite") ?? true,
                    AdConsent = o.Value<bool?>("ad_consent") ?? false,
                    AdConsentAt = Time(o.Value<string>("ad_consent_at")),
                    NightConsent = o.Value<bool?>("night_consent") ?? false,
                    NightConsentAt = Time(o.Value<string>("night_consent_at")),
                });
            }
            catch (JsonException)
            {
                return SupabaseResult<SupabasePushPreferences>.Fail(InvalidResponse);
            }
        }

        private static DateTimeOffset? Time(string s) =>
            DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t) ? t : (DateTimeOffset?)null;
    }
}
