using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using TrueBase.Core.Common;
using UnityEngine;

namespace TrueBase.Unity
{
    /// <summary>저장이 서버로 나간 이유. 저장 요약 로그의 칸입니다.</summary>
    internal enum UserSaveSendCause
    {
        AutoUrgent,
        AutoNormal,
        AutoLazy,
        /// <summary>게임이 부른 <c>SaveNowAsync</c>·<c>RequestSave</c>·<c>SaveIfChangedAsync</c>.</summary>
        Game,
        /// <summary>앱 전환, 로그아웃, 로드 직후 초기값, 플레이나누 동기화처럼 게임이 부르지 않았는데 SDK 가 보낸 것. 응답을 받은 뒤 세므로 앱 종료 때 보낸 저장은 대개 못 센다.</summary>
        Sdk
    }

    /// <summary>
    /// <see cref="StaticUserSave{TRow}"/> 인스턴스들의 자동 동기화 스케줄러.
    /// key별로 dirty 검사·쿨다운 타이머·flush 실행을 관리하며, <see cref="Tick"/>은 <c>SupabaseRuntime.Update</c>에서 매 프레임 호출됩니다.
    /// </summary>
    internal static class UserSaveStaticSyncRegistry
    {
        private sealed class Entry
        {
            public string Key;
            public Func<bool> HasDirty;
            /// <summary>throttle 캐시를 무시한 정확한 dirty 검사. null이면 <see cref="HasDirty"/>로 대체.</summary>
            public Func<bool> HasFreshDirty;
            /// <summary>인자는 즉시 저장을 요청한 쪽. 자동 저장이면 null(세이브가 지금 값의 우선순위로 가른다).</summary>
            public Func<UserSaveSendCause?, Task<SupabaseResult>> FlushAsync;
            public Action ResetLocalState;
            public bool IsInFlight;
            public bool RequestImmediateAfterInFlight;
            /// <summary>마지막 즉시 저장 요청의 출처. 전송 중에 받은 요청도 끝난 뒤 재전송 때 이 값을 쓴다.</summary>
            public UserSaveSendCause ImmediateCause = UserSaveSendCause.Game;
            /// <summary>flush가 완료될 때마다 1 증가. "내가 요청한 전송이 끝났는가" 판정에 사용.</summary>
            public long FlushCompletedCount;
            /// <summary>마지막 flush의 실패 사유. 성공했으면 null. 즉시 저장이 결과를 돌려줄 때 씁니다.</summary>
            public string LastFlushError;
            /// <summary><see cref="ResetAll"/> 횟수. 기다리는 사이 로그아웃·계정 교체가 있었는지 가릅니다.</summary>
            public long ResetCount;
            /// <summary>마지막 <see cref="ResetAll"/> 때 받은 실패 사유. 일반 로그아웃·계정 교체면 null입니다.</summary>
            public string LastResetReason;
            /// <summary>이 시각이 되면 보낸다. 0 이하면 기다리는 변경이 없다.</summary>
            public float NextAllowedAtRealtime;
            /// <summary>마지막 전송이 끝난 시각. 컬렉션 변경이 쿨타임 안에 다시 나가지 않게 합니다.</summary>
            public float LastFlushEndedAtRealtime = float.MinValue;
            /// <summary>null이면 전역 <see cref="_cooldownSeconds"/> 사용. non-null이면 dirty 우선순위별 쿨다운 반환.</summary>
            public Func<float> GetDirtyCooldown;
            /// <summary>연달아 실패한 횟수. 성공하면 0으로 돌아갑니다.</summary>
            public int ConsecutiveFailures;
            /// <summary>실패 뒤 자동 저장을 다시 시도해도 되는 시각. 값을 바꿔도 앞당겨지지 않습니다.</summary>
            public float RetryNotBeforeRealtime;
        }

        private static readonly Dictionary<string, Entry> Entries = new(StringComparer.Ordinal);
        private static float _cooldownSeconds = 5f;
        private static float _lastRealtime;

