// =============================================================================
// PlayNanoo → Supabase SDK 이관 브릿지
//
// [사용법]
// 1. 이 파일을 프로젝트로 복사 (Package Manager > Samples > PlayNanoo 이관)
// 2. YourSaveData → 생성기로 만든 실제 세이브 클래스명으로 교체
// 3. NanooStorageKey → PlayNanoo 콘솔에 등록한 스토리지 키로 교체
// 4. 씬에서 SupabaseRuntime 대신 이 컴포넌트를 배치
//
// [PlayNanoo 제거 후]
// 1. 이 파일 삭제
// 2. 씬에 SupabaseRuntime 배치 후 TriggerAutoLoginAsync() 직접 호출
// 3. YourSaveData.* 호출은 변경 없음
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
/// [TODO] YourSaveData → 생성기로 만든 실제 세이브 클래스명으로 교체하세요.
/// </summary>
public class PlayNanooMigrationBridge : SupabaseRuntime
{
    // [TODO] PlayNanoo 콘솔에 등록한 스토리지 키로 교체
    private const string NanooStorageKey = "save";

    private Plugin _plugin;
    private string _nanooAccessToken;  // 로그인 성공 시 저장, 로그아웃에 사용
    private string _pendingLoginType;  // "guest"|"google"|"apple" — 탈퇴 복구 후 재로그인에 사용

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
    }

    private void Start()
    {
        Application.deepLinkActivated += OnGoogleDeepLink;    // Android 구글 OAuth 콜백
        _plugin.SetGoogleAuthCallback(OnGoogleAuthCallback);   // iOS 구글 OAuth 콜백
    }

    private void OnDestroy()
    {
        Application.deepLinkActivated -= OnGoogleDeepLink;
    }

    // ── 공통 로그인 콜백 처리 ─────────────────────────────────────────────────

    /// <summary>
    /// PlayNanoo 로그인 콜백을 처리합니다.
    /// 성공 시 SDK 로그인 → 데이터 동기화, 탈퇴 감지 시 OnWithdrawalPending 이벤트를 발행합니다.
    /// </summary>
    private async Task HandleLoginCallback(
        string status,
        Dictionary<string, object> values,
        Func<Task<bool>> sdkLogin,
        string loginType)
    {
        if (status != Configure.PN_API_STATE_SUCCESS)
        {
            if (values?["ErrorCode"]?.ToString() == "30007")
            {
                _pendingLoginType = loginType;
                OnWithdrawalPending?.Invoke(values["WithdrawalKey"]?.ToString());
            }
            return;
        }

        _nanooAccessToken = values["access_token"]?.ToString();

        if (!await sdkLogin()) return;
        await SyncDataAfterLogin();
    }

    // ── 로그인 ───────────────────────────────────────────────────────────────

    /// <summary>게스트(익명) 로그인. PlayNanoo + SDK 동시 처리.</summary>
    public void GuestSignIn()
    {
        _plugin.AccountManagerV20240401.GuestSignIn(async (status, _, _, values) =>
            await HandleLoginCallback(status, values,
                () => Supabase.TrySignInAnonymouslyAsync(), "guest"));
    }

    /// <summary>구글 로그인 Step 1: 브라우저 열기.</summary>
    public void StartGoogleSignIn(string clientId) =>
        _plugin.AccountManagerV20240401.SignInWithGoogle(clientId);

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

    /// <summary>구글 로그인 Step 2: 토큰 수신 후 PlayNanoo + SDK 동시 처리.</summary>
    private void CompleteGoogleSignIn(string token)
    {
        _plugin.AccountManagerV20240401.SocialSignIn(
            token, Configure.PN_ACCOUNT_GOOGLE,
            async (status, _, _, values) =>
                await HandleLoginCallback(status, values,
                    () => Supabase.TrySignInWithGoogleIdTokenAsync(token), "google"));
    }

    private static string ExtractIdToken(string url)
    {
        if (!url.Contains("#")) return null;
        foreach (var p in url.Split('#')[1].Split('&'))
            if (p.StartsWith("id_token="))
                return p.Substring("id_token=".Length);
        return null;
    }

    /// <summary>
    /// 애플 로그인 (iOS: AppleAuthManager로 idToken 획득 후 호출 / Android: StartAppleSignInAndroid() 내부에서 자동 호출).
    /// </summary>
    public void CompleteAppleSignIn(string idToken)
    {
        _plugin.AccountManagerV20240401.SocialSignIn(
            idToken, Configure.PN_ACCOUNT_APPLE_ID,
            async (status, _, _, values) =>
                await HandleLoginCallback(status, values,
                    () => Supabase.TrySignInWithAppleIdTokenAsync(idToken), "apple"));
    }

    /// <summary>애플 로그인 (Android). PlayNanoo 내장 WebView로 토큰 획득 후 CompleteAppleSignIn 자동 호출.</summary>
    public void StartAppleSignInAndroid() =>
        _plugin.OpenAppleID(CompleteAppleSignIn, _ => { });

    // ── 로그아웃 ─────────────────────────────────────────────────────────────

    /// <summary>로그아웃. PlayNanoo 토큰 해지 후 SDK 로그아웃.</summary>
    public void SignOut()
    {
        _plugin.AccountManagerV20240401.TokenSignOut(
            _nanooAccessToken,
            async (_, _, _, _) =>
            {
                _nanooAccessToken = null;
                await Supabase.TrySignOutFullyAsync();
            });
    }

    // ── 탈퇴 ─────────────────────────────────────────────────────────────────

    /// <summary>탈퇴 신청. PlayNanoo + SDK 동시 처리.</summary>
    public void RequestWithdrawal(int periodDays = 15)
    {
        _plugin.AccountManagerV20240401.WithDrawal(periodDays, async (status, _, _, _) =>
        {
            if (status != Configure.PN_API_STATE_SUCCESS) return;
            await Supabase.TryRequestMyWithdrawalAsync();
        });
    }

    /// <summary>
    /// 탈퇴 복구. OnWithdrawalPending에서 받은 withdrawalKey를 사용합니다.
    /// 게스트: 자동 재로그인 / Google·Apple: OnWithdrawalRestored 이벤트 후 개발자가 재인증 UI 표시.
    /// </summary>
    public void RestoreWithdrawal(string withdrawalKey)
    {
        _plugin.AccountManagerV20240401.WithDrawalRestore(
            withdrawalKey,
            (status, _, _, _) =>
            {
                if (status != Configure.PN_API_STATE_SUCCESS) return;

                if (_pendingLoginType == "guest")
                    GuestSignIn();               // 토큰 불필요 → PlayNanoo + SDK 자동 재로그인
                else
                    OnWithdrawalRestored?.Invoke(); // 개발자가 재로그인 UI 표시

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
