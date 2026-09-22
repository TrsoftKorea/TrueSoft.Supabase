using System;
using System.Threading.Tasks;
using TrueBase.Core.Common;
using TrueBase.Core.Models;

namespace TrueBase.Unity
{
    /// <summary>
    /// IAP(인앱 결제) 관련 Supabase API.
    /// Unity IAP 5.0 이상(<c>com.unity.purchasing</c>)이 필요합니다. iOS SK1 강제는 5.1 이상입니다.
    /// </summary>
    public static class SupabaseIAP
    {

        /// <summary>통합 IAP 파사드를 생성합니다. Android/iOS를 자동 감지합니다.</summary>
        private static IAPFacade CreateIAP()
            => new IAPFacade(VerifyForIAPFacadeAsync, VerifyReceiptForIAPFacadeAsync);

        /// <summary>Google Play IAP 파사드를 생성합니다.</summary>
        private static GooglePlayIAPFacade CreateGooglePlayIAP()
            => new GooglePlayIAPFacade((token, productId, priceAmount, priceCurrency, rawReceipt) =>
                SupabaseSDK.TryVerifyGooglePlayPurchaseAsync(token, productId, priceAmount: priceAmount, priceCurrency: priceCurrency, rawReceipt: rawReceipt));

        /// <summary>Apple App Store IAP 파사드를 생성합니다.</summary>
        private static AppleIAPFacade CreateAppleIAP()
            => new AppleIAPFacade(
                (jws, productId)     => SupabaseSDK.TryVerifyApplePurchaseAsync(jws, productId),
                (receipt, productId) => SupabaseSDK.TryVerifyApplePurchaseLegacyAsync(receipt, productId));