        private static float _urgentCooldown = 1f;
        private static float _normalCooldown = 5f;
        private static float _lazyCooldown   = 30f;

        // 실패 뒤 자동 저장을 쉬는 시간. 오프라인이나 없는 칸(400)이면 쉬지 않고 다시 보내 화면이 멈추고 요청이 쏟아진다.
        private const float FailureRetryBaseSeconds = 2f;
        private const float FailureRetryMaxSeconds  = 60f;

        // 저장 요약 로그. 저장마다 찍으면 콘솔이 덮이고, 어떤 칸이 바뀌었는지는 서버 기록(user_data_logs)에 있으므로
        // "왜 나갔는지"만 1분에 한 줄로 모은다. 그 시간에 저장이 없으면 찍지 않는다.
        private const float SendSummarySeconds = 60f;
        private static readonly int[] SendCounts = new int[(int)UserSaveSendCause.Sdk + 1];
        private static int _sendFailures;
        private static float _sendWindowStart = -1f;

        /// <summary>
        /// 모든 <see cref="StaticUserSave{TRow}"/> 인스턴스에 공통으로 적용되는
        /// 우선순위별 기본 쿨다운(초)을 설정합니다.
        /// 인스턴스별 <c>ConfigureCooldown</c> 오버라이드가 있으면 그 값이 우선합니다.
        /// </summary>
        public static void ConfigurePriorityCooldowns(float urgent, float normal, float lazy)
        {
            _urgentCooldown  = Mathf.Max(0f, urgent);
            _normalCooldown  = Mathf.Max(0f, normal);
            _lazyCooldown    = Mathf.Max(0f, lazy);
            _cooldownSeconds = _normalCooldown; // 레거시 fallback도 Normal과 동기화
        }

        /// <summary>우선순위 정수(0=Urgent, 1=Normal, 2=Lazy)에 해당하는 전역 기본 쿨다운(초)을 반환합니다.</summary>
        internal static float GetPriorityCooldown(int priority) => priority switch
        {
            0 => _urgentCooldown,
            2 => _lazyCooldown,
            _ => _normalCooldown
        };

        /// <summary>
        /// 세이브 항목을 등록합니다. 같은 key로 재등록하면 콜백만 교체되고 타이머 상태는 유지됩니다.
        /// </summary>
        /// <param name="key">항목 식별 key. 공백이면 무시됩니다(앞뒤 공백은 trim).</param>
        /// <param name="hasDirty">전송할 변경이 있는지 검사. null이면 등록 자체가 무시됩니다.</param>
        /// <param name="flushAsync">변경분 전송. null이면 등록 자체가 무시됩니다.</param>
        /// <param name="resetLocalState"><see cref="ResetAll"/> 시 로컬 상태 초기화. null 허용.</param>
        /// <param name="getDirtyCooldown">dirty 우선순위별 쿨다운(초) 반환. null이면 전역 단일 쿨다운을 사용합니다.</param>
        /// <param name="hasFreshDirty">throttle 캐시를 무시한 정확한 dirty 검사. null이면 <paramref name="hasDirty"/>를 사용합니다.</param>
        public static void Register(
            string key,
            Func<bool> hasDirty,
            Func<UserSaveSendCause?, Task<SupabaseResult>> flushAsync,
            Action resetLocalState = null,
            Func<float> getDirtyCooldown = null,
            Func<bool> hasFreshDirty = null)
        {
            if (string.IsNullOrWhiteSpace(key) || hasDirty == null || flushAsync == null)
                return;

            var id = key.Trim();
            if (Entries.TryGetValue(id, out var existing))
            {
                existing.HasDirty = hasDirty;
                existing.HasFreshDirty = hasFreshDirty;
                existing.FlushAsync = flushAsync;
                existing.ResetLocalState = resetLocalState;
                existing.GetDirtyCooldown = getDirtyCooldown;
                return;
            }

            Entries[id] = new Entry
            {
                Key = id,
                HasDirty = hasDirty,
                HasFreshDirty = hasFreshDirty,
                FlushAsync = flushAsync,
                ResetLocalState = resetLocalState,
                GetDirtyCooldown = getDirtyCooldown,
                NextAllowedAtRealtime = 0f
            };
        }

