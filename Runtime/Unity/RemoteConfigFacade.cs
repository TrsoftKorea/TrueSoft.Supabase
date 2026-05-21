using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Truesoft.Supabase.Core.Common;
using Truesoft.Supabase.Core.Data;
using UnityEngine;

namespace Truesoft.Supabase.Unity
{
    /// <summary>
    /// RemoteConfig 캐시 + 조회 API. Cold Start(시작 시 fetch 없음), 키 단위 폴링, Stale-While-Revalidate 조회를 지원합니다.
    /// 설계: 1키 = 1설정묶음(JSON) = 1폴링주기 (category 없음)
    /// </summary>
    public sealed class RemoteConfigFacade
    {
        private readonly SupabaseRemoteConfigService _service;
        private readonly Func<string> _accessTokenGetter;
        private readonly Dictionary<string, string> _cache = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, CachedKeyMeta> _keyMeta = new Dictionary<string, CachedKeyMeta>(StringComparer.Ordinal);
        private readonly Dictionary<string, KeyPollState> _keyPollStates = new Dictionary<string, KeyPollState>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<Action<string>>> _keySubscribers = new Dictionary<string, List<Action<string>>>(StringComparer.Ordinal);
        private readonly Dictionary<string, float> _pollIntervalOverrideByKey = new Dictionary<string, float>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _maxStaleByKey = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>Remote config가 변경되어 캐시가 갱신되면 호출됩니다. 인자는 변경된 key 목록.</summary>
        public event Action<IReadOnlyList<string>> OnChanged;

        /// <summary>
        /// 최근 <c>TickKeyPollsAsync</c>에서 캐시에 실제 변경이 있었는지 여부입니다.
        /// </summary>
        public bool LastApplyHadChanges { get; private set; }

        public RemoteConfigFacade(
            SupabaseRemoteConfigService service,
            Func<string> accessTokenGetter = null)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _accessTokenGetter = accessTokenGetter;
        }

        /// <summary>
        /// 키별 폴링 주기(초)를 설정합니다.
        /// <paramref name="overrideSeconds"/>: 0 이하이면 해당 키 백그라운드 폴링 비활성, 0 초과이면 해당 초 간격.
        /// </summary>
        public void SetKeyPollIntervalOverride(string key, float overrideSeconds)
        {
            if (string.IsNullOrWhiteSpace(key))
                return;

            var k = key.Trim();
            _pollIntervalOverrideByKey[k] = overrideSeconds <= 0f ? 0f : overrideSeconds;
        }

        /// <summary>
        /// 키별 캐시 유효 시간(초)을 설정합니다.
        /// <paramref name="seconds"/>가 0 이하이면 기본값(300초)이 사용됩니다.
        /// </summary>
        public void SetKeyMaxStaleSeconds(string key, int seconds)
        {
            if (string.IsNullOrWhiteSpace(key))
                return;

            _maxStaleByKey[key.Trim()] = seconds > 0 ? seconds : 0;
        }

        /// <summary>특정 key가 서버에서 갱신될 때마다 콜백을 호출합니다.</summary>
        public void Subscribe(string key, Action<string> onValueChanged, bool invokeIfCached = true)
        {
            if (string.IsNullOrWhiteSpace(key) || onValueChanged == null)
                return;

            var k = key.Trim();
            if (!_keySubscribers.TryGetValue(k, out var list))
            {
                list = new List<Action<string>>();
                _keySubscribers[k] = list;
            }

            if (list.Contains(onValueChanged) == false)
                list.Add(onValueChanged);

            if (invokeIfCached && TryGetRaw(k, out var json) && IsObjectRootJson(json))
                onValueChanged.Invoke(json);
        }

        public void Unsubscribe(string key, Action<string> onValueChanged)
        {
            if (string.IsNullOrWhiteSpace(key) || onValueChanged == null)
                return;

            var k = key.Trim();
            if (_keySubscribers.TryGetValue(k, out var list) == false)
                return;

            list.Remove(onValueChanged);
            if (list.Count == 0)
                _keySubscribers.Remove(k);
        }

