// =============================================================================
// PlayNanoo → Supabase SDK 이관 브릿지
//
// [사용법]
// 1. 이 파일을 프로젝트로 복사 (Package Manager > Samples > PlayNanoo 이관)
// 2. YourSaveData → 생성기로 만든 실제 세이브 클래스명으로 교체
// 3. NanooStorageKey → PlayNanoo 콘솔에 등록한 스토리지 키로 교체
// 4. Inspector에서 Google Client Id 입력 (Google 로그인 사용 시)
// 5. 씬에서 SupabaseRuntime 대신 이 컴포넌트를 배치
//
// [게임 코드에서 로그인 호출]
//   await Supabase.TrySignInAnonymouslyAsync()         — 그대로 사용
//   bridge.TrySignInWithGoogleAsync()                  — Google OAuth (브릿지 메서드)
//   await Supabase.TrySignInWithAppleIdTokenAsync(tok) — 그대로 사용
//   await Supabase.TrySignOutFullyAsync()              — 그대로 사용
//   await Supabase.TryRequestMyWithdrawalAsync()       — 그대로 사용
//
// [PlayNanoo 제거 후]
// 1. 이 파일 삭제
// 2. 씬에 SupabaseRuntime 배치
// 3. bridge.TrySignInWithGoogleAsync() → await Supabase.TrySignInWithGoogleAsync() 로 교체
// 4. 나머지 Supabase.* 호출은 변경 없음
// 5. YourSaveData.* 접근 코드는 변경 없음
// =============================================================================

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PlayNANOO;
using TrueBase.Unity;
using UnityEngine;

/// <summary>
/// PlayNanoo + SDK 병행 운영 브릿지.
/// SupabaseRuntime을 대신하여 씬에 하나만 배치합니다.
/// Awake 시 SupabaseSDK에 인터셉터를 등록해 Supabase.Try* 호출을 자동으로 가로챕니다.
/// [TODO] YourSaveData → 생성기로 만든 실제 세이브 클래스명으로 교체하세요.
/// </summary>
public class PlayNanooMigrationBridge : SupabaseRuntime
{
    // [TODO] PlayNanoo 콘솔에 등록한 스토리지 키로 교체
    private const string NanooStorageKey = "save";

    // Google 로그인에 사용하는 웹 OAuth 클라이언트 ID (Google Cloud Console에서 발급)
    [SerializeField] private string _googleClientId;

    private Plugin _plugin;
    private string _nanooAccessToken;  // 로그인 성공 시 저장, 로그아웃에 사용
    private string _pendingLoginType;  // "guest"|"google"|"apple" — 탈퇴 복구 후 재로그인에 사용

    private TaskCompletionSource<bool> _googleSignInTcs;

    // ── 이벤트 ───────────────────────────────────────────────────────────────

    /// <summary>로그인 시 탈퇴 신청 계정이 감지됨. withdrawalKey를 받아 복구 여부를 결정하세요.</summary>
    public event Action<string> OnWithdrawalPending;

    /// <summary>탈퇴 복구 완료. Google·Apple은 재인증 UI를 표시하고 로그인을 다시 호출하세요.</summary>
    public event Action OnWithdrawalRestored;

    // ── 초기화 ───────────────────────────────────────────────────────────────

    protected override void Awake()
    {
        base.Awake();
        _plugin = Plugin.GetInstance();

        // SupabaseSDK.Try* 호출을 이 브릿지가 가로챕니다.
        SupabaseSDK.RegisterPlayNanooInterceptors(
            signInAnonymously:       InterceptSignInAnonymously,
            signInWithGoogleIdToken: InterceptSignInWithGoogleIdToken,
            signInWithAppleIdToken:  InterceptSignInWithAppleIdToken,
            signOutFully:            InterceptSignOutFully,
            requestMyWithdrawal:     InterceptRequestMyWithdrawal
        );
    }

    private void Start()
    {
        Application.deepLinkActivated += OnGoogleDeepLink;   // Android 구글 OAuth 콜백
        _plugin.SetGoogleAuthCallback(OnGoogleAuthCallback);  // iOS 구글 OAuth 콜백
    }

    private void OnDestroy()
    {
        Application.deepLinkActivated -= OnGoogleDeepLink;
        SupabaseSDK.UnregisterPlayNanooInterceptors();
    }

    // ── 인터셉터 구현 ─────────────────────────────────────────────────────────

    private async Task<bool> InterceptSignInAnonymously(Func<Task<bool>> sdkSignIn)
    {
        var tcs = new TaskCompletionSource<bool>();
        _plugin.AccountManagerV20240401.GuestSignIn(async (status, _, _, values) =>
        {
            if (!await HandleNanooCallback(status, values, "guest"))
            {
                tcs.SetResult(false);
                return;
            }
            var ok = await sdkSignIn();
            if (ok) await SyncDataAfterLogin();
            tcs.SetResult(ok);
        });
        return await tcs.Task;
    }

