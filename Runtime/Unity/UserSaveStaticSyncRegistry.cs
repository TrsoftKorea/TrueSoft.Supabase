using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TrueBase.Core.Common;
using UnityEngine;

namespace TrueBase.Unity
{
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
            public Func<Task<SupabaseResult>> FlushAsync;
            public Action ResetLocalState;
            public bool IsInFlight;
            public bool RequestImmediateAfterInFlight;
            /// <summary>flush가 완료될 때마다 1 증가. "내가 요청한 전송이 끝났는가" 판정에 사용.</summary>
            public long FlushCompletedCount;
            /// <summary>마지막 flush의 실패 사유. 성공했으면 null. 즉시 저장이 결과를 돌려줄 때 씁니다.</summary>
            public string LastFlushError;
            /// <summary><see cref="ResetAll"/> 횟수. 기다리는 사이 로그아웃·계정 교체가 있었는지 가릅니다.</summary>
            public long ResetCount;
            /// <summary>마지막 <see cref="ResetAll"/> 때 받은 실패 사유. 일반 로그아웃·계정 교체면 null입니다.</summary>
            public string LastResetReason;
            public float NextAllowedAtRealtime;
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
            Func<Task<SupabaseResult>> flushAsync,
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
                var cooldown  = entry.GetDirtyCooldown.Invoke();
                var candidate = now + cooldown;

                if (entry.NextAllowedAtRealtime <= now)
                    // 타이머가 만료됐거나 초기 상태 → 새로 시작
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
        public static bool RequestImmediateFlush(string key)
        {
            if (!TryGetEntry(key, out var entry))
                return false;

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
                _ = RequestImmediateFlush(pair.Key);
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
                _ = RequestImmediateFlush(pair.Key);

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
                    continue;

                if (realtimeNow < entry.NextAllowedAtRealtime)
                    continue;

                _ = StartFlushAsync(entry, immediate: false);
            }
        }

        /// <summary>모든 항목의 전송 상태·타이머를 초기화하고 <c>resetLocalState</c> 콜백을 호출합니다. 세션 해제 시 호출됩니다.</summary>
        /// <param name="reason">저장이 끝나길 기다리던 호출에 돌려줄 실패 사유. 비우면 <c>UserSaveFlushFailed</c>.</param>
        public static void ResetAll(string reason = null)
        {
            foreach (var entry in Entries.Values)
            {
                entry.IsInFlight = false;
                entry.RequestImmediateAfterInFlight = false;
                entry.NextAllowedAtRealtime = 0f;
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
            var succeeded = false;
            string error = SupabaseErrorCode.UserSaveFlushFailed;

            try
            {
                var result = await entry.FlushAsync();
                succeeded = result != null && result.IsSuccess;
                error = succeeded ? null : result?.ErrorCode ?? SupabaseErrorCode.UserSaveFlushFailed;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Supabase] user save flush failed: " + e.Message);
            }
            finally
            {
                var now = Time.realtimeSinceStartup;
                _lastRealtime = now;

                if (entry.GetDirtyCooldown != null)
                {
                    // 우선순위 기반: dirty 없으면 타이머 리셋(대기 상태),
                    // dirty 남아있으면 MarkDirty가 이미 올바르게 설정한 타이머 유지
                    if (!SafeHasDirty(entry))
                        entry.NextAllowedAtRealtime = 0f;
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
