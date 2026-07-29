using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TrueBase.Core.Auth;
using TrueBase.Core.Common;
using TrueBase.Core.Data;

namespace TrueBase.Unity
{
    /// <summary>
    /// 채팅 구독 하나. 채널별 커서를 들고 주기적으로 새 메시지를 받아 콜백으로 넘깁니다.
    /// 채팅창을 닫을 때 <see cref="Dispose"/>를 부르면 폴링이 멈춥니다.
    /// </summary>
    public sealed class ChatSubscription : IDisposable
    {
        private readonly ChatFacade _owner;
        private readonly Dictionary<string, long> _cursors;
        private readonly Action<IReadOnlyList<ChatMessage>> _onMessages;
        private readonly List<ChatMessage> _batch = new List<ChatMessage>();

        private readonly float _minInterval;
        private readonly float _maxInterval;

        private float _interval;
        private float _nextDueTime;

        internal ChatSubscription(
            ChatFacade owner,
            IEnumerable<string> channelCodes,
            Action<IReadOnlyList<ChatMessage>> onMessages,
            float minIntervalSeconds,
            float maxIntervalSeconds)
        {
            _owner = owner;
            _onMessages = onMessages;
            _minInterval = Math.Max(0.5f, minIntervalSeconds);
            _maxInterval = Math.Max(_minInterval, maxIntervalSeconds);
            _interval = _minInterval;
            _nextDueTime = float.MinValue;

            _cursors = new Dictionary<string, long>();
            foreach (var code in channelCodes)
            {
                if (string.IsNullOrWhiteSpace(code))
                    continue;

                _cursors[code.Trim()] = 0L;
            }
        }

        /// <summary>구독 중인 채널 코드.</summary>
        public IReadOnlyCollection<string> ChannelCodes => _cursors.Keys;

        /// <summary>다음 조회까지의 현재 간격(초). 대화가 없으면 늘어납니다.</summary>
        public float CurrentIntervalSeconds => _interval;

        public bool IsDisposed { get; private set; }

        /// <summary>
        /// 채널의 커서를 되감아 다음 조회에서 최근 메시지를 다시 받게 합니다.
        /// 채팅창을 다시 열 때 지난 대화를 채우는 용도입니다.
        /// </summary>
        public void Reload()
        {
            var codes = new List<string>(_cursors.Keys);
            foreach (var code in codes)
                _cursors[code] = 0L;

            _interval = _minInterval;
            _nextDueTime = float.MinValue;
        }

        public void Dispose()
        {
            if (IsDisposed)
                return;

            IsDisposed = true;
            _owner.Remove(this);
        }

        internal bool IsDue(float now) => !IsDisposed && now >= _nextDueTime;

        internal IReadOnlyDictionary<string, long> Cursors => _cursors;

        /// <summary>
        /// 조회 결과를 반영하고 다음 조회 시각을 정합니다.
        /// 채널이 여럿이어도 한 번만, 시간순으로 합쳐 넘깁니다 — 게임이 다시 정렬할 일이 없도록.
        /// 새 메시지가 없으면 간격을 늘려 빈 응답 폴링을 줄이고, 오면 최소 간격으로 되돌립니다.
        /// </summary>
        internal void Apply(float now, IReadOnlyDictionary<string, IReadOnlyList<ChatMessage>> byChannel)
        {
            _batch.Clear();

            if (byChannel != null)
            {
                foreach (var pair in byChannel)
                {
                    var list = pair.Value;
                    if (list == null || list.Count == 0)
                        continue;

                    // 서버가 오래된 순으로 주므로 마지막 것이 가장 큰 id다.
                    var last = list[list.Count - 1].Id;
                    if (_cursors.TryGetValue(pair.Key, out var prev) && last > prev)
                        _cursors[pair.Key] = last;

                    _batch.AddRange(list);
                }
            }

            if (_batch.Count > 0)
            {
                // id 는 테이블 전체에서 하나뿐인 일련번호라, 채널이 달라도 이 순서가 곧 시간순이다.
                _batch.Sort((a, b) => a.Id.CompareTo(b.Id));

                try
                {
                    _onMessages?.Invoke(_batch);
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogError($"[Supabase.Chat] 메시지 콜백에서 예외: {e}");
                }
            }

            _interval = _batch.Count > 0 ? _minInterval : Math.Min(_maxInterval, _interval * 1.5f);
            _nextDueTime = now + _interval;
        }

        /// <summary>조회에 실패했을 때. 간격을 늘려 실패를 반복해서 때리지 않게 합니다.</summary>
        internal void ApplyFailure(float now)
        {
            _interval = Math.Min(_maxInterval, Math.Max(_minInterval, _interval * 2f));
            _nextDueTime = now + _interval;
        }
    }

