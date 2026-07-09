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

        /// <summary>싱글턴 GameObject를 생성합니다. 이미 존재하면 아무것도 하지 않습니다.</summary>
        internal static void EnsureExists()
        {
            if (_instance != null)
                return;

            var go = new GameObject("TruesoftSupabaseDuplicateSession");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<SupabaseDuplicateSessionCoordinator>();
        }

        /// <summary>백그라운드 세션 토큰 폴링을 중지합니다. 로그아웃·세션 교체 시 호출됩니다.</summary>
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

        /// <summary>
        /// 중요 동작(저장 등) 직전에 현재 세션이 여전히 유효한지 서버 토큰과 대조합니다.
        /// 반환값 false는 비로그인 또는 중복 로그인 감지를 의미합니다.
        /// <para>
        /// 쿨다운(<c>SupabaseSDK.DuplicateSessionActionCheckCooldownSeconds</c>) 이내의 재호출은
        /// 네트워크 없이 true를 반환하되, 쿨다운 만료 시점에 지연 검사를 예약합니다.
        /// 이미 검사가 진행 중이면 새 요청 없이 그 결과를 공유합니다.
        /// </para>
        /// </summary>
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

        /// <summary>쿨다운 만료 후 1회 실행할 지연 검사를 예약합니다. 이미 예약돼 있으면 무시합니다.</summary>
        /// <param name="dueAtRealtime">검사를 실행할 <c>Time.realtimeSinceStartup</c> 기준 시각(초).</param>
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

        /// <summary>
        /// 세션 변경 후 서버 <c>session_token</c>과 로컬 값을 동기화하는 루틴을 시작합니다.
        /// 진행 중이던 동기화·폴링은 중지되고 새로 시작합니다.
        /// </summary>
        /// <param name="kind"><c>NewSignIn</c>이면 새 토큰을 발급해 무조건 덮어쓰고, 그 외(복원·갱신)에는 서버·로컬 토큰을 대조해 불일치 시 중복 로그인으로 처리합니다.</param>
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

                var getTask = svc.GetSessionTokenAsync(accessToken, accountId);
                yield return new WaitUntil(() => getTask.IsCompleted);

                var serverResult = getTask.Result;
                if (serverResult == null || !serverResult.IsSuccess)
                    yield break;

                var serverToken = serverResult.Data;

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

        /// <summary>
        /// 세션 토큰 폴링을 시작합니다. <c>SupabaseSDK.DuplicateSessionPollSeconds</c>가 0 이하이면 폴링하지 않습니다.
        /// 기존 폴링은 중지하고 새로 시작합니다.
        /// </summary>
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

        /// <summary>
        /// 주기적으로 서버 <c>session_token</c>을 조회해 로컬 값과 대조합니다.
        /// 불일치가 감지되면 <c>RaiseDuplicateLoginDetected</c>를 발생시키고 루프를 종료합니다.
        /// 조회 실패는 무시하고 다음 주기에 재시도합니다.
        /// </summary>
        /// <param name="intervalSeconds">폴링 주기(초).</param>
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

        /// <summary>
        /// 서버 <c>session_token</c>과 로컬 값을 즉시 1회 대조합니다.
        /// 서버에 토큰이 없으면 로컬 값(없으면 새 토큰)을 업서트하고 유효로 처리합니다.
        /// 서비스 미초기화·조회 실패 시에는 true를 반환합니다(네트워크 문제로 게임 진행을 막지 않음 — fail-open).
        /// </summary>
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
