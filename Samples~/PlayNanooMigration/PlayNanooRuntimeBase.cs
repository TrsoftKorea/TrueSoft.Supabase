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
//   await Supabase.SignInAnonymouslyAsync()
//   await Supabase.SignInWithGoogleAsync()
//   await Supabase.SignInWithAppleIdTokenAsync(token)
//   await Supabase.LinkGoogleToGuestWithIdTokenAsync(token)   // 익명 → Google 연동
//   await Supabase.LinkAppleToGuestWithIdTokenAsync(token)    // 익명 → Apple 연동
//   await Supabase.SignOutFullyAsync()
//   await Supabase.RequestWithdrawalAsync()
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
    private string   _nanooAccessToken;    // 로그인 성공 시 저장, 로그아웃·롤백에 사용
    private string   _nanooNickname;       // 닉네임 변경 롤백용

    private DateTime _nanooTokenRefreshedAt       = DateTime.MinValue;
    private float    _lastNanooRefreshCheckTime   = float.MinValue;

    private const string NanooAccessTokenKey        = "TrueBase.NanooAccessToken";
    private const double NanooTokenLifetimeHours    = 24.0;
    private const double NanooTokenRefreshLeadHours = 1.0;   // 만료 1시간 전부터 갱신
    private const float  NanooRefreshCheckInterval  = 600f;  // 10분마다 체크

    /// <summary>PlayNANOO 로그인 성공 시 반환된 uuid. 로그인 전에는 null.</summary>
    public static string UserId { get; private set; }

    /// <summary>PlayNANOO 로그인 성공 시 반환된 openid. SDK가 반환하지 않으면 null.</summary>
    public static string OpenId { get; private set; }

    private INanooSaveSyncable Save => Supabase.GetNanooSaveBridge();

    // ── 이벤트 ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 로그인 시 탈퇴 예약 계정이 감지됨. 취소 UI를 띄우고 <see cref="Supabase.RedeemWithdrawalCancelAsync"/>를 호출하세요.
    /// 표준 SDK 취소 API가 인터셉터로 나누 복구까지 함께 처리하므로 별도 취소 메서드가 필요 없습니다.
    /// </summary>
    public event Action OnWithdrawalPending;

    // 로그인 시 감지한 나누 탈퇴 복구 키. RedeemWithdrawalCancel 인터셉터가 내부적으로 사용합니다.
    private string _pendingWithdrawalKey;

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
            requestMyWithdrawal:                     InterceptRequestWithdrawal,
            linkGoogleToGuestWithIdToken: InterceptLinkGoogleToGuestWithIdToken,
            linkAppleToGuestWithIdToken:  InterceptLinkAppleToGuestWithIdToken,
            setMyName:                        InterceptSetName,
            linkGoogleWithIdToken:                   InterceptLinkGoogleWithIdToken,
            linkAppleWithIdToken:                    InterceptLinkAppleWithIdToken,
            redeemWithdrawalCancel:                  InterceptRedeemWithdrawalCancel
        );

        // 세이브 삭제 시 PlayNANOO 스토리지도 초기값으로 되돌립니다.
        // 안 그러면 다음 로그인의 동기화가 옛 데이터를 다시 밀어 넣어 삭제가 무효가 됩니다.
        Supabase.RegisterNanooStorageReset(ResetNanooStorageAsync);

        // IAP: PlayNanooRuntime이 있으면 SK1을 강제하고 PlayNanoo IAP를 인터셉터로 등록합니다.
#if UNITY_IAP_V5_1 && UNITY_IOS
        UnityEngine.Purchasing.StoreKitSelector.forceStoreKit1 = true;
