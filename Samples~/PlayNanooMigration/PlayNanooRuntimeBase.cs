// =============================================================================
// PlayNANOO → Supabase SDK 이관 런타임 — 추상 베이스
//
// [사용법]
// 1. Package Manager > Samples > PlayNANOO Migration 에서 Import
// 2. 씬에서 SupabaseRuntime 대신 버전에 맞는 컴포넌트를 배치
//    - PlayNANOO SDK 신버전 (AccountManagerV20240401): PlayNanooRuntime
//    - PlayNANOO SDK 구버전 (AccountGuestSignIn / AccountManager.*): PlayNanooLegacyRuntime
//    (StaticUserSave 인스턴스는 자동 연결됩니다)
//
// [게임 코드에서 로그인 호출 — 런타임 유무와 무관하게 동일]
//   await Supabase.TrySignInAnonymouslyAsync()
//   await Supabase.TrySignInWithGoogleAsync()
//   await Supabase.TrySignInWithAppleIdTokenAsync(token)
//   await Supabase.TryLinkGoogleToCurrentAnonymousWithIdTokenAsync(token)   // 익명 → Google 연동
//   await Supabase.TryLinkAppleToCurrentAnonymousWithIdTokenAsync(token)    // 익명 → Apple 연동
//   await Supabase.TrySignOutFullyAsync()
//   await Supabase.TryRequestMyWithdrawalAsync()
//
// [PlayNANOO 제거 후]
// 1. 이 파일과 PlayNanooRuntime.cs / PlayNanooLegacyRuntime.cs 삭제
// 2. 씬에 SupabaseRuntime 배치
// 3. 게임 코드 변경 없음 (Supabase.* 호출은 그대로)
// =============================================================================

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PlayNANOO;
using TrueBase.Core.Common;
using TrueBase.Core.Models;
using TrueBase.Unity;
using TrueBase.Unity.Config;
using UnityEngine;

/// <summary>
/// PlayNANOO + SDK 병행 운영 런타임 공통 베이스.
/// PlayNANOO SDK 버전에 무관한 인터셉터·데이터 동기화·탈퇴 처리를 담당합니다.
/// API 호출부(5개 추상 메서드)만 서브클래스에서 버전별로 구현합니다.
/// </summary>
public abstract class PlayNanooRuntimeBase : SupabaseRuntime
{
    protected Plugin _plugin;
    private string _nanooAccessToken;   // 로그인 성공 시 저장, 로그아웃·롤백에 사용
    private string _nanooNickname;      // 닉네임 변경 롤백용
    private string _pendingLoginType;   // "guest"|"google"|"apple" — 탈퇴 취소 후 재로그인에 사용

    private const string NanooAccessTokenKey = "TrueBase.NanooAccessToken";

    /// <summary>PlayNANOO 로그인 성공 시 반환된 uuid. 로그인 전에는 null.</summary>
    public static string UserId { get; private set; }

    /// <summary>PlayNANOO 로그인 성공 시 반환된 openid. SDK가 반환하지 않으면 null.</summary>
    public static string OpenId { get; private set; }

    private INanooSaveSyncable Save => Supabase.GetNanooSaveBridge();

    // ── 이벤트 ───────────────────────────────────────────────────────────────

    /// <summary>로그인 시 탈퇴 신청 계정이 감지됨. withdrawalKey를 받아 복구 여부를 결정하세요.</summary>
    public event Action<string> OnWithdrawalPending;

    /// <summary>탈퇴 취소 완료. Google·Apple은 재인증 UI를 표시하고 로그인을 다시 호출하세요.</summary>
    public event Action OnWithdrawalRestored;

    /// <summary>탈퇴 취소는 성공했지만 Supabase 재로그인에 실패. 개발자가 직접 재로그인 호출.</summary>
    public event Action OnWithdrawalRestoreLoginFailed;

    // ── 추상 메서드 (PlayNANOO SDK 버전별 구현) ────────────────────────────────

    /// <summary>PlayNANOO 게스트 로그인. 완료 후 callback(status, values)을 호출해야 합니다.</summary>
    protected abstract void NanooGuestSignIn(
        Func<string, Dictionary<string, object>, Task> callback);

