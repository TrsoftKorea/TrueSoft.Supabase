using System;
using System.Threading.Tasks;
using TrueBase.Core.Common;
using TrueBase.Core.Models;

namespace TrueBase.Unity
{
    /// <summary>
    /// IAP(인앱 결제) 관련 Supabase API.
    /// Unity IAP v4 (<c>com.unity.purchasing</c> 4.x) 및 v5 (5.0 이상) 모두 지원합니다. (iOS SK1 강제는 5.1+)
    /// </summary>
    public static class SupabaseIAP
    {

        /// <summary>통합 IAP 파사드를 생성합니다. Android/iOS를 자동 감지합니다.</summary>
        internal static IAPFacade CreateIAP()
        {
#if UNITY_IAP_V5
            return new IAPFacade(VerifyForIAPFacadeAsync, VerifyReceiptForIAPFacadeAsync);
#else
            return new IAPFacade(VerifyGoogleForIAPFacadeV4Async, VerifyAppleForIAPFacadeV4Async);
#endif
        }

        /// <summary>Google Play IAP 파사드를 생성합니다.</summary>
        internal static GooglePlayIAPFacade CreateGooglePlayIAP()
            => new GooglePlayIAPFacade((token, productId, priceAmount, priceCurrency) =>
                Supabase.TryVerifyGooglePlayPurchaseAsync(token, productId, priceAmount: priceAmount, priceCurrency: priceCurrency));

        /// <summary>Apple App Store IAP 파사드를 생성합니다.</summary>
        internal static AppleIAPFacade CreateAppleIAP()
        {
#if UNITY_IAP_V5
            return new AppleIAPFacade(
                (jws, productId)     => Supabase.TryVerifyApplePurchaseAsync(jws, productId),
                (receipt, productId) => Supabase.TryVerifyApplePurchaseLegacyAsync(receipt, productId));
#else
            return new AppleIAPFacade(
                (receipt, productId) => Supabase.TryVerifyApplePurchaseLegacyAsync(receipt, productId));
#endif
        }


        /// <summary>
        /// 통합 IAP 파사드를 생성하고 초기화까지 수행합니다. Android/iOS를 자동 감지합니다.
        /// </summary>
        /// <param name="productIds">등록할 소모품(Consumable) 상품 ID 목록.</param>
        /// <param name="onGrant">아이템 지급 콜백. (productId, isResuming, alreadyVerified) → true면 소모품 소비.</param>
        /// <param name="onFailed">구매 실패 콜백 (선택).</param>
        /// <param name="timeoutMs">초기화 완료 대기 최대 시간(ms). 기본 10초.</param>
        /// <returns>초기화 성공이면 <c>.Data</c>에 <see cref="IAPFacade"/> 인스턴스, 실패이면 실패 사유.</returns>
        public static async Task<SupabaseResult<IAPFacade>> CreateIAPAsync(
            string[]                             productIds,
            Func<string, bool, bool, Task<bool>> onGrant,
            Action<IAPPurchaseFailedInfo>         onFailed  = null,
            int                                  timeoutMs = 10_000)
        {
            var facade = CreateIAP();
            facade.OnGrantItemAsync = onGrant;
            if (onFailed != null) facade.OnPurchaseFailed += onFailed;
            var init = await facade.InitializeAsync(productIds, timeoutMs);
            if (!init) { facade.Dispose(); return SupabaseResult<IAPFacade>.Fail(init.ErrorCode); }
            return SupabaseResult<IAPFacade>.Success(facade);
        }

        /// <summary>
        /// Google Play IAP 파사드를 생성하고 초기화까지 수행합니다.
        /// </summary>
        /// <param name="productIds">등록할 소모품(Consumable) 상품 ID 목록.</param>
        /// <param name="onGrant">아이템 지급 콜백. (productId, isResuming, alreadyVerified) → true면 소모품 소비.</param>
        /// <param name="onFailed">구매 실패 콜백 (선택).</param>
        /// <param name="timeoutMs">초기화 완료 대기 최대 시간(ms). 기본 10초.</param>
        /// <returns>초기화 성공이면 <c>.Data</c>에 <see cref="GooglePlayIAPFacade"/> 인스턴스, 실패이면 실패 사유.</returns>
        public static async Task<SupabaseResult<GooglePlayIAPFacade>> CreateGooglePlayIAPAsync(
            string[]                             productIds,
            Func<string, bool, bool, Task<bool>> onGrant,
            Action<IAPPurchaseFailedInfo>         onFailed  = null,
            int                                  timeoutMs = 10_000)
        {
            var facade = CreateGooglePlayIAP();
            facade.OnGrantItemAsync = onGrant;
            if (onFailed != null) facade.OnPurchaseFailed += onFailed;
            var init = await facade.InitializeAsync(productIds, timeoutMs);
            if (!init) { facade.Dispose(); return SupabaseResult<GooglePlayIAPFacade>.Fail(init.ErrorCode); }
            return SupabaseResult<GooglePlayIAPFacade>.Success(facade);
        }

