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
//   await Supabase.SignInWithAppleAsync()                     // Android 는 브라우저 대신 나누 웹뷰가 열린다
//   await Supabase.SignInWithAppleIdTokenAsync(token)
//   await Supabase.LinkGoogleToGuestWithIdTokenAsync(token)   // 익명 → Google 연동
//   await Supabase.LinkAppleToGuestWithIdTokenAsync(token)    // 익명 → Apple 연동
//   await Supabase.SignOutFullyAsync()
//   await Supabase.RequestWithdrawalAsync()
//
// [PlayNANOO 제거 후]
// 1. PlayNANOO Migration 폴더를 통째로 삭제 (이 파일 · PlayNanooRuntime.cs ·
//    PlayNanooLegacyRuntime.cs · TrueBaseNanoo.cs)
// 2. 씬에 SupabaseRuntime 배치
// 3. 게임의 TrueBaseNanoo.* 호출을 지운다 (Supabase.* 호출은 그대로)
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

    // 이번 로그인의 나누 동기화가 성공으로 끝났는가. 기본은 false(쓰기 금지)이고 성공한 갈래에서만 켠다.
    // "SDK 세이브를 한 번이라도 읽었는가"만 보면 게임의 LoadUserSaveAsync 만으로 통과돼, 동기화가 도중에
    // 실패했거나 아직 안 끝났을 때 기본값·이전 계정 데이터로 나누 원본을 덮는다.
    private bool     _nanooSyncedThisLogin;

    private const string NanooStorageReadFailedReason = "playnanoo_storage_read_failed";
    private string   _nanooNickname;       // 닉네임 변경 롤백용

    private DateTime _nanooTokenRefreshedAt       = DateTime.MinValue;
    private float    _lastNanooRefreshCheckTime   = float.MinValue;

    private const string NanooAccessTokenKey        = "TrueBase.NanooAccessToken";
    private const double NanooTokenLifetimeHours    = 24.0;
    private const double NanooTokenRefreshLeadHours = 1.0;   // 만료 1시간 전부터 갱신
    private const float  NanooRefreshCheckInterval  = 600f;  // 10분마다 체크

    // 로그인 정보(UserId·OpenId)·탈퇴 이벤트는 TrueBaseNanoo(정적 진입점)로 옮겼다. 애플 로그인은 표준 Supabase.SignInWithAppleAsync() 를 쓴다.
    // 게임이 씬에서 컴포넌트를 찾지 않고 부를 수 있게 하려는 것이다.

    private INanooSaveSyncable Save => SupabaseBridge.GetNanooSaveBridge();

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
        Func<string, string, Dictionary<string, object>, Task> callback)
        => _plugin.IAP.IOS(receipt, productId, currency, (float)price,
            async (s, errorMessage, _, values) => await callback(s, errorMessage, values));

    /// <summary>
    /// PlayNANOO Android IAP 검증 호출. callback(status, errorMessage, values)로 결과를 반환합니다.
    /// 구/신버전 PlayNANOO 모두 동일 API이므로 일반적으로 override 불필요합니다.
    /// </summary>
    /// <param name="receipt">
    /// Unity IAP 영수증 <b>원문</b>. PlayNANOO 는 이 값에서 직접 영수증을 읽으므로
    /// purchaseToken 만 넘기면 서버가 "영수증 없음"(20004)으로 거절합니다.
    /// </param>
    protected virtual void NanooIAPAndroid(
        string receipt,
        Func<string, string, Dictionary<string, object>, Task> callback)
        => _plugin.IAP.Android(receipt,
            async (s, errorMessage, _, values) => await callback(s, errorMessage, values));

    // ── 초기화 ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 씬에 배치된 런타임. 게임이 <see cref="TrueBaseNanoo"/> 정적 진입점으로 부를 수 있게 잡아 둡니다.
    /// 구버전(<c>PlayNanooLegacyRuntime</c>)을 배치했어도 같은 자리에 들어가므로, 게임 코드는
    /// 어느 쪽을 쓰는지 몰라도 됩니다.
    /// </summary>
    internal static PlayNanooRuntimeBase Instance { get; private set; }

    protected override void Awake()
    {
        base.Awake();

        // 이미 살아 있는 런타임이 있으면 베이스가 이 쪽(새로 생긴 중복)을 지운다. 그런데도 여기서 등록하면
        // 이 중복이 파괴될 때 OnDestroy 가 살아 있는 쪽의 인터셉터까지 해제한다(타이틀 씬 재로드 등).
        if (Instance != null && !ReferenceEquals(Instance, this))
            return;

        Instance = this;
        _plugin = Plugin.GetInstance();

        SupabaseBridge.RegisterPlayNanooInterceptors(
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
        SupabaseBridge.RegisterNanooStorageReset(ResetNanooStorageAsync);

        // Android 의 Supabase.SignInWithAppleAsync() 가 브라우저 대신 나누 웹뷰로 토큰을 받게 한다.
        // 브라우저 로그인은 토큰이 앱을 거치지 않아 나누를 함께 로그인시킬 수 없다.
        SupabaseBridge.RegisterNanooAppleAndroidIdToken(RequestAppleIdTokenAsync);

        // IAP: PlayNanooRuntime이 있으면 SK1을 강제하고 PlayNanoo IAP를 인터셉터로 등록합니다.
#if UNITY_IAP_V5_1 && UNITY_IOS
        // 네임스페이스가 UnityEngine.Purchasing 이 아니다 — StoreKitSelector 는 Purchasing.Utilities 에 있다.
        Purchasing.Utilities.StoreKitSelector.forceStoreKit1 = true;
#elif UNITY_IOS
        Debug.LogError("[PlayNanooRuntime] Unity IAP 5.0.x에서는 iOS 15+에서 PlayNanoo IAP가 작동하지 않습니다. Unity IAP 5.1+로 업그레이드하세요.");
#endif

        SupabaseBridge.RegisterIAPAppleInterceptor(async (receipt, productId, sdkVerify) =>
        {
            var tcs = new TaskCompletionSource<SupabaseResult<AppleIAPPurchaseResponse>>();
            NanooIAPIOS(receipt, productId, string.Empty, 0d, async (status, errorMessage, values) =>
            {
                if (status != Configure.PN_API_STATE_SUCCESS)
                {
                    LogNanooIAPFailure("iOS", productId, status, errorMessage, values);
                    tcs.SetResult(SupabaseResult<AppleIAPPurchaseResponse>.Fail("playnanoo_iap_ios_failed"));
                    return;
                }
                tcs.SetResult(await sdkVerify());
            });
            return await tcs.Task;
        });

        // rawReceipt(유니티 영수증 원문)를 넘긴다. PlayNANOO 는 purchaseToken 이 아니라 영수증을 받는다.
        SupabaseBridge.RegisterIAPGoogleInterceptor(async (purchaseToken, productId, priceAmount, priceCurrency, rawReceipt, sdkVerify) =>
        {
            var tcs = new TaskCompletionSource<SupabaseResult<GooglePlayPurchaseResponse>>();
            NanooIAPAndroid(rawReceipt, async (status, errorMessage, values) =>
            {
                if (status != Configure.PN_API_STATE_SUCCESS)
                {
                    LogNanooIAPFailure("Android", productId, status, errorMessage, values);
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
        // 등록한 적 없는 중복 런타임이면 아무것도 건드리지 않는다 — 살아 있는 쪽의 등록이다.
        if (!ReferenceEquals(Instance, this)) return;

        Instance = null;
        SupabaseBridge.UnregisterPlayNanooInterceptors();
        // IAP 인터셉터는 UnregisterPlayNanooInterceptors 내부에서 함께 해제됩니다.

        // 애플 로그인을 기다리는 쪽이 영영 풀리지 않게 끝내 둔다.
        if (_appleRequest != null)
            FinishAppleSignIn(_appleRequest, SupabaseResult<string>.Fail("playnanoo_runtime_destroyed"));
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
            return await SyncOrFailLoginAsync(sdkResult);

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
            return await SyncOrFailLoginAsync(sdkResult);

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
                // false 면 호출부가 양쪽을 로그아웃하고 실패로 돌린다.
                tcs.TrySetResult(await SyncDataAfterLogin());
            }
            else
            {
                Debug.LogWarning($"[PlayNanooRuntime] 계정 재가입 후 PlayNANOO {loginType} 재로그인 실패.");
                tcs.TrySetResult(false);
            }
        });
        return tcs.Task;
    }

    /// <summary>로그인 성공 뒤 동기화를 돌리고, 계속하면 안 되는 경우 양쪽을 로그아웃하고 실패로 돌립니다.</summary>
    private async Task<SupabaseResult> SyncOrFailLoginAsync(SupabaseResult sdkResult)
    {
        if (await SyncDataAfterLogin())
            return sdkResult;

        await Supabase.SignOutFullyAsync();
        return SupabaseResult.Fail(NanooStorageReadFailedReason);
    }

    private async Task<SupabaseResult> InterceptSignOutFully(Func<Task<SupabaseResult>> sdkSignOut)
    {
        _nanooSyncedThisLogin = false;

        if (string.IsNullOrEmpty(_nanooAccessToken))
            return await sdkSignOut();

        var tcs = new TaskCompletionSource<SupabaseResult>();
        NanooTokenSignOut(_nanooAccessToken, async () =>
        {
            _nanooAccessToken = null;
            ClearNanooTokens();
            TrueBaseNanoo.UserId = null;
            TrueBaseNanoo.OpenId = null;
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

    /// <summary>
    /// PlayNANOO 결제 검증 실패를 사유까지 남깁니다. 이게 없으면 SDK 로그에는
    /// playnanoo_iap_*_failed 만 남아, 나누가 왜 거절했는지 따로 찾아야 합니다.
    /// </summary>
    private static void LogNanooIAPFailure(
        string platform, string productId, string status, string errorMessage, Dictionary<string, object> values)
    {
        string errorCode = null;
        if (values != null && values.TryGetValue("ErrorCode", out var ecObj))
            errorCode = ecObj?.ToString();

        Debug.LogWarning(
            $"[PlayNanooRuntime] PlayNANOO {platform} 결제 검증 실패 — product: {productId}, " +
            $"status: {status}, ErrorCode: {errorCode}, Message: {errorMessage}");
    }

    private bool HandleNanooCallback(string status, Dictionary<string, object> values, string loginType)
    {
        if (status == Configure.PN_API_STATE_SUCCESS)
        {
            _nanooAccessToken      = values["access_token"]?.ToString();
            _nanooNickname         = values["nickname"]?.ToString();
            TrueBaseNanoo.UserId   = values.TryGetValue("uuid",   out var uuidVal)   ? uuidVal?.ToString()   : null;
            TrueBaseNanoo.OpenId   = values.TryGetValue("openID", out var openidVal) ? openidVal?.ToString() : null;
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
            TrueBaseNanoo.RaiseWithdrawalPending();
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
        _nanooSyncedThisLogin = false;
        if (string.IsNullOrEmpty(_nanooAccessToken)) return Task.CompletedTask;
        var token = _nanooAccessToken;
        _nanooAccessToken = null;
        ClearNanooTokens();
        TrueBaseNanoo.UserId = null;
        TrueBaseNanoo.OpenId = null;
        var tcs = new TaskCompletionSource<bool>();
        NanooTokenSignOut(token, async () => tcs.SetResult(true));
        return tcs.Task;
    }

    // ── Apple 로그인 (Android 전용) ───────────────────────────────────────────

    // 나누 애플 웹뷰는 성공했을 때만 토큰을 보낸다. 애플 쪽 실패·닫기 버튼·뒤로가기는 창만 닫히고 아무 신호가 없다
    // (PlayNANOOAndroid.dll · PlayNANOOPlugin.aar 를 풀어 확인, 2026-09-29). 그래서 창이 닫혀 앱으로 포커스가
    // 돌아왔는데 잠시 기다려도 토큰이 없으면 취소로 본다. 포커스 신호가 안 오는 기기에서는 판정이 안 걸릴 뿐
    // 로그인을 잘못 끊지는 않는다.
    private const float AppleCloseGraceSeconds = 2f;
    private const string AppleUrlFailedLog = "Apple Account URL Creation Failed"; // 나누 DLL 이 주소 생성 실패 때 남기는 유일한 흔적

    // 창을 열자마자 다시 누른 연타만 막는 시간. 창은 전체 화면이라 창이 떠 있는 동안에는 게임 버튼을 누를 수 없다.
    private const float AppleRetapGuardSeconds = 1f;
    // 나누가 애플 로그인 주소를 받는 HTTP 제한(Common.WWW_TIMEOUT = 10초, DLL 디컴파일로 확인) + 여유.
    private const float AppleUrlRequestTimeoutSeconds = 12f;

    private sealed class AppleSignInRequest
    {
        public readonly TaskCompletionSource<SupabaseResult<string>> Tcs =
            new TaskCompletionSource<SupabaseResult<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly float StartedAt = Time.realtimeSinceStartup;
        public NanooAppleWebViewWatcher Watcher;
        public bool Done;
    }

    private AppleSignInRequest _appleRequest;
    // 나누 OpenAppleID 를 부르는 동안만 켠다. 나누가 그 안에서 예외를 삼키고 LogError 로만 남기기 때문이다.
    private bool _openingAppleView;

    /// <summary>
    /// 애플 로그인 웹뷰를 열고 ID 토큰을 받아 옵니다. SDK 가 Android 의 <c>Supabase.SignInWithAppleAsync()</c>에서 부르며,
    /// 받은 토큰으로 하는 로그인(나누 포함)은 SDK 가 이어서 합니다.
    /// </summary>
    private Task<SupabaseResult<string>> RequestAppleIdTokenAsync()
    {
        var previous = _appleRequest;
        if (previous != null)
        {
            // 게임 화면에 포커스가 있으면 창은 이미 없다 — 닫힘을 놓친 것이니 앞 요청을 끝내고 새로 연다.
            // 그러지 않으면 한 번 놓친 닫힘이 앱을 다시 켤 때까지 애플 로그인을 막는다.
            // 창이 한 번도 안 떴으면 나누가 아직 주소를 받는 중일 수 있다(최대 10초). 그때 새로 열면 창이 두 겹으로 뜨고
            // 나누의 토큰 콜백 칸이 하나뿐이라 토큰이 엉뚱한 요청으로 간다 — 주소 요청 제한 시간이 지난 뒤에만 연다.
            var age = Time.realtimeSinceStartup - previous.StartedAt;
            var windowShown = previous.Watcher != null && previous.Watcher.LostFocus;
            var stale = Application.isFocused &&
                        age > (windowShown ? AppleRetapGuardSeconds : AppleUrlRequestTimeoutSeconds);
            if (!stale)
                return Task.FromResult(SupabaseResult<string>.Fail("oauth_login_already_in_progress"));

            FinishAppleSignIn(previous, SupabaseResult<string>.Fail("apple_signin_cancelled"));
        }

        var request = new AppleSignInRequest();
        _appleRequest = request;
        Application.logMessageReceived += OnNanooLogDuringAppleSignIn;
        request.Watcher = gameObject.AddComponent<NanooAppleWebViewWatcher>();
        request.Watcher.Begin(AppleCloseGraceSeconds,
            () => FinishAppleSignIn(request, SupabaseResult<string>.Fail("apple_signin_cancelled")));

        _openingAppleView = true;
        try
        {
            _plugin.OpenAppleID(token => OnAppleTokenReceived(request, token));
        }
        finally
        {
            _openingAppleView = false;
        }

        return request.Tcs.Task;
    }

    private void OnAppleTokenReceived(AppleSignInRequest request, string token)
    {
        if (request.Done)
        {
            // 이미 끝난 요청(취소 판정·재시도로 대체·런타임 파괴)에 온 토큰. 게임은 이미 결과를 받았으니 로그인하지 않는다.
            Debug.LogWarning("[PlayNanooRuntime] 애플 로그인: 이미 끝난 요청에 토큰이 도착해 무시합니다. 다시 로그인하면 됩니다.");
            return;
        }

        FinishAppleSignIn(request, SupabaseResult<string>.Success(token));
    }

    private void OnNanooLogDuringAppleSignIn(string condition, string stackTrace, LogType type)
    {
        var request = _appleRequest;
        if (request == null)
            return;

        // 여는 도중의 오류(나누 키 미설정 등)는 나누가 예외를 삼키고 문구 없이 남긴다. 연 뒤에는 주소 생성 실패 문구만 믿는다.
        var failed = _openingAppleView
            ? type == LogType.Error || type == LogType.Exception
            : type == LogType.Error && condition == AppleUrlFailedLog;
        if (!failed)
            return;

        Debug.LogWarning("[PlayNanooRuntime] 애플 로그인 창을 열지 못했습니다: " + condition);
        FinishAppleSignIn(request, SupabaseResult<string>.Fail("playnanoo_apple_webview_failed"));
    }

    private void FinishAppleSignIn(AppleSignInRequest request, SupabaseResult<string> result)
    {
        if (request.Done) return;
        request.Done = true;

        if (ReferenceEquals(_appleRequest, request))
        {
            _appleRequest = null;
            Application.logMessageReceived -= OnNanooLogDuringAppleSignIn;
        }

        if (request.Watcher != null)
            Destroy(request.Watcher);

        request.Tcs.TrySetResult(result);
    }

    /// <summary>
    /// 애플 웹뷰가 떠 있는 동안만 붙는 감시 부품. 창이 뜨면 앱이 포커스를 잃고, 닫히면 되찾는다 — 되찾은 뒤
    /// 유예 시간 안에 토큰이 없으면 닫힘으로 알린다.
    /// </summary>
    /// <remarks>
    /// 다른 앱에 갔다 돌아올 때 게임 화면이 잠깐 포커스를 받더라도, 창이 아직 떠 있으면 곧 창이 포커스를 가져가
    /// focus(false) 가 다시 오고 그때 유예를 멈춘다. 일시정지 신호로 따로 거르지 않는 건 재개 때 두 신호의 순서를
    /// 확인하지 못했고, 켜 둔 표시가 나중의 진짜 닫힘을 삼킬 수 있어서다.
    /// SupabaseRuntime 이 비공개 OnApplicationPause(백그라운드 저장)를 갖고 있어 런타임에 같은 이름의 메시지를 두지 않으려고
    /// 따로 붙였다 떼는 부품으로 뺐다.
    /// </remarks>
    private sealed class NanooAppleWebViewWatcher : MonoBehaviour
    {
        private float _grace;
        private Action _onClosed;
        private bool _lostFocus;
        private Coroutine _pending;

        /// <summary>창이 한 번이라도 떠서 게임 화면이 포커스를 잃은 적이 있는지.</summary>
        public bool LostFocus => _lostFocus;

        public void Begin(float graceSeconds, Action onClosed)
        {
            _grace = graceSeconds;
            _onClosed = onClosed;
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused)
            {
                _lostFocus = true;
                if (_pending != null) { StopCoroutine(_pending); _pending = null; }
                return;
            }

            if (!_lostFocus) return;
            if (_pending != null) StopCoroutine(_pending);
            _pending = StartCoroutine(WaitThenReport());
        }

        private System.Collections.IEnumerator WaitThenReport()
        {
            // 토큰 메시지는 창이 닫히기 직전에 보내지지만 포커스 변화와 도착 순서가 보장되지 않아 잠시 기다린다.
            yield return new WaitForSecondsRealtime(_grace);
            _pending = null;
            _onClosed?.Invoke();
        }
    }

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

        // 수동 로그인과 같게 동기화한다. 빠지면 매일 자동 로그인만 타는 유저는 나누와 한 번도 맞추지
        // 않은 채 플레이하게 되고, 나누 쓰기도 영영 열리지 않는다.
        if (nanooOk && !await SyncDataAfterLogin())
            nanooOk = false;

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
                TrueBaseNanoo.UserId = values.TryGetValue("uuid",   out var uv) ? uv?.ToString() : null;
                TrueBaseNanoo.OpenId = values.TryGetValue("openID", out var ov) ? ov?.ToString() : null;
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

    // ── 진단 로그 ─────────────────────────────────────────────────────────────

    // 인스펙터 값이라 씬에 저장돼 빌드까지 따라간다 — 실기기에서 쫓을 때도 체크만 하면 된다.
    // 로그인마다 여러 줄이 남고 쓰기 로그에는 호출 경로 전체가 붙으므로 평소에는 끈다.
    [Header("진단")]
    [Tooltip("켜면 플레이나누 읽기·쓰기와 동기화 판정이 콘솔에 남습니다. 플레이나누 데이터가 언제 무엇에 덮였는지 추적할 때만 켜세요.")]
    [SerializeField] private bool nanooTrace;

    private const string TraceTag = "[PlayNanooTrace]";

    /// <summary>
    /// 플레이나누에 쓰기 직전에 남깁니다. <b>나누에 쓰는 경로는 전부 여기를 지나갑니다</b> —
    /// 동기화의 SDK 승 갈래, 세이브 삭제(초기값 되돌리기), 게임이 직접 부르는 SaveCurrentToNanoo.
    /// 호출 경로를 함께 남겨야 셋 중 무엇이었는지 사후에 가릴 수 있습니다.
    /// </summary>
    protected void TraceNanooWrite(string caller, string json)
    {
        if (!nanooTrace) return;

        var len  = json?.Length ?? -1;
        var head = string.IsNullOrEmpty(json)
            ? "(비어 있음 — 이 쓰기는 무시됩니다)"
            : json.Substring(0, Math.Min(400, json.Length));

        Debug.Log($"{TraceTag} 나누에 쓰기 시도 ← {caller} / 길이={len}\n" +
                  $"  내용 앞부분: {head}\n" +
                  $"  호출 경로:\n{StackTraceUtility.ExtractStackTrace()}");
    }

    private void Trace(string message)
    {
        if (nanooTrace) Debug.Log($"{TraceTag} {message}");
    }

    // ── 데이터 동기화 ─────────────────────────────────────────────────────────

    /// <summary>
    /// PlayNANOO 스토리지 읽기 결과. <b>"못 읽었다"와 "비어 있다"를 반드시 구분합니다.</b>
    /// <para>
    /// 둘을 null 하나로 합치면, 통신이 한 번 실패했을 뿐인데 "나누에 데이터가 없다"로 읽혀
    /// SDK 데이터가 나누 원본을 덮어씁니다. 실제로 그렇게 원본이 초기값으로 날아간 적이 있습니다.
    /// </para>
    /// </summary>
    protected readonly struct NanooRead
    {
        /// <summary>읽기 자체가 실패했는지. true면 나누 상태를 알 수 없으므로 아무것도 쓰면 안 됩니다.</summary>
        public bool ReadFailed { get; }

        /// <summary>읽어온 JSON. 실패했거나 스토리지가 비었으면 null.</summary>
        public string Json { get; }

        /// <summary>읽기에 성공했고 내용도 있는지.</summary>
        public bool HasData => !ReadFailed && !string.IsNullOrEmpty(Json);

        private NanooRead(bool readFailed, string json) { ReadFailed = readFailed; Json = json; }

        public static NanooRead Failed()            => new NanooRead(true, null);
        public static NanooRead From(string json)   => new NanooRead(false, string.IsNullOrEmpty(json) ? null : json);
    }

    /// <summary>
    /// 로그인 직후 SDK 와 나누 세이브를 맞춥니다. 성공한 갈래에서만 나누 쓰기를 엽니다.
    /// </summary>
    /// <returns>
    /// 로그인을 계속해도 되면 true. 나누를 못 읽었는데 SDK 행도 없을 때만 false —
    /// 그대로 진행하면 SDK 에 기본값 행이 생기고, 그 위에서 플레이한 기록이 다음 로그인에 나누 원본을 이긴다.
    /// </returns>
    private async Task<bool> SyncDataAfterLogin()
    {
        // await 보다 먼저 닫는다 — 동기화가 도는 동안(로그아웃 없이 계정을 바꾼 경우 이전 계정 데이터가
        // 로컬에 남아 있다) 게임이 SaveNow 를 불러도 나누에 쓰지 않게.
        _nanooSyncedThisLogin = false;

        var save = Save;
        if (save == null)
        {
            Debug.LogWarning("[PlayNanooRuntime] StaticUserSave 인스턴스가 없습니다. 세이브 클래스를 초기화했는지 확인하세요.");
            return true;
        }

        var (ok, hasRow, sdkTime) = await save.NanooLoadWithStateAsync();
        if (!ok)
        {
            Debug.LogWarning("[PlayNanooRuntime] SDK 세이브를 읽지 못해 동기화를 건너뜁니다. 이번 로그인에서는 PlayNANOO 에 쓰지 않습니다.");
            return true;
        }

        var nanoo = await LoadRawFromNanoo();

        Trace($"동기화 시작 — SDK행={(hasRow ? "있음" : "없음")}, SDK시각={sdkTime:o}, " +
              $"나누읽기={(nanoo.ReadFailed ? "실패" : nanoo.HasData ? "데이터있음" : "비어있음")}, " +
              $"나누길이={nanoo.Json?.Length ?? 0}, 나누UserId={TrueBaseNanoo.UserId ?? "(없음)"}");

        // 나누를 못 읽었으면 어느 쪽이 최신인지 판단할 근거가 없다. 여기서 멈춘다 —
        // 예전에는 못 읽은 것을 "나누가 낡음"으로 읽어 SDK 데이터로 원본을 덮어썼다.
        if (nanoo.ReadFailed)
        {
            if (!hasRow)
            {
                // 여기서 행을 만들면 유저는 새 게임을 보고, 그 플레이가 다음 로그인에 나누 원본을 이긴다.
                Debug.LogWarning("[PlayNanooRuntime] PlayNANOO 스토리지를 읽지 못했고 SDK 세이브도 없어 로그인을 실패로 돌립니다. " +
                                 "다시 시도하세요 — 이대로 진행하면 새 게임 데이터가 기존 PlayNANOO 데이터를 덮게 됩니다.");
                return false;
            }

            Debug.LogWarning("[PlayNanooRuntime] PlayNANOO 스토리지를 읽지 못해 SDK 세이브로 진행합니다. " +
                             "이번 로그인에서는 PlayNANOO 에 쓰지 않습니다.");
            await save.TryLoadAsync();
            return true;
        }

        if (!hasRow)
        {
            if (nanoo.HasData)
            {
                Trace("판정: SDK행 없음 + 나누 데이터 있음 → 나누에서 가져옴(이관)");
                _nanooSyncedThisLogin = await save.NanooPatchFromEmptyAsync(nanoo.Json);
            }
            else
            {
                Trace("판정: SDK행 없음 + 나누도 비어 있음 → 새 게임으로 시작");
                _nanooSyncedThisLogin = await save.TryLoadAsync();
            }
            if (!_nanooSyncedThisLogin) Trace("동기화 실패 — 이번 로그인에서는 나누에 쓰지 않음");
            return true;
        }

        var nanooTime = save.NanooParseCompareTimestamp(nanoo.Json);
        if (nanooTime > sdkTime)
        {
            Trace($"판정: 나누가 최신(나누={nanooTime:o} > SDK={sdkTime:o}) → 나누에서 가져옴");
            _nanooSyncedThisLogin = await save.NanooPatchFromLastLoadedAsync(nanoo.Json);
            if (!_nanooSyncedThisLogin) Trace("동기화 실패 — 이번 로그인에서는 나누에 쓰지 않음");
        }
        else
        {
            Trace($"판정: SDK가 최신(나누={nanooTime:o} <= SDK={sdkTime:o}) → 나누를 SDK 데이터로 덮어씀");
            save.NanooApplyLastLoaded();
            SaveToNanoo(save.NanooGetLastLoadedJson());
            _nanooSyncedThisLogin = true;
        }
        return true;
    }

    /// <summary>
    /// PlayNANOO 스토리지를 세이브 클래스의 초기값으로 되돌립니다. <c>Supabase.DeleteUserSaveAsync()</c>가 호출합니다.
    /// <para>PlayNANOO Storage에는 삭제 API가 없어 초기값 JSON을 덮어씁니다.
    /// SDK가 만들어 넘겨주므로 여기서는 저장만 합니다.</para>
    /// </summary>
    /// <param name="defaultsJson">세이브 클래스 초기값 + <c>updated_at</c>이 담긴 JSON.</param>
    protected virtual Task ResetNanooStorageAsync(string defaultsJson)
    {
        // 이 경로는 나누 원본을 의도적으로 지웁니다. 테스트 초기화 목적으로 DeleteUserSaveAsync 를
        // 부르면 여기로 들어와 원본이 사라지므로, 눈에 띄게 남깁니다.
        Debug.LogWarning("[PlayNanooRuntime] 세이브 삭제 요청 — PlayNANOO 스토리지를 초기값으로 되돌립니다. " +
                         "원본 데이터가 사라집니다.\n호출 경로:\n" + StackTraceUtility.ExtractStackTrace());
        SaveToNanoo(defaultsJson);
        return Task.CompletedTask;
    }

    // ── PlayNANOO 저장/로드 ───────────────────────────────────────────────────

    protected virtual Task<NanooRead> LoadRawFromNanoo()
    {
        var tcs = new TaskCompletionSource<NanooRead>();
        _plugin.Storage.Load("Data", (status, message, _, values) =>
            tcs.SetResult(ToNanooRead(status, message, values)));
        return tcs.Task;
    }

    /// <summary>
    /// PlayNANOO 스토리지 읽기 결과를 분류합니다. 나누는 저장된 값이 없을 때도 실패 상태로 답하고
    /// message 에 <c>10006</c> 을 싣습니다 — 이걸 읽기 실패로 보면 빈 저장소 유저는 매번 쓰기가 막혀
    /// 나누가 영영 채워지지 않습니다(기존 게임 코드도 10006 을 "서버 데이터 없음"으로 처리합니다).
    /// </summary>
    protected NanooRead ToNanooRead(string status, string message, Dictionary<string, object> values)
    {
        if (status == Configure.PN_API_STATE_SUCCESS)
        {
            var raw = values != null && values.TryGetValue("StorageValue", out var v) ? v?.ToString() : null;
            Trace($"나누 읽기 성공 — 길이={raw?.Length ?? 0}");
            return NanooRead.From(raw);
        }

        if (message == NanooStorageNotFoundMessage)
        {
            Trace("나누 읽기 — 저장된 데이터 없음(10006)");
            return NanooRead.From(null);
        }

        Trace($"나누 읽기 실패 — status={status}, message={message}");
        return NanooRead.Failed();
    }

    private const string NanooStorageNotFoundMessage = "10006";

    /// <summary>
    /// PlayNANOO에 JSON 데이터를 그대로 저장합니다. 가드가 없어 게임에 열지 않습니다 — 게임은
    /// <see cref="TrueBaseNanoo.SaveNow"/>(가드를 거치는 <see cref="SaveCurrentToNanoo"/>)를 씁니다.
    /// </summary>
    protected virtual void SaveToNanoo(string json)
    {
        TraceNanooWrite("SaveToNanoo", json);
        if (string.IsNullOrEmpty(json)) return;
        _plugin.Storage.Save("Data", json, true,
            (status, _, _, _) =>
            {
                if (status != Configure.PN_API_STATE_SUCCESS)
                    Debug.LogWarning("[PlayNanooRuntime] PlayNANOO 저장 실패");
            });
    }

    /// <summary>
    /// 현재 로컬 세이브 데이터를 PlayNANOO에 저장합니다.
    /// <para>로그인·동기화가 끝나기 전에 부르면 아무것도 하지 않습니다 — 그 시점의 로컬 데이터는
    /// 아직 기본값이라, 내보내면 나누 원본을 지우게 됩니다.</para>
    /// </summary>
    public void SaveCurrentToNanoo()
    {
        if (!CanWriteToNanoo("SaveCurrentToNanoo")) return;

        var json = Save?.NanooCurrentJson;
        if (json != null) SaveToNanoo(json);
    }

    /// <summary>
    /// 지금 나누에 써도 되는 상태인지. 이번 로그인의 동기화가 성공으로 끝나지 않았거나 서버 정본을
    /// 못 읽었으면 쓰지 않습니다. 기본값으로 원본을 덮는 사고를 막는 마지막 방어선입니다.
    /// </summary>
    private bool CanWriteToNanoo(string caller)
    {
        if (!_nanooSyncedThisLogin)
        {
            Debug.LogWarning($"[PlayNanooRuntime] {caller} 건너뜀 — 이번 로그인의 PlayNANOO 동기화가 끝나지 않았거나 실패했습니다.");
            return false;
        }

        var save = Save;
        if (save == null || !save.NanooHasServerData)
        {
            Debug.LogWarning($"[PlayNanooRuntime] {caller} 건너뜀 — 서버 데이터를 아직 못 읽어 로컬이 기본값일 수 있습니다.");
            return false;
        }

        return true;
    }
}