    /// <summary>PlayNANOO 소셜 로그인. 완료 후 callback(status, values)을 호출해야 합니다.</summary>
    protected abstract void NanooSocialSignIn(
        string token, string accountType,
        Func<string, Dictionary<string, object>, Task> callback);

    /// <summary>PlayNANOO 토큰 로그아웃. 완료 후 callback()을 호출해야 합니다.</summary>
    protected abstract void NanooTokenSignOut(string accessToken, Func<Task> callback);

    /// <summary>PlayNANOO 탈퇴 신청. 완료 후 callback(status)을 호출해야 합니다.</summary>
    protected abstract void NanooWithDrawal(int holdDays, Func<string, Task> callback);

    /// <summary>PlayNANOO 탈퇴 취소. 완료 후 callback(status)을 호출해야 합니다.</summary>
    protected abstract void NanooWithDrawalRestore(string key, Func<string, Task> callback);

    /// <summary>PlayNANOO 닉네임 변경. 완료 후 callback(status)을 호출해야 합니다.</summary>
    protected abstract void NanooSetNickname(string nickname, Func<string, Task> callback);

    /// <summary>PlayNANOO access token으로 로그인합니다. 완료 후 callback(status, values)을 호출해야 합니다.</summary>
    protected abstract void NanooTokenSignIn(string accessToken, Func<string, Dictionary<string, object>, Task> callback);

    // ── PlayNANOO IAP 메서드 (virtual — 필요 시 서브클래스에서 override) ─────────

    /// <summary>
    /// PlayNANOO iOS IAP 검증 호출. callback(status)로 결과를 반환합니다.
    /// 구/신버전 PlayNANOO 모두 동일 API이므로 일반적으로 override 불필요합니다.
    /// </summary>
    protected virtual void NanooIAPIOS(
        string receipt, string productId, string currency, double price,
        Func<string, Task> callback)
        => _plugin.IAP.IOS(receipt, productId, currency, (float)price,
            async (s, _, _, _) => await callback(s));

    /// <summary>
    /// PlayNANOO Android IAP 검증 호출. callback(status)로 결과를 반환합니다.
    /// 구/신버전 PlayNANOO 모두 동일 API이므로 일반적으로 override 불필요합니다.
    /// </summary>
    protected virtual void NanooIAPAndroid(
        string purchaseToken,
        Func<string, Task> callback)
        => _plugin.IAP.Android(purchaseToken,
            async (s, _, _, _) => await callback(s));

    // ── 초기화 ───────────────────────────────────────────────────────────────

    protected override void Awake()
    {
        base.Awake();
        _plugin = Plugin.GetInstance();

        Supabase.RegisterPlayNanooInterceptors(
            signInAnonymously:                       InterceptSignInAnonymously,
            signInWithGoogleIdToken:                 InterceptSignInWithGoogleIdToken,
            signInWithAppleIdToken:                  InterceptSignInWithAppleIdToken,
            signOutFully:                            InterceptSignOutFully,
            requestMyWithdrawal:                     InterceptRequestMyWithdrawal,
            linkGoogleToCurrentAnonymousWithIdToken: InterceptLinkGoogleToCurrentAnonymousWithIdToken,
            linkAppleToCurrentAnonymousWithIdToken:  InterceptLinkAppleToCurrentAnonymousWithIdToken,
            setMyDisplayName:                        InterceptSetMyDisplayName
        );

        // IAP: PlayNanooRuntime이 있으면 SK1을 강제하고 PlayNanoo IAP를 인터셉터로 등록합니다.
#if UNITY_IAP_V5_1 && UNITY_IOS
        UnityEngine.Purchasing.StoreKitSelector.forceStoreKit1 = true;
#elif UNITY_IAP_V5 && UNITY_IOS
        Debug.LogError("[PlayNanoo] Unity IAP 5.0.x에서는 iOS 15+에서 PlayNanoo IAP가 작동하지 않습니다. Unity IAP 5.1+로 업그레이드하세요.");
#endif

        Supabase.RegisterIAPAppleInterceptor(async (receipt, productId, sdkVerify) =>
        {
            var tcs = new TaskCompletionSource<SupabaseResult<AppleIAPPurchaseResponse>>();
            NanooIAPIOS(receipt, productId, string.Empty, 0d, async status =>
            {
                if (status != Configure.PN_API_STATE_SUCCESS)
                {
                    tcs.SetResult(SupabaseResult<AppleIAPPurchaseResponse>.Fail("playnanoo_iap_ios_failed"));
                    return;
                }
                tcs.SetResult(await sdkVerify());
            });
            return await tcs.Task;
        });

        Supabase.RegisterIAPGoogleInterceptor(async (purchaseToken, productId, priceAmount, priceCurrency, sdkVerify) =>
        {
            var tcs = new TaskCompletionSource<SupabaseResult<GooglePlayPurchaseResponse>>();
            NanooIAPAndroid(purchaseToken, async status =>
            {
                if (status != Configure.PN_API_STATE_SUCCESS)
                {
                    tcs.SetResult(SupabaseResult<GooglePlayPurchaseResponse>.Fail("playnanoo_iap_android_failed"));
                    return;
                }
                tcs.SetResult(await sdkVerify());
            });
            return await tcs.Task;
        });
    }