        /// <summary>
        /// 값 변경을 알리고 쿨다운 타이머를 재계산합니다. 타이머는 더 짧아지는 방향으로만 갱신됩니다
        /// (긴 쿨다운의 dirty가 이미 예약된 짧은 타이머를 늘리지 않도록).
        /// </summary>
        public static void MarkDirty(string key)
        {
            if (!TryGetEntry(key, out var entry))
                return;

            // 우선순위 기반 타이머 재계산
            if (entry.GetDirtyCooldown != null)
            {
                var now       = Time.realtimeSinceStartup;
                var waiting   = entry.NextAllowedAtRealtime > 0f;

                // 이미 쿨타임을 다 기다린 변경이 있다. 여기서 다시 미루면 매 프레임 바뀌는 값은
                // 이 코드가 Tick 보다 먼저 도는 경우 영영 안 나간다. 보내기는 Tick 이 한다.
                if (waiting && now >= entry.NextAllowedAtRealtime)
                    return;

                var candidate = now + entry.GetDirtyCooldown.Invoke();

                if (!waiting)
                    // 기다리는 변경이 없음 → 새로 시작
                    entry.NextAllowedAtRealtime = candidate;
                else if (candidate < entry.NextAllowedAtRealtime)
                    // 더 높은 우선순위(짧은 쿨다운) → 타이머 단축
                    entry.NextAllowedAtRealtime = candidate;
                // else: 낮은 우선순위(긴 쿨다운) → 기존 타이머 유지
            }

            TryStartFlush(entry, immediate: false);
        }

        /// <summary>
        /// 쿨다운을 무시하고 즉시 flush를 시작합니다(완료 대기 없음).
        /// <para>요청이 접수되면 true입니다. 이미 전송 중이면 완료 후 1회 재전송을 예약하고 true를 반환하므로,
        /// 같은 프레임에 여러 번 호출해도 안전합니다. 등록되지 않은 key만 false입니다.</para>
        /// </summary>
        public static bool RequestImmediateFlush(string key) => RequestImmediateFlush(key, UserSaveSendCause.Game);

        private static bool RequestImmediateFlush(string key, UserSaveSendCause cause)
        {
            if (!TryGetEntry(key, out var entry))
                return false;

            entry.ImmediateCause = cause;

            if (entry.IsInFlight)
            {
                entry.RequestImmediateAfterInFlight = true;
                return true;
            }

            return TryStartFlush(entry, immediate: true);
        }

        /// <summary>즉시 flush를 시작하고 그 전송이 끝날 때까지 대기합니다. 전송이 실패하면 그 사유를 돌려줍니다.</summary>
        /// <param name="timeoutMs">대기 최대 시간(밀리초). 250 미만은 250으로 보정되며, 초과 시 <c>UserSaveTimeout</c> 실패를 반환합니다.</param>
        public static async Task<SupabaseResult> RequestImmediateFlushAsync(string key, int timeoutMs = 5000)
        {
            if (!TryGetEntry(key, out var entry))
                return SupabaseResult.Fail(SupabaseErrorCode.UserSaveNotReady);

            var startCount = entry.FlushCompletedCount;
            var startResetCount = entry.ResetCount;
            _ = RequestImmediateFlush(key);
            return await WaitForSettledAsync(entry, startCount, startResetCount, timeoutMs);
        }

        /// <summary>등록된 모든 항목에 즉시 flush를 요청합니다(완료 대기 없음).</summary>
        public static void RequestImmediateFlushAll()
        {
            foreach (var pair in Entries)
                _ = RequestImmediateFlush(pair.Key, UserSaveSendCause.Sdk);
        }