    private async Task<bool> InterceptSignInWithGoogleIdToken(string token, Func<Task<bool>> sdkSignIn)
    {
        var tcs = new TaskCompletionSource<bool>();
        _plugin.AccountManagerV20240401.SocialSignIn(
            token, Configure.PN_ACCOUNT_GOOGLE,
            async (status, _, _, values) =>
            {
                if (!await HandleNanooCallback(status, values, "google"))
                {
                    tcs.SetResult(false);
                    return;
                }
                var ok = await sdkSignIn();
                if (ok) await SyncDataAfterLogin();
                tcs.SetResult(ok);
            });
        return await tcs.Task;
    }

    private async Task<bool> InterceptSignInWithAppleIdToken(string token, Func<Task<bool>> sdkSignIn)
    {
        var tcs = new TaskCompletionSource<bool>();
        _plugin.AccountManagerV20240401.SocialSignIn(
            token, Configure.PN_ACCOUNT_APPLE_ID,
            async (status, _, _, values) =>
            {
                if (!await HandleNanooCallback(status, values, "apple"))
                {
                    tcs.SetResult(false);
                    return;
                }
                var ok = await sdkSignIn();
                if (ok) await SyncDataAfterLogin();
                tcs.SetResult(ok);
            });
        return await tcs.Task;
    }

    private async Task<bool> InterceptSignOutFully(Func<Task<bool>> sdkSignOut)
    {
        var tcs = new TaskCompletionSource<bool>();
        _plugin.AccountManagerV20240401.TokenSignOut(
            _nanooAccessToken,
            async (_, _, _, _) =>
            {
                _nanooAccessToken = null;
                tcs.SetResult(await sdkSignOut());
            });
        return await tcs.Task;
    }

    private async Task<bool> InterceptRequestMyWithdrawal(Func<Task<bool>> sdkWithdrawal)
    {
        var tcs = new TaskCompletionSource<bool>();
        _plugin.AccountManagerV20240401.WithDrawal(15, async (status, _, _, _) =>
        {
            if (status != Configure.PN_API_STATE_SUCCESS) { tcs.SetResult(false); return; }
            tcs.SetResult(await sdkWithdrawal());
        });
        return await tcs.Task;
    }

    // ── PlayNanoo 콜백 공통 처리 ──────────────────────────────────────────────

    /// <summary>PlayNanoo 로그인 콜백 상태를 처리합니다. false 반환 시 SDK 로그인을 중단하세요.</summary>
    private Task<bool> HandleNanooCallback(string status, Dictionary<string, object> values, string loginType)
    {
        if (status == Configure.PN_API_STATE_SUCCESS)
        {
            _nanooAccessToken = values["access_token"]?.ToString();
            return Task.FromResult(true);
        }

        if (values?["ErrorCode"]?.ToString() == "30007")
        {
            _pendingLoginType = loginType;
            OnWithdrawalPending?.Invoke(values["WithdrawalKey"]?.ToString());
        }
        return Task.FromResult(false);
    }

    // ── Google 로그인 (브릿지 전용 — OAuth 흐름 특성상 브릿지 메서드 필요) ───────

    /// <summary>
    /// 구글 로그인. PlayNanoo OAuth 브라우저를 열고 토큰 수신 후 Supabase.TrySignInWithGoogleIdTokenAsync를 자동 호출합니다.
    /// clientId는 Inspector의 Google Client Id 필드에 입력합니다.
    /// 제거 후: await Supabase.TrySignInWithGoogleAsync()
    /// </summary>
    public Task<bool> TrySignInWithGoogleAsync()
    {
        _googleSignInTcs = new TaskCompletionSource<bool>();
        _plugin.AccountManagerV20240401.SignInWithGoogle(_googleClientId);
        return _googleSignInTcs.Task;
    }

    // Android: DeepLink 콜백
    private void OnGoogleDeepLink(string url)
    {
        var token = ExtractIdToken(url);
        if (!string.IsNullOrEmpty(token)) CompleteGoogleSignIn(token);
    }

    // iOS: Google OAuth 콜백
    private void OnGoogleAuthCallback(string result)
    {
        if (result.StartsWith("error:")) return;
        var token = ExtractIdToken(result);
        if (!string.IsNullOrEmpty(token)) CompleteGoogleSignIn(token);
    }

    // 토큰 수신 → Supabase.TrySignInWithGoogleIdTokenAsync 호출 (인터셉터가 PlayNanoo SocialSignIn 처리)
    private async void CompleteGoogleSignIn(string token)
    {
        var ok = await Supabase.TrySignInWithGoogleIdTokenAsync(token);
        _googleSignInTcs?.TrySetResult(ok);
        _googleSignInTcs = null;
    }