    private void OnDestroy()
    {
        Supabase.UnregisterPlayNanooInterceptors();
        // IAP 인터셉터는 UnregisterPlayNanooInterceptors 내부에서 함께 해제됩니다.
    }

    // ── 인터셉터 구현 ─────────────────────────────────────────────────────────

    private async Task<SupabaseCallResult> InterceptSignInAnonymously(Func<Task<SupabaseCallResult>> sdkSignIn)
    {
        var tcs = new TaskCompletionSource<SupabaseCallResult>();
        NanooGuestSignIn(async (status, values) =>
        {
            if (!HandleNanooCallback(status, values, "guest"))
            {
                tcs.SetResult(SupabaseCallResult.Fail("playnanoo_guest_signin_failed"));
                return;
            }
            var result = await sdkSignIn();
            if (!result.Success) { await RollbackNanooLoginAsync(); tcs.SetResult(result); return; }
            await SyncDataAfterLogin();
            tcs.SetResult(result);
        });
        return await tcs.Task;
    }

    private async Task<SupabaseCallResult> InterceptSignInWithGoogleIdToken(string token, Func<Task<SupabaseCallResult>> sdkSignIn)
    {
        var tcs = new TaskCompletionSource<SupabaseCallResult>();
        NanooSocialSignIn(token, Configure.PN_ACCOUNT_GOOGLE, async (status, values) =>
        {
            if (!HandleNanooCallback(status, values, "google"))
            {
                tcs.SetResult(SupabaseCallResult.Fail("playnanoo_google_signin_failed"));
                return;
            }
            var result = await sdkSignIn();
            if (!result.Success) { await RollbackNanooLoginAsync(); tcs.SetResult(result); return; }
            await SyncDataAfterLogin();
            tcs.SetResult(result);
        });
        return await tcs.Task;
    }

    private async Task<SupabaseCallResult> InterceptSignInWithAppleIdToken(string token, Func<Task<SupabaseCallResult>> sdkSignIn)
    {
        var tcs = new TaskCompletionSource<SupabaseCallResult>();
        NanooSocialSignIn(token, Configure.PN_ACCOUNT_APPLE_ID, async (status, values) =>
        {
            if (!HandleNanooCallback(status, values, "apple"))
            {
                tcs.SetResult(SupabaseCallResult.Fail("playnanoo_apple_signin_failed"));
                return;
            }
            var result = await sdkSignIn();
            if (!result.Success) { await RollbackNanooLoginAsync(); tcs.SetResult(result); return; }
            await SyncDataAfterLogin();
            tcs.SetResult(result);
        });
        return await tcs.Task;
    }

    private async Task<SupabaseCallResult> InterceptSignOutFully(Func<Task<SupabaseCallResult>> sdkSignOut)
    {
        if (string.IsNullOrEmpty(_nanooAccessToken))
            return await sdkSignOut();

        var tcs = new TaskCompletionSource<SupabaseCallResult>();
        NanooTokenSignOut(_nanooAccessToken, async () =>
        {
            _nanooAccessToken = null;
            ClearNanooTokens();
            UserId = null;
            OpenId = null;
            var result = await sdkSignOut();
            if (!result.Success)
                Debug.LogWarning("[PlayNanooRuntime] Supabase 로그아웃 실패. PlayNANOO 로그아웃은 완료됨.");
            tcs.SetResult(result);
        });
        return await tcs.Task;
    }