        /// <summary>
        /// Apple App Store IAP 파사드를 생성하고 초기화까지 수행합니다.
        /// </summary>
        /// <param name="productIds">등록할 소모품(Consumable) 상품 ID 목록.</param>
        /// <param name="onGrant">아이템 지급 콜백. (productId, isResuming, alreadyVerified) → true면 소모품 소비.</param>
        /// <param name="onFailed">구매 실패 콜백 (선택).</param>
        /// <param name="timeoutMs">초기화 완료 대기 최대 시간(ms). 기본 10초.</param>
        /// <returns>초기화 성공이면 <c>.Data</c>에 <see cref="AppleIAPFacade"/> 인스턴스, 실패이면 실패 사유.</returns>
        public static async Task<SupabaseResult<AppleIAPFacade>> CreateAppleIAPAsync(
            string[]                             productIds,
            Func<string, bool, bool, Task<bool>> onGrant,
            Action<IAPPurchaseFailedInfo>         onFailed  = null,
            int                                  timeoutMs = 10_000)
        {
            var facade = CreateAppleIAP();
            facade.OnGrantItemAsync = onGrant;
            if (onFailed != null) facade.OnPurchaseFailed += onFailed;
            var init = await facade.InitializeAsync(productIds, timeoutMs);
            if (!init) { facade.Dispose(); return SupabaseResult<AppleIAPFacade>.Fail(init.ErrorCode); }
            return SupabaseResult<AppleIAPFacade>.Success(facade);
        }

        // 내부 검증 헬퍼 (IAPFacade 전용)

#if UNITY_IAP_V5
        /// <summary>
        /// v5 <see cref="IAPFacade"/>용 검증 헬퍼. 플랫폼별 Edge Function 검증 결과를 공통 <c>IAPPurchaseResponse</c>로 변환합니다.
        /// </summary>
        /// <param name="token">Android는 Google Play purchaseToken, iOS는 StoreKit 2 JWS 토큰.</param>
        /// <param name="productId">스토어 상품 ID.</param>
        /// <param name="priceAmount">결제 금액. micros(주 단위 ×1,000,000) 정수. Android 가격 검증용, iOS 경로에서는 0.</param>
        /// <param name="priceCurrency">ISO 4217 통화 코드. Android 전용, iOS 경로에서는 null.</param>
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
            // SK2 (JWS) 경로
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

        /// <summary>
        /// v5 iOS StoreKit 1 폴백 검증 헬퍼. iOS 14 이하 또는 <c>forceStoreKit1</c> 활성화 시 사용됩니다.
        /// </summary>
        /// <param name="receipt">Unity IAP 영수증에서 추출한 base64 SK1 영수증 Payload.</param>
        /// <param name="productId">스토어 상품 ID.</param>
        private static async Task<(bool, IAPPurchaseResponse)> VerifyReceiptForIAPFacadeAsync(
            string receipt, string productId)
        {
            var (ok, r) = await Supabase.TryVerifyApplePurchaseLegacyAsync(receipt, productId);
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
        }

#else
        /// <summary>
        /// v4 <see cref="IAPFacade"/>용 Google Play 검증 헬퍼.
        /// </summary>
        /// <param name="token">Google Play purchaseToken.</param>
        /// <param name="productId">스토어 상품 ID.</param>
        /// <param name="priceAmount">결제 금액. micros(주 단위 ×1,000,000) 정수.</param>
        /// <param name="priceCurrency">ISO 4217 통화 코드.</param>
        private static async Task<(bool, IAPPurchaseResponse)> VerifyGoogleForIAPFacadeV4Async(
            string token, string productId, long priceAmount = 0, string priceCurrency = null)
        {
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
        }

        /// <summary>
        /// v4 <see cref="IAPFacade"/>용 Apple StoreKit 1 검증 헬퍼.
        /// </summary>
        /// <param name="receipt">Unity IAP 영수증에서 추출한 base64 SK1 영수증 Payload.</param>
        /// <param name="productId">스토어 상품 ID.</param>
        private static async Task<(bool, IAPPurchaseResponse)> VerifyAppleForIAPFacadeV4Async(
            string receipt, string productId)
        {
            var (ok, r) = await Supabase.TryVerifyApplePurchaseLegacyAsync(receipt, productId);
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
        }
#endif
    }
}
