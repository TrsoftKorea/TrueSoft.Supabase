using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace TrueBase.Unity
{
    internal static class UserSaveStaticSyncRegistry
    {
        private sealed class Entry
        {
            public string Key;
            public Func<bool> HasDirty;
            public Func<Task<bool>> FlushAsync;
            public Func<Task<bool>> LoadAsync;
            public Action ResetLocalState;
            public bool IsInFlight;
            public bool RequestImmediateAfterInFlight;
            public float NextAllowedAtRealtime;
            /// <summary>null이면 전역 <see cref="_cooldownSeconds"/> 사용. non-null이면 dirty 우선순위별 쿨다운 반환.</summary>
            public Func<float> GetDirtyCooldown;
        }

        private static readonly Dictionary<string, Entry> Entries = new(StringComparer.Ordinal);
        private static float _cooldownSeconds = 5f;
        private static float _lastRealtime;

        // ── 전역 Priority 쿨다운 테이블 ────────────────────────────────────────
        private static float _urgentCooldown = 1f;
        private static float _normalCooldown = 5f;
        private static float _lazyCooldown   = 30f;

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

        public static void ConfigureCooldown(float seconds)
        {
            _cooldownSeconds = Mathf.Max(0f, seconds);
        }

        public static void Register(
            string key,
            Func<bool> hasDirty,
            Func<Task<bool>> flushAsync,
            Action resetLocalState = null,
            Func<Task<bool>> loadAsync = null,
            Func<float> getDirtyCooldown = null)
        {
            if (string.IsNullOrWhiteSpace(key) || hasDirty == null || flushAsync == null)
                return;

            var id = key.Trim();
            if (Entries.TryGetValue(id, out var existing))
            {
                existing.HasDirty = hasDirty;
                existing.FlushAsync = flushAsync;
                existing.ResetLocalState = resetLocalState;
                existing.LoadAsync = loadAsync;
                existing.GetDirtyCooldown = getDirtyCooldown;
                return;
            }

            Entries[id] = new Entry
            {
                Key = id,
                HasDirty = hasDirty,
                FlushAsync = flushAsync,
                ResetLocalState = resetLocalState,
                LoadAsync = loadAsync,
                GetDirtyCooldown = getDirtyCooldown,
                NextAllowedAtRealtime = 0f
            };
        }

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

        public static bool RequestImmediateFlush(string key)
        {
            if (!TryGetEntry(key, out var entry))
                return false;

            if (entry.IsInFlight)
            {
                entry.RequestImmediateAfterInFlight = true;
                return false;
            }

            return TryStartFlush(entry, immediate: true);
        }

        public static async Task<bool> RequestImmediateFlushAsync(string key, int timeoutMs = 5000)
        {
            if (!TryGetEntry(key, out var entry))
                return false;

            _ = RequestImmediateFlush(key);
            return await WaitForSettledAsync(entry, timeoutMs);
        }

        public static void RequestImmediateFlushAll()
        {
            foreach (var pair in Entries)
                _ = RequestImmediateFlush(pair.Key);
        }

        public static async Task<bool> RequestImmediateFlushAllAsync(int timeoutMs = 5000)
        {
            foreach (var pair in Entries)
                _ = RequestImmediateFlush(pair.Key);

            var deadline = DateTime.UtcNow.AddMilliseconds(Math.Max(250, timeoutMs));
            while (DateTime.UtcNow < deadline)
            {
                var allSettled = true;
                foreach (var entry in Entries.Values)
                {
                    if (entry.IsInFlight || SafeHasDirty(entry))
                    {
                        allSettled = false;
                        break;
                    }
                }

                if (allSettled)
                    return true;

                await Task.Delay(16);
            }

            return false;
        }

        public static void Tick(float realtimeNow)
        {
            _lastRealtime = realtimeNow;

            foreach (var entry in Entries.Values)
            {
                if (entry.IsInFlight)
                    continue;

                if (!SafeHasDirty(entry))
                    continue;

                if (realtimeNow < entry.NextAllowedAtRealtime)
                    continue;

                _ = StartFlushAsync(entry, immediate: false);
            }
        }

        public static async Task<bool> LoadAllAsync()
        {
            var tasks = new List<Task<bool>>(Entries.Count);
            foreach (var entry in Entries.Values)
            {
                if (entry.LoadAsync != null)
                    tasks.Add(SafeLoadAsync(entry));
            }

            if (tasks.Count == 0) return true;

            var results = await Task.WhenAll(tasks);
            foreach (var r in results)
                if (!r) return false;
            return true;
        }

        private static async Task<bool> SafeLoadAsync(Entry entry)
        {
            try { return await entry.LoadAsync(); }
            catch (Exception e)
            {
                Debug.LogWarning("[Supabase] user save load failed: " + e.Message);
                return false;
            }
        }

        public static void ResetAll()
        {
            foreach (var entry in Entries.Values)
            {
                entry.IsInFlight = false;
                entry.RequestImmediateAfterInFlight = false;
                entry.NextAllowedAtRealtime = 0f;

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

            if (!immediate && now < entry.NextAllowedAtRealtime)
                return false;

            _ = StartFlushAsync(entry, immediate);
            return true;
        }

        private static async Task StartFlushAsync(Entry entry, bool immediate)
        {
            entry.IsInFlight = true;

            try
            {
                await entry.FlushAsync();
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

                entry.IsInFlight = false;
            }

            if (entry.RequestImmediateAfterInFlight)
            {
                entry.RequestImmediateAfterInFlight = false;
                if (SafeHasDirty(entry))
                    _ = StartFlushAsync(entry, immediate: true);
                return;
            }

            if (!immediate && SafeHasDirty(entry) && _lastRealtime >= entry.NextAllowedAtRealtime)
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

        private static async Task<bool> WaitForSettledAsync(Entry entry, int timeoutMs)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var limitMs = Math.Max(250, timeoutMs);
            while (sw.ElapsedMilliseconds < limitMs)
            {
                if (!entry.IsInFlight && !SafeHasDirty(entry))
                    return true;
                await Task.Delay(16).ConfigureAwait(true);
            }

            return false;
        }
    }
}
