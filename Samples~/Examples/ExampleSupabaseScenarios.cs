using System;
using System.Threading.Tasks;
using TrueBase.Core.Data;
using TrueBase.Core.Models;
using TrueBase.Unity;
using TrueBase.Unity.Config;
using UnityEngine;
using SupabaseClient = global::TrueBase.Unity.Supabase;

/// <summary>
/// Supabase SDK 기능 예제 컴포넌트.
/// SupabaseRuntime이 씬에 있어야 합니다 (같은 GameObject에 붙이거나 별도 배치).
///
/// 씬 시작 시 저장된 세션으로 자동 로그인을 시도합니다 (Start → TriggerAutoLoginAsync).
/// 로그인 성공 여부는 HandleAutoLoginCompleted 콜백으로 전달됩니다.
///
/// 키보드 단축키 (Play Mode):
///   Q — 익명 로그인       I — Google 로그인      P — Google 연동       W — 로그아웃
///   O — 세션 복원
///
///   R — 데이터 로드       V — 즉시 저장           F — 레벨 +1 (변경 시연)
///
///   T — RC Reader         U — RC Binding          E — RC Listener 토글
///
///   N — 닉네임 가용성 확인 + 설정 + 프로필 조회
///   A — 내 상태 출력       J — 서버 시간 조회      B — 차단 정보 조회
///
///   D — 탈퇴 신청          S — 탈퇴 상태 조회      C — 탈퇴 취소
///
///</summary>
public sealed class ExampleSupabaseScenarios : MonoBehaviour
{
    // ─── 초기화 이벤트 ───────────────────────────────────────────────────────

    private void Awake()
    {
        SupabaseClient.OnDuplicateLoginDetected += HandleDuplicateLoginDetected;
    }

    // 구독 시점에 이미 완료된 경우를 자동 처리하므로 OnEnable/OnDisable을 사용합니다.
    private void OnEnable()  => SupabaseRuntime.SubscribeAutoLoginCompleted(HandleAutoLoginCompleted);
    private void OnDisable() => SupabaseRuntime.UnsubscribeAutoLoginCompleted(HandleAutoLoginCompleted);

    private void Start()
    {
        // 저장된 세션을 복원합니다. 완료 시 HandleAutoLoginCompleted가 호출됩니다.
        // 로그인 성공 시 등록된 모든 StaticUserSave 데이터 로드를 자동으로 수행합니다.
        _ = SupabaseRuntime.TriggerAutoLoginAsync();
    }

    private void OnDestroy()
    {
        SupabaseClient.OnDuplicateLoginDetected -= HandleDuplicateLoginDetected;
        _rcListener?.Dispose();
        _rcBinding?.Dispose();
    }

    private void HandleAutoLoginCompleted(bool success)
    {
        if (success)
            Debug.Log($"[Supabase] 자동 로그인 성공. Level={SamplePlayerSave.Level}, Coins={SamplePlayerSave.Coins}");
        else
            Debug.Log("[Supabase] 저장된 세션 없음 — 익명 로그인(Q) 또는 소셜 로그인(I)을 시도하세요.");
    }

    private void HandleDuplicateLoginDetected()
    {
        Debug.LogWarning("[Supabase] 다른 기기에서 같은 계정으로 로그인되었습니다.");
        // TODO: 로그인 화면으로 이동하거나 팝업 표시
    }

    // ─── 인증 ────────────────────────────────────────────────────────────────

    /// <summary>Q — 익명 로그인.</summary>
    private async Task SignInAnonymouslyAsync()
    {
        var ok = await SupabaseClient.TrySignInAnonymouslyAsync();
        if (!ok) Debug.LogWarning($"[Supabase] 익명 로그인 실패: {ok.Reason}");
        else     Debug.Log("[Supabase] 익명 로그인 성공.");
    }

    /// <summary>I — Google 로그인 (Android 네이티브).</summary>
    private async Task SignInWithGoogleAsync()
    {
        var ok = await SupabaseClient.TrySignInWithGoogleAsync();
        if (!ok) Debug.LogWarning($"[Supabase] Google 로그인 실패: {ok.Reason}");
        else     Debug.Log("[Supabase] Google 로그인 성공.");
    }

    /// <summary>P — 익명 계정에 Google 연동. 익명 세션이 아니면 실패.</summary>
    private async Task LinkGoogleAsync()
    {
        if (!SupabaseClient.IsLoggedIn || SupabaseClient.Session?.User?.IsAnonymous != true)
        {
            Debug.LogWarning("[Supabase] Google 연동 실패: 익명 세션이 아닙니다.");
            return;
        }

        var ok = await SupabaseClient.TryLinkGoogleToCurrentAnonymousAsync();
        if (!ok) Debug.LogWarning($"[Supabase] Google 연동 실패: {ok.Reason}");
        else     Debug.Log("[Supabase] Google 연동 성공.");
    }