        /// <summary>
        /// 등록된 모든 항목에 즉시 flush를 요청하고, 요청한 전송이 전부 끝날 때까지 대기합니다.
        /// <para>단건 대기와 같은 기준입니다 — 대기 중 새로 생긴 변경분까지 기다리지 않습니다.</para>
        /// </summary>
        /// <param name="timeoutMs">대기 최대 시간(밀리초). 250 미만은 250으로 보정되며, 초과하거나 전송이 실패하면 false를 반환합니다.</param>
        public static async Task<bool> RequestImmediateFlushAllAsync(int timeoutMs = 5000)
        {
            var startCounts = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var pair in Entries)
                startCounts[pair.Key] = pair.Value.FlushCompletedCount;

            foreach (var pair in Entries)
                _ = RequestImmediateFlush(pair.Key, UserSaveSendCause.Sdk);

            var deadline = DateTime.UtcNow.AddMilliseconds(Math.Max(250, timeoutMs));
            while (DateTime.UtcNow < deadline)
            {
                var allSettled = true;
                foreach (var pair in Entries)
                {
                    var entry = pair.Value;
                    if (entry.IsInFlight)
                    {
                        allSettled = false;
                        break;
                    }

                    var startCount = startCounts.TryGetValue(pair.Key, out var c) ? c : 0L;
                    if (entry.FlushCompletedCount <= startCount && SafeHasFreshDirty(entry))
                    {
                        allSettled = false;
                        break;
                    }
                }

                if (allSettled)
                {
                    foreach (var pair in Entries)
                    {
                        var startCount = startCounts.TryGetValue(pair.Key, out var c) ? c : 0L;
                        if (pair.Value.FlushCompletedCount > startCount && pair.Value.LastFlushError != null)
                            return false;
                    }

                    return true;
                }

                await Task.Delay(16);
            }

            return false;
        }

        /// <summary>쿨다운이 만료된 dirty 항목의 flush를 시작합니다. 매 프레임 호출됩니다.</summary>
        /// <param name="realtimeNow"><c>Time.realtimeSinceStartup</c> 값(초).</param>
        public static void Tick(float realtimeNow)
        {
            _lastRealtime = realtimeNow;

            foreach (var entry in Entries.Values)
            {
                if (entry.IsInFlight)
                    continue;

                if (realtimeNow < entry.RetryNotBeforeRealtime)
                    continue;

                if (!SafeHasDirty(entry))
                {
                    // 기다리던 변경을 게임이 직접 보냈다. 지난 시각을 남겨 두면 다음 변경이 쿨타임 없이 나간다.
                    if (entry.GetDirtyCooldown != null && entry.NextAllowedAtRealtime > 0f && realtimeNow >= entry.NextAllowedAtRealtime)
                        entry.NextAllowedAtRealtime = 0f;
                    continue;
                }

                // 컬렉션을 제자리에서 고치면 MarkDirty 가 안 불려 타이머가 없다. 마지막 전송에서 쿨타임이 지났으면 바로 보내고
                // (값 비교 주기만큼 이미 늦었다), 아니면 그때까지 기다린다. 실패 뒤 재시도는 RetryNotBeforeRealtime 이 정한다.
                if (entry.GetDirtyCooldown != null && entry.NextAllowedAtRealtime <= 0f && entry.ConsecutiveFailures == 0)
                {
                    var allowedAt = entry.LastFlushEndedAtRealtime + SafeGetDirtyCooldown(entry);
                    if (realtimeNow < allowedAt)
                    {
                        entry.NextAllowedAtRealtime = allowedAt;
                        continue;
                    }
                }

                if (realtimeNow < entry.NextAllowedAtRealtime)
                    continue;

                _ = StartFlushAsync(entry, immediate: false);
            }

            TickSendSummary(realtimeNow);
        }

