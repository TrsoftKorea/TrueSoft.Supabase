#if TRUESOFT_IAP_AVAILABLE
using System.Threading.Tasks;
using TrueBase.Unity;
using UnityEngine;
using SupabaseClient = global::TrueBase.Unity.Supabase;

/// <summary>
/// Supabase IAP 서버 검증 예제 컴포넌트.
/// SupabaseRuntime이 씬에 있어야 하며, 로그인(수동 또는 세션 복원) 후 IAP를 자동 초기화합니다.
///
/// 키보드 단축키 (Play Mode):
///   B — 아이템 구매 (IAP 초기화 완료 후 동작)
///
/// 사전 준비:
///   1. Window > Package Manager > com.unity.purchasing 4.x 또는 5.2.1 이상 설치
///   2. Google Service Account / Apple Shared Secret → Supabase Secrets 등록
///   3. supabase functions deploy purchase-verify-google (또는 purchase-verify-apple)
///   4. Sql/player/07_purchases.sql 실행
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

    private bool _initializationAttempted;

    private void OnDestroy()
    {
        _iapFacade?.Dispose(); // 씬 언로드 시 반드시 호출
    }

    // ─── 자동 초기화 ─────────────────────────────────────────────────────────
    // Update에서 로그인 상태를 감지해 초기화합니다.
    // - 수동 로그인:  TrySignInAnonymouslyAsync() 완료 → 다음 프레임에서 감지
    // - 세션 복원:    SupabaseRuntime이 Awake에서 복원 → Start 이후 프레임에서 감지
    // 실제 게임에서는 로그인 완료 콜백(OnLoginComplete 등) 안에서 직접 호출하는 것이 더 명확합니다.

    private void Update()
    {
        // 로그인 감지 → IAP 자동 초기화 (중복 방지)
        if (!_initializationAttempted && SupabaseClient.IsLoggedIn)
        {
            _initializationAttempted = true;
            _ = InitializeIAPAsync();
        }

        if (Input.GetKeyDown(KeyCode.B)) StartPurchase();
    }

    // ─── 초기화 ──────────────────────────────────────────────────────────────

    private async Task InitializeIAPAsync()
    {
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
            Debug.LogWarning("[Supabase.IAP] IAP가 아직 초기화되지 않았습니다. 로그인 여부를 확인하세요.");
            return;
        }

        _iapFacade.Purchase(productId);
    }

    // ─── 콜백 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 서버 검증 성공 후 호출됩니다.
    ///
    /// alreadyVerified=true: 서버 DB에 이미 검증 기록이 있음
    ///   → 아이템 지급 후 ConfirmPurchase 전에 크래시된 경우
    ///   → DB에서 지급 여부를 확인해 중복 지급을 방지하세요
    ///
    /// true 반환 → SDK가 ConfirmPurchase(소모품 소비) 호출
    /// false 반환 → Pending 유지 → 다음 InitializeAsync에서 재처리
    /// </summary>
    private async Task<bool> OnGrantItemAsync(string grantedProductId, bool isResuming, bool alreadyVerified)
    {
        if (alreadyVerified)
        {
            // 서버는 이미 이 영수증을 처리했음 (지급 후 크래시 케이스)
            // DB에서 지급 여부를 확인해 중복 지급을 방지하세요
            // bool alreadyGranted = await MyInventory.HasItemAsync(grantedProductId);
            // if (alreadyGranted) return true; // 이미 지급됨 → 소비만 완료
        }

        // ── 실제 아이템 지급 로직을 여기에 작성하세요 ──
        Debug.Log($"[Supabase.IAP] 아이템 지급: {grantedProductId} " +
                  $"(isResuming={isResuming}, alreadyVerified={alreadyVerified})");
        // await MyInventory.GiveItemAsync(grantedProductId);

        await Task.CompletedTask; // 위 주석 해제 시 제거
        return true;
    }

    /// <summary>결제 실패 시 호출됩니다 (사용자 취소 포함).</summary>
    private void OnPurchaseFailed(IAPPurchaseFailedInfo info)
    {
        Debug.LogWarning($"[Supabase.IAP] 구매 실패: product={info.ProductId}, reason={info.FailureReason}");
        // TODO: UI 피드백 표시
    }
}
#endif // TRUESOFT_IAP_AVAILABLE