    /// <summary>W — 로그아웃. Google 네이티브 로그아웃 포함, 로그아웃 전 자동 flush.</summary>
    private async Task SignOutAsync()
    {
        var ok = await SupabaseClient.TrySignOutFullyAsync();
        if (!ok) Debug.LogWarning($"[Supabase] 로그아웃 실패: {ok.Reason}");
        else     Debug.Log("[Supabase] 로그아웃 완료.");
    }

    /// <summary>O — 로컬 저장 세션으로 로그인 복원.</summary>
    private async Task RestoreSessionAsync()
    {
        var ok = await SupabaseClient.TryRestoreSessionAsync();
        if (!ok) Debug.LogWarning($"[Supabase] 세션 복원 실패: {ok.Reason}");
        else     Debug.Log("[Supabase] 세션 복원 성공.");
    }

    // ─── 유저 데이터 ─────────────────────────────────────────────────────────

    /// <summary>
    /// R — 등록된 모든 세이브 클래스를 서버에서 불러옵니다.
    /// 새 세이브 클래스를 추가해도 이 메서드는 수정할 필요가 없습니다.
    /// </summary>
    private async Task LoadPlayerDataAsync()
    {
        if (!SupabaseClient.IsLoggedIn) { Debug.LogWarning("[Supabase] 로그인 필요."); return; }

        var ok = await SupabaseClient.TryLoadAllUserSavesAsync();
        if (!ok) Debug.LogWarning("[Supabase] 데이터 로드 실패.");
        else     Debug.Log($"[Supabase] 데이터 로드 완료. Level={SamplePlayerSave.Level}, Coins={SamplePlayerSave.Coins}");
    }

    /// <summary>
    /// V — 변경사항을 즉시 저장합니다. 변경이 없으면 네트워크 전송을 생략합니다.
    /// 저장 완료를 확인해야 하는 시점(씬 전환, 로그아웃 직전 등)에 사용하세요.
    /// 평상시 저장은 SupabaseRuntime이 자동 처리합니다.
    /// </summary>
    private async Task SavePlayerDataAsync()
    {
        if (!SupabaseClient.IsLoggedIn) { Debug.LogWarning("[Supabase] 로그인 필요."); return; }

        var ok = await SupabaseClient.TrySaveAllAsync();
        if (!ok) Debug.LogWarning("[Supabase] 데이터 저장 실패.");
        else     Debug.Log("[Supabase] 데이터 저장 완료.");
    }

    /// <summary>F — 레벨을 1 올립니다. MarkDirty 자동 호출 → 쿨타임 후 자동 저장됩니다.</summary>
    private void IncrementLevel()
    {
        SamplePlayerSave.Level += 1;
        Debug.Log($"[Supabase] Level = {SamplePlayerSave.Level} (dirty — 쿨타임 후 자동 저장)");
    }

    // ─── RemoteConfig ─────────────────────────────────────────────────────────
    // ① Reader (T): 처음 호출 시 생성, 캐시 만료 시 서버 응답 대기 후 반환
    // ② Binding (U): 60초 주기 폴링으로 자동 갱신, .Value로 즉시 읽기
    // ③ Listener (E): 값 변경 시 콜백 호출 — E를 다시 누르면 종료
    // ─────────────────────────────────────────────────────────────────────────

    [Serializable]
    [RemoteConfigKey("test")]
    private sealed class TestConfig { public string a; public int b; public bool c; }

    private Func<Task<TestConfig>>           _rcReader;
    private RemoteConfigBinding<TestConfig>  _rcBinding;
    private RemoteConfigListener<TestConfig> _rcListener;

    /// <summary>T — RemoteConfig Reader. 캐시 만료 시 서버에서 최신 값을 가져옵니다.</summary>
    private async Task TestRemoteConfigReaderAsync()
    {
        _rcReader ??= RemoteConfig<TestConfig>.CreateReader();

        var val = await _rcReader();
        if (val != null) Debug.Log($"[RC ①] Reader: a={val.a}, b={val.b}, c={val.c}");
        else             Debug.LogWarning("[RC ①] Reader: null (키 없음 또는 역직렬화 실패)");
    }

    /// <summary>U — RemoteConfig Binding. 60초 폴링, .Value로 즉시 읽기.</summary>
    private void TestRemoteConfigBinding()
    {
        _rcBinding ??= RemoteConfig<TestConfig>.CreateBinding(pollInterval: 60f);

        var val = _rcBinding.Value;
        if (val != null) Debug.Log($"[RC ②] Binding: a={val.a}, b={val.b}, c={val.c}");
        else             Debug.LogWarning("[RC ②] Binding: 아직 null (첫 fetch 전이거나 키 없음)");
    }

