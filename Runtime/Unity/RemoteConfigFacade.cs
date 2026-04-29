using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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
        private readonly Func<string> _applicationVersionProvider;
        private readonly Dictionary<string, string> _cache = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, CachedKeyMeta> _keyMeta = new Dictionary<string, CachedKeyMeta>(StringComparer.Ordinal);
        private readonly Dictionary<string, KeyPollState> _keyPollStates = new Dictionary<string, KeyPollState>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<Action<string>>> _keySubscribers = new Dictionary<string, List<Action<string>>>(StringComparer.Ordinal);
        private readonly Dictionary<string, float> _pollIntervalOverrideByKey = new Dictionary<string, float>(StringComparer.Ordinal);

        /// <summary>Remote config가 변경되어 캐시가 갱신되면 호출됩니다. 인자는 변경된 key 목록.</summary>
        public event Action<IReadOnlyList<string>> OnChanged;

        /// <summary>마지막 동기 시각(ISO). 키 단위 polling용.</summary>
        public string LastUpdatedAtIso { get; private set; }

        /// <summary>
        /// 최근 <c>RefreshAllAsync</c>/<c>PollAsync</c>/<c>TickKeyPollsAsync</c>에서 캐시에 실제 변경이 있었는지 여부입니다.
        /// </summary>
        public bool LastApplyHadChanges { get; private set; }

        public RemoteConfigFacade(
            SupabaseRemoteConfigService service,
            Func<string> accessTokenGetter = null,
            Func<string> applicationVersionProvider = null)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _accessTokenGetter = accessTokenGetter;
            _applicationVersionProvider = applicationVersionProvider ?? (() => Application.version);
        }

        /// <summary>
        /// 키별 폴링 주기(초)를 인스펙터에서 덮어씁니다.
        /// <paramref name="overrideSeconds"/>: &lt; 0이면 DB의 <c>poll_interval_seconds</c> 사용, 0이면 해당 키 백그라운드 폴링 비활성, &gt; 0이면 해당 초 간격.
        /// </summary>
        public void SetKeyPollIntervalOverride(string key, float overrideSeconds)
        {
            if (string.IsNullOrWhiteSpace(key))
                return;

            var k = key.Trim();
            if (overrideSeconds < 0f)
                _pollIntervalOverrideByKey.Remove(k);
            else
                _pollIntervalOverrideByKey[k] = overrideSeconds;
        }

        public void ClearKeyPollIntervalOverrides() => _pollIntervalOverrideByKey.Clear();

        /// <summary>특정 key가 서버에서 갱신될 때마다 콜백을 호출합니다.</summary>
        public void Subscribe(string key, Action<string> onValueChanged, bool invokeIfCached = true)
        {
            if (string.IsNullOrWhiteSpace(key) || onValueChanged == null)
                return;

            if (!_keySubscribers.TryGetValue(key, out var list))
            {
                list = new List<Action<string>>();
                _keySubscribers[key] = list;
            }

            if (list.Contains(onValueChanged) == false)
                list.Add(onValueChanged);

            if (invokeIfCached && TryGetRaw(key, out var json) && IsObjectRootJson(json))
                onValueChanged.Invoke(json);
        }

        public void Unsubscribe(string key, Action<string> onValueChanged)
        {
            if (string.IsNullOrWhiteSpace(key) || onValueChanged == null)
                return;

            if (_keySubscribers.TryGetValue(key, out var list) == false)
                return;

            list.Remove(onValueChanged);
            if (list.Count == 0)
                _keySubscribers.Remove(key);
        }

        public bool TryGetRaw(string key, out string valueJson)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                valueJson = null;
                return false;
            }

            return _cache.TryGetValue(key, out valueJson);
        }

        public T Get<T>(string key, T defaultValue = default)
        {
            if (_cache.TryGetValue(key, out var json) == false || string.IsNullOrWhiteSpace(json))
                return defaultValue;

            try
            {
                if (IsObjectRootJson(json) == false)
                    return defaultValue;

                return JsonUtility.FromJson<T>(json);
            }
            catch
            {
                return defaultValue;
            }
        }

        /// <summary>
        /// Cold Start + Stale-While-Revalidate: 캐시에 없으면 키 단위로 fetch합니다.
        /// 캐시 유효 시간은 DB <c>max_stale_seconds</c>를 사용합니다(0 이하이면 300초).
        /// fetch 실패·키 없음·역직렬화 실패 시 <see cref="SupabaseResult{T}.Fail"/>를 반환합니다.
        /// 실패 시 <see cref="SupabaseResult{T}.ErrorMessage"/> 예:
        /// <c>remote_config_key_not_in_database</c>(테이블/RLS에 행 없음),
        /// <c>remote_config_key_disabled</c>, <c>remote_config_key_requires_auth</c>,
        /// <c>remote_config_key_client_version_mismatch</c>,
        /// <c>remote_config_value_must_be_object_json</c>(뒤에 <c>:</c>로 이유·접두 미리보기가 붙을 수 있음).
        /// </summary>
        public async Task<SupabaseResult<T>> GetTypedAsync<T>(string key) where T : class, new()
        {
            if (string.IsNullOrWhiteSpace(key))
                return SupabaseResult<T>.Fail("remote_config_key_empty");

            var trimmedKey = key.Trim();

            if (_cache.TryGetValue(trimmedKey, out _) == false)
            {
                var fetchOutcome = await EnsureKeysFetchedWithOutcomeAsync(new[] { trimmedKey }).ConfigureAwait(true);
                if (fetchOutcome.Success == false)
                    return SupabaseResult<T>.Fail(fetchOutcome.Error ?? "remote_config_fetch_failed");
            }
            else if (_keyMeta.TryGetValue(trimmedKey, out var metaStale))
            {
                var maxStale = TimeSpan.FromSeconds(NormalizeMaxStaleSeconds(metaStale.MaxStaleSeconds));
                if (DateTime.UtcNow - metaStale.FetchedAtUtc > maxStale)
                {
                    _ = RefreshKeyInBackgroundAsync(trimmedKey);
                }
            }

            if (TryGetRaw(trimmedKey, out var json) == false || string.IsNullOrWhiteSpace(json))
                return SupabaseResult<T>.Fail("remote_config_key_not_found_or_filtered");

            if (IsObjectRootJson(json) == false)
                return SupabaseResult<T>.Fail("remote_config_value_must_be_object_json:" + BuildValueJsonShapeHint(json));

            try
            {
                var obj = JsonUtility.FromJson<T>(json);
                if (obj == null)
                    return SupabaseResult<T>.Fail("remote_config_deserialize_null");

                return SupabaseResult<T>.Success(obj);
            }
            catch (Exception e)
            {
                return SupabaseResult<T>.Fail("remote_config_deserialize_exception:" + e.Message);
            }
        }

        /// <summary>전체 설정을 다시 받아 캐시를 교체합니다.</summary>
        public async Task<bool> RefreshAllAsync()
        {
            LastApplyHadChanges = false;
            var accessToken = _accessTokenGetter?.Invoke();
            var result = await _service.GetAllAsync(accessToken).ConfigureAwait(true);
            if (result.IsSuccess == false || result.Data == null)
                return false;

            return ApplyRows(result.Data, replace: true);
        }

        /// <summary>
        /// 마지막 동기 이후 변경분만 머지합니다.
        /// </summary>
        public async Task<bool> PollAsync()
        {
            LastApplyHadChanges = false;
            var accessToken = _accessTokenGetter?.Invoke();

            var result = await _service.GetChangedSinceAsync(LastUpdatedAtIso, accessToken).ConfigureAwait(true);
            if (result.IsSuccess == false || result.Data == null)
                return false;

            return ApplyRows(result.Data, replace: false);
        }

        /// <summary>
        /// DB에 설정된 주기(및 인스펙터 오버라이드)에 따라, 만기된 키만 폴링합니다. <see cref="SupabaseRuntime"/> 또는 <c>Update</c>에서 호출하세요.
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

                var interval = GetEffectivePollIntervalSeconds(key, state.DbPollIntervalSeconds);
                if (interval <= 0)
                    continue;

                if (realtimeSinceStartup < state.NextPollAtRealtime)
                    continue;

                await PollKeyAsync(key, accessToken).ConfigureAwait(true);
                state.NextPollAtRealtime = realtimeSinceStartup + interval;
            }
        }

        /// <summary>온디맨드 전체 갱신 후 모든 키의 다음 폴링 시각을 뒤로 미룹니다.</summary>
        public void PushBackAllKeyPolls(float realtimeSinceStartup, float delaySeconds)
        {
            if (delaySeconds <= 0f)
                return;

            var target = realtimeSinceStartup + delaySeconds;
            foreach (var state in _keyPollStates.Values)
            {
                if (state.NextPollAtRealtime < target)
                    state.NextPollAtRealtime = target;
            }
        }

        private async Task<bool> PollKeyAsync(string key, string accessToken)
        {
            var result = await _service.GetByKeysAsync(new[] { key }, accessToken).ConfigureAwait(true);
            if (result.IsSuccess == false || result.Data == null)
                return false;

            return ApplyRows(result.Data, replace: false);
        }

        private async Task RefreshKeyInBackgroundAsync(string key)
        {
            try
            {
                var accessToken = _accessTokenGetter?.Invoke();
                await PollKeyAsync(key, accessToken).ConfigureAwait(false);
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
                ApplyRows(result.Data, replace: false);

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

            if (PassesClientVersion(match) == false)
                return "remote_config_key_client_version_mismatch";

            var v = match.value_json ?? string.Empty;
            if (string.IsNullOrWhiteSpace(v) || IsObjectRootJson(v) == false)
                return "remote_config_value_must_be_object_json:" + BuildValueJsonShapeHint(v);

            return "remote_config_key_not_found_or_filtered";
        }

        private static int NormalizeMaxStaleSeconds(int secondsFromDb) => secondsFromDb > 0 ? secondsFromDb : 300;

        private int GetEffectivePollIntervalSeconds(string key, int fromDb)
        {
            if (_pollIntervalOverrideByKey.TryGetValue(key, out var o))
            {
                if (o <= 0f)
                    return 0;

                return Mathf.RoundToInt(o);
            }

            return fromDb > 0 ? fromDb : 0;
        }

        private bool ApplyRows(SupabaseRemoteConfigService.RemoteConfigRow[] rows, bool replace)
        {
            if (rows == null)
                rows = Array.Empty<SupabaseRemoteConfigService.RemoteConfigRow>();

            Dictionary<string, string> previousValues = null;
            HashSet<string> acceptedKeys = null;

            if (replace)
            {
                previousValues = new Dictionary<string, string>(_cache, StringComparer.Ordinal);
                _cache.Clear();
                _keyMeta.Clear();
                _keyPollStates.Clear();
                acceptedKeys = new HashSet<string>(StringComparer.Ordinal);
            }

            var changedKeys = new HashSet<string>(StringComparer.Ordinal);
            var now = DateTime.UtcNow;
            var realtime = Time.realtimeSinceStartup;

            foreach (var row in rows)
            {
                if (row == null || string.IsNullOrWhiteSpace(row.key))
                    continue;

                TouchGlobalLastUpdated(row.updated_at);

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

                if (PassesClientVersion(row) == false)
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

                if (replace)
                    acceptedKeys.Add(row.key);

                if (replace)
                {
                    previousValues.TryGetValue(row.key, out var oldValue);
                    if (string.Equals(oldValue, newValue, StringComparison.Ordinal) == false)
                        changedKeys.Add(row.key);

                    _cache[row.key] = newValue;
                    _keyMeta[row.key] = new CachedKeyMeta(now, NormalizeMaxStaleSeconds(row.max_stale_seconds));
                }
                else
                {
                    if (_cache.TryGetValue(row.key, out var oldValue))
                    {
                        if (string.Equals(oldValue, newValue, StringComparison.Ordinal))
                        {
                            UpdateKeyTimestampFromRow(row.key, row.updated_at);
                            continue;
                        }
                    }

                    _cache[row.key] = newValue;
                    _keyMeta[row.key] = new CachedKeyMeta(now, NormalizeMaxStaleSeconds(row.max_stale_seconds));
                    changedKeys.Add(row.key);
                }

                UpdateKeyTimestampFromRow(row.key, row.updated_at);
                RecomputeKeyPollState(row.key, row.poll_interval_seconds, realtime);
            }

            if (replace && previousValues != null && previousValues.Count > 0)
            {
                foreach (var pair in previousValues)
                {
                    var k = pair.Key;
                    if (string.IsNullOrWhiteSpace(k))
                        continue;

                    if (acceptedKeys.Contains(k) == false)
                    {
                        changedKeys.Add(k);
                        _keyMeta.Remove(k);
                    }
                }
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

        private void RecomputeKeyPollState(string key, int pollIntervalFromDb, float realtimeSinceStartup)
        {
            if (_keyPollStates.TryGetValue(key, out var state) == false)
                state = new KeyPollState();

            state.DbPollIntervalSeconds = pollIntervalFromDb;
            var effective = GetEffectivePollIntervalSeconds(key, pollIntervalFromDb);
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

        private void TouchGlobalLastUpdated(string updatedAtIso)
        {
            if (string.IsNullOrWhiteSpace(updatedAtIso))
                return;

            if (string.IsNullOrWhiteSpace(LastUpdatedAtIso) || string.CompareOrdinal(updatedAtIso, LastUpdatedAtIso) > 0)
                LastUpdatedAtIso = updatedAtIso;
        }

        private void RemoveCacheKey(string key, List<string> changedKeys)
        {
            if (_cache.Remove(key))
            {
                _keyMeta.Remove(key);
                changedKeys.Add(key);
            }
        }

        private bool PassesClientVersion(SupabaseRemoteConfigService.RemoteConfigRow row)
        {
            var ver = _applicationVersionProvider?.Invoke() ?? Application.version;
            if (string.IsNullOrWhiteSpace(ver))
                ver = "0";

            if (string.IsNullOrWhiteSpace(row.client_version_min) == false
                && string.CompareOrdinal(ver, row.client_version_min.Trim()) < 0)
                return false;

            if (string.IsNullOrWhiteSpace(row.client_version_max) == false
                && string.CompareOrdinal(ver, row.client_version_max.Trim()) > 0)
                return false;

            return true;
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

        /// <summary>DB <c>value_json</c>이 객체 루트가 아닐 때 <see cref="SupabaseResult{T}.ErrorMessage"/> 접미사로만 사용합니다.</summary>
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
            public readonly int MaxStaleSeconds;

            public CachedKeyMeta(DateTime fetchedAtUtc, int maxStaleSeconds)
            {
                FetchedAtUtc = fetchedAtUtc;
                MaxStaleSeconds = maxStaleSeconds;
            }
        }

        private sealed class KeyPollState
        {
            public string LastUpdatedAtIso;
            public int DbPollIntervalSeconds;
            public float NextPollAtRealtime;
        }
    }
}