        /// <summary>
        /// 통합 IAP 파사드를 생성하고 초기화까지 수행합니다. Android/iOS를 자동 감지합니다.
        /// </summary>
        /// <param name="productIds">등록할 소모품(Consumable) 상품 ID 목록.</param>
        /// <param name="onGrant">아이템 지급 콜백. (productId, alreadyGranted) → true면 소모품 소비.</param>
        /// <param name="onFailed">구매 실패 콜백 (선택).</param>
        /// <param name="timeoutMs">초기화 완료 대기 최대 시간(ms). 기본 10초.</param>
        /// <returns>초기화 성공이면 <c>.Data</c>에 <see cref="IAPFacade"/> 인스턴스, 실패이면 실패 사유.</returns>
        public static async Task<SupabaseResult<IAPFacade>> CreateIAPAsync(
            string[]                       productIds,
            Func<string, bool, Task<bool>> onGrant,
            Action<IAPPurchaseFailedInfo>  onFailed  = null,
            int                            timeoutMs = 10_000)
        {
            var facade = CreateIAP();
            SupabaseSDK.RegisterActiveIapFacade(facade);
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
        /// <param name="onGrant">아이템 지급 콜백. (productId, alreadyGranted) → true면 소모품 소비.</param>
        /// <param name="onFailed">구매 실패 콜백 (선택).</param>
        /// <param name="timeoutMs">초기화 완료 대기 최대 시간(ms). 기본 10초.</param>
        /// <returns>초기화 성공이면 <c>.Data</c>에 <see cref="GooglePlayIAPFacade"/> 인스턴스, 실패이면 실패 사유.</returns>
        public static async Task<SupabaseResult<GooglePlayIAPFacade>> CreateGooglePlayIAPAsync(
            string[]                       productIds,
            Func<string, bool, Task<bool>> onGrant,
            Action<IAPPurchaseFailedInfo>  onFailed  = null,
            int                            timeoutMs = 10_000)
        {
            var facade = CreateGooglePlayIAP();
            SupabaseSDK.RegisterActiveIapFacade(facade);
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
        /// <param name="onGrant">아이템 지급 콜백. (productId, alreadyGranted) → true면 소모품 소비.</param>
        /// <param name="onFailed">구매 실패 콜백 (선택).</param>
        /// <param name="timeoutMs">초기화 완료 대기 최대 시간(ms). 기본 10초.</param>
        /// <returns>초기화 성공이면 <c>.Data</c>에 <see cref="AppleIAPFacade"/> 인스턴스, 실패이면 실패 사유.</returns>
        public static async Task<SupabaseResult<AppleIAPFacade>> CreateAppleIAPAsync(
            string[]                       productIds,
            Func<string, bool, Task<bool>> onGrant,
            Action<IAPPurchaseFailedInfo>  onFailed  = null,
            int                            timeoutMs = 10_000)
        {
            var facade = CreateAppleIAP();
            SupabaseSDK.RegisterActiveIapFacade(facade);
            facade.OnGrantItemAsync = onGrant;
            if (onFailed != null) facade.OnPurchaseFailed += onFailed;
            var init = await facade.InitializeAsync(productIds, timeoutMs);
            if (!init) { facade.Dispose(); return SupabaseResult<AppleIAPFacade>.Fail(init.ErrorCode); }
            return SupabaseResult<AppleIAPFacade>.Success(facade);
        }

        // 내부 검증 헬퍼 (IAPFacade 전용)

        /// <summary>
        /// <see cref="IAPFacade"/>용 검증 헬퍼. 플랫폼별 Edge Function 검증 결과를 공통 <c>IAPPurchaseResponse</c>로 변환합니다.
        /// </summary>
        /// <param name="token">Android는 Google Play purchaseToken, iOS는 StoreKit 2 JWS 토큰.</param>
        /// <param name="productId">스토어 상품 ID.</param>
        /// <param name="priceAmount">결제 금액. micros(주 단위 ×1,000,000) 정수. Android 가격 검증용, iOS 경로에서는 0.</param>
        /// <param name="priceCurrency">ISO 4217 통화 코드. Android 전용, iOS 경로에서는 null.</param>
        /// <param name="rawReceipt">Unity IAP 영수증 원문. SDK 검증에는 쓰지 않고 외부 결제 서버 인터셉터에만 전달합니다.</param>
        private static async Task<(bool, IAPPurchaseResponse)> VerifyForIAPFacadeAsync(
            string token, string productId, long priceAmount = 0, string priceCurrency = null, string rawReceipt = null)
        {
#if UNITY_ANDROID
            var (ok, r) = await SupabaseSDK.TryVerifyGooglePlayPurchaseAsync(token, productId, priceAmount: priceAmount, priceCurrency: priceCurrency, rawReceipt: rawReceipt);
            if (!ok || r == null) return (false, default);
            return (true, new IAPPurchaseResponse {
                ok               = true,
                already_verified = r.already_verified,
                already_granted  = r.already_granted,
                order_id         = r.order_id,
                reason           = r.reason,
                store            = "google_play"
            });
#elif UNITY_IOS
            // SK2 (JWS) 경로
            var (ok, r) = await SupabaseSDK.TryVerifyApplePurchaseAsync(token, productId);
            if (!ok || r == null) return (false, default);
            return (true, new IAPPurchaseResponse {
                ok               = true,
                already_verified = r.already_verified,
                already_granted  = r.already_granted,
                order_id         = r.transaction_id,
                product_id       = r.product_id,
                reason           = r.reason,
                store            = "apple_app_store"
            });
#else
            await System.Threading.Tasks.Task.CompletedTask;
            return (false, default);
#endif
        }

        /// <summary>
        /// iOS StoreKit 1 폴백 검증 헬퍼. iOS 14 이하 또는 <c>forceStoreKit1</c> 활성화 시 사용됩니다.
        /// </summary>
        /// <param name="receipt">Unity IAP 영수증에서 추출한 base64 SK1 영수증 Payload.</param>
        /// <param name="productId">스토어 상품 ID.</param>
        private static async Task<(bool, IAPPurchaseResponse)> VerifyReceiptForIAPFacadeAsync(
            string receipt, string productId)
        {
            var (ok, r) = await SupabaseSDK.TryVerifyApplePurchaseLegacyAsync(receipt, productId);
            if (!ok || r == null) return (false, default);
            return (true, new IAPPurchaseResponse {
                ok               = true,
                already_verified = r.already_verified,
                already_granted  = r.already_granted,
                order_id         = r.transaction_id,
                product_id       = r.product_id,
                reason           = r.reason,
                store            = "apple_app_store"
            });
        }

    }
}
