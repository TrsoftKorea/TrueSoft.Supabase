// =============================================================================
// PlayNANOO 정적 진입점 (TrueBase SDK 제공)
//
// 게임 코드가 부르는 자리입니다. Supabase 파사드와 같은 모양으로 씁니다.
//
//   TrueBaseNanoo.SaveNow();
//   TrueBaseNanoo.StartAppleSignInAndroid();
//   TrueBaseNanoo.OnWithdrawalPending += ShowCancelPopup;
//   var uuid = TrueBaseNanoo.UserId;
//
// 씬에 배치한 것이 PlayNanooRuntime(신버전)이든 PlayNanooLegacyRuntime(구버전)이든
// 같은 호출로 동작합니다 — 어느 쪽이 배치됐는지 게임이 알 필요가 없습니다.
//
// PlayNANOO 제거 시 이 파일도 함께 지웁니다.
// =============================================================================

using System;
using UnityEngine;

/// <summary>
/// 게임이 부르는 PlayNANOO 진입점.
/// <para>씬에 <c>PlayNanooRuntime</c> 또는 <c>PlayNanooLegacyRuntime</c>이 있어야 동작합니다.
/// 없으면 경고만 남기고 아무것도 하지 않습니다.</para>
/// </summary>
public static class TrueBaseNanoo
{
    // ── 로그인 정보 ───────────────────────────────────────────────────────────

    /// <summary>PlayNANOO 로그인 성공 시 받은 uuid. 로그인 전에는 null.</summary>
    public static string UserId { get; internal set; }

    /// <summary>PlayNANOO 로그인 성공 시 받은 openid. PlayNANOO가 주지 않으면 null.</summary>
    public static string OpenId { get; internal set; }

    // ── 이벤트 ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 로그인 중 탈퇴 예약 계정이 감지되면 발생합니다. 취소 UI를 띄우고
    /// <see cref="Supabase.RedeemWithdrawalCancelAsync"/>를 호출하세요 — 그 하나로
    /// PlayNANOO 복구까지 함께 처리됩니다.
    /// <para><b>정적 이벤트라 씬을 다시 열어도 구독이 남습니다.</b> 같은 대상을 두 번 구독하지
    /// 않도록, 구독한 쪽이 사라질 때 <c>-=</c>로 반드시 해제하세요.</para>
    /// </summary>
    public static event Action OnWithdrawalPending;

    internal static void RaiseWithdrawalPending() => OnWithdrawalPending?.Invoke();

    // ── 기능 ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// 현재 세이브 데이터를 PlayNANOO에 지금 저장합니다. 씬 전환·앱 종료처럼 다음 로그인까지
    /// 기다릴 수 없는 시점에 부릅니다.
    /// <para>평소에는 부를 필요가 없습니다 — <c>Supabase.SaveNowAsync()</c>로 저장한 내용은
    /// 다음 로그인의 동기화에서 PlayNANOO에 반영됩니다.</para>
    /// <para>로그인·동기화가 끝나기 전에 부르면 아무것도 하지 않습니다. 그 시점의 데이터는 아직
    /// 기본값이라, 내보내면 PlayNANOO 원본을 지우게 됩니다.</para>
    /// </summary>
    public static void SaveNow()
    {
        if (!TryGetRuntime(nameof(SaveNow), out var runtime)) return;
        runtime.SaveCurrentToNanoo();
    }

    /// <summary>
    /// 애플 로그인을 시작합니다. PlayNANOO 내장 웹뷰로 토큰을 받아
    /// <c>Supabase.SignInWithAppleIdTokenAsync</c>까지 자동으로 이어집니다. Android 전용입니다.
    /// </summary>
    public static void StartAppleSignInAndroid()
    {
        if (!TryGetRuntime(nameof(StartAppleSignInAndroid), out var runtime)) return;
        runtime.OpenAppleIdSignIn();
    }

    private static bool TryGetRuntime(string caller, out PlayNanooRuntimeBase runtime)
    {
        runtime = PlayNanooRuntimeBase.Instance;
        if (runtime != null) return true;

        Debug.LogWarning($"[TrueBaseNanoo] {caller} 건너뜀 — 씬에 PlayNanooRuntime이 없습니다.");
        return false;
    }
}