    private async Task<SupabaseCallResult> InterceptRequestMyWithdrawal(Func<Task<SupabaseCallResult>> sdkWithdrawal)
    {
        var tcs = new TaskCompletionSource<SupabaseCallResult>();
        NanooWithDrawal(15, async status =>
        {
            if (status != Configure.PN_API_STATE_SUCCESS)
            {
                tcs.SetResult(SupabaseCallResult.Fail("playnanoo_withdrawal_request_failed"));
                return;
            }
            var result = await sdkWithdrawal();
            if (!result.Success)
                Debug.LogWarning("[PlayNanooRuntime] Supabase 탈퇴 실패. PlayNANOO 탈퇴는 완료됨.");
            tcs.SetResult(result);
        });
        return await tcs.Task;
    }

    // ── 닉네임 변경 인터셉터 ──────────────────────────────────────────────────────

    private Task<SupabaseCallResult> InterceptSetMyDisplayName(string nickname, Func<Task<SupabaseCallResult>> sdkSet)
    {
        var tcs = new TaskCompletionSource<SupabaseCallResult>();
        NanooSetNickname(nickname, async status =>
        {
            if (status != Configure.PN_API_STATE_SUCCESS)
            {
                tcs.SetResult(SupabaseCallResult.Fail("playnanoo_set_nickname_failed"));
                return;
            }
            var result = await sdkSet();
            if (!result.Success)
            {
                var prev = _nanooNickname;
                if (!string.IsNullOrEmpty(prev))
                    NanooSetNickname(prev, _ => Task.CompletedTask);
                tcs.SetResult(result);
                return;
            }
            _nanooNickname = nickname;
            tcs.SetResult(result);
        });
        return tcs.Task;
    }

    // ── 익명 → 소셜 연동 인터셉터 ────────────────────────────────────────────────

    private async Task<SupabaseCallResult> InterceptLinkGoogleToCurrentAnonymousWithIdToken(string token, Func<Task<SupabaseCallResult>> sdkLink)
    {
        var tcs = new TaskCompletionSource<SupabaseCallResult>();
        NanooSocialSignIn(token, Configure.PN_ACCOUNT_GOOGLE, async (status, values) =>
        {
            if (!HandleNanooCallback(status, values, "google"))
            {
                tcs.SetResult(SupabaseCallResult.Fail("playnanoo_google_link_failed"));
                return;
            }
            var result = await sdkLink();
            if (!result.Success) { await RollbackNanooLoginAsync(); tcs.SetResult(result); return; }
            await SyncDataAfterLogin();
            tcs.SetResult(result);
        });
        return await tcs.Task;
    }

    private async Task<SupabaseCallResult> InterceptLinkAppleToCurrentAnonymousWithIdToken(string token, Func<Task<SupabaseCallResult>> sdkLink)
    {
        var tcs = new TaskCompletionSource<SupabaseCallResult>();
        NanooSocialSignIn(token, Configure.PN_ACCOUNT_APPLE_ID, async (status, values) =>
        {
            if (!HandleNanooCallback(status, values, "apple"))
            {
                tcs.SetResult(SupabaseCallResult.Fail("playnanoo_apple_link_failed"));
                return;
            }
            var result = await sdkLink();
            if (!result.Success) { await RollbackNanooLoginAsync(); tcs.SetResult(result); return; }
            await SyncDataAfterLogin();
            tcs.SetResult(result);
        });
        return await tcs.Task;
    }

    // ── PlayNANOO 콜백 공통 처리 ──────────────────────────────────────────────

    /// <summary>PlayNANOO 로그인 성공 시 호출. 서브클래스에서 응답 values를 추가로 처리할 수 있습니다.</summary>
    protected virtual void OnNanooLoginSuccess(Dictionary<string, object> values) { }

