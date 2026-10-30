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
            var r = await SupabaseRestHelpers.CallRpcAsync(_httpClient, _supabaseUrl, _publishableKey, accessToken, "ts_push_register", body);
            return r.IsSuccess ? SupabaseResult.Ok : SupabaseResult.Fail(r.ErrorCode);
        }

        /// <summary>내 기기 토큰을 해제합니다(로그아웃 때).</summary>
        public async Task<SupabaseResult> UnregisterAsync(string accessToken, string token)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
                return SupabaseResult.Fail(SupabaseErrorCode.NotSignedIn);
            var body = JsonConvert.SerializeObject(new Dictionary<string, object> { ["p_token"] = token });
            var r = await SupabaseRestHelpers.CallRpcAsync(_httpClient, _supabaseUrl, _publishableKey, accessToken, "ts_push_unregister", body);
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