        /// <summary>서버까지 실제로 나간 저장을 셉니다. 보낼 변경이 없어 요청을 안 한 경우에는 부르지 않습니다.</summary>
        internal static void RecordSend(UserSaveSendCause cause)
        {
            if (!SupabaseSDK.ApiResultLogsEnabled)
                return;

            SendCounts[(int)cause]++;
            OpenSendWindow();
        }

        /// <summary>실패한 저장을 셉니다. 서버에 남지 않았으므로 원인별 칸과 따로 셉니다.</summary>
        internal static void RecordSendFailure()
        {
            if (!SupabaseSDK.ApiResultLogsEnabled)
                return;

            _sendFailures++;
            OpenSendWindow();
        }

        private static void OpenSendWindow()
        {
            if (_sendWindowStart < 0f)
                _sendWindowStart = Time.realtimeSinceStartup;
        }

        private static void TickSendSummary(float realtimeNow)
        {
            if (_sendWindowStart < 0f || realtimeNow - _sendWindowStart < SendSummarySeconds)
                return;

            var total = 0;
            foreach (var count in SendCounts)
                total += count;

            // 앱이 내려가 있던 시간이 끼면 60초와 달라질 수 있다(플랫폼별 시계 동작은 확인 안 함). 횟수를 잘못 읽지 않게 잰 시간을 적는다.
            var message = $"{Mathf.RoundToInt(realtimeNow - _sendWindowStart)}초간 저장 {total}회"
                + $" · 자동 긴급 {SendCounts[(int)UserSaveSendCause.AutoUrgent]}"
                + $" · 자동 보통 {SendCounts[(int)UserSaveSendCause.AutoNormal]}"
                + $" · 자동 느림 {SendCounts[(int)UserSaveSendCause.AutoLazy]}"
                + $" · 게임 직접 호출 {SendCounts[(int)UserSaveSendCause.Game]}"
                + $" · SDK 내부 {SendCounts[(int)UserSaveSendCause.Sdk]}"
                + (_sendFailures > 0 ? $" · 실패 {_sendFailures}" : "")
                + $" (쿨타임 긴급 {FormatSeconds(_urgentCooldown)} · 보통 {FormatSeconds(_normalCooldown)} · 느림 {FormatSeconds(_lazyCooldown)})";

            Array.Clear(SendCounts, 0, SendCounts.Length);
            _sendFailures = 0;
            _sendWindowStart = -1f;

            SupabaseSDK.LogUserSaveSummary(message);
        }

        private static string FormatSeconds(float seconds) =>
            seconds.ToString("0.##", CultureInfo.InvariantCulture) + "초";

        /// <summary>모든 항목의 전송 상태·타이머를 초기화하고 <c>resetLocalState</c> 콜백을 호출합니다. 세션 해제 시 호출됩니다.</summary>
        /// <param name="reason">저장이 끝나길 기다리던 호출에 돌려줄 실패 사유. 비우면 <c>UserSaveFlushFailed</c>.</param>
        public static void ResetAll(string reason = null)
        {
            foreach (var entry in Entries.Values)
            {
                entry.IsInFlight = false;
                entry.RequestImmediateAfterInFlight = false;
                entry.NextAllowedAtRealtime = 0f;
                entry.LastFlushEndedAtRealtime = float.MinValue;
                entry.ConsecutiveFailures = 0;
                entry.RetryNotBeforeRealtime = 0f;
                entry.LastFlushError = null;
                entry.ResetCount++;
                entry.LastResetReason = reason;

                try
                {
                    entry.ResetLocalState?.Invoke();
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[Supabase] user save reset failed: " + e.Message);
                }
            }
        }

        private static bool TryGetEntry(string key, out Entry entry)
        {
            entry = null;
            if (string.IsNullOrWhiteSpace(key))
                return false;

            return Entries.TryGetValue(key.Trim(), out entry);
        }

