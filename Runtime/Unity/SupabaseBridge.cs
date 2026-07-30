using System;
using System.Threading.Tasks;
using TrueBase.Core.Common;
using TrueBase.Core.Models;

namespace TrueBase.Unity
{
    /// <summary>
    /// SDK 배선 전용 진입점입니다. 게임이 부르는 API가 아니라, 어셈블리 밖에 사는 통합 런타임
    /// (<c>Samples~/PlayNanooMigration</c> 등)이 SDK 내부에 훅을 꽂기 위한 통로입니다.
    /// <para>
    /// <see cref="SupabaseSDK"/>가 <c>internal</c>이라 어셈블리 밖에서는 직접 부를 수 없어 여기를 거칩니다.
    /// 게임에 공개하는 API는 <see cref="Supabase"/>에만 두고, 배선은 이 클래스에 모읍니다.
    /// </para>
    /// </summary>
    public static class SupabaseBridge
    {
        /// <inheritdoc cref="SupabaseSDK.RegisterPlayNanooInterceptors"/>
        public static void RegisterPlayNanooInterceptors(
            Func<Func<Task<SupabaseResult>>, Task<SupabaseResult>>         signInAnonymously,
            Func<string, Func<Task<SupabaseResult>>, Task<SupabaseResult>> signInWithGoogleIdToken,
            Func<string, Func<Task<SupabaseResult>>, Task<SupabaseResult>> signInWithAppleIdToken,
            Func<Func<Task<SupabaseResult>>, Task<SupabaseResult>>         signOutFully,
            Func<Func<Task<SupabaseResult>>, Task<SupabaseResult>>         requestMyWithdrawal,
            Func<string, Func<Task<SupabaseResult>>, Task<SupabaseResult>> linkGoogleToGuestWithIdToken = null,
            Func<string, Func<Task<SupabaseResult>>, Task<SupabaseResult>> linkAppleToGuestWithIdToken  = null,
            Func<string, Func<Task<SupabaseResult>>, Task<SupabaseResult>> setMyName                    = null,
            Func<string, Func<Task<SupabaseResult>>, Task<SupabaseResult>> linkGoogleWithIdToken        = null,
            Func<string, Func<Task<SupabaseResult>>, Task<SupabaseResult>> linkAppleWithIdToken         = null,
            Func<Func<Task<SupabaseResult>>, Task<SupabaseResult>>         redeemWithdrawalCancel       = null) =>
            SupabaseSDK.RegisterPlayNanooInterceptors(
                signInAnonymously, signInWithGoogleIdToken, signInWithAppleIdToken,
                signOutFully, requestMyWithdrawal,
                linkGoogleToGuestWithIdToken, linkAppleToGuestWithIdToken,
                setMyName, linkGoogleWithIdToken, linkAppleWithIdToken,
                redeemWithdrawalCancel);

        /// <inheritdoc cref="SupabaseSDK.UnregisterPlayNanooInterceptors"/>
        public static void UnregisterPlayNanooInterceptors() =>
            SupabaseSDK.UnregisterPlayNanooInterceptors();

        /// <inheritdoc cref="SupabaseSDK.RegisterNanooStorageReset"/>
        public static void RegisterNanooStorageReset(Func<string, Task> reset) =>
            SupabaseSDK.RegisterNanooStorageReset(reset);

        /// <inheritdoc cref="SupabaseSDK.RegisterIAPAppleInterceptor"/>
        public static void RegisterIAPAppleInterceptor(
            Func<string, string, Func<Task<SupabaseResult<AppleIAPPurchaseResponse>>>, Task<SupabaseResult<AppleIAPPurchaseResponse>>> interceptor) =>
            SupabaseSDK.RegisterIAPAppleInterceptor(interceptor);

        /// <inheritdoc cref="SupabaseSDK.RegisterIAPGoogleInterceptor"/>
        public static void RegisterIAPGoogleInterceptor(
            Func<string, string, long, string, Func<Task<SupabaseResult<GooglePlayPurchaseResponse>>>, Task<SupabaseResult<GooglePlayPurchaseResponse>>> interceptor) =>
            SupabaseSDK.RegisterIAPGoogleInterceptor(interceptor);

        /// <inheritdoc cref="SupabaseSDK.GetNanooSaveBridge"/>
        public static INanooSaveSyncable GetNanooSaveBridge() => SupabaseSDK.GetNanooSaveBridge();
    }
}
