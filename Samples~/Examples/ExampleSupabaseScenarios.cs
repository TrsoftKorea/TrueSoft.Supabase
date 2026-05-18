using System;
using System.Threading.Tasks;
using Truesoft.Supabase.Unity;
using Truesoft.Supabase.Unity.RemoteConfig;
using UnityEngine;
using SupabaseClient = global::Truesoft.Supabase.Unity.Supabase;

/// <summary>
/// Supabase SDK 기능 예제 컴포넌트.
/// SupabaseRuntime이 씬에 있어야 합니다 (같은 GameObject에 붙이거나 별도 배치).
///
/// 키보드 단축키 (Play Mode):
///   Q — 익명 로그인       I — Google 로그인      P — Google 연동       W — 로그아웃
///   R — 데이터 로드       V — 즉시 저장           F — 레벨 +1 (변경 시연)
///   T — RC Reader         U — RC Binding          E — RC Listener 토글
///   Y — Edge Function 호출  N — 닉네임 설정 및 프로필 조회
/// </summary>
public sealed class ExampleSupabaseScenarios : MonoBehaviour
{
    // ─── 초기화 이벤트 ───────────────────────────────────────────────────────

    private void Awake()
    {
        SupabaseClient.OnDuplicateLoginDetected += HandleDuplicateLoginDetected;
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

    /// <summary>Q — 익명 로그인.</summary>
    private async Task SignInAnonymouslyAsync()
    {
        var ok = await SupabaseClient.TrySignInAnonymouslyAsync();
        if (!ok) Debug.LogWarning("[Supabase] 익명 로그인 실패.");
        else     Debug.Log("[Supabase] 익명 로그인 성공.");
    }

    /// <summary>I — Google 로그인 (Android 네이티브).</summary>
    private async Task SignInWithGoogleAsync()
    {
        var ok = await SupabaseClient.TrySignInWithGoogleAsync();
        if (!ok) Debug.LogWarning("[Supabase] Google 로그인 실패.");
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
        if (!ok) Debug.LogWarning("[Supabase] Google 연동 실패 (이미 사용 중인 계정일 수 있음).");
        else     Debug.Log("[Supabase] Google 연동 성공.");
    }

    /// <summary>W — 로그아웃. Google 네이티브 로그아웃 포함, 로그아웃 전 자동 flush.</summary>
    private async Task SignOutAsync()
    {
        await SupabaseClient.TrySignOutFullyAsync();
        Debug.Log("[Supabase] 로그아웃 완료.");
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

        var ok = await SupabaseClient.TryFlushAllUserSaveImmediateAsync();
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

    private const string TestRemoteConfigKey = "test";

    [Serializable]
    private sealed class TestConfig { public string a; public int b; public bool c; }

    private Func<Task<TestConfig>>           _rcReader;
    private RemoteConfigBinding<TestConfig>  _rcBinding;
    private RemoteConfigListener<TestConfig> _rcListener;

    /// <summary>T — RemoteConfig Reader. 캐시 만료 시 서버에서 최신 값을 가져옵니다.</summary>
    private async Task TestRemoteConfigReaderAsync()
    {
        _rcReader ??= SupabaseClient.CreateRemoteConfigReader<TestConfig>(TestRemoteConfigKey);

        var val = await _rcReader();
        if (val != null) Debug.Log($"[RC ①] Reader: a={val.a}, b={val.b}, c={val.c}");
        else             Debug.LogWarning("[RC ①] Reader: null (키 없음 또는 역직렬화 실패)");
    }

    /// <summary>U — RemoteConfig Binding. 60초 폴링, .Value로 즉시 읽기.</summary>
    private void TestRemoteConfigBinding()
    {
        _rcBinding ??= SupabaseClient.CreateRemoteConfigBinding<TestConfig>(
            TestRemoteConfigKey, pollIntervalSeconds: 60f);

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

        _rcListener = SupabaseClient.CreateRemoteConfigListener<TestConfig>(
            TestRemoteConfigKey, pollIntervalSeconds: 60f,
            val => Debug.Log($"[RC ③] Listener 콜백: a={val.a}, b={val.b}, c={val.c}"));
        Debug.Log("[RC ③] Listener 시작.");
    }

    // ─── Edge Function ────────────────────────────────────────────────────────

    /// <summary>
    /// Y — Edge Function 호출 예시.
    /// "my-function"을 실제 배포된 함수 이름으로, 요청 본문을 함수에 맞게 변경하세요.
    /// Edge Function이 배포되어 있어야 합니다 (가이드: edge-functions.md).
    /// </summary>
    private async Task InvokeEdgeFunctionAsync()
    {
        if (!SupabaseClient.IsLoggedIn) { Debug.LogWarning("[Supabase] 로그인 필요."); return; }

        // ↓ 함수 이름과 요청 본문을 프로젝트에 맞게 변경하세요.
        var result = await SupabaseClient.TryInvokeFunctionAsync<object>(
            "my-function", new { });

        if (result != null) Debug.Log($"[Supabase] Edge Function 결과: {result}");
        else                Debug.LogWarning("[Supabase] Edge Function 호출 실패 또는 null 응답.");
    }

    // ─── 공개 프로필 ──────────────────────────────────────────────────────────

    /// <summary>
    /// N — 닉네임 설정 후 내 프로필을 조회합니다.
    /// displayname-set / displayname-get Edge Function이 배포되어 있어야 합니다.
    /// </summary>
    private async Task TestPublicProfileAsync()
    {
        if (!SupabaseClient.IsLoggedIn) { Debug.LogWarning("[Supabase] 로그인 필요."); return; }

        var setOk = await SupabaseClient.TrySetMyDisplayNameAsync("TestPlayer");
        if (!setOk)
        {
            Debug.LogWarning("[Supabase] 닉네임 설정 실패 (이미 사용 중이거나 Edge Function 미배포).");
            return;
        }
        Debug.Log("[Supabase] 닉네임 설정 완료: TestPlayer");

        var myId = SupabaseClient.Session?.User?.Id;
        var profile = await SupabaseClient.TryGetPublicProfileAsync(myId);
        if (profile != null) Debug.Log($"[Supabase] 프로필 조회 완료 — 닉네임: {profile.DisplayName}");
        else                 Debug.LogWarning("[Supabase] 프로필 조회 실패.");
    }

    // ─── Update ──────────────────────────────────────────────────────────────

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Q)) _ = SignInAnonymouslyAsync();
        if (Input.GetKeyDown(KeyCode.I)) _ = SignInWithGoogleAsync();
        if (Input.GetKeyDown(KeyCode.P)) _ = LinkGoogleAsync();
        if (Input.GetKeyDown(KeyCode.W)) _ = SignOutAsync();

        if (Input.GetKeyDown(KeyCode.R)) _ = LoadPlayerDataAsync();
        if (Input.GetKeyDown(KeyCode.V)) _ = SavePlayerDataAsync();
        if (Input.GetKeyDown(KeyCode.F)) IncrementLevel();

        if (Input.GetKeyDown(KeyCode.T)) _ = TestRemoteConfigReaderAsync();
        if (Input.GetKeyDown(KeyCode.U)) TestRemoteConfigBinding();
        if (Input.GetKeyDown(KeyCode.E)) ToggleRemoteConfigListener();

        if (Input.GetKeyDown(KeyCode.Y)) _ = InvokeEdgeFunctionAsync();
        if (Input.GetKeyDown(KeyCode.N)) _ = TestPublicProfileAsync();
    }
}