    /// <summary>
    /// 로그인 세션을 사용하는 채팅 API.
    /// 채널 생성·삭제, 메시지 숨김, 채팅 차단은 운영(Retool) 전용이라 여기에 없습니다.
    /// </summary>
    public sealed class ChatFacade
    {
        private readonly SupabaseChatService _chat;
        private readonly Func<SupabaseSession> _sessionGetter;

        private readonly List<ChatSubscription> _subscriptions = new List<ChatSubscription>();
        private IReadOnlyList<ChatChannelInfo> _channelCache;

        /// <param name="chat">REST 호출을 수행할 서비스. null이면 예외.</param>
        /// <param name="sessionGetter">현재 세션 제공자. null이면 세션 없는 오버로드는 <c>auth_not_signed_in</c>으로 실패합니다.</param>
        public ChatFacade(SupabaseChatService chat, Func<SupabaseSession> sessionGetter = null)
        {
            _chat = chat ?? throw new ArgumentNullException(nameof(chat));
            _sessionGetter = sessionGetter;
        }

        /// <summary>
        /// 채널 목록. 운영자가 정하는 정적인 값이라 한 번 받아 캐시합니다.
        /// 운영에서 채널을 바꾼 뒤 반영하려면 <paramref name="forceRefresh"/>를 켜세요.
        /// </summary>
        public async Task<SupabaseResult<IReadOnlyList<ChatChannelInfo>>> GetChannelsAsync(bool forceRefresh = false)
        {
            if (!forceRefresh && _channelCache != null)
                return SupabaseResult<IReadOnlyList<ChatChannelInfo>>.Success(_channelCache);

            var token = RequireToken(_sessionGetter?.Invoke());
            if (token == null)
                return SupabaseResult<IReadOnlyList<ChatChannelInfo>>.Fail(SupabaseErrorCode.NotSignedIn);

            var r = await _chat.GetChannelsAsync(token);
            if (r.IsSuccess)
                _channelCache = r.Data;

            return r;
        }

        /// <summary>메시지를 보냅니다.</summary>
        public async Task<SupabaseResult<ChatSendResult>> SendAsync(string channelCode, string content)
        {
            var token = RequireToken(_sessionGetter?.Invoke());
            if (token == null)
                return SupabaseResult<ChatSendResult>.Fail(SupabaseErrorCode.NotSignedIn);

            return await _chat.SendAsync(token, channelCode, content);
        }

        /// <summary>
        /// 채널들을 구독해 새 메시지를 콜백으로 받습니다. 채팅창을 닫을 때 Dispose 하세요.
        /// 대화가 없으면 조회 간격이 <paramref name="maxIntervalSeconds"/>까지 늘어납니다.
        /// </summary>
        public SupabaseResult<ChatSubscription> Subscribe(
            IEnumerable<string> channelCodes,
            Action<IReadOnlyList<ChatMessage>> onMessages,
            float minIntervalSeconds = 2f,
            float maxIntervalSeconds = 10f)
        {
            if (channelCodes == null)
                return SupabaseResult<ChatSubscription>.Fail(SupabaseErrorCode.ChatChannelsEmpty);

            var sub = new ChatSubscription(this, channelCodes, onMessages, minIntervalSeconds, maxIntervalSeconds);
            if (sub.Cursors.Count == 0)
                return SupabaseResult<ChatSubscription>.Fail(SupabaseErrorCode.ChatChannelsEmpty);

            _subscriptions.Add(sub);
            return SupabaseResult<ChatSubscription>.Success(sub);
        }

        internal void Remove(ChatSubscription sub) => _subscriptions.Remove(sub);

        /// <summary>
        /// 만기된 구독만 조회합니다. <see cref="SupabaseRuntime.Update"/> 등에서 호출하세요.
        /// </summary>
        public async Task TickAsync(float realtimeSinceStartup)
        {
            if (_subscriptions.Count == 0)
                return;

            var token = RequireToken(_sessionGetter?.Invoke());
            if (token == null)
                return;

            // 콜백 안에서 Dispose 할 수 있으므로 복사본을 돈다.
            var snapshot = _subscriptions.ToArray();
            foreach (var sub in snapshot)
            {
                if (!sub.IsDue(realtimeSinceStartup))
                    continue;

                var r = await _chat.FetchManyAsync(token, sub.Cursors);
                if (sub.IsDisposed)
                    continue;

                if (r.IsSuccess)
                    sub.Apply(realtimeSinceStartup, r.Data);
                else
                    sub.ApplyFailure(realtimeSinceStartup);
            }
        }

        /// <summary>로그아웃·계정 전환 시 구독과 캐시를 비웁니다.</summary>
        internal void Reset()
        {
            _subscriptions.Clear();
            _channelCache = null;
        }

        /// <summary>세션에서 액세스 토큰을 추출합니다. 세션이 null이거나 토큰이 비어 있으면 null.</summary>
        private static string RequireToken(SupabaseSession session)
        {
            if (session == null || string.IsNullOrWhiteSpace(session.AccessToken))
                return null;

            return session.AccessToken;
        }
    }
}