    /// <summary>E — RemoteConfig Listener 시작/종료 토글. 값 변경 시 콜백 호출.</summary>
    private void ToggleRemoteConfigListener()
    {
        if (_rcListener != null)
        {
            _rcListener.Dispose();
            _rcListener = null;
            Debug.Log("[RC ③] Listener 종료.");
            return;
        }

        _rcListener = RemoteConfig<TestConfig>.CreateListener(
            val => Debug.Log($"[RC ③] Listener 콜백: a={val.a}, b={val.b}, c={val.c}"),
            pollInterval: 60f);
        Debug.Log("[RC ③] Listener 시작.");
    }

    // ─── 공개 프로필 ──────────────────────────────────────────────────────────

    /// <summary>
    /// N — 닉네임 가용성 확인 → 설정 → 내 프로필 조회.
    /// displayname-set / displayname-get Edge Function이 배포되어 있어야 합니다.
    /// </summary>
    private async Task TestPublicProfileAsync()
    {
        if (!SupabaseClient.IsLoggedIn) { Debug.LogWarning("[Supabase] 로그인 필요."); return; }

        const string nickname = "TestPlayer";

        var available = await SupabaseClient.TryIsDisplayNameAvailableAsync(nickname);
        if (!available)
        {
            Debug.LogWarning($"[Supabase] 닉네임 '{nickname}' 이미 사용 중 또는 확인 실패: {available.Reason}");
            return;
        }
        Debug.Log($"[Supabase] 닉네임 '{nickname}' 사용 가능.");

        var setOk = await SupabaseClient.TrySetMyDisplayNameAsync(nickname);
        if (!setOk)
        {
            Debug.LogWarning($"[Supabase] 닉네임 설정 실패: {setOk.Reason}");
            return;
        }
        Debug.Log($"[Supabase] 닉네임 설정 완료: {nickname}");

        var myId = SupabaseClient.Session?.User?.Id;
        var profile = await SupabaseClient.TryGetPublicProfileAsync(myId);
        if (profile != null) Debug.Log($"[Supabase] 프로필 — 닉네임: {profile.DisplayName}, 서버: {profile.ServerCode}");
        else                 Debug.LogWarning("[Supabase] 프로필 조회 실패.");
    }

    // ─── 세션 상태 / 서버 ────────────────────────────────────────────────────

    /// <summary>A — 현재 세션 상태 및 서버 정보를 Console에 출력합니다.</summary>
    private async Task PrintStatusAsync()
    {
        if (!SupabaseClient.IsLoggedIn)
        {
            Debug.Log("[Supabase] 로그인되지 않은 상태.");
            return;
        }
        var profile    = SupabaseClient.MyProfile;
        var serverInfo = await SupabaseClient.TryGetMyServerInfoAsync();
        Debug.Log($"[Supabase] 상태\n" +
                  $"  IsAnonymous = {SupabaseClient.IsAnonymous}\n" +
                  $"  AccountId   = {SupabaseClient.Session?.User?.Id}\n" +
                  $"  UserId      = {SupabaseClient.UserId}\n" +
                  $"  DisplayName = {profile?.DisplayName ?? "(없음)"}\n" +
                  $"  ServerCode  = {profile?.ServerCode ?? "(없음)"}\n" +
                  $"  IsWithdrawn = {profile?.IsWithdrawn}\n" +
                  $"  ServerId    = {serverInfo.ServerId}, ServerCode = {serverInfo.ServerCode}");
    }

    /// <summary>J — 서버 시간을 조회합니다.</summary>
    private async Task GetServerTimeAsync()
    {
        var time = await SupabaseClient.TryGetServerUtcNowAsync();
        if (time == default) Debug.LogWarning("[Supabase] 서버 시간 조회 실패.");
        else                 Debug.Log($"[Supabase] 서버 시간: {time:yyyy-MM-dd HH:mm:ss} UTC");
    }

    /// <summary>B — 현재 계정의 차단 정보를 조회합니다.</summary>
    private async Task GetBanInfoAsync()
    {
        if (!SupabaseClient.IsLoggedIn) { Debug.LogWarning("[Supabase] 로그인 필요."); return; }

        var accountId = SupabaseClient.Session?.User?.Id;
        var banInfo = await SupabaseClient.TryGetBanInfoAsync(accountId);
        if (banInfo == null)
            Debug.Log("[Supabase] 차단 정보 없음 (정상 계정).");
        else
            Debug.LogWarning($"[Supabase] 차단 정보 — IsPermanent: {banInfo.IsPermanentBan}, Until: {banInfo.BannedUntil}, Message: {banInfo.BanMessage}");
    }