    private bool HandleNanooCallback(string status, Dictionary<string, object> values, string loginType)
    {
        if (status == Configure.PN_API_STATE_SUCCESS)
        {
            _nanooAccessToken = values["access_token"]?.ToString();
            _nanooNickname    = values["nickname"]?.ToString();
            UserId  = values.TryGetValue("uuid",   out var uuidVal)   ? uuidVal?.ToString()   : null;
            OpenId  = values.TryGetValue("openID", out var openidVal) ? openidVal?.ToString() : null;
            SaveNanooTokens();
            OnNanooLoginSuccess(values);
            return true;
        }
        var errorCode = values?["ErrorCode"]?.ToString();
        if (errorCode == "30007")
        {
            _pendingLoginType = loginType;
            OnWithdrawalPending?.Invoke(values["WithdrawalKey"]?.ToString());
        }
        else
        {
            Debug.LogWarning($"[PlayNanooRuntime] PlayNANOO {loginType} 로그인 실패 — status: {status}, ErrorCode: {errorCode}");
        }
        return false;
    }

    // ── 롤백 헬퍼 ────────────────────────────────────────────────────────────

    /// <summary>PlayNANOO 로그인 성공 후 Supabase 실패 시 PlayNANOO 로그아웃으로 되돌립니다.</summary>
    private Task RollbackNanooLoginAsync()
    {
        if (string.IsNullOrEmpty(_nanooAccessToken)) return Task.CompletedTask;
        var token = _nanooAccessToken;
        _nanooAccessToken = null;
        ClearNanooTokens();
        UserId = null;
        OpenId = null;
        var tcs = new TaskCompletionSource<bool>();
        NanooTokenSignOut(token, async () => tcs.SetResult(true));
        return tcs.Task;
    }

    // ── Apple 로그인 (Android 전용) ───────────────────────────────────────────

    /// <summary>애플 로그인 (Android). PlayNANOO 내장 WebView로 토큰 획득 후 Supabase.TrySignInWithAppleIdTokenAsync 자동 호출.</summary>
    public void StartAppleSignInAndroid() =>
        _plugin.OpenAppleID(
            async token => await Supabase.TrySignInWithAppleIdTokenAsync(token));

    // ── 탈퇴 취소 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 탈퇴 취소. OnWithdrawalPending에서 받은 withdrawalKey를 사용합니다.
    /// 게스트: 자동 재로그인 후 Supabase withdrawn_at도 해제 / Google·Apple: OnWithdrawalRestored 이벤트 후 개발자가 재인증 UI 표시.
    /// 게스트 재로그인 실패 시 OnWithdrawalRestoreLoginFailed가 발행됩니다.
    /// </summary>
    public void RestoreWithdrawal(string withdrawalKey)
    {
        NanooWithDrawalRestore(withdrawalKey, async status =>
        {
            if (status != Configure.PN_API_STATE_SUCCESS) return;
            if (_pendingLoginType == "guest")
            {
                var result = await Supabase.TrySignInAnonymouslyAsync();
                if (!result.Success) { OnWithdrawalRestoreLoginFailed?.Invoke(); }
                else                 { await Supabase.TryClearMyWithdrawalAsync(); }
            }
            else
                OnWithdrawalRestored?.Invoke();
            _pendingLoginType = null;
        });
    }

    // ── 자동 로그인 후 PlayNANOO 세션 복원 ────────────────────────────────────

    protected override async Task<bool> OnAfterAutoLoginAsync(bool success)
    {
        if (!success) return false;
        var storedToken = PlayerPrefs.GetString(NanooAccessTokenKey, null);
        if (string.IsNullOrEmpty(storedToken)) return true;

        var nanooOk = await RestoreNanooSessionAsync(storedToken);
        if (!nanooOk)
            await Supabase.TrySignOutFullyAsync();
        return nanooOk;
    }

