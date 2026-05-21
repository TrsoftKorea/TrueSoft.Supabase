using System;
using System.Collections;
using System.Threading.Tasks;
using TrueBase.Core.Auth;
using UnityEngine;

namespace TrueBase.Unity
{
    /// <summary>
    /// <c>user_sessions</c>의 <c>session_token</c>을 등록·폴링해 다른 기기에서 같은 계정으로 로그인했을 때 이 기기에서 세션을 끊습니다.
    /// </summary>
    internal sealed class SupabaseDuplicateSessionCoordinator : MonoBehaviour
    {
        private static SupabaseDuplicateSessionCoordinator _instance;
        private Coroutine _pollRoutine;
        private Coroutine _syncRoutine;
        private Coroutine _deferredActionCheckRoutine;
        private static float _lastActionCheckAtRealtime = -9999f;
        private static Task<bool> _inflightActionCheck;

        internal static void EnsureExists()
        {
            if (_instance != null)
                return;

            var go = new GameObject("TruesoftSupabaseDuplicateSession");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<SupabaseDuplicateSessionCoordinator>();
        }

        internal static void StopPolling()
        {
            if (_instance == null)
                return;

            if (_instance._pollRoutine != null)
            {
                _instance.StopCoroutine(_instance._pollRoutine);
                _instance._pollRoutine = null;
            }
        }

        internal static async Task<bool> VerifyCurrentSessionForActionAsync()
        {
            if (SupabaseSDK.IsLoggedIn == false || SupabaseSDK.Session?.User == null)
                return false;

            var cooldown = SupabaseSDK.DuplicateSessionActionCheckCooldownSeconds;
            var now = Time.realtimeSinceStartup;

            if (_inflightActionCheck != null)
                return await _inflightActionCheck;

            if (cooldown > 0f && (now - _lastActionCheckAtRealtime) < cooldown)
            {
                EnsureExists();
                _instance.ScheduleDeferredActionCheck(_lastActionCheckAtRealtime + cooldown);
                return true;
            }

            EnsureExists();
            _inflightActionCheck = _instance.VerifyCurrentSessionCoreAsync();

            try
            {
                var ok = await _inflightActionCheck;
                _lastActionCheckAtRealtime = Time.realtimeSinceStartup;
                return ok;
            }
            finally
            {
                _inflightActionCheck = null;
            }
        }

        private void ScheduleDeferredActionCheck(float dueAtRealtime)
        {
            if (_deferredActionCheckRoutine != null)
                return;

            _deferredActionCheckRoutine = StartCoroutine(DeferredActionCheckAfterCooldown(dueAtRealtime));
        }

        private IEnumerator DeferredActionCheckAfterCooldown(float dueAtRealtime)
        {
            while (Time.realtimeSinceStartup < dueAtRealtime)
                yield return null;

            if (SupabaseSDK.IsLoggedIn == false || SupabaseSDK.Session?.User == null)
            {
                _deferredActionCheckRoutine = null;
                yield break;
            }

            if (_inflightActionCheck != null)
            {
                yield return new WaitUntil(() => _inflightActionCheck.IsCompleted);
                _deferredActionCheckRoutine = null;
                yield break;
            }

            _inflightActionCheck = VerifyCurrentSessionCoreAsync();
            yield return new WaitUntil(() => _inflightActionCheck.IsCompleted);

            _lastActionCheckAtRealtime = Time.realtimeSinceStartup;
            _inflightActionCheck = null;
            _deferredActionCheckRoutine = null;
        }

        internal static void ScheduleSyncAfterSessionChange(SupabaseSessionChangeKind kind)
        {
            EnsureExists();
            if (_instance._syncRoutine != null)
                _instance.StopCoroutine(_instance._syncRoutine);

            StopPolling();
            _instance._syncRoutine = _instance.StartCoroutine(_instance.RunSyncAfterChangeRoutine(kind));
        }

