using System;
using System.Threading.Tasks;
using TrueBase.Core.Models;

namespace TrueBase.Unity
{
    /// <summary>
    /// IAP(인앱 결제) 관련 Supabase API.
    /// <c>com.unity.purchasing</c> 5.2.1 이상이 프로젝트에 설치되어 있어야 사용 가능합니다.
    /// </summary>
    public static class SupabaseIAP
    {
        // ── 파사드 생성 ────────────────────────────────────────────────────────

        /// <summary>통합 IAP 파사드를 생성합니다. Android/iOS를 자동 감지합니다.</summary>
        public static IAPFacade CreateIAP()
            => new IAPFacade(VerifyForIAPFacadeAsync);

        /// <summary>Google Play IAP 파사드를 생성합니다.</summary>
        public static GooglePlayIAPFacade CreateGooglePlayIAP()
            => new GooglePlayIAPFacade((token, productId, priceAmount, priceCurrency) =>
                Supabase.TryVerifyGooglePlayPurchaseAsync(token, productId, priceAmount: priceAmount, priceCurrency: priceCurrency));

        /// <summary>Apple App Store IAP 파사드를 생성합니다.</summary>
        public static AppleIAPFacade CreateAppleIAP()
            => new AppleIAPFacade((jws, productId) =>
                Supabase.TryVerifyApplePurchaseAsync(jws, productId));

        // ── 파사드 생성 + 초기화 ───────────────────────────────────────────────

        /// <summary>
        /// 통합 IAP 파사드를 생성하고 초기화까지 수행합니다. Android/iOS를 자동 감지합니다.
        /// </summary>
        /// <param name="productIds">등록할 소모품(Consumable) 상품 ID 목록.</param>
        /// <param name="onGrant">아이템 지급 콜백. (productId, isResuming, alreadyVerified) → true면 소모품 소비.</param>
        /// <param name="onFailed">구매 실패 콜백 (선택).</param>
        /// <param name="timeoutMs">초기화 완료 대기 최대 시간(ms). 기본 10초.</param>
        /// <returns>초기화 성공이면 <see cref="IAPFacade"/> 인스턴스, 실패이면 null.</returns>
        public static async Task<IAPFacade> CreateIAPAsync(
            string[]                             productIds,
            Func<string, bool, bool, Task<bool>> onGrant,
            Action<FailedOrder>                  onFailed  = null,
            int                                  timeoutMs = 10_000)
        {
            var facade = CreateIAP();
            facade.OnGrantItemAsync = onGrant;
            if (onFailed != null) facade.OnPurchaseFailed += onFailed;
            var ok = await facade.InitializeAsync(productIds, timeoutMs);
            if (!ok) { facade.Dispose(); return null; }
            return facade;
        }

        /// <summary>
        /// Google Play IAP 파사드를 생성하고 초기화까지 수행합니다.
        /// </summary>
        /// <param name="productIds">등록할 소모품(Consumable) 상품 ID 목록.</param>
        /// <param name="onGrant">아이템 지급 콜백. (productId, isResuming, alreadyVerified) → true면 소모품 소비.</param>
        /// <param name="onFailed">구매 실패 콜백 (선택).</param>
        /// <param name="timeoutMs">초기화 완료 대기 최대 시간(ms). 기본 10초.</param>
        /// <returns>초기화 성공이면 <see cref="GooglePlayIAPFacade"/> 인스턴스, 실패이면 null.</returns>
        public static async Task<GooglePlayIAPFacade> CreateGooglePlayIAPAsync(
            string[]                             productIds,
            Func<string, bool, bool, Task<bool>> onGrant,
            Action<FailedOrder>                  onFailed  = null,
            int                                  timeoutMs = 10_000)
        {
            var facade = CreateGooglePlayIAP();
            facade.OnGrantItemAsync = onGrant;
            if (onFailed != null) facade.OnPurchaseFailed += onFailed;
            var ok = await facade.InitializeAsync(productIds, timeoutMs);
            if (!ok) { facade.Dispose(); return null; }
            return facade;
        }

        /// <summary>
        /// Apple App Store IAP 파사드를 생성하고 초기화까지 수행합니다.
        /// </summary>
        /// <param name="productIds">등록할 소모품(Consumable) 상품 ID 목록.</param>
        /// <param name="onGrant">아이템 지급 콜백. (productId, isResuming, alreadyVerified) → true면 소모품 소비.</param>
        /// <param name="onFailed">구매 실패 콜백 (선택).</param>
        /// <param name="timeoutMs">초기화 완료 대기 최대 시간(ms). 기본 10초.</param>
        /// <returns>초기화 성공이면 <see cref="AppleIAPFacade"/> 인스턴스, 실패이면 null.</returns>
        public static async Task<AppleIAPFacade> CreateAppleIAPAsync(
            string[]                             productIds,
            Func<string, bool, bool, Task<bool>> onGrant,
            Action<FailedOrder>                  onFailed  = null,
            int                                  timeoutMs = 10_000)
        {
            var facade = CreateAppleIAP();
            facade.OnGrantItemAsync = onGrant;
            if (onFailed != null) facade.OnPurchaseFailed += onFailed;
            var ok = await facade.InitializeAsync(productIds, timeoutMs);
            if (!ok) { facade.Dispose(); return null; }
            return facade;
        }

        // ── 내부 검증 ──────────────────────────────────────────────────────────

        private static async Task<(bool, IAPPurchaseResponse)> VerifyForIAPFacadeAsync(
            string token, string productId, long priceAmount = 0, string priceCurrency = null)
        {
#if UNITY_ANDROID
            var (ok, r) = await Supabase.TryVerifyGooglePlayPurchaseAsync(token, productId, priceAmount: priceAmount, priceCurrency: priceCurrency);
            if (!ok || r == null) return (false, default);
            return (true, new IAPPurchaseResponse {
                ok               = true,
                already_verified = r.already_verified,
                order_id         = r.order_id,
                purchase_state   = r.purchase_state,
                reason           = r.reason,
                store            = "google_play"
            });
#elif UNITY_IOS
            var (ok, r) = await Supabase.TryVerifyApplePurchaseAsync(token, productId);
            if (!ok || r == null) return (false, default);
            return (true, new IAPPurchaseResponse {
                ok               = true,
                already_verified = r.already_verified,
                order_id         = r.transaction_id,
                product_id       = r.product_id,
                purchase_state   = r.purchase_state,
                reason           = r.reason,
                store            = "apple_app_store"
            });
#else
            await System.Threading.Tasks.Task.CompletedTask;
            return (false, default);
#endif
        }
    }
}
