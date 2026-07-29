using System.Threading.Tasks;
using TrueBase.Core.Common;
using TrueBase.Unity;
using UnityEngine;

/// <summary>
/// 쿠폰 예제 컴포넌트. SupabaseRuntime이 씬에 있어야 합니다.
///
/// 쿠폰 생성은 운영(Retool) 전용입니다 — Retool의 쿠폰 페이지에서 먼저 만들고,
/// 발급된 코드를 Inspector의 <c>code</c>에 넣어 실행하세요.
///
/// 보상은 응답으로 오지 않고 우편으로 지급됩니다. 사용 후 우편함을 새로 조회하세요.
///
/// 샘플을 여러 개 함께 쓰면 단축키가 겹칩니다. 그때는 <b>Tab</b> 으로 키를 받을 샘플을 고르세요.
/// 씬에 샘플이 하나뿐이면 그냥 눌러도 됩니다.
///
/// 키보드 단축키 (Play Mode):
///   1 — 익명 로그인
///   2 — 쿠폰 사용
///   3 — 우편함 확인
/// </summary>
public sealed class SampleCoupon : MonoBehaviour
{
    private const string Tag = "[Supabase.Coupon]";

    [Tooltip("사용할 쿠폰 코드. 대소문자·앞뒤 공백은 서버가 정규화합니다.")]
    [SerializeField] private string code = "THANKYOU";

    private void Update()
    {
        // 여러 샘플을 한 씬에 놓으면 단축키가 겹친다. Tab 으로 고른 대상만 키를 읽는다.
        if (!SampleFocus.IsActive(this)) return;

        if (Input.GetKeyDown(KeyCode.Alpha1)) _ = SignInAsync();
        if (Input.GetKeyDown(KeyCode.Alpha2)) _ = RedeemAsync();
        if (Input.GetKeyDown(KeyCode.Alpha3)) _ = ShowMailsAsync();
    }

    /// <summary>1 — 익명 로그인.</summary>
    private async Task SignInAsync()
    {
        var ok = await Supabase.SignInAnonymouslyAsync();
        if (!ok) { Debug.LogWarning($"{Tag} 로그인 실패: {ok.ErrorCode}"); return; }

        Debug.Log($"{Tag} 로그인됨. UserId = {Supabase.UserId}");
    }

    /// <summary>2 — 쿠폰 사용. 실패 사유는 Reason으로 분기해 유저에게 안내합니다.</summary>
    private async Task RedeemAsync()
    {
        var r = await Supabase.RedeemCouponAsync(code);
        if (r.IsSuccess)
        {
            Debug.Log($"{Tag} 쿠폰을 사용했습니다. 보상은 우편함에 있습니다. 3번으로 확인하세요.");
            return;
        }

        Debug.Log($"{Tag} {MessageFor(r.Reason, r.ErrorCode)}");
    }

    /// <summary>3 — 우편함 확인. 쿠폰 보상이 들어와 있어야 합니다.</summary>
    private async Task ShowMailsAsync()
    {
        var r = await Supabase.GetMailsAsync();
        if (!r.IsSuccess) { Debug.LogWarning($"{Tag} 우편함 조회 실패: {r.ErrorCode}"); return; }

        Debug.Log($"{Tag} 우편 {r.Data.Count}통");
        foreach (var mail in r.Data)
            Debug.Log($"{Tag}  {mail.Title} — 수령 {(mail.ItemsClaimedAt.HasValue ? "완료" : "가능")}");
    }

    /// <summary>실패 사유를 유저에게 보여줄 문구로 바꿉니다.</summary>
    private static string MessageFor(SupabaseReason reason, string errorCode) => reason switch
    {
        SupabaseReason.CouponNotFound    => "존재하지 않는 쿠폰 코드입니다.",
        SupabaseReason.CouponInactive    => "지금은 사용할 수 없는 쿠폰입니다.",
        SupabaseReason.CouponExpired     => "사용 기한이 지난 쿠폰입니다.",
        SupabaseReason.CouponAlreadyUsed => "이미 사용한 쿠폰입니다.",
        SupabaseReason.CouponExhausted   => "쿠폰이 모두 소진되었습니다.",
        SupabaseReason.NotSignedIn       => "먼저 로그인하세요.",
        _                                => $"쿠폰 사용에 실패했습니다: {errorCode}",
    };
}