        private IEnumerator RunSyncAfterChangeRoutine(SupabaseSessionChangeKind kind)
        {
            try
            {
                var svc = SupabaseSDK.UserSessionService;
                if (svc == null)
                    yield break;

                if (SupabaseSDK.IsLoggedIn == false || SupabaseSDK.Session == null || SupabaseSDK.Session.User == null)
                    yield break;

                var accountId = SupabaseSDK.Session.User.Id;
                var accessToken = SupabaseSDK.Session.AccessToken;
                if (string.IsNullOrWhiteSpace(accountId) || string.IsNullOrWhiteSpace(accessToken))
                    yield break;

                var getTask = svc.GetSessionTokenAsync(accessToken, accountId);
                yield return new WaitUntil(() => getTask.IsCompleted);

                var serverResult = getTask.Result;
                if (serverResult == null || !serverResult.IsSuccess)
                    yield break;

                var serverToken = serverResult.Data;

                if (kind == SupabaseSessionChangeKind.NewSignIn)
                {
                    var newToken = Guid.NewGuid().ToString("D");
                    var upsertTask = svc.UpsertSessionTokenAsync(accessToken, accountId, newToken);
                    yield return new WaitUntil(() => upsertTask.IsCompleted);
                    if (upsertTask.Result == null || !upsertTask.Result.IsSuccess)
                        yield break;

                    PlayerPrefs.SetString(SupabaseSDK.SessionTokenPlayerPrefsKeyPrefix + accountId, newToken);
                    PlayerPrefs.Save();
                    StartPollingIfNeeded(accountId);
                    yield break;
                }

                var localKey = SupabaseSDK.SessionTokenPlayerPrefsKeyPrefix + accountId;
                var localToken = PlayerPrefs.GetString(localKey, "");

                if (string.IsNullOrWhiteSpace(serverToken))
                {
                    if (string.IsNullOrWhiteSpace(localToken))
                    {
                        var fresh = Guid.NewGuid().ToString("D");
                        var t = svc.UpsertSessionTokenAsync(accessToken, accountId, fresh);
                        yield return new WaitUntil(() => t.IsCompleted);
                        if (t.Result == null || !t.Result.IsSuccess)
                            yield break;

                        PlayerPrefs.SetString(localKey, fresh);
                        PlayerPrefs.Save();
                    }
                    else
                    {
                        var t = svc.UpsertSessionTokenAsync(accessToken, accountId, localToken.Trim());
                        yield return new WaitUntil(() => t.IsCompleted);
                    }

                    StartPollingIfNeeded(accountId);
                    yield break;
                }

                if (string.IsNullOrWhiteSpace(localToken))
                {
                    PlayerPrefs.SetString(localKey, serverToken.Trim());
                    PlayerPrefs.Save();
                    StartPollingIfNeeded(accountId);
                    yield break;
                }

                if (!string.Equals(localToken.Trim(), serverToken.Trim(), StringComparison.Ordinal))
                {
                    SupabaseSDK.RaiseDuplicateLoginDetected();
                    yield break;
                }

                StartPollingIfNeeded(accountId);
            }
            finally
            {
                _syncRoutine = null;
            }
        }

        private void StartPollingIfNeeded(string accountId)
        {
            var interval = SupabaseSDK.DuplicateSessionPollSeconds;
            if (interval <= 0f)
                return;

            if (_pollRoutine != null)
            {
                StopCoroutine(_pollRoutine);
                _pollRoutine = null;
            }

            _pollRoutine = StartCoroutine(PollLoop(accountId, interval));
        }

        private IEnumerator PollLoop(string accountId, float intervalSeconds)
        {
            while (SupabaseSDK.IsLoggedIn && SupabaseSDK.Session != null)
            {
                yield return new WaitForSeconds(intervalSeconds);

                var svc = SupabaseSDK.UserSessionService;
                if (svc == null)
                    yield break;

                if (SupabaseSDK.Session?.User == null)
                    yield break;

                var uid = SupabaseSDK.Session.User.Id;
                var token = SupabaseSDK.Session.AccessToken;
                if (string.IsNullOrWhiteSpace(uid) || string.IsNullOrWhiteSpace(token))
                    yield break;

                var t = svc.GetSessionTokenAsync(token, uid);
                yield return new WaitUntil(() => t.IsCompleted);

                if (!t.Result.IsSuccess)
                    continue;

                var server = t.Result.Data;
                var key = SupabaseSDK.SessionTokenPlayerPrefsKeyPrefix + accountId;
                var local = PlayerPrefs.GetString(key, "");

                if (string.IsNullOrWhiteSpace(server))
                    continue;

                if (string.IsNullOrWhiteSpace(local))
                {
                    PlayerPrefs.SetString(key, server.Trim());
                    PlayerPrefs.Save();
                    continue;
                }

                if (!string.Equals(local.Trim(), server.Trim(), StringComparison.Ordinal))
                {
                    SupabaseSDK.RaiseDuplicateLoginDetected();
                    yield break;
                }
            }

            _pollRoutine = null;
        }

        private async Task<bool> VerifyCurrentSessionCoreAsync()
        {
            var svc = SupabaseSDK.UserSessionService;
            if (svc == null)
                return true;

            if (SupabaseSDK.Session?.User == null)
                return false;

            var accountId = SupabaseSDK.Session.User.Id;
            var accessToken = SupabaseSDK.Session.AccessToken;
            if (string.IsNullOrWhiteSpace(accountId) || string.IsNullOrWhiteSpace(accessToken))
                return false;

            var serverResult = await svc.GetSessionTokenAsync(accessToken, accountId);
            if (serverResult == null || serverResult.IsSuccess == false)
                return true;

            var serverToken = serverResult.Data;
            var localKey = SupabaseSDK.SessionTokenPlayerPrefsKeyPrefix + accountId;
            var localToken = PlayerPrefs.GetString(localKey, "");

            if (string.IsNullOrWhiteSpace(serverToken))
            {
                if (string.IsNullOrWhiteSpace(localToken))
                {
                    var fresh = Guid.NewGuid().ToString("D");
                    var t = await svc.UpsertSessionTokenAsync(accessToken, accountId, fresh);
                    if (t != null && t.IsSuccess)
                    {
                        PlayerPrefs.SetString(localKey, fresh);
                        PlayerPrefs.Save();
                    }

                    return true;
                }

                _ = await svc.UpsertSessionTokenAsync(accessToken, accountId, localToken.Trim());
                return true;
            }

            if (string.IsNullOrWhiteSpace(localToken))
            {
                PlayerPrefs.SetString(localKey, serverToken.Trim());
                PlayerPrefs.Save();
                return true;
            }

            if (string.Equals(localToken.Trim(), serverToken.Trim(), StringComparison.Ordinal))
                return true;

            SupabaseSDK.RaiseDuplicateLoginDetected();
            return false;
        }
    }
}