        public bool TryGetRaw(string key, out string valueJson)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                valueJson = null;
                return false;
            }

            return _cache.TryGetValue(key.Trim(), out valueJson);
        }

        public T Get<T>(string key, T defaultValue = default)
        {
            if (string.IsNullOrWhiteSpace(key) ||
                _cache.TryGetValue(key.Trim(), out var json) == false ||
                string.IsNullOrWhiteSpace(json))
                return defaultValue;

            try
            {
                if (IsObjectRootJson(json) == false)
                    return defaultValue;

                return JsonConvert.DeserializeObject<T>(json);
            }
            catch
            {
                return defaultValue;
            }
        }

        /// <summary>
        /// Cold Start 패턴: 캐시에 없으면 키 단위로 fetch합니다.
        /// 폴링이 활성화된 키는 읽기 시 stale 체크를 건너뜁니다(폴링이 갱신 담당).
        /// 폴링이 없는 키는 Stale-While-Revalidate: <paramref name="maxStale"/> 초과 시 백그라운드 갱신을 트리거합니다(기본 300초).
        /// 폴링 설정은 <see cref="SetKeyPollIntervalOverride"/>를 사용합니다.
        /// fetch 실패·키 없음·역직렬화 실패 시 <see cref="SupabaseResult{T}.Fail"/>를 반환합니다.
        /// 실패 시 <see cref="SupabaseResult{T}.ErrorMessage"/> 예:
        /// <c>remote_config_key_not_in_database</c>(테이블/RLS에 행 없음),
        /// <c>remote_config_key_disabled</c>, <c>remote_config_key_requires_auth</c>,
        /// <c>remote_config_value_must_be_object_json</c>(뒤에 <c>:</c>로 이유·접두 미리보기가 붙을 수 있음).
        /// </summary>
        public async Task<SupabaseResult<T>> GetTypedAsync<T>(string key, int maxStale = 0) where T : class, new()
        {
            if (string.IsNullOrWhiteSpace(key))
                return SupabaseResult<T>.Fail("remote_config_key_empty");

            var trimmedKey = key.Trim();

            if (maxStale > 0)
                SetKeyMaxStaleSeconds(trimmedKey, maxStale);

            if (_cache.TryGetValue(trimmedKey, out _) == false)
            {
                var fetchOutcome = await EnsureKeysFetchedWithOutcomeAsync(new[] { trimmedKey }).ConfigureAwait(true);
                if (fetchOutcome.Success == false)
                    return SupabaseResult<T>.Fail(fetchOutcome.Error ?? "remote_config_fetch_failed");
            }
            else if (!IsPollingActive(trimmedKey) && _keyMeta.TryGetValue(trimmedKey, out var metaStale))
            {
                // 폴링 없는 경우에만 stale-while-revalidate 체크
                if (DateTime.UtcNow - metaStale.FetchedAtUtc > TimeSpan.FromSeconds(GetEffectiveMaxStaleSeconds(trimmedKey)))
                    _ = RefreshKeyInBackgroundAsync(trimmedKey);
            }

            return ReadCachedKey<T>(trimmedKey);
        }

        /// <summary>
        /// Remote Config를 가져옵니다. 캐시가 없거나 만료된 경우 서버 응답을 기다린 후 신선한 값을 반환합니다.
        /// <see cref="GetTypedAsync{T}"/>와 달리, 만료 시 현재 호출이 서버 응답을 기다립니다.
        /// </summary>
        public async Task<SupabaseResult<T>> GetTypedFreshAsync<T>(string key, int maxStale = 0) where T : class, new()
        {
            if (string.IsNullOrWhiteSpace(key))
                return SupabaseResult<T>.Fail("remote_config_key_empty");

            var trimmedKey = key.Trim();

            if (maxStale > 0)
                SetKeyMaxStaleSeconds(trimmedKey, maxStale);

            // 캐시 없음 또는 만료 시 서버에서 직접 fetch (await)
            bool needsFetch = !_cache.ContainsKey(trimmedKey);
            if (!needsFetch)
            {
                if (_keyMeta.TryGetValue(trimmedKey, out var meta))
                    needsFetch = DateTime.UtcNow - meta.FetchedAtUtc > TimeSpan.FromSeconds(GetEffectiveMaxStaleSeconds(trimmedKey));
                else
                    needsFetch = true;
            }

            if (needsFetch)
            {
                var fetchOutcome = await EnsureKeysFetchedWithOutcomeAsync(new[] { trimmedKey }).ConfigureAwait(true);
                if (fetchOutcome.Success == false)
                    return SupabaseResult<T>.Fail(fetchOutcome.Error ?? "remote_config_fetch_failed");
            }

            return ReadCachedKey<T>(trimmedKey);
        }

        /// <summary>캐시에서 읽어 역직렬화합니다. fetch 후 공통 처리에 사용합니다.</summary>
        private SupabaseResult<T> ReadCachedKey<T>(string trimmedKey) where T : class, new()
        {
            if (TryGetRaw(trimmedKey, out var json) == false || string.IsNullOrWhiteSpace(json))
                return SupabaseResult<T>.Fail("remote_config_key_not_found_or_filtered");

            if (IsObjectRootJson(json) == false)
                return SupabaseResult<T>.Fail("remote_config_value_must_be_object_json:" + BuildValueJsonShapeHint(json));

            try
            {
                var obj = JsonConvert.DeserializeObject<T>(json);
                if (obj == null)
                    return SupabaseResult<T>.Fail("remote_config_deserialize_null");

                return SupabaseResult<T>.Success(obj);
            }
            catch (Exception e)
            {
                return SupabaseResult<T>.Fail("remote_config_deserialize_exception:" + e.Message);
            }
        }

        /// <summary>
        /// 설정된 주기에 따라, 만기된 키만 폴링합니다. <see cref="SupabaseRuntime"/> 또는 <c>Update</c>에서 호출하세요.
        /// </summary>
        public async Task TickKeyPollsAsync(float realtimeSinceStartup)
        {
            LastApplyHadChanges = false;
            var accessToken = _accessTokenGetter?.Invoke();
            var keys = new List<string>(_keyPollStates.Keys);
            foreach (var key in keys)
            {
                if (_keyPollStates.TryGetValue(key, out var state) == false)
                    continue;

                var interval = GetEffectivePollIntervalSeconds(key);
                if (interval <= 0)
                    continue;

                if (realtimeSinceStartup < state.NextPollAtRealtime)
                    continue;

                await PollKeyAsync(key, accessToken).ConfigureAwait(true);
                state.NextPollAtRealtime = realtimeSinceStartup + interval;
            }
        }


        private async Task<bool> PollKeyAsync(string key, string accessToken)
        {
            var result = await _service.GetByKeysAsync(new[] { key }, accessToken).ConfigureAwait(true);
            if (result.IsSuccess == false || result.Data == null)
                return false;

            return ApplyRows(result.Data);
        }

        private async Task RefreshKeyInBackgroundAsync(string key)
        {
            try
            {
                var accessToken = _accessTokenGetter?.Invoke();
                await PollKeyAsync(key, accessToken).ConfigureAwait(true);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Supabase] RemoteConfig 백그라운드 갱신 실패. key={key}, err={e.Message}");
            }
        }

        private readonly struct FetchOutcome
        {
            public readonly bool Success;
            public readonly string Error;

            public FetchOutcome(bool success, string error)
            {
                Success = success;
                Error = error;
            }
        }

        private async Task<FetchOutcome> EnsureKeysFetchedWithOutcomeAsync(string[] keys)
        {
            if (keys == null || keys.Length == 0)
                return new FetchOutcome(true, null);

            var accessToken = _accessTokenGetter?.Invoke();
            var result = await _service.GetByKeysAsync(keys, accessToken).ConfigureAwait(true);
            if (result.IsSuccess == false)
                return new FetchOutcome(false, result.ErrorMessage ?? "remote_config_fetch_failed");

            if (result.Data != null)
                ApplyRows(result.Data);

            foreach (var rawKey in keys)
            {
                if (string.IsNullOrWhiteSpace(rawKey))
                    continue;

                var k = rawKey.Trim();
                if (_cache.TryGetValue(k, out var cached) && string.IsNullOrWhiteSpace(cached) == false)
                    continue;

                return new FetchOutcome(false, DiagnoseKeyNotCached(k, result.Data, accessToken));
            }

            return new FetchOutcome(true, null);
        }

        /// <summary>
        /// <see cref="ApplyRows"/> 이후에도 캐시에 없을 때, 서버 응답 행을 기준으로 이유를 좁힙니다.
        /// </summary>
        private string DiagnoseKeyNotCached(string key, SupabaseRemoteConfigService.RemoteConfigRow[] rows, string accessToken)
        {
            SupabaseRemoteConfigService.RemoteConfigRow match = null;
            if (rows != null)
            {
                foreach (var r in rows)
                {
                    if (r == null || string.IsNullOrWhiteSpace(r.key))
                        continue;
                    if (string.Equals(r.key.Trim(), key, StringComparison.Ordinal))
                    {
                        match = r;
                        break;
                    }
                }
            }

            if (match == null)
                return "remote_config_key_not_in_database";

            if (match.enabled == false)
                return "remote_config_key_disabled";

            if (match.requires_auth && string.IsNullOrWhiteSpace(accessToken))
                return "remote_config_key_requires_auth";

            var v = match.value_json ?? string.Empty;
            if (string.IsNullOrWhiteSpace(v) || IsObjectRootJson(v) == false)
                return "remote_config_value_must_be_object_json:" + BuildValueJsonShapeHint(v);

            return "remote_config_key_not_found_or_filtered";
        }

        private bool IsPollingActive(string key) =>
            _pollIntervalOverrideByKey.TryGetValue(key, out var v) && v > 0f;

        private const int DefaultMaxStaleSeconds = 300;

        private int GetEffectiveMaxStaleSeconds(string key)
        {
            if (_maxStaleByKey.TryGetValue(key, out var s) && s > 0)
                return s;

            return DefaultMaxStaleSeconds;
        }

        private int GetEffectivePollIntervalSeconds(string key)
        {
            if (_pollIntervalOverrideByKey.TryGetValue(key, out var o) && o > 0f)
                return Mathf.RoundToInt(o);

            return 0;
        }

        private bool ApplyRows(SupabaseRemoteConfigService.RemoteConfigRow[] rows)
        {
            if (rows == null)
                rows = Array.Empty<SupabaseRemoteConfigService.RemoteConfigRow>();

            var changedKeys = new HashSet<string>(StringComparer.Ordinal);
            var now = DateTime.UtcNow;
            var realtime = Time.realtimeSinceStartup;

            foreach (var row in rows)
            {
                if (row == null || string.IsNullOrWhiteSpace(row.key))
                    continue;

                var newValue = row.value_json ?? string.Empty;

                if (row.enabled == false)
                {
                    RemoveCacheKey(row.key, changedKeys);
                    continue;
                }

                if (row.requires_auth && string.IsNullOrWhiteSpace(_accessTokenGetter?.Invoke()))
                {
                    RemoveCacheKey(row.key, changedKeys);
                    continue;
                }

                if (IsObjectRootJson(newValue) == false)
                {
                    Debug.LogError($"[Supabase] RemoteConfig value_json은 객체 루트(JSON이 '{{'로 시작)여야 합니다. key={row.key}, value={TruncateForLog(newValue, 200)}");
                    UpdateKeyTimestampFromRow(row.key, row.updated_at);
                    continue;
                }

                if (_cache.TryGetValue(row.key, out var oldValue))
                {
                    if (string.Equals(oldValue, newValue, StringComparison.Ordinal))
                    {
                        UpdateKeyTimestampFromRow(row.key, row.updated_at);
                        continue;
                    }
                }

                _cache[row.key] = newValue;
                _keyMeta[row.key] = new CachedKeyMeta(now);
                changedKeys.Add(row.key);

                UpdateKeyTimestampFromRow(row.key, row.updated_at);
                RecomputeKeyPollState(row.key, realtime);
            }

            if (changedKeys.Count == 0)
                return false;

            var changedList = new List<string>(changedKeys);
            foreach (var k in changedList)
                NotifyKeySubscribers(k);

            OnChanged?.Invoke(changedList);
            LastApplyHadChanges = true;
            return true;
        }

        private void RecomputeKeyPollState(string key, float realtimeSinceStartup)
        {
            if (_keyPollStates.TryGetValue(key, out var state) == false)
                state = new KeyPollState();

            var effective = GetEffectivePollIntervalSeconds(key);
            if (effective > 0 && state.NextPollAtRealtime <= 0f)
                state.NextPollAtRealtime = realtimeSinceStartup + effective;

            _keyPollStates[key] = state;
        }

        private void UpdateKeyTimestampFromRow(string key, string updatedAtIso)
        {
            if (string.IsNullOrWhiteSpace(updatedAtIso))
                return;

            if (_keyPollStates.TryGetValue(key, out var state) == false)
                state = new KeyPollState();

            if (string.IsNullOrWhiteSpace(state.LastUpdatedAtIso) || string.CompareOrdinal(updatedAtIso, state.LastUpdatedAtIso) > 0)
                state.LastUpdatedAtIso = updatedAtIso;

            _keyPollStates[key] = state;
        }

        private void RemoveCacheKey(string key, ICollection<string> changedKeys)
        {
            if (_cache.Remove(key))
            {
                _keyMeta.Remove(key);
                changedKeys.Add(key);
            }
        }


        private void NotifyKeySubscribers(string key)
        {
            if (_keySubscribers.TryGetValue(key, out var list) == false || list.Count == 0)
                return;

            TryGetRaw(key, out var json);
            if (IsObjectRootJson(json) == false)
                return;
            var snapshot = new List<Action<string>>(list);
            foreach (var cb in snapshot)
            {
                try
                {
                    cb?.Invoke(json ?? string.Empty);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Supabase] RemoteConfig 구독자 처리 중 오류. key={key}, err={e.Message}");
                }
            }
        }

        private static bool IsObjectRootJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return false;

            var trimmed = json.TrimStart();
            return trimmed.StartsWith("{", StringComparison.Ordinal);
        }

        /// <summary><c>value_json</c>이 객체 루트가 아닐 때 <see cref="SupabaseResult{T}.ErrorMessage"/> 접미사로만 사용합니다.</summary>
        private static string BuildValueJsonShapeHint(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "empty_or_whitespace";

            var t = raw.TrimStart();
            if (t.Length == 0)
                return "empty_or_whitespace";

            switch (t[0])
            {
                case '[':
                    return "array_root(use_object_like_{\"v\":...})";
                case '"':
                    return "string_root(use_object_like_{\"v\":\"...\"})";
                case 't':
                case 'f':
                case 'n':
                    return "scalar_or_keyword_root(use_object_wrapper)";
                case '{':
                    return "unexpected";
                default:
                    return "non_object_prefix=" + TruncateForLog(t, 80);
            }
        }

        private static string TruncateForLog(string value, int maxLen)
        {
            if (string.IsNullOrEmpty(value))
                return "(빈 값)";

            value = value.Trim();
            if (value.Length <= maxLen)
                return value;

            return value.Substring(0, maxLen) + "...(일부 생략)";
        }

        private readonly struct CachedKeyMeta
        {
            public readonly DateTime FetchedAtUtc;

            public CachedKeyMeta(DateTime fetchedAtUtc)
            {
                FetchedAtUtc = fetchedAtUtc;
            }
        }

        private sealed class KeyPollState
        {
            public string LastUpdatedAtIso;
            public float NextPollAtRealtime;
        }
    }
}