        /// <summary>flush 시작을 시도합니다. 시작했으면 true.</summary>
        /// <param name="immediate">true면 쿨다운·dirty 사전 검사를 건너뛰고, 전송 중이면 완료 후 재전송을 예약합니다.</param>
        private static bool TryStartFlush(Entry entry, bool immediate)
        {
            if (entry == null)
                return false;

            if (entry.IsInFlight)
            {
                if (immediate)
                    entry.RequestImmediateAfterInFlight = true;
                return false;
            }

            // 즉시 flush는 throttle된 dirty 캐시를 우회하고 FlushAsync 내부의 신선한 검사에 맡깁니다.
            // (컬렉션 제자리 수정이 값 비교 throttle 창 안에서 유실되는 것을 방지)
            if (!immediate && !SafeHasDirty(entry))
                return false;

            var now = Time.realtimeSinceStartup;
            _lastRealtime = now;

            if (!immediate && (now < entry.NextAllowedAtRealtime || now < entry.RetryNotBeforeRealtime))
                return false;

            _ = StartFlushAsync(entry, immediate);
            return true;
        }

        private static async Task StartFlushAsync(Entry entry, bool immediate)
        {
            entry.IsInFlight = true;
            // 지금까지의 변경은 이 전송에 실린다. 보내는 사이 생긴 변경은 MarkDirty·전송 뒤 검사가 새로 잡는다.
            entry.NextAllowedAtRealtime = 0f;
            var resetCountAtStart = entry.ResetCount;
            var succeeded = false;
            string error = SupabaseErrorCode.UserSaveFlushFailed;

            try
            {
                var result = await entry.FlushAsync(immediate ? entry.ImmediateCause : (UserSaveSendCause?)null);
                succeeded = result != null && result.IsSuccess;
                error = succeeded ? null : result?.ErrorCode ?? SupabaseErrorCode.UserSaveFlushFailed;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Supabase] user save flush failed: " + e.Message);
            }
            finally
            {
                // 보내는 사이 초기화됐으면 이 결과는 옛 계정 몫이다. 적으면 새 계정의 전송 중 표시·실패 사유·재시도 시각을 덮는다.
                if (entry.ResetCount == resetCountAtStart)
                {
                    var now = Time.realtimeSinceStartup;
                    _lastRealtime = now;
                    entry.LastFlushEndedAtRealtime = now;

                    if (entry.GetDirtyCooldown != null)
                    {
                        // 우선순위 기반: dirty 없으면 타이머 리셋(대기 상태),
                        // dirty 남아있으면 MarkDirty가 이미 올바르게 설정한 타이머 유지.
                        // 보내는 사이 컬렉션만 바뀌었으면 타이머가 없다 — 없는 채로 두면 끝나자마자 다시 보낸다.
                        if (!SafeHasDirty(entry))
                            entry.NextAllowedAtRealtime = 0f;
                        else if (succeeded && entry.NextAllowedAtRealtime <= 0f)
                            entry.NextAllowedAtRealtime = now + SafeGetDirtyCooldown(entry);
                    }
                    else
                    {
                        // 전역 쿨다운(레거시): flush 후 항상 쿨다운 재설정
                        entry.NextAllowedAtRealtime = now + _cooldownSeconds;
                    }

                    if (succeeded)
                    {
                        entry.ConsecutiveFailures = 0;
                        entry.RetryNotBeforeRealtime = 0f;
                    }
                    else
                    {
                        entry.ConsecutiveFailures++;
                        entry.RetryNotBeforeRealtime = now + Mathf.Min(
                            FailureRetryMaxSeconds,
                            FailureRetryBaseSeconds * Mathf.Pow(2f, entry.ConsecutiveFailures - 1));
                    }

                    entry.LastFlushError = error;
                    entry.IsInFlight = false;
                    entry.FlushCompletedCount++;
                }
            }

            if (entry.ResetCount != resetCountAtStart)
                return;

            if (entry.RequestImmediateAfterInFlight)
            {
                entry.RequestImmediateAfterInFlight = false;
                // 예약된 재전송은 throttle 캐시를 믿지 않고 신선하게 검사한다.
                // (전송 중에 컬렉션이 제자리 수정된 경우 캐시가 false라 재전송이 취소될 수 있음)
                if (SafeHasFreshDirty(entry))
                    _ = StartFlushAsync(entry, immediate: true);
                return;
            }