    private static string ExtractIdToken(string url)
    {
        if (!url.Contains("#")) return null;
        foreach (var p in url.Split('#')[1].Split('&'))
            if (p.StartsWith("id_token="))
                return p.Substring("id_token=".Length);
        return null;
    }

    /// <summary>애플 로그인 (Android). PlayNanoo 내장 WebView로 토큰 획득 후 Supabase.TrySignInWithAppleIdTokenAsync 자동 호출.</summary>
    public void StartAppleSignInAndroid() =>
        _plugin.OpenAppleID(
            async token => await Supabase.TrySignInWithAppleIdTokenAsync(token),
            _ => { });

    // ── 탈퇴 복구 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 탈퇴 복구. OnWithdrawalPending에서 받은 withdrawalKey를 사용합니다.
    /// 게스트: 자동 재로그인 / Google·Apple: OnWithdrawalRestored 이벤트 후 개발자가 재인증 UI 표시.
    /// </summary>
    public void RestoreWithdrawal(string withdrawalKey)
    {
        _plugin.AccountManagerV20240401.WithDrawalRestore(
            withdrawalKey,
            async (status, _, _, _) =>
            {
                if (status != Configure.PN_API_STATE_SUCCESS) return;

                if (_pendingLoginType == "guest")
                    await Supabase.TrySignInAnonymouslyAsync(); // 인터셉터가 PlayNanoo + SDK 처리
                else
                    OnWithdrawalRestored?.Invoke();             // 개발자가 재로그인 UI 표시

                _pendingLoginType = null;
            });
    }

    // ── 데이터 동기화 ─────────────────────────────────────────────────────────

    private async Task SyncDataAfterLogin()
    {
        var (ok, hasRow, sdkRow) =
            await Supabase.TryLoadUserDataAttributedWithRowStateAsync<YourSaveData.Row>();
        if (!ok) return;

        var nanooJson = await LoadRawFromNanoo();

        if (!hasRow)
        {
            // 최초 이관: PlayNanoo → SDK
            if (nanooJson != null)
            {
                var nanooRow = JsonUtility.FromJson<YourSaveData.Row>(nanooJson);
                await Supabase.TryPatchUserDataDiffAsync(new YourSaveData.Row(), nanooRow);
                YourSaveData.Instance.ApplyRow(nanooRow);
            }
            else
            {
                await YourSaveData.TryLoadAsync();
            }
            return;
        }

        // lastCheckTime(PlayNanoo) vs updated_at(SDK) 비교
        var nanooTime = ParseNanooTimestamp(nanooJson);
        var sdkTime   = DateTime.TryParse(sdkRow.updated_at, out var t1) ? t1 : DateTime.MinValue;

        if (nanooTime > sdkTime)
        {
            // PlayNanoo 최신 → SDK 갱신
            var nanooRow = JsonUtility.FromJson<YourSaveData.Row>(nanooJson);
            await Supabase.TryPatchUserDataDiffAsync(sdkRow, nanooRow);
            YourSaveData.Instance.ApplyRow(nanooRow);
        }
        else
        {
            // SDK 최신 (또는 동점) → PlayNanoo 갱신
            YourSaveData.Instance.ApplyRow(sdkRow);
            SaveToNanoo(sdkRow);
        }
    }

    // ── PlayNanoo 저장/로드 ───────────────────────────────────────────────────

    private Task<string> LoadRawFromNanoo()
    {
        var tcs = new TaskCompletionSource<string>();
        _plugin.Storage.Load(NanooStorageKey, (status, _, _, values) =>
        {
            if (status != Configure.PN_API_STATE_SUCCESS) { tcs.SetResult(null); return; }
            tcs.SetResult(values["StorageValue"]?.ToString());
        });
        return tcs.Task;
    }

    [Serializable]
    private class NanooTimestampHelper { public string lastCheckTime; }

    private static DateTime ParseNanooTimestamp(string json)
    {
        if (string.IsNullOrEmpty(json)) return DateTime.MinValue;
        var h = JsonUtility.FromJson<NanooTimestampHelper>(json);
        return DateTime.TryParse(h?.lastCheckTime, out var t) ? t : DateTime.MinValue;
    }

    /// <summary>PlayNanoo에 데이터를 저장합니다. SDK 저장 직후 또는 SDK 데이터가 최신일 때 호출하세요.</summary>
    public void SaveToNanoo(YourSaveData.Row row)
    {
        _plugin.Storage.Save(NanooStorageKey, JsonUtility.ToJson(row), true,
            (status, _, _, _) =>
            {
                if (status != Configure.PN_API_STATE_SUCCESS)
                    Debug.LogWarning("[Bridge] PlayNanoo 저장 실패");
            });
    }
}