    private Task<bool> RestoreNanooSessionAsync(string accessToken)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        NanooTokenSignIn(accessToken, async (status, values) =>
        {
            if (status == Configure.PN_API_STATE_SUCCESS)
            {
                _nanooAccessToken = values["access_token"]?.ToString();
                _nanooNickname    = values.TryGetValue("nickname", out var nk) ? nk?.ToString() : _nanooNickname;
                UserId = values.TryGetValue("uuid",   out var uv) ? uv?.ToString() : null;
                OpenId = values.TryGetValue("openID", out var ov) ? ov?.ToString() : null;
                SaveNanooTokens();
                Debug.Log("[PlayNanooRuntime] PlayNANOO 토큰 로그인 성공.");
                tcs.TrySetResult(true);
            }
            else
            {
                Debug.LogWarning("[PlayNanooRuntime] PlayNANOO 토큰 로그인 실패 (토큰 만료 또는 오류). 재로그인이 필요합니다.");
                ClearNanooTokens();
                tcs.TrySetResult(false);
            }
            await Task.CompletedTask;
        });
        return tcs.Task;
    }

    private void SaveNanooTokens()
    {
        if (!string.IsNullOrEmpty(_nanooAccessToken))
            PlayerPrefs.SetString(NanooAccessTokenKey, _nanooAccessToken);
        else
            PlayerPrefs.DeleteKey(NanooAccessTokenKey);
        PlayerPrefs.Save();
    }

    private void ClearNanooTokens()
    {
        _nanooAccessToken = null;
        PlayerPrefs.DeleteKey(NanooAccessTokenKey);
        PlayerPrefs.Save();
    }

    // ── 데이터 동기화 ─────────────────────────────────────────────────────────

    private async Task SyncDataAfterLogin()
    {
        var save = Save;
        if (save == null)
        {
            Debug.LogWarning("[PlayNanooRuntime] StaticUserSave 인스턴스가 없습니다. 세이브 클래스를 초기화했는지 확인하세요.");
            return;
        }

        var (ok, hasRow, sdkTime) = await save.NanooLoadWithStateAsync();
        if (!ok) return;

        var nanooJson = await LoadRawFromNanoo();

        if (!hasRow)
        {
            if (nanooJson != null)
                await save.NanooPatchFromEmptyAsync(nanooJson);
            else
                await save.TryLoadAsync();
            return;
        }

        var nanooTime = ParseNanooTimestamp(nanooJson);
        if (nanooTime > sdkTime)
            await save.NanooPatchFromLastLoadedAsync(nanooJson);
        else
        {
            save.NanooApplyLastLoaded();
            SaveToNanoo(save.NanooGetLastLoadedJson());
        }
    }

    // ── PlayNANOO 저장/로드 ───────────────────────────────────────────────────

    protected virtual Task<string> LoadRawFromNanoo()
    {
        var tcs = new TaskCompletionSource<string>();
        _plugin.Storage.Load("Data", (status, _, _, values) =>
        {
            if (status != Configure.PN_API_STATE_SUCCESS) { tcs.SetResult(null); return; }
            tcs.SetResult(values["StorageValue"]?.ToString());
        });
        return tcs.Task;
    }

    [Serializable]
    private class NanooTimestampHelper { public string updated_at; }

    private static DateTime ParseNanooTimestamp(string json)
    {
        if (string.IsNullOrEmpty(json)) return DateTime.MinValue;
        var h = JsonUtility.FromJson<NanooTimestampHelper>(json);
        if (!string.IsNullOrEmpty(h?.updated_at) && DateTime.TryParse(h.updated_at, out var t))
            return t;
        // updated_at 없음 = 이관 전 순수 PlayNANOO 데이터 → PlayNANOO 항상 우선
        return DateTime.MaxValue;
    }

    /// <summary>PlayNANOO에 JSON 데이터를 저장합니다.</summary>
    public virtual void SaveToNanoo(string json)
    {
        if (string.IsNullOrEmpty(json)) return;
        _plugin.Storage.Save("Data", json, true,
            (status, _, _, _) =>
            {
                if (status != Configure.PN_API_STATE_SUCCESS)
                    Debug.LogWarning("[PlayNanooRuntime] PlayNANOO 저장 실패");
            });
    }

    /// <summary>현재 로컬 세이브 데이터를 PlayNANOO에 저장합니다.</summary>
    public void SaveCurrentToNanoo()
    {
        var json = Save?.NanooCurrentJson;
        if (json != null) SaveToNanoo(json);
    }
}