            if (!immediate && succeeded && SafeHasDirty(entry) && _lastRealtime >= entry.NextAllowedAtRealtime)
                _ = StartFlushAsync(entry, immediate: false);
        }

        // 전송 마무리(finally) 안에서도 불린다. 여기서 예외가 나면 전송 중 표시가 안 풀려 그 세이브의 자동 저장이 멈춘다.
        private static float SafeGetDirtyCooldown(Entry entry)
        {
            try
            {
                return entry.GetDirtyCooldown();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Supabase] user save cooldown check failed: " + e.Message);
                return _normalCooldown;
            }
        }

        private static bool SafeHasDirty(Entry entry)
        {
            try
            {
                return entry.HasDirty != null && entry.HasDirty();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Supabase] user save dirty check failed: " + e.Message);
                return false;
            }
        }

        /// <summary>throttle 캐시를 무시한 정확한 dirty 검사. 등록되지 않았으면 <see cref="SafeHasDirty"/>로 대체합니다.</summary>
        private static bool SafeHasFreshDirty(Entry entry)
        {
            if (entry.HasFreshDirty == null)
                return SafeHasDirty(entry);

            try
            {
                return entry.HasFreshDirty();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Supabase] user save dirty check failed: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// 요청한 전송이 끝날 때까지 16ms 간격으로 대기합니다.
        /// <para>
        /// "dirty가 하나도 없는 상태"가 아니라 <b>호출 시점 이후 flush가 한 번 완료됐는지</b>를 봅니다.
        /// 대기하는 동안 게임이 값을 계속 바꾸면 dirty는 영영 사라지지 않으므로, 그 기준으로는
        /// 전송이 성공해도 타임아웃이 납니다. 대기 중 생긴 새 변경분은 다음 자동 저장이 처리합니다.
        /// </para>
        /// <para>
        /// 앞선 전송이 진행 중이었으면 끝나는 즉시 같은 흐름에서 재전송이 시작되므로, 여기서는 그 재전송까지 기다립니다.
        /// 돌려주는 결과는 마지막 전송의 것입니다.
        /// </para>
        /// </summary>
        /// <param name="startCount">요청 직전의 <see cref="Entry.FlushCompletedCount"/>.</param>
        /// <param name="startResetCount">요청 직전의 <see cref="Entry.ResetCount"/>.</param>
        /// <param name="timeoutMs">대기 최대 시간(밀리초). 250 미만은 250으로 보정됩니다.</param>
        private static async Task<SupabaseResult> WaitForSettledAsync(Entry entry, long startCount, long startResetCount, int timeoutMs)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var limitMs = Math.Max(250, timeoutMs);
            while (sw.ElapsedMilliseconds < limitMs)
            {
                // 기다리는 사이 로그아웃·계정 교체로 비워졌다 — 저장됐는지 알 수 없으므로 성공으로 알리지 않는다.
                if (entry.ResetCount != startResetCount)
                    return SupabaseResult.Fail(entry.LastResetReason ?? SupabaseErrorCode.UserSaveFlushFailed);

                if (!entry.IsInFlight)
                {
                    // 요청 이후 전송이 한 번이라도 끝났으면 그 결과를 돌려준다.
                    if (entry.FlushCompletedCount > startCount)
                        return entry.LastFlushError == null
                            ? SupabaseResult.Ok
                            : SupabaseResult.Fail(entry.LastFlushError);
                    // 보낼 것이 없어 전송이 시작조차 안 된 경우도 완료로 본다.
                    if (!SafeHasFreshDirty(entry))
                        return SupabaseResult.Ok;
                }
                await Task.Delay(16).ConfigureAwait(true);
            }

            return SupabaseResult.Fail(SupabaseErrorCode.UserSaveTimeout);
        }
    }
}
