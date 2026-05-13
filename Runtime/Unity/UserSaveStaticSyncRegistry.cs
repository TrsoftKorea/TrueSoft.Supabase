using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Truesoft.Supabase.Unity
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
        }

        private static readonly Dictionary<string, Entry> Entries = new(StringComparer.Ordinal);
        private static float _cooldownSeconds = 1f;
        private static float _lastRealtime;

        public static void ConfigureCooldown(float seconds)
        {
            _cooldownSeconds = Mathf.Max(0f, seconds);
        }

        public static void Register(
            string key,
            Func<bool> hasDirty,
            Func<Task<bool>> flushAsync,
            Action resetLocalState = null,
            Func<Task<bool>> loadAsync = null)
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
                return;
            }

            Entries[id] = new Entry
            {
                Key = id,
                HasDirty = hasDirty,
                FlushAsync = flushAsync,
                ResetLocalState = resetLocalState,
                LoadAsync = loadAsync,
                NextAllowedAtRealtime = 0f
            };
        }

        public static void MarkDirty(string key)
        {
            if (!TryGetEntry(key, out var entry))
                return;

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

            if (!SafeHasDirty(entry))
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
                entry.NextAllowedAtRealtime = now + _cooldownSeconds;
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
            var deadline = DateTime.UtcNow.AddMilliseconds(Math.Max(250, timeoutMs));
            while (DateTime.UtcNow < deadline)
            {
                if (!entry.IsInFlight && !SafeHasDirty(entry))
                    return true;
                await Task.Delay(16);
            }

            return false;
        }
    }
}
