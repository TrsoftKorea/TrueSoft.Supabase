// =============================================================================
// PlayNanoo → Supabase SDK 이관 런타임
//
// [사용법]
// 1. 이 파일을 프로젝트로 복사 (Package Manager > Samples > PlayNanoo 이관)
// 2. YourSaveData → 생성기로 만든 실제 세이브 클래스명으로 교체
// 3. NanooStorageKey → PlayNanoo 콘솔에 등록한 스토리지 키로 교체
// 4. 씬에서 SupabaseRuntime 대신 이 컴포넌트를 배치
//
// [게임 코드에서 로그인 호출 — 런타임 유무와 무관하게 동일]
//   await Supabase.TrySignInAnonymouslyAsync()
//   await Supabase.TrySignInWithGoogleAsync()
//   await Supabase.TrySignInWithAppleIdTokenAsync(token)
//   await Supabase.TrySignOutFullyAsync()
//   await Supabase.TryRequestMyWithdrawalAsync()
//
// [PlayNanoo 제거 후]
// 1. 이 파일 삭제
// 2. 씬에 SupabaseRuntime 배치
// 3. 게임 코드 변경 없음 (Supabase.* 호출은 그대로)
// 4. YourSaveData.* 접근 코드 변경 없음
// =============================================================================

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PlayNANOO;
using TrueBase.Unity;
using TrueBase.Unity.Config;
using UnityEngine;

/// <summary>
/// PlayNanoo + SDK 병행 운영 런타임.
/// SupabaseRuntime을 대신하여 씬에 하나만 배치합니다.
/// Awake 시 인터셉터를 등록해 Supabase.Try* 호출이 PlayNanoo를 자동으로 경유합니다.
/// [TODO] YourSaveData → 생성기로 만든 실제 세이브 클래스명으로 교체하세요.
/// </summary>
public class PlayNanooRuntime : SupabaseRuntime
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

        Supabase.RegisterPlayNanooInterceptors(
            signInAnonymously:       InterceptSignInAnonymously,
            signInWithGoogleIdToken: InterceptSignInWithGoogleIdToken,
            signInWithAppleIdToken:  InterceptSignInWithAppleIdToken,
            signOutFully:            InterceptSignOutFully,
            requestMyWithdrawal:     InterceptRequestMyWithdrawal
        );
    }

    private void OnDestroy()
    {
        Supabase.UnregisterPlayNanooInterceptors();
    }

    // ── 인터셉터 구현 ─────────────────────────────────────────────────────────

    private async Task<bool> InterceptSignInAnonymously(Func<Task<bool>> sdkSignIn)
    {
        var tcs = new TaskCompletionSource<bool>();
        _plugin.AccountManagerV20240401.GuestSignIn(async (status, _, _, values) =>
        {
            if (!await HandleNanooCallback(status, values, "guest")) { tcs.SetResult(false); return; }
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
                if (!await HandleNanooCallback(status, values, "google")) { tcs.SetResult(false); return; }
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
                if (!await HandleNanooCallback(status, values, "apple")) { tcs.SetResult(false); return; }
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

    // ── Apple 로그인 (Android 전용) ───────────────────────────────────────────

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
                    await Supabase.TrySignInAnonymouslyAsync();
                else
                    OnWithdrawalRestored?.Invoke();
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

        var nanooTime = ParseNanooTimestamp(nanooJson);
        var sdkTime   = DateTime.TryParse(sdkRow.updated_at, out var t1) ? t1 : DateTime.MinValue;

        if (nanooTime > sdkTime)
        {
            var nanooRow = JsonUtility.FromJson<YourSaveData.Row>(nanooJson);
            await Supabase.TryPatchUserDataDiffAsync(sdkRow, nanooRow);
            YourSaveData.Instance.ApplyRow(nanooRow);
        }
        else
        {
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
                    Debug.LogWarning("[PlayNanooRuntime] PlayNanoo 저장 실패");
            });
    }
}
