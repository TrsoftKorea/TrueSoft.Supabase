using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using TrueBase.Core.Auth;
using TrueBase.Core.Common;
using TrueBase.Core.Data;
using TrueBase.Core.Http;
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
        private static Task<bool> _inflightActionCheck;

        // 접속 신호 주기. 신호가 나갈 때마다 서버 토큰과 맞춰 본다. 저장이 성공하면 서버 트리거(ts_user_data_session_guard)가
        // 신호가 하던 기록을 대신하므로 타이머를 처음부터 다시 센다 — 저장이 이어지는 동안에는 신호가 나가지 않는다.
        // 서버 트리거가 heartbeat_seconds 에 같은 값(60)을 적는다 — 바꾸면 install.sql 도 함께 바꾼다.
        private const float HeartbeatSeconds = 60f;

        // 앱 복귀·쓰기 전 확인은 마지막으로 맞춰 본 지 이 시간 안이면 다시 묻지 않는다. 대가로 다른 기기 로그인 뒤 최대 이만큼은
        // 그런 쓰기가 통과한다. 세이브 저장은 저장 요청 안에서 서버가 매번 가리므로 이 창과 상관없다.
        private const float TrustSeconds = 30f;

        // 이 기기의 토큰이 서버와 맞는다고 마지막으로 확인한 시각과 그 계정. 쓰기 전 확인·접속 신호·로그인 직후 맞춰 보기·저장 성공이 적는다.
        private static float _confirmedAtRealtime = float.MinValue;
        private static DateTime _confirmedAtUtc;
        private static string _confirmedAccountId;
        private static Task _resumeCheck;
        private static float _nextHeartbeatAt;

        private static void MarkConfirmed(string accountId)
        {
            _confirmedAccountId = accountId;
            _confirmedAtRealtime = Time.realtimeSinceStartup;
            _confirmedAtUtc = DateTime.UtcNow;
        }

        /// <summary>
        /// 이 기기에 토큰이 없을 때(새 로그인 직후·등록 실패·기록이 지워짐) 등록할 토큰을 서버에 올립니다. 서버에 있는 토큰을 받아 적지 않습니다 —
        /// 그 토큰은 다른 기기 것일 수 있어, 받아 적으면 두 기기가 같은 토큰으로 서로를 영영 감지하지 못합니다.
        /// 올릴 토큰은 처음 한 번 정해 대기 값으로 남기고, 성공할 때까지 같은 값으로 다시 올립니다 — 시간 초과로 실패한 등록이
        /// 나중에 서버에 닿아도 같은 값이라 어긋나지 않습니다.
        /// </summary>
        private static async Task RegisterPendingTokenAsync(SupabaseUserSessionService svc, string accessToken, string accountId)
        {
            var pendingKey = SupabaseSDK.SessionTokenPendingPlayerPrefsKeyPrefix + accountId;
            var pending = PlayerPrefs.GetString(pendingKey, "");
            if (string.IsNullOrWhiteSpace(pending))
            {
                pending = Guid.NewGuid().ToString("D");
                PlayerPrefs.SetString(pendingKey, pending);
                PlayerPrefs.Save();
            }

            var result = await svc.UpsertSessionTokenAsync(accessToken, accountId, pending.Trim());
            if (result == null || !result.IsSuccess || SupabaseSDK.Session?.User?.Id != accountId)
                return;

            PlayerPrefs.SetString(SupabaseSDK.SessionTokenPlayerPrefsKeyPrefix + accountId, pending.Trim());
            PlayerPrefs.DeleteKey(pendingKey);
            PlayerPrefs.Save();
            MarkConfirmed(accountId);
        }

        private static void ClearConfirmed()
        {
            _confirmedAccountId = null;
            _confirmedAtRealtime = float.MinValue;
            _confirmedAtUtc = default;
        }

        // 기기 시계도 함께 본다 — 게임 안 시계는 절전 중 멈출 수 있어, 그것만 보면 오래 나갔다 와도 "방금 확인"으로 보인다.
        // 기기 시계를 뒤로 돌려 경과가 음수면 확인한 지 오래된 것으로 본다.
        private static bool IsRecentlyConfirmed()
        {
            if (SupabaseSDK.Session?.User == null)
                return false;

            if (!string.Equals(_confirmedAccountId, SupabaseSDK.Session.User.Id, StringComparison.Ordinal))
                return false;

            var wall = (DateTime.UtcNow - _confirmedAtUtc).TotalSeconds;
            return Time.realtimeSinceStartup - _confirmedAtRealtime < TrustSeconds && wall >= 0 && wall < TrustSeconds;
        }

        /// <summary>
        /// 이 기기의 세션 토큰을 실은 저장이 서버에서 받아들여졌을 때 부릅니다. 토큰이 맞았다는 뜻이고, 서버가 접속 기록도 남겼으므로
        /// 접속 신호를 처음부터 다시 셉니다.
        /// </summary>
        internal static void OnSaveAccepted(string accountId)
        {
            if (string.IsNullOrWhiteSpace(accountId) || SupabaseSDK.Session?.User?.Id != accountId)
                return;

            MarkConfirmed(accountId);
            _nextHeartbeatAt = Time.realtimeSinceStartup + HeartbeatSeconds;
        }

        // 앱으로 돌아오면 바로 맞춰 본다 — 다른 기기에서 하다 돌아온 플레이어를 다음 저장이 아니라 돌아온 즉시 내보낸다.
        // 휴대폰 복귀는 일시정지 해제와 포커스가 함께 오고, 포커스는 알림창·광고·결제 창을 닫을 때도 온다 — 한 번만 보낸다.
        private void OnApplicationPause(bool pause)
        {
            if (!pause)
                CheckOnResume();
        }

        private void OnApplicationFocus(bool focus)
        {
            if (focus)
                CheckOnResume();
        }

        private static void CheckOnResume()
        {
            if (_resumeCheck != null && !_resumeCheck.IsCompleted)
                return;

            if (SupabaseSDK.IsLoggedIn == false || SupabaseSDK.Session?.User == null || SupabaseSDK.UserSessionService == null)
                return;

            if (IsRecentlyConfirmed())
                return;

            _resumeCheck = CheckOnResumeAsync(SupabaseSDK.Session.User.Id);
        }

        private static async Task CheckOnResumeAsync(string accountId)
        {
            try
            {
                // 오래 나가 있었으면 토큰이 만료돼 확인 요청이 거절된다 — 먼저 갱신한다. 갱신이 끝나면 세션 교체 뒤 맞춰 보기가
                // 같은 확인을 하므로 여기서는 보내지 않는다. 갱신이 일시적으로 실패했으면 다음 기회(접속 신호·저장)에 맡긴다.
                if (await SupabaseSDK.RefreshIfNearExpiryAsync() != null)
                    return;

                if (_instance != null && _instance._syncRoutine != null)
                    return;

                if (SupabaseSDK.Session?.User?.Id != accountId)
                    return;

                await VerifyCurrentSessionForActionAsync();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Supabase] 앱 복귀 중복 로그인 확인 실패: " + e.Message);
            }
        }

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
            ClearConfirmed();
            if (_instance == null)
                return;

            if (_instance._pollRoutine != null)
            {
                _instance.StopCoroutine(_instance._pollRoutine);
                _instance._pollRoutine = null;
            }
        }

        /// <summary>
        /// 쓰기 직전에 현재 세션이 여전히 유효한지 서버 토큰과 대조합니다.
        /// 반환값 false는 비로그인 또는 중복 로그인 감지를 의미합니다.
        /// <para>
        /// 마지막으로 맞춰 본 뒤 <see cref="TrustSeconds"/> 안이면 네트워크 없이 true를 반환합니다.
        /// 접속 신호·저장 성공이 맞춰 본 것도 셉니다. 이미 검사가 진행 중이면 새 요청 없이 그 결과를 공유합니다.
        /// </para>
        /// </summary>
        internal static async Task<bool> VerifyCurrentSessionForActionAsync()
        {
            if (SupabaseSDK.IsLoggedIn == false || SupabaseSDK.Session?.User == null)
                return false;

            // 로그인 직후 토큰 등록·대조가 도는 중이면 끝날 때까지 기다린다 — 함께 돌면 둘이 서로 다른 토큰을 이 기기에 적어,
            // 서버와 어긋난 토큰이 요청에 실리고 이 기기가 중복 로그인으로 거절된다.
            EnsureExists();
            var waitUntil = Time.realtimeSinceStartup + 30f;
            while (_instance._syncRoutine != null && Time.realtimeSinceStartup < waitUntil)
                await Task.Yield();

            if (SupabaseSDK.IsLoggedIn == false || SupabaseSDK.Session?.User == null)
                return false;

            if (_inflightActionCheck != null)
                return await _inflightActionCheck;

            if (IsRecentlyConfirmed())
                return true;

            _inflightActionCheck = _instance.VerifyCurrentSessionCoreAsync();

            try
            {
                return await _inflightActionCheck;
            }
            finally
            {
                _inflightActionCheck = null;
            }
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
                    // 등록이 실패해도 접속 신호는 시작한다 — 신호·쓰기 전 확인이 같은 대기 토큰으로 다시 등록한다.
                    var reg = RegisterPendingTokenAsync(svc, accessToken, accountId);
                    yield return new WaitUntil(() => reg.IsCompleted);
                    StartPollingIfNeeded(accountId);
                    yield break;
                }

                var getTask = svc.GetSessionTokenAsync(accessToken, accountId);
                yield return new WaitUntil(() => getTask.IsCompleted);

                var serverResult = getTask.Result;
                if (serverResult == null || !serverResult.IsSuccess)
                    yield break;

                if (SupabaseSDK.Session?.User?.Id != accountId)
                    yield break;

                var serverToken = serverResult.Data;

                var localKey = SupabaseSDK.SessionTokenPlayerPrefsKeyPrefix + accountId;
                var localToken = PlayerPrefs.GetString(localKey, "");

                if (string.IsNullOrWhiteSpace(localToken))
                {
                    var reg = RegisterPendingTokenAsync(svc, accessToken, accountId);
                    yield return new WaitUntil(() => reg.IsCompleted);
                    StartPollingIfNeeded(accountId);
                    yield break;
                }

                if (string.IsNullOrWhiteSpace(serverToken))
                {
                    var t = svc.UpsertSessionTokenAsync(accessToken, accountId, localToken.Trim());
                    yield return new WaitUntil(() => t.IsCompleted);
                    if (t.Result != null && t.Result.IsSuccess)
                        MarkConfirmed(accountId);

                    StartPollingIfNeeded(accountId);
                    yield break;
                }

                if (!string.Equals(localToken.Trim(), serverToken.Trim(), StringComparison.Ordinal))
                {
                    SupabaseSDK.RaiseDuplicateLoginDetected();
                    yield break;
                }

                MarkConfirmed(accountId);
                StartPollingIfNeeded(accountId);
            }
            finally
            {
                _syncRoutine = null;
            }
        }

        /// <summary>접속 신호를 시작합니다. 기존 신호는 중지하고 새로 시작합니다.</summary>
        private void StartPollingIfNeeded(string accountId)
        {
            if (_pollRoutine != null)
            {
                StopCoroutine(_pollRoutine);
                _pollRoutine = null;
            }

            _nextHeartbeatAt = Time.realtimeSinceStartup + HeartbeatSeconds;
            _pollRoutine = StartCoroutine(PollLoop(accountId));
        }

        private static bool HeartbeatDue() => Time.realtimeSinceStartup >= _nextHeartbeatAt;

        /// <summary>
        /// <see cref="HeartbeatSeconds"/>마다 접속 중 신호를 보내며 서버 <c>session_token</c>을 받아 로컬 값과 대조합니다.
        /// 저장이 성공하면(<see cref="OnSaveAccepted"/>) 다음 신호를 그만큼 미룹니다.
        /// 불일치가 감지되면 <c>RaiseDuplicateLoginDetected</c>를 발생시키고 루프를 종료합니다.
        /// 조회 실패는 연속 실패의 첫 번만 경고로 남기고 다음 주기에 재시도합니다.
        /// </summary>
        private IEnumerator PollLoop(string accountId)
        {
            var heartbeatFailing = false;
            var waitDue = new WaitUntil(HeartbeatDue);
            while (SupabaseSDK.IsLoggedIn && SupabaseSDK.Session != null)
            {
                // 게임 시간(timeScale)을 따르면 일시정지 화면에서 신호가 끊겨 접속자에서 빠진다 — 실제 시간으로 잰다.
                yield return waitDue;
                _nextHeartbeatAt = Time.realtimeSinceStartup + HeartbeatSeconds;

                var svc = SupabaseSDK.UserSessionService;
                if (svc == null)
                    yield break;

                if (SupabaseSDK.Session?.User == null)
                    yield break;

                var uid = SupabaseSDK.Session.User.Id;
                var token = SupabaseSDK.Session.AccessToken;
                if (string.IsNullOrWhiteSpace(uid) || string.IsNullOrWhiteSpace(token))
                    yield break;

                var t = svc.HeartbeatAsync(token, Mathf.RoundToInt(HeartbeatSeconds));
                yield return new WaitUntil(() => t.IsCompleted);

                // 실패가 이어지면 중복 감지와 접속자 집계가 함께 멈춘다 — 연속 실패의 첫 번만 알린다(주기마다 찍으면 콘솔이 덮인다).
                var ok = t.Result != null && t.Result.IsSuccess;
                // 점검으로 거절된 신호는 응답 자리에서 세션을 이미 끊었다(SessionTokenHttpClient) — 고장이 아니다.
                if (!ok && t.Result?.ErrorCode == SupabaseErrorCode.Maintenance)
                    yield break;
                if (!ok && !heartbeatFailing)
                    Debug.LogWarning("[Supabase] 접속 신호 실패 — 중복 로그인 감지가 멈춥니다(DB 에 ts_session_heartbeat 가 있는지 확인): " + (t.Result?.ErrorCode ?? "unknown"));
                heartbeatFailing = !ok;

                if (!ok)
                    continue;

                if (SupabaseSDK.Session?.User?.Id != accountId)
                    yield break;

                var server = t.Result.Data;
                var key = SupabaseSDK.SessionTokenPlayerPrefsKeyPrefix + accountId;
                var local = PlayerPrefs.GetString(key, "");

                if (string.IsNullOrWhiteSpace(local))
                {
                    var reg = RegisterPendingTokenAsync(svc, token, accountId);
                    yield return new WaitUntil(() => reg.IsCompleted);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(server))
                    continue;

                if (!string.Equals(local.Trim(), server.Trim(), StringComparison.Ordinal))
                {
                    SupabaseSDK.RaiseDuplicateLoginDetected();
                    yield break;
                }

                MarkConfirmed(accountId);
            }

            _pollRoutine = null;
        }

        /// <summary>
        /// 서버 <c>session_token</c>과 로컬 값을 즉시 1회 대조합니다.
        /// 이 기기에 토큰이 없으면 대기 토큰을 등록하고(<see cref="RegisterPendingTokenAsync"/>), 서버에 토큰이 없으면 이 기기 토큰을 다시 올립니다.
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

            var localKey = SupabaseSDK.SessionTokenPlayerPrefsKeyPrefix + accountId;
            if (string.IsNullOrWhiteSpace(PlayerPrefs.GetString(localKey, "")))
            {
                await RegisterPendingTokenAsync(svc, accessToken, accountId);
                return SupabaseSDK.Session?.User?.Id == accountId;
            }

            var serverResult = await svc.GetSessionTokenAsync(accessToken, accountId);
            if (serverResult == null || serverResult.IsSuccess == false)
                return true;

            // 기다리는 사이 로그아웃·계정 전환·중복 처리가 있었으면 이 결과는 앞 로그인 몫이다.
            if (SupabaseSDK.Session?.User?.Id != accountId)
                return false;

            var serverToken = serverResult.Data;
            var localToken = PlayerPrefs.GetString(localKey, "");

            if (string.IsNullOrWhiteSpace(localToken))
                return true;

            if (string.IsNullOrWhiteSpace(serverToken))
            {
                var restored = await svc.UpsertSessionTokenAsync(accessToken, accountId, localToken.Trim());
                if (restored != null && restored.IsSuccess)
                    MarkConfirmed(accountId);
                return true;
            }

            if (string.Equals(localToken.Trim(), serverToken.Trim(), StringComparison.Ordinal))
            {
                MarkConfirmed(accountId);
                return true;
            }

            SupabaseSDK.RaiseDuplicateLoginDetected();
            return false;
        }
    }

    /// <summary>
    /// 지금 로그인의 데이터 요청(PostgREST)마다 이 기기의 세션 토큰을 <c>x-session-token</c>으로 싣습니다. 서버의 요청 앞 검사
    /// (<c>ts_request_limit_check</c>)가 다른 기기 로그인이면 거절하고, 그 응답이 오면 이 자리에서 중복 로그인으로 처리합니다 —
    /// 앱이 따로 묻지 않아도 서버와 주고받는 순간 잡히고, 확인 요청이 따로 나가지 않습니다.
    /// </summary>
    internal sealed class SessionTokenHttpClient : ISupabaseHttpClient
    {
        internal const string HeaderName = "x-session-token";

        private readonly ISupabaseHttpClient _inner;
        private readonly string _sessionsPath;

        /// <param name="sessionsTable">세션 토큰 표 이름. 토큰을 등록·대조하는 요청이라 싣지 않습니다.</param>
        public SessionTokenHttpClient(ISupabaseHttpClient inner, string sessionsTable)
        {
            _inner = inner;
            _sessionsPath = "/rest/v1/" + sessionsTable;
        }

        public async Task<SupabaseHttpResponse> SendAsync(string method, string url, string jsonBody, Dictionary<string, string> headers)
        {
            var bearer = AccessTokenOf(headers);
            var sent = headers != null && headers.ContainsKey(HeaderName);

            if (!sent && ShouldAttach(url, bearer))
            {
                var token = SupabaseSDK.LocalSessionTokenFor(SupabaseSDK.Session.User.Id);
                if (token != null)
                {
                    // 부른 쪽이 넘긴 사전은 건드리지 않는다 — 같은 사전을 다시 쓰는 곳이 있다.
                    headers = new Dictionary<string, string>(headers) { [HeaderName] = token };
                    sent = true;
                }
            }

            var response = await _inner.SendAsync(method, url, jsonBody, headers);

            // 이 요청을 보낸 로그인이 아직 지금 로그인일 때만 끊는다 — 그사이 로그아웃·계정 전환이 있었으면 앞 로그인의 몫이다.
            if (sent && response.IsDuplicateLogin && SupabaseSDK.IsLoggedIn && bearer != null && bearer == SupabaseSDK.Session?.AccessToken)
                SupabaseSDK.RaiseDuplicateLoginDetected();
            // 점검은 세션 토큰 머리글과 상관없이 로그인한 요청 전부에 걸린다. 접속 신호(60초)가 늦어도 그 안에 잡는다.
            else if (response.IsMaintenance && SupabaseSDK.IsLoggedIn && bearer != null && bearer == SupabaseSDK.Session?.AccessToken)
                SupabaseSDK.RaiseMaintenanceDetected();

            return response;
        }

        private bool ShouldAttach(string url, string bearer)
        {
            if (bearer == null || url == null || url.IndexOf("/rest/v1/", StringComparison.Ordinal) < 0)
                return false;

            if (url.IndexOf(_sessionsPath, StringComparison.Ordinal) >= 0 || url.IndexOf("/rpc/ts_session_heartbeat", StringComparison.Ordinal) >= 0)
                return false;

            // 공개 키로 부르는 요청이나 다른 로그인의 토큰을 들고 온 요청에는 싣지 않는다.
            return SupabaseSDK.IsLoggedIn && SupabaseSDK.Session?.User != null && bearer == SupabaseSDK.Session.AccessToken;
        }

        private static string AccessTokenOf(Dictionary<string, string> headers)
        {
            if (headers == null || !headers.TryGetValue("Authorization", out var auth) || auth == null)
                return null;
            return auth.StartsWith("Bearer ", StringComparison.Ordinal) ? auth.Substring(7) : null;
        }
    }
}
