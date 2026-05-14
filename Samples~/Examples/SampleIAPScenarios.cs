#if TRUESOFT_IAP_AVAILABLE
using System.Threading.Tasks;
using Truesoft.Supabase.Unity;
using UnityEngine;
using UnityEngine.Purchasing;
using SupabaseClient = global::Truesoft.Supabase.Unity.Supabase;

/// <summary>
/// Supabase IAP 서버 검증 예제 컴포넌트.
/// SupabaseRuntime이 씬에 있어야 하며, 로그인 후에 IAP를 초기화하세요.
///
/// 키보드 단축키 (Play Mode):
///   O — IAP 초기화     B — 아이템 구매
///
/// 사전 준비:
///   1. Window > Package Manager > com.unity.purchasing 5.2.1 이상 설치
///   2. Google Service Account / Apple Shared Secret → Supabase Secrets 등록
///   3. supabase functions deploy purchase-verify-google (또는 purchase-verify-apple)
///   4. Sql/player/14_purchases.sql 실행
///   5. Inspector에서 productId 입력
/// </summary>
public sealed class SampleIAPScenarios : MonoBehaviour
{
    [SerializeField] private string productId = "com.mygame.item_1000";

    // ─── IAP 파사드 ──────────────────────────────────────────────────────────

    // 플랫폼 자동 감지 (Android → Google Play, iOS → Apple App Store)
    private IAPFacade _iapFacade;

    // 플랫폼별로 직접 지정할 경우:
    // private GooglePlayIAPFacade _iapFacade;
    // private AppleIAPFacade      _iapFacade;

    private void OnDestroy()
    {
        _iapFacade?.Dispose(); // 씬 언로드 시 반드시 호출
    }

    // ─── 초기화 ──────────────────────────────────────────────────────────────

    /// <summary>O — IAP 초기화. 로그인 직후 한 번 호출하세요.</summary>
    private async Task InitializeIAPAsync()
    {
        if (_iapFacade != null)
        {
            Debug.Log("[Supabase.IAP] 이미 초기화되었습니다.");
            return;
        }

        if (!SupabaseClient.IsLoggedIn)
        {
            Debug.LogWarning("[Supabase.IAP] 로그인 후 IAP를 초기화하세요.");
            return;
        }

        _iapFacade = await SupabaseClient.CreateIAPAsync(
            productIds: new[] { productId },
            onGrant:    OnGrantItemAsync,
            onFailed:   OnPurchaseFailed);

        if (_iapFacade == null)
            Debug.LogWarning("[Supabase.IAP] 초기화 실패. 네트워크 상태를 확인하세요.");
        else
            Debug.Log("[Supabase.IAP] 초기화 완료.");
    }

    // ─── 구매 ────────────────────────────────────────────────────────────────

    /// <summary>B — 결제창을 표시합니다.</summary>
    private void StartPurchase()
    {
        if (_iapFacade == null)
        {
            Debug.LogWarning("[Supabase.IAP] 먼저 O키로 초기화하세요.");
            return;
        }

        _iapFacade.Purchase(productId);
    }

    // ─── 콜백 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 서버 검증 성공 후 호출됩니다.
    /// true를 반환하면 SDK가 ConfirmPurchase(소모품 소비)를 호출합니다.
    /// false를 반환하면 Pending 유지 → 다음 InitializeAsync에서 재처리됩니다.
    /// </summary>
    private async Task<bool> OnGrantItemAsync(string grantedProductId, bool isResuming)
    {
        if (isResuming)
        {
            // 앱 재시작 후 미처리 구매를 재처리 중
            // 이전에 이미 지급했는지 확인해 중복 지급을 방지하세요
            // bool alreadyGranted = await MyInventory.HasItemAsync(grantedProductId);
            // if (alreadyGranted) return true; // 소비만 완료
        }

        // ── 실제 아이템 지급 로직을 여기에 작성하세요 ──
        Debug.Log($"[Supabase.IAP] 아이템 지급: {grantedProductId} (isResuming={isResuming})");
        // await MyInventory.GiveItemAsync(grantedProductId);

        await Task.CompletedTask; // 위 주석 해제 시 제거
        return true; // true → SDK가 ConfirmPurchase 호출 (소모품 소비 완료)
    }

    /// <summary>결제 실패 시 호출됩니다 (사용자 취소 포함).</summary>
    private void OnPurchaseFailed(FailedOrder order)
    {
        Debug.LogWarning($"[Supabase.IAP] 구매 실패: {order}");
        // TODO: UI 피드백 표시
    }

    // ─── Update ──────────────────────────────────────────────────────────────

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.O)) _ = InitializeIAPAsync();
        if (Input.GetKeyDown(KeyCode.B)) StartPurchase();
    }
}
#endif // TRUESOFT_IAP_AVAILABLE