    // ─── 탈퇴 ────────────────────────────────────────────────────────────────

    /// <summary>D — 탈퇴 신청 (15일 유예). 실제 테스트 시 주의.</summary>
    private async Task RequestWithdrawalAsync()
    {
        if (!SupabaseClient.IsLoggedIn) { Debug.LogWarning("[Supabase] 로그인 필요."); return; }

        var ok = await SupabaseClient.TryRequestMyWithdrawalAsync();
        if (!ok) Debug.LogWarning($"[Supabase] 탈퇴 신청 실패: {ok.Reason}");
        else     Debug.Log("[Supabase] 탈퇴 신청 완료. 15일 후 삭제됩니다.");
    }

    /// <summary>S — 탈퇴 예약 상태를 조회합니다.</summary>
    private async Task GetWithdrawalStatusAsync()
    {
        if (!SupabaseClient.IsLoggedIn) { Debug.LogWarning("[Supabase] 로그인 필요."); return; }

        var status = await SupabaseClient.TryGetMyWithdrawalStatusAsync();
        if (status == null)
            Debug.Log("[Supabase] 탈퇴 예약 없음.");
        else
            Debug.Log($"[Supabase] 탈퇴 예약 — IsScheduled: {status.IsScheduled}, 남은 시간: {status.SecondsRemaining}초, 예약일: {status.WithdrawnAtIso}");
    }

    /// <summary>C — 탈퇴 취소. 탈퇴 예약 상태여야 합니다.</summary>
    private async Task ClearWithdrawalAsync()
    {
        if (!SupabaseClient.IsLoggedIn) { Debug.LogWarning("[Supabase] 로그인 필요."); return; }

        var ok = await SupabaseClient.TryClearMyWithdrawalAsync();
        if (!ok) Debug.LogWarning($"[Supabase] 탈퇴 취소 실패: {ok.Reason}");
        else     Debug.Log("[Supabase] 탈퇴 취소 완료.");
    }

    // ─── Update ──────────────────────────────────────────────────────────────

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Q)) _ = SignInAnonymouslyAsync();
        if (Input.GetKeyDown(KeyCode.I)) _ = SignInWithGoogleAsync();
        if (Input.GetKeyDown(KeyCode.P)) _ = LinkGoogleAsync();
        if (Input.GetKeyDown(KeyCode.W)) _ = SignOutAsync();
        if (Input.GetKeyDown(KeyCode.O)) _ = RestoreSessionAsync();

        if (Input.GetKeyDown(KeyCode.R)) _ = LoadPlayerDataAsync();
        if (Input.GetKeyDown(KeyCode.V)) _ = SavePlayerDataAsync();
        if (Input.GetKeyDown(KeyCode.F)) IncrementLevel();

        if (Input.GetKeyDown(KeyCode.T)) _ = TestRemoteConfigReaderAsync();
        if (Input.GetKeyDown(KeyCode.U)) TestRemoteConfigBinding();
        if (Input.GetKeyDown(KeyCode.E)) ToggleRemoteConfigListener();

        if (Input.GetKeyDown(KeyCode.N)) _ = TestPublicProfileAsync();

        if (Input.GetKeyDown(KeyCode.A)) _ = PrintStatusAsync();
        if (Input.GetKeyDown(KeyCode.J)) _ = GetServerTimeAsync();
        if (Input.GetKeyDown(KeyCode.B)) _ = GetBanInfoAsync();

        if (Input.GetKeyDown(KeyCode.D)) _ = RequestWithdrawalAsync();
        if (Input.GetKeyDown(KeyCode.S)) _ = GetWithdrawalStatusAsync();
        if (Input.GetKeyDown(KeyCode.C)) _ = ClearWithdrawalAsync();
    }
}

// =============================================================================
// 예제용 유저 저장 클래스.
// SupabaseSettings의 User Data Table 항목과 DB 테이블 컬럼명을 맞춰주세요.
// 실제 프로젝트에서는 이 파일 대신 별도 파일에 정의하는 것을 권장합니다.
// =============================================================================

public sealed class SamplePlayerSave : StaticUserSave<SamplePlayerSave.Row>
{
    public static readonly SamplePlayerSave Instance = new();
    private SamplePlayerSave() : base() { }

    [Serializable]
    public sealed class Row
    {
        [DataColumn("level")] public int level;
        [DataColumn("coins")] public int coins;
    }

    public static int Level
    {
        get => Instance.Current.level;
        set { if (Instance.Current.level == value) return; Instance.Current.level = value; Instance.MarkDirty(); }
    }

    public static int Coins
    {
        get => Instance.Current.coins;
        set { if (Instance.Current.coins == value) return; Instance.Current.coins = value; Instance.MarkDirty(); }
    }
}
