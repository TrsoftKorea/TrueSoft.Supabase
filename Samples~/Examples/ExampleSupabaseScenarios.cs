using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using TrueBase.Core.Data;
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
///   Q — 익명 로그인
///   I — Google 로그인      P — Google 연동        K — Google 연동 해제
///   B — Apple 로그인       H — Apple 연동         L — Apple 연동 해제
///   W — 로그아웃           O — 세션 복원
///
///   R — 데이터 로드       V — 즉시 저장           F — 레벨 +1 (변경 시연)
///   X — 세이브 삭제 (기본값 리셋 + 재로드)
///
///   T — RC Reader         U — RC Binding          E — RC Listener 토글
///
///   N — 닉네임 가용성 확인 + 설정 + 프로필 조회
///   A — 내 상태 출력       J — 서버 시간 조회      G — 차단 정보 조회
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

    private async void Start()
    {
        var result = await SupabaseRuntime.TriggerAutoLoginAsync();
        if (result)
            Debug.Log($"[Supabase] 자동 로그인 성공. Level={SamplePlayerSave.Level}, Coins={SamplePlayerSave.Coins}");
        else
            Debug.Log("[Supabase] 저장된 세션 없음 — 익명 로그인(Q) 또는 소셜 로그인(I)을 시도하세요.");
    }

    private void OnDestroy()
    {
        SupabaseClient.OnDuplicateLoginDetected -= HandleDuplicateLoginDetected;
        _rcListener?.Dispose();
        _rcBinding?.Dispose();
    }


    private void HandleDuplicateLoginDetected()
    {
        Debug.LogWarning("[Supabase] 다른 기기에서 같은 계정으로 로그인되었습니다.");
        // TODO: 로그인 화면으로 이동하거나 팝업 표시
    }

    // ─── 인증 ────────────────────────────────────────────────────────────────

    /// <summary>Q — 익명 로그인. 로그인과 데이터 로드는 별개 단계 — 로그인 후 데이터 로드(R)를 호출합니다.</summary>
    private async Task SignInAnonymouslyAsync()
    {
        var ok = await SupabaseClient.SignInAnonymouslyAsync();
        if (!ok) Debug.LogWarning($"[Supabase] 익명 로그인 실패: {ok.ErrorMessage}");
        else     Debug.Log("[Supabase] 익명 로그인 성공. 이제 데이터 로드(R)를 호출하세요.");
    }

    /// <summary>I — Google 로그인 (Android 네이티브). 로그인 후 데이터 로드(R)를 호출합니다.</summary>
    private async Task SignInWithGoogleAsync()
    {
        var ok = await SupabaseClient.SignInWithGoogleAsync();
        if (!ok) Debug.LogWarning($"[Supabase] Google 로그인 실패: {ok.ErrorMessage}");
        else     Debug.Log("[Supabase] Google 로그인 성공. 이제 데이터 로드(R)를 호출하세요.");
    }

    /// <summary>P — 익명 계정에 Google 연동. 익명 세션이 아니면 실패.</summary>
    private async Task LinkGoogleAsync()
    {
        if (!SupabaseClient.IsLoggedIn || !SupabaseClient.IsAnonymous)
        {
            Debug.LogWarning("[Supabase] Google 연동 실패: 익명 세션이 아닙니다.");
            return;
        }

        var ok = await SupabaseClient.LinkGoogleToCurrentAnonymousAsync();
        if (!ok) Debug.LogWarning($"[Supabase] Google 연동 실패: {ok.ErrorMessage}");
        else     Debug.Log("[Supabase] Google 연동 성공.");
    }

    /// <summary>B — Apple 로그인 (iOS 네이티브 Sign in with Apple). 익명 세션이면 실패 — 연동은 H를 쓰세요.</summary>
    private async Task SignInWithAppleAsync()
    {
        var ok = await SupabaseClient.SignInWithAppleAsync();
        if (!ok) Debug.LogWarning($"[Supabase] Apple 로그인 실패: {ok.ErrorMessage}");
        else     Debug.Log("[Supabase] Apple 로그인 성공. 이제 데이터 로드(R)를 호출하세요.");
    }

    /// <summary>H — 익명 계정에 Apple 연동 (iOS 네이티브 Sign in with Apple). 익명 세션이 아니면 실패.</summary>
    private async Task LinkAppleAsync()
    {
        if (!SupabaseClient.IsLoggedIn || !SupabaseClient.IsAnonymous)
        {
            Debug.LogWarning("[Supabase] Apple 연동 실패: 익명 세션이 아닙니다.");
            return;
        }

        var ok = await SupabaseClient.LinkAppleToCurrentAnonymousAsync();
        if (!ok) Debug.LogWarning($"[Supabase] Apple 연동 실패: {ok.ErrorMessage}");
        else     Debug.Log("[Supabase] Apple 연동 성공.");
    }

    /// <summary>K — 현재 계정에서 Google 연동 해제. 마지막 남은 연동이면 실패하며, 성공 시 다음 연동 때 계정 선택창이 다시 뜹니다.</summary>
    private async Task UnlinkGoogleAsync()
    {
        if (!SupabaseClient.IsLoggedIn)
        {
            Debug.LogWarning("[Supabase] Google 연동 해제 실패: 로그인 상태가 아닙니다.");
            return;
        }

        var ok = await SupabaseClient.UnlinkGoogleAsync();
        if (!ok)
        {
            if (ok.ErrorMessage == SupabaseFailReason.CannotUnlinkLastIdentity)
                Debug.LogWarning("[Supabase] Google 연동 해제 실패: 마지막 남은 연동은 해제할 수 없습니다. 다른 연동을 먼저 추가하세요.");
            else
                Debug.LogWarning($"[Supabase] Google 연동 해제 실패: {ok.ErrorMessage}");
        }
        else
        {
            Debug.Log("[Supabase] Google 연동 해제 성공. 다음 Google 연동 때 계정 선택창이 다시 표시됩니다.");
        }
    }

    /// <summary>L — 현재 계정에서 Apple 연동 해제. 마지막 남은 연동이면 실패합니다.</summary>
    private async Task UnlinkAppleAsync()
    {
        if (!SupabaseClient.IsLoggedIn)
        {
            Debug.LogWarning("[Supabase] Apple 연동 해제 실패: 로그인 상태가 아닙니다.");
            return;
        }

        var ok = await SupabaseClient.UnlinkAppleAsync();
        if (!ok)
        {
            if (ok.ErrorMessage == SupabaseFailReason.CannotUnlinkLastIdentity)
                Debug.LogWarning("[Supabase] Apple 연동 해제 실패: 마지막 남은 연동은 해제할 수 없습니다. 다른 연동을 먼저 추가하세요.");
            else
                Debug.LogWarning($"[Supabase] Apple 연동 해제 실패: {ok.ErrorMessage}");
        }
        else
        {
            Debug.Log("[Supabase] Apple 연동 해제 성공.");
        }
    }

    /// <summary>W — 로그아웃. Google 네이티브 로그아웃 포함, 로그아웃 전 자동 flush.</summary>
    private async Task SignOutAsync()
    {
        var ok = await SupabaseClient.SignOutFullyAsync();
        if (!ok) Debug.LogWarning($"[Supabase] 로그아웃 실패: {ok.ErrorMessage}");
        else     Debug.Log("[Supabase] 로그아웃 완료.");
    }

    /// <summary>O — 로컬 저장 세션으로 로그인 복원.</summary>
    private async Task RestoreSessionAsync()
    {
        var ok = await SupabaseClient.RestoreSessionAsync();
        if (!ok) Debug.LogWarning($"[Supabase] 세션 복원 실패: {ok.ErrorMessage}");
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

        var ok = await SupabaseClient.LoadAllUserSavesAsync();
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

        var ok = await SupabaseClient.SaveAllAsync();
        if (!ok) Debug.LogWarning("[Supabase] 데이터 저장 실패.");
        else     Debug.Log("[Supabase] 데이터 저장 완료.");
    }

    /// <summary>F — 레벨을 1 올립니다. MarkDirty 자동 호출 → 쿨타임 후 자동 저장됩니다.</summary>
    private void IncrementLevel()
    {
        SamplePlayerSave.Level += 1;
        Debug.Log($"[Supabase] Level = {SamplePlayerSave.Level} (dirty — 쿨타임 후 자동 저장)");
    }

    /// <summary>
    /// X — 세이브를 삭제합니다(서버 행 DELETE + 로컬 기본값 리셋). 탈퇴가 아니라 세이브만 비웁니다.
    /// 삭제 후 로컬이 기본값인지 확인하고, 재로드 시 기본 행이 재생성되는지 확인합니다.
    /// </summary>
    private async Task DeletePlayerDataAsync()
    {
        if (!SupabaseClient.IsLoggedIn) { Debug.LogWarning("[Supabase] 로그인 필요."); return; }

        var ok = await SamplePlayerSave.DeleteAsync();
        if (!ok) { Debug.LogWarning($"[Supabase] 세이브 삭제 실패: {ok.ErrorMessage}"); return; }
        Debug.Log($"[Supabase] 세이브 삭제 완료. 로컬 기본값 — Level={SamplePlayerSave.Level}, Coins={SamplePlayerSave.Coins}");

        var loaded = await SamplePlayerSave.LoadAsync();
        Debug.Log(loaded
            ? $"[Supabase] 재로드 완료(기본 행 재생성) — Level={SamplePlayerSave.Level}, Coins={SamplePlayerSave.Coins}"
            : "[Supabase] 재로드 실패.");
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

        var available = await SupabaseClient.IsDisplayNameAvailableAsync(nickname);
        if (!available)
        {
            Debug.LogWarning($"[Supabase] 닉네임 '{nickname}' 이미 사용 중 또는 확인 실패: {available.ErrorMessage}");
            return;
        }
        Debug.Log($"[Supabase] 닉네임 '{nickname}' 사용 가능.");

        var setOk = await SupabaseClient.SetMyDisplayNameAsync(nickname);
        if (!setOk)
        {
            Debug.LogWarning($"[Supabase] 닉네임 설정 실패: {setOk.ErrorMessage}");
            return;
        }
        Debug.Log($"[Supabase] 닉네임 설정 완료: {nickname}");

        var myId = SupabaseClient.UserId;
        var profile = await SupabaseClient.GetPublicProfileAsync(myId);
        if (profile) Debug.Log($"[Supabase] 프로필 — 닉네임: {profile.Data.DisplayName}, 서버: {profile.Data.ServerCode}");
        else         Debug.LogWarning("[Supabase] 프로필 조회 실패.");
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
        var serverInfo = (await SupabaseClient.GetMyServerInfoAsync()).Data;
        Debug.Log($"[Supabase] 상태\n" +
                  $"  IsAnonymous = {SupabaseClient.IsAnonymous}\n" +
                  $"  UserId      = {SupabaseClient.UserId}\n" +
                  $"  DisplayName = {profile?.DisplayName ?? "(없음)"}\n" +
                  $"  ServerCode  = {profile?.ServerCode ?? "(없음)"}\n" +
                  $"  IsWithdrawn = {profile?.IsWithdrawn}\n" +
                  $"  ServerId    = {serverInfo.ServerId}, ServerCode = {serverInfo.ServerCode}");
    }

    /// <summary>J — 서버 시간을 조회합니다.</summary>
    private async Task GetServerTimeAsync()
    {
        var time = await SupabaseClient.GetServerUtcNowAsync();
        if (!time) Debug.LogWarning("[Supabase] 서버 시간 조회 실패.");
        else       Debug.Log($"[Supabase] 서버 시간: {time.Data:yyyy-MM-dd HH:mm:ss} UTC");
    }

    /// <summary>G — 현재 계정의 차단 정보를 조회합니다.</summary>
    private async Task GetBanInfoAsync()
    {
        if (!SupabaseClient.IsLoggedIn) { Debug.LogWarning("[Supabase] 로그인 필요."); return; }

        var accountId = SupabaseClient.UserId;
        var banInfo = await SupabaseClient.GetBanInfoAsync(accountId);
        if (!banInfo)
            Debug.LogWarning($"[Supabase] 차단 정보 조회 실패: {banInfo.ErrorMessage}");
        else if (banInfo.Data == null)
            Debug.Log("[Supabase] 차단 정보 없음 (정상 계정).");
        else
            Debug.LogWarning($"[Supabase] 차단 정보 — IsPermanent: {banInfo.Data.IsPermanentBan}, Until: {banInfo.Data.BannedUntil}, Message: {banInfo.Data.BanMessage}");
    }

    // ─── 탈퇴 ────────────────────────────────────────────────────────────────

    /// <summary>D — 탈퇴 신청 (15일 유예). 실제 테스트 시 주의.</summary>
    private async Task RequestWithdrawalAsync()
    {
        if (!SupabaseClient.IsLoggedIn) { Debug.LogWarning("[Supabase] 로그인 필요."); return; }

        var ok = await SupabaseClient.RequestMyWithdrawalAsync();
        if (!ok) Debug.LogWarning($"[Supabase] 탈퇴 신청 실패: {ok.ErrorMessage}");
        else     Debug.Log("[Supabase] 탈퇴 신청 완료. 15일 후 삭제됩니다.");
    }

    /// <summary>S — 탈퇴 예약 상태를 조회합니다.</summary>
    private async Task GetWithdrawalStatusAsync()
    {
        if (!SupabaseClient.IsLoggedIn) { Debug.LogWarning("[Supabase] 로그인 필요."); return; }

        var status = await SupabaseClient.GetMyWithdrawalStatusAsync();
        if (!status)
            Debug.LogWarning($"[Supabase] 탈퇴 상태 조회 실패: {status.ErrorMessage}");
        else if (status.Data == null || !status.Data.IsScheduled)
            Debug.Log("[Supabase] 탈퇴 예약 없음.");
        else
            Debug.Log($"[Supabase] 탈퇴 예약 — IsScheduled: {status.Data.IsScheduled}, 남은 시간: {status.Data.SecondsRemaining}초, 예약일: {status.Data.WithdrawnAtIso}");
    }

    /// <summary>C — 탈퇴 취소. 탈퇴 예약 상태여야 합니다.</summary>
    private async Task ClearWithdrawalAsync()
    {
        if (!SupabaseClient.IsLoggedIn) { Debug.LogWarning("[Supabase] 로그인 필요."); return; }

        var ok = await SupabaseClient.ClearMyWithdrawalAsync();
        if (!ok) Debug.LogWarning($"[Supabase] 탈퇴 취소 실패: {ok.ErrorMessage}");
        else     Debug.Log("[Supabase] 탈퇴 취소 완료.");
    }

    // ─── Update ──────────────────────────────────────────────────────────────

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Q)) _ = SignInAnonymouslyAsync();
        if (Input.GetKeyDown(KeyCode.I)) _ = SignInWithGoogleAsync();
        if (Input.GetKeyDown(KeyCode.P)) _ = LinkGoogleAsync();
        if (Input.GetKeyDown(KeyCode.B)) _ = SignInWithAppleAsync();
        if (Input.GetKeyDown(KeyCode.H)) _ = LinkAppleAsync();
        if (Input.GetKeyDown(KeyCode.K)) _ = UnlinkGoogleAsync();
        if (Input.GetKeyDown(KeyCode.L)) _ = UnlinkAppleAsync();
        if (Input.GetKeyDown(KeyCode.W)) _ = SignOutAsync();
        if (Input.GetKeyDown(KeyCode.O)) _ = RestoreSessionAsync();

        if (Input.GetKeyDown(KeyCode.R)) _ = LoadPlayerDataAsync();
        if (Input.GetKeyDown(KeyCode.V)) _ = SavePlayerDataAsync();
        if (Input.GetKeyDown(KeyCode.F)) IncrementLevel();
        if (Input.GetKeyDown(KeyCode.X)) _ = DeletePlayerDataAsync();

        if (Input.GetKeyDown(KeyCode.T)) _ = TestRemoteConfigReaderAsync();
        if (Input.GetKeyDown(KeyCode.U)) TestRemoteConfigBinding();
        if (Input.GetKeyDown(KeyCode.E)) ToggleRemoteConfigListener();

        if (Input.GetKeyDown(KeyCode.N)) _ = TestPublicProfileAsync();

        if (Input.GetKeyDown(KeyCode.A)) _ = PrintStatusAsync();
        if (Input.GetKeyDown(KeyCode.J)) _ = GetServerTimeAsync();
        if (Input.GetKeyDown(KeyCode.G)) _ = GetBanInfoAsync();

        if (Input.GetKeyDown(KeyCode.D)) _ = RequestWithdrawalAsync();
        if (Input.GetKeyDown(KeyCode.S)) _ = GetWithdrawalStatusAsync();
        if (Input.GetKeyDown(KeyCode.C)) _ = ClearWithdrawalAsync();
    }
}

// =============================================================================
// 예제용 유저 저장 클래스.
// 실제 프로젝트에서는 TrueSoft > Supabase > 유저 데이터 클래스 생성으로 자동 생성하세요.
// =============================================================================

public sealed class SamplePlayerSave : StaticUserSave<SamplePlayerSave.Row>
{
    public static readonly SamplePlayerSave Instance = new();
    private SamplePlayerSave() : base() { }

    // 필드는 internal — 정적 프로퍼티(MarkDirty 포함)로 접근합니다. (private는 중첩 클래스라 바깥에서 접근 불가)
    [Serializable]
    [JsonObject(MemberSerialization.Fields)]
    public sealed class Row
    {
        [DataColumn("level")] internal int level;
        [DataColumn("coins")] internal int coins;
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