#elif UNITY_IAP_V5 && UNITY_IOS
        Debug.LogError("[PlayNanooRuntime] Unity IAP 5.0.x에서는 iOS 15+에서 PlayNanoo IAP가 작동하지 않습니다. Unity IAP 5.1+로 업그레이드하세요.");
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

    // ── PlayNANOO 로그인 Task 래퍼 (병렬 실행용) ──────────────────────────────

    /// <summary>PlayNANOO 로그인 결과. Ok=성공, ErrorCode=실패 시 PlayNANOO ErrorCode(예: 탈퇴 신청 중 "30007").</summary>
    private readonly struct NanooSignInResult
    {
        public readonly bool Ok;
        public readonly string ErrorCode;
        public NanooSignInResult(bool ok, string errorCode) { Ok = ok; ErrorCode = errorCode; }
    }

    private static string ExtractNanooErrorCode(Dictionary<string, object> values)
        => values != null && values.TryGetValue("ErrorCode", out var ec) ? ec?.ToString() : null;

    /// <summary>콜백 기반 게스트 로그인을 await 가능하게 감쌉니다. HandleNanooCallback으로 PlayNANOO 상태를 세팅합니다.</summary>
    private Task<NanooSignInResult> NanooGuestSignInAsync()
    {
        var tcs = new TaskCompletionSource<NanooSignInResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        NanooGuestSignIn((status, values) =>
        {
            var ok = HandleNanooCallback(status, values, "guest");
            tcs.SetResult(new NanooSignInResult(ok, ExtractNanooErrorCode(values)));
            return Task.CompletedTask;
        });
        return tcs.Task;
    }

    /// <summary>콜백 기반 소셜 로그인을 await 가능하게 감쌉니다.</summary>
    private Task<NanooSignInResult> NanooSocialSignInAsync(string token, string accountType, string loginType)
    {
        var tcs = new TaskCompletionSource<NanooSignInResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        NanooSocialSignIn(token, accountType, (status, values) =>
        {
            var ok = HandleNanooCallback(status, values, loginType);
            tcs.SetResult(new NanooSignInResult(ok, ExtractNanooErrorCode(values)));
            return Task.CompletedTask;
        });
        return tcs.Task;
    }

    // ── 인터셉터 구현 ─────────────────────────────────────────────────────────

    private async Task<SupabaseResult> InterceptSignInAnonymously(Func<Task<SupabaseResult>> sdkSignIn)
    {
        // PlayNANOO·Supabase 로그인은 서로 독립적(입력 토큰만 공유, 결과 의존 없음)이라 동시에 실행해 지연을 줄입니다.
        var nanooTask = NanooGuestSignInAsync();
        var sdkTask   = sdkSignIn();
        await Task.WhenAll(nanooTask, sdkTask);
        var nanoo     = nanooTask.Result;
        var sdkResult = sdkTask.Result;

        if (nanoo.Ok && sdkResult.IsSuccess)
        {
            await SyncDataAfterLogin();
            return sdkResult;
        }

        // PlayNANOO 성공·Supabase 실패 → PlayNANOO 롤백
        if (nanoo.Ok)
        {
            await RollbackNanooLoginAsync();
            return sdkResult;
        }

        // 탈퇴 예약 게이트로 막힌 경우(WithdrawalGateBlocked) 그 사유·취소 토큰을 그대로 전달한다. OnWithdrawalPending은 이미 발행됨.
        if (sdkResult.Reason == SupabaseReason.WithdrawalGateBlocked)
            return sdkResult;

        // PlayNANOO 실패 → 병렬로 이미 생성된 Supabase 세션이 있으면 정리
        if (sdkResult.IsSuccess)
            await Supabase.SignOutFullyAsync();
        return SupabaseResult.Fail("playnanoo_guest_signin_failed");
    }

    private Task<SupabaseResult> InterceptSignInWithGoogleIdToken(string token, Func<Task<SupabaseResult>> sdkSignIn)
        => InterceptSocialSignInAsync(token, Configure.PN_ACCOUNT_GOOGLE, "google", "playnanoo_google_signin_failed", sdkSignIn);

    private Task<SupabaseResult> InterceptSignInWithAppleIdToken(string token, Func<Task<SupabaseResult>> sdkSignIn)
        => InterceptSocialSignInAsync(token, Configure.PN_ACCOUNT_APPLE_ID, "apple", "playnanoo_apple_signin_failed", sdkSignIn);

    /// <summary>구글·애플 공통 소셜 로그인 인터셉터. PlayNANOO·Supabase 로그인을 동시에 실행한 뒤 결과를 재조정합니다.</summary>
    private async Task<SupabaseResult> InterceptSocialSignInAsync(
        string token, string accountType, string loginType, string failReason,
        Func<Task<SupabaseResult>> sdkSignIn)
    {
        // 둘 다 같은 id token만 입력으로 쓰고 서로의 결과에 의존하지 않으므로 동시에 실행합니다(둘 다 성공 시 max(두 왕복)).
        var nanooTask = NanooSocialSignInAsync(token, accountType, loginType);
        var sdkTask   = sdkSignIn();
        await Task.WhenAll(nanooTask, sdkTask);
        var nanoo     = nanooTask.Result;
        var sdkResult = sdkTask.Result;

        if (nanoo.Ok && sdkResult.IsSuccess)
        {
            await SyncDataAfterLogin();
            return sdkResult;
        }

        // PlayNANOO 성공·Supabase 실패 → PlayNANOO 롤백
        if (nanoo.Ok)
        {
            await RollbackNanooLoginAsync();
            return sdkResult;
        }

        // 30007: 탈퇴 신청 중 — Supabase도 게이트로 막혔으면 그 사유(WithdrawalGateBlocked)·취소 토큰을 그대로 전달한다.
        // 취소 토큰은 이미 병렬로 발급됐고 세션은 게이트가 정리한다. OnWithdrawalPending은 이미 발행됨.
        if (nanoo.ErrorCode == "30007")
        {
            if (sdkResult.Reason == SupabaseReason.WithdrawalGateBlocked)
                return sdkResult;
            if (sdkResult.IsSuccess)
                await Supabase.SignOutFullyAsync();
            return SupabaseResult.Fail(failReason);
        }

        // 그 외 실패(탈퇴 완료 후 계정 삭제 등): Supabase 재가입(이미 병렬 실행) → PlayNANOO 재로그인
        if (!sdkResult.IsSuccess)
            return sdkResult;
        if (!await RetryNanooSignInAfterRecreateAsync(token, accountType, loginType))
        {
            await Supabase.SignOutFullyAsync();
            return SupabaseResult.Fail(failReason);
        }
        return sdkResult;
    }

    private Task<bool> RetryNanooSignInAfterRecreateAsync(string token, string accountType, string loginType)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        NanooSocialSignIn(token, accountType, async (status, values) =>
        {
            if (HandleNanooCallback(status, values, loginType))
            {
                await SyncDataAfterLogin();
                tcs.TrySetResult(true);
            }
            else
            {
                Debug.LogWarning($"[PlayNanooRuntime] 계정 재가입 후 PlayNANOO {loginType} 재로그인 실패.");
                tcs.TrySetResult(false);
            }
        });
        return tcs.Task;
    }

    private async Task<SupabaseResult> InterceptSignOutFully(Func<Task<SupabaseResult>> sdkSignOut)
    {
        if (string.IsNullOrEmpty(_nanooAccessToken))
            return await sdkSignOut();

        var tcs = new TaskCompletionSource<SupabaseResult>();
        NanooTokenSignOut(_nanooAccessToken, async () =>
        {
            _nanooAccessToken = null;
            ClearNanooTokens();
            UserId = null;
            OpenId = null;
            var result = await sdkSignOut();
            if (!result.IsSuccess)
                Debug.LogWarning("[PlayNanooRuntime] Supabase 로그아웃 실패. PlayNANOO 로그아웃은 완료됨.");
            tcs.SetResult(result);
        });
        return await tcs.Task;
    }

    private async Task<SupabaseResult> InterceptRequestWithdrawal(Func<Task<SupabaseResult>> sdkWithdrawal)
    {
        var tcs = new TaskCompletionSource<SupabaseResult>();
        var isGoogle = Supabase.IsLinkedWithGoogle; // sdkWithdrawal()이 세션을 정리하므로 미리 확인
        NanooWithDrawal(15, async status =>
        {
            if (status != Configure.PN_API_STATE_SUCCESS)
            {
                tcs.SetResult(SupabaseResult.Fail("playnanoo_withdrawal_request_failed"));
                return;
            }
            var result = await sdkWithdrawal();
            if (!result.IsSuccess)
                Debug.LogWarning("[PlayNanooRuntime] Supabase 탈퇴 실패. PlayNANOO 탈퇴는 완료됨.");
            if (isGoogle)
                await Supabase.RevokeGoogleAccessAsync();
            tcs.SetResult(result);
        });
        return await tcs.Task;
    }

    // ── 닉네임 변경 인터셉터 ──────────────────────────────────────────────────────

    private Task<SupabaseResult> InterceptSetName(string nickname, Func<Task<SupabaseResult>> sdkSet)
    {
        var tcs = new TaskCompletionSource<SupabaseResult>();
        NanooSetNickname(nickname, async status =>
        {
            if (status != Configure.PN_API_STATE_SUCCESS)
            {
                tcs.SetResult(SupabaseResult.Fail("playnanoo_set_nickname_failed"));
                return;
            }
            var result = await sdkSet();
            if (!result.IsSuccess)
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

    private async Task<SupabaseResult> InterceptLinkGoogleToGuestWithIdToken(string token, Func<Task<SupabaseResult>> sdkLink)
    {
        var tcs = new TaskCompletionSource<SupabaseResult>();
        NanooSocialSignIn(token, Configure.PN_ACCOUNT_GOOGLE, async (status, values) =>
        {
            if (!HandleNanooCallback(status, values, "google"))
            {
                tcs.SetResult(SupabaseResult.Fail("playnanoo_google_link_failed"));
                return;
            }
            var result = await sdkLink();
            if (!result.IsSuccess) { await RollbackNanooLoginAsync(); tcs.SetResult(result); return; }
            await SyncDataAfterLogin();
            tcs.SetResult(result);
        });
        return await tcs.Task;
    }

    private async Task<SupabaseResult> InterceptLinkAppleToGuestWithIdToken(string token, Func<Task<SupabaseResult>> sdkLink)
    {
        var tcs = new TaskCompletionSource<SupabaseResult>();
        NanooSocialSignIn(token, Configure.PN_ACCOUNT_APPLE_ID, async (status, values) =>
        {
            if (!HandleNanooCallback(status, values, "apple"))
            {
                tcs.SetResult(SupabaseResult.Fail("playnanoo_apple_link_failed"));
                return;
            }
            var result = await sdkLink();
            if (!result.IsSuccess) { await RollbackNanooLoginAsync(); tcs.SetResult(result); return; }
            await SyncDataAfterLogin();
            tcs.SetResult(result);
        });
        return await tcs.Task;
    }

    // ── 소셜 → 소셜 추가 연동 인터셉터 ───────────────────────────────────────────

    private async Task<SupabaseResult> InterceptLinkGoogleWithIdToken(string token, Func<Task<SupabaseResult>> sdkLink)
    {
        var tcs = new TaskCompletionSource<SupabaseResult>();
        NanooSocialSignIn(token, Configure.PN_ACCOUNT_GOOGLE, async (status, values) =>
        {
            if (!HandleNanooCallback(status, values, "google"))
            {
                tcs.SetResult(SupabaseResult.Fail("playnanoo_google_link_failed"));
                return;
            }
            var result = await sdkLink();
            if (!result.IsSuccess) { await RollbackNanooLoginAsync(); tcs.SetResult(result); return; }
            await SyncDataAfterLogin();
            tcs.SetResult(result);
        });
        return await tcs.Task;
    }

    private async Task<SupabaseResult> InterceptLinkAppleWithIdToken(string token, Func<Task<SupabaseResult>> sdkLink)
    {
        var tcs = new TaskCompletionSource<SupabaseResult>();
        NanooSocialSignIn(token, Configure.PN_ACCOUNT_APPLE_ID, async (status, values) =>
        {
            if (!HandleNanooCallback(status, values, "apple"))
            {
                tcs.SetResult(SupabaseResult.Fail("playnanoo_apple_link_failed"));
                return;
            }
            var result = await sdkLink();
            if (!result.IsSuccess) { await RollbackNanooLoginAsync(); tcs.SetResult(result); return; }
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
            _nanooAccessToken      = values["access_token"]?.ToString();
            _nanooNickname         = values["nickname"]?.ToString();
            UserId                 = values.TryGetValue("uuid",   out var uuidVal)   ? uuidVal?.ToString()   : null;
            OpenId                 = values.TryGetValue("openID", out var openidVal) ? openidVal?.ToString() : null;
            _nanooTokenRefreshedAt = DateTime.UtcNow;
            SaveNanooTokens();
            OnNanooLoginSuccess(values);
            return true;
        }
        string errorCode = null;
        if (values != null && values.TryGetValue("ErrorCode", out var ecObj))
            errorCode = ecObj?.ToString();
        if (errorCode == "30007")
        {
            _pendingWithdrawalKey = null;
            if (values != null && values.TryGetValue("WithdrawalKey", out var wkObj))
                _pendingWithdrawalKey = wkObj?.ToString();
            OnWithdrawalPending?.Invoke();
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

    /// <summary>애플 로그인 (Android). PlayNANOO 내장 WebView로 토큰 획득 후 Supabase.SignInWithAppleIdTokenAsync 자동 호출.</summary>
    public void StartAppleSignInAndroid() =>
        _plugin.OpenAppleID(
            async token => await Supabase.SignInWithAppleIdTokenAsync(token));

    // ── 탈퇴 취소 인터셉터 ─────────────────────────────────────────────────────

    /// <summary>
    /// Supabase.RedeemWithdrawalCancelAsync 인터셉터. 나누 탈퇴 복구를 먼저 수행하고,
    /// 성공하면 Supabase 예약 철회(sdkRedeem)를 이어 실행해 양쪽을 함께 취소합니다.
    /// 나누 복구 키가 없으면(예약 미감지 등) Supabase만 취소합니다.
    /// </summary>
    private async Task<SupabaseResult> InterceptRedeemWithdrawalCancel(Func<Task<SupabaseResult>> sdkRedeem)
    {
        var key = _pendingWithdrawalKey;
        if (string.IsNullOrEmpty(key))
            return await sdkRedeem();

        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        NanooWithDrawalRestore(key, status => { tcs.TrySetResult(status); return Task.CompletedTask; });
        var nanooStatus = await tcs.Task;

        // 나누 복구가 실패하면 Supabase 예약은 건드리지 않는다(양쪽 lockstep 유지).
        if (nanooStatus != Configure.PN_API_STATE_SUCCESS)
        {
            Debug.LogWarning($"[PlayNanooRuntime] PlayNANOO 탈퇴 취소(WithDrawalRestore) 실패 — status: {nanooStatus}");
            return SupabaseResult.Fail("playnanoo_withdrawal_restore_failed");
        }

        _pendingWithdrawalKey = null;
        return await sdkRedeem();
    }

    // ── PlayNANOO 토큰 독립 갱신 (24시간 주기) ───────────────────────────────────

    protected override void Update()
    {
        base.Update();
        TickNanooTokenRefresh(Time.realtimeSinceStartup);
    }

    private void TickNanooTokenRefresh(float realtimeSinceStartup)
    {
        if (_nanooTokenRefreshedAt == DateTime.MinValue) return;
        if (realtimeSinceStartup - _lastNanooRefreshCheckTime < NanooRefreshCheckInterval) return;
        _lastNanooRefreshCheckTime = realtimeSinceStartup;

        var hoursSinceRefresh = (DateTime.UtcNow - _nanooTokenRefreshedAt).TotalHours;
        if (hoursSinceRefresh < NanooTokenLifetimeHours - NanooTokenRefreshLeadHours) return;

        var storedToken = PlayerPrefs.GetString(NanooAccessTokenKey, null);
        if (string.IsNullOrEmpty(storedToken)) return;

        _ = RestoreNanooSessionAsync(storedToken).ContinueWith(t =>
        {
            if (!t.Result)
                Debug.LogWarning("[PlayNanooRuntime] PlayNANOO 토큰 갱신 실패. 재로그인이 필요할 수 있습니다.");
        });
    }

    // ── 자동 로그인 후 PlayNANOO 세션 복원 ────────────────────────────────────

    protected override async Task<bool> OnAfterAutoLoginAsync(bool success)
    {
        if (!success) return false;
        var storedToken = PlayerPrefs.GetString(NanooAccessTokenKey, null);

        // PlayNANOO 세션을 복원할 수 없으면(토큰 없음 또는 복원 실패) UserId/OpenId가 비어,
        // 이 상태로 자동 로그인을 성공 처리하면 게임이 빈 정체성으로 초기화에 진입합니다.
        // 두 세션을 lockstep으로 유지하기 위해 Supabase 세션까지 정리하고 실패를 반환 →
        // 게임은 자동 로그인 실패로 받아 명시 로그인(게스트/소셜)으로 유도합니다.
        var nanooOk = !string.IsNullOrEmpty(storedToken)
                      && await RestoreNanooSessionAsync(storedToken);

        if (!nanooOk)
            await Supabase.SignOutFullyAsync();
        return nanooOk;
    }

    private Task<bool> RestoreNanooSessionAsync(string accessToken)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        NanooTokenSignIn(accessToken, async (status, values) =>
        {
            if (status == Configure.PN_API_STATE_SUCCESS)
            {
                _nanooAccessToken    = values["access_token"]?.ToString();
                _nanooNickname      = values.TryGetValue("nickname", out var nk) ? nk?.ToString() : _nanooNickname;
                UserId              = values.TryGetValue("uuid",   out var uv) ? uv?.ToString() : null;
                OpenId              = values.TryGetValue("openID", out var ov) ? ov?.ToString() : null;
                _nanooTokenRefreshedAt = DateTime.UtcNow;
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

    /// <summary>
    /// PlayNANOO 스토리지를 세이브 클래스의 초기값으로 되돌립니다. <c>Supabase.DeleteUserSaveAsync()</c>가 호출합니다.
    /// <para>PlayNANOO Storage에는 삭제 API가 없어 초기값 JSON을 덮어씁니다.
    /// SDK가 만들어 넘겨주므로 여기서는 저장만 합니다.</para>
    /// </summary>
    /// <param name="defaultsJson">세이브 클래스 초기값 + <c>updated_at</c>이 담긴 JSON.</param>
    protected virtual Task ResetNanooStorageAsync(string defaultsJson)
    {
        SaveToNanoo(defaultsJson);
        Debug.Log("[PlayNanooRuntime] 세이브 삭제 — PlayNANOO 스토리지를 초기값으로 되돌렸습니다.");
        return Task.CompletedTask;
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
