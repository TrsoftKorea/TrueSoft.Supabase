using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using TrueBase.Core.Common;
using TrueBase.Core.Data;
using TrueBase.Core.Models;
using UnityEngine;

namespace TrueBase.Unity
{
    /// <summary>
    /// 유저 데이터 저장/불러오기 싱글턴 베이스 클래스.
    /// <para>
    /// 사용법:
    /// <code>
    /// public sealed class GameSave : StaticUserSave&lt;GameSave.Row&gt;
    /// {
    ///     public static readonly GameSave Instance = new();
    ///     private GameSave() : base() { }
    ///
    ///     [Serializable]
    ///     public sealed class Row
    ///     {
    ///         [DataColumn("level")] public int level;
    ///     }
    ///
    ///     // static 프로퍼티로 선언하면 GameSave.Level = 5; 처럼 간결하게 사용 가능
    ///     public static int Level
    ///     {
    ///         get => Instance.Current.level;
    ///         set { if (Instance.Current.level == value) return; Instance.Current.level = value; Instance.MarkDirty(); }
    ///     }
    /// }
    /// </code>
    /// </para>
    /// <para>syncKey는 등록된 세이브 간 고유해야 합니다. 기본값은 <c>typeof(TRow).FullName</c>입니다.</para>
    /// <para>
    /// <b>단일 서브클래스 정책</b>: 이 클래스를 상속하는 클래스는 프로젝트 전체에서 정확히 하나여야 합니다.
    /// 두 번째 서브클래스의 인스턴스가 생성되면 <see cref="InvalidOperationException"/>이 발생합니다.
    /// 모든 게임 데이터는 하나의 <c>Row</c> 클래스 안에 <c>[DataColumn]</c> 필드로 선언하세요.
    /// </para>
    /// </summary>
    public abstract class StaticUserSave<TRow> : INanooSaveSyncable, IUserSaveOperations where TRow : class, new()
    {
        private static class SingletonGuard
        {
            private static Type _registeredType;

            public static void Assert(Type newType)
            {
                if (_registeredType == null)
                {
                    _registeredType = newType;
                    return;
                }

                if (_registeredType == newType)
                    return;

                throw new InvalidOperationException(
                    $"[StaticUserSave] 서브클래스는 프로젝트 전체에서 정확히 하나만 허용됩니다.\n" +
                    $"이미 등록됨: {_registeredType.FullName}\n" +
                    $"중복 등록 시도: {newType.FullName}\n" +
                    $"모든 게임 데이터를 하나의 Row 클래스 안에 [DataColumn] 필드로 선언하세요.");
            }
        }

        private static StaticUserSave<TRow> _sharedInstance;

        private TRow _nanooLastLoaded;  // NanooLoadWithStateAsync 캐시 (PlayNanoo 동기화용)

        protected readonly TRow   Current;
        private            TRow   _lastSynced;
        private            bool   _isDirty;
        private            bool   _isRegistered;
        private readonly   bool   _hasReferenceColumns;  // 컬렉션·클래스 컬럼 보유 여부(값 비교 감지용)
        private            float  _lastDeepCheckTime = float.MinValue;  // 값 비교 throttle 타임스탬프
        private            bool   _lastDeepResult;                      // 캐시된 값 비교 결과
        private            bool   _hasLoadedOnce;                       // 최초 로드 완료 여부(로드 전 auto-flush 차단용)
        private            TRow   _loadFallback;                        // 로드 전에 세팅한 초기값 스냅샷(1회 캡처)
        private            bool   _fallbackCaptured;                    // _loadFallback 캡처 여부
        protected readonly string _syncKey;
        protected readonly string LogTag;

        /// <summary>syncKey를 <c>typeof(TRow).FullName</c>으로 자동 설정합니다.</summary>
        protected StaticUserSave() : this(typeof(TRow).FullName) { }

        /// <summary>syncKey를 직접 지정합니다. 여러 인스턴스가 같은 TRow를 공유하는 경우 사용합니다.</summary>
        protected StaticUserSave(string syncKey)
        {
            SingletonGuard.Assert(GetType());
            _sharedInstance = this;
            SupabaseSDK._nanooSaveBridge = this;
            SupabaseSDK._userSave = this;   // Supabase 파사드가 Row 타입 없이 위임할 수 있도록 등록

            if (string.IsNullOrWhiteSpace(syncKey))
                throw new ArgumentException("syncKey must not be empty.", nameof(syncKey));

            _syncKey    = syncKey.Trim();
            LogTag      = $"[StaticUserSave<{typeof(TRow).Name}>]";
            Current     = new TRow();
            _lastSynced = new TRow();
            // 로드 전에도 [AutoDefault]가 주입돼 있도록 생성 즉시 기본값 적용
            // (안 하면 로드 전 AutoDict 클래스 값 접근이 null을 반환해 NRE).
            DataSchema.ApplyAutoDefaults(Current);
            DataSchema.ApplyAutoDefaults(_lastSynced);
            _hasReferenceColumns = DataSchema.HasReferenceColumns<TRow>();

            EnsureRegistered();
        }

        private void EnsureRegistered()
        {
            if (_isRegistered) return;
            SupabaseSDK.RegisterUserSaveStaticSync(
                _syncKey, HasDirty, FlushDirtyAsync, ResetLocalState,
                GetDirtyCooldown, HasFreshDirty);
            _isRegistered = true;
        }

        // 우선순위 추적 — GetHighestDirtyPriority(전체 행 diff)를 매 set마다 부르는 렉의 근본 원인 제거:
        //  · 스칼라 필드는 setter가 MarkDirty(우선순위)로 넘겨 가장 높은(작은 int) 우선순위를 O(1)로 누적.
        //  · 참조 컬럼(컬렉션·클래스)의 제자리 수정은 setter가 없어, 참조 컬럼만 throttle된 diff로 판정
        //    (참조 컬럼이 있는 Row에서만, 0.25초 간격).
        private const int   PriNone = int.MaxValue;    // dirty 스칼라 없음 표식
        private int   _scalarDirtyPri  = PriNone;      // 가장 높은(작은) 스칼라 우선순위
        private int   _refDirtyPri      = PriNone;     // 참조 컬럼 diff로 얻은 우선순위(캐시)
        private float _refPriCheckedAt  = float.MinValue;
        private const float RefPriorityRecomputeInterval = 0.25f;

        /// <summary>
        /// dirty 필드 중 가장 높은 우선순위(Urgent에 가까운)의 쿨다운(초)을 반환합니다.
        /// 스칼라는 O(1) 증분 추적, 참조 컬럼만 throttle된 diff로 판정합니다.
        /// </summary>
        private float GetDirtyCooldown()
        {
            var p = _scalarDirtyPri;

            if (_hasReferenceColumns)
            {
                var now = Time.realtimeSinceStartup;
                if (now - _refPriCheckedAt >= RefPriorityRecomputeInterval)
                {
                    _refPriCheckedAt = now;
                    var refP = DataSchema.GetHighestDirtyPriority(_lastSynced, Current, referenceOnly: true);
                    _refDirtyPri = refP.HasValue ? (int)refP.Value : PriNone;
                }
                if (_refDirtyPri < p) p = _refDirtyPri;
            }

            if (p == PriNone) p = 1; // 아무 것도 dirty가 아니면 Normal(1)
            return UserSaveStaticSyncRegistry.GetPriorityCooldown(p);
        }

        // 스칼라는 setter의 MarkDirty(_isDirty)로 즉시 잡습니다. 컬렉션·클래스는 직접 수정 시
        // 플래그가 안 걸리므로 마지막 동기화본과 값 비교(HasChanges)로 변경을 감지합니다.
        // → 리스트를 일반 리스트처럼 자유롭게 수정해도 자동 저장됩니다.
        //
        // Tick은 매 프레임 호출되므로 비싼 값 비교는 throttle합니다. 어차피 전송은 쿨타임까지
        // 대기하므로 그보다 자주 검사할 이유가 없어, 검사 주기를 쿨타임에 맞춰 idle GC를 최소화합니다.
        // 즉시 저장(앱 종료 등)은 FlushDirtyAsync가 throttle을 무시하고 신선하게 검사하므로 정확성은 보장됩니다.
        private bool HasDirty()
        {
            // 최초 로드 완료 전에는 저장하지 않는다. 로드 전에 지정한 초기값(fallback)이
            // 서버 데이터를 확인하기도 전에 auto-flush로 조기 저장되는 것을 막는다.
            if (!_hasLoadedOnce) return false;
            if (_isDirty) return true;
            if (!_hasReferenceColumns) return false;

            var interval = Mathf.Max(1f, UserSaveStaticSyncRegistry.GetPriorityCooldown(1)); // Normal 쿨타임
            var now = Time.realtimeSinceStartup;
            if (now - _lastDeepCheckTime >= interval)
            {
                _lastDeepCheckTime = now;
                _lastDeepResult = DataSchema.HasChanges(_lastSynced, Current);
            }
            return _lastDeepResult;
        }

        private async Task<bool> FlushDirtyAsync()
        {
            // flush 시점에는 throttle을 무시하고 신선하게 검사 — 즉시 저장(앱 종료 등)에서
            // 컬렉션 제자리 수정이 throttle 캐시 때문에 누락되지 않도록 합니다.
            _lastDeepCheckTime = float.MinValue;
            if (!HasDirty()) return true;

            // await 이전에 현재 값을 스냅샷하고 dirty를 먼저 해제합니다.
            // 이렇게 하면 네트워크 대기 중 게임 코드가 새 값을 쓰고 MarkDirty()를 호출해도
            // dirty 플래그가 올바르게 유지됩니다.
            var snapshot = DataSchema.CloneRow(Current);
            DataSchema.ApplyAutoDefaults(snapshot); // 클론은 [AutoDefault]가 다시 안 걸려 있음 — diff·다음 _lastSynced 기준으로 쓰이기 전에 재주입
            _isDirty = false;
            _scalarDirtyPri = PriNone; // 스냅샷 이후의 스칼라 변경은 await 중 MarkDirty가 다시 채운다
            _refPriCheckedAt = float.MinValue; // 참조 우선순위도 새 스냅샷 기준으로 재평가

            var ok = await SupabaseSDK.TryPatchUserDataDiffAsync(
                _lastSynced, snapshot,
                ensureRowFirst: true, setUpdatedAtIsoUtc: true);

            if (!ok)
            {
                // 전송 실패 시 dirty 복원 — 다음 flush 때 재전송
                _isDirty = true;
                return false;
            }

            _lastSynced = snapshot;
            // 방금 동기화했으므로 값 비교·참조 우선순위 캐시를 초기화(새 _lastSynced 기준으로 재평가)
            _lastDeepResult = false;
            _lastDeepCheckTime = float.MinValue;
            _refDirtyPri = PriNone;
            _refPriCheckedAt = float.MinValue;
            return true;
        }

        private void ResetLocalState()
        {
            DataSchema.CopyInto(Current, new TRow());
            DataSchema.ApplyAutoDefaults(Current);
            _lastSynced = new TRow();
            DataSchema.ApplyAutoDefaults(_lastSynced); // 비교 양쪽이 같은 기본값 기준을 갖도록
            _isDirty       = false;
            _hasLoadedOnce = false;  // 재로그인 시 다시 로드 완료 전까지 auto-flush 차단
            // _loadFallback/_fallbackCaptured는 유지 — fallback은 앱 수명 설정이라
            // 로그아웃 후 재로그인에도 동일 적용(Current가 리셋돼도 스냅샷으로 재적용).
        }

        /// <summary>
        /// 이 TRow 타입에 등록된 유일한 StaticUserSave 인스턴스를 반환합니다.
        /// <para>PlayNanooRuntime 등 외부에서 세이브 인스턴스를 참조할 때 사용합니다.</para>
        /// </summary>
        public static StaticUserSave<TRow> SharedInstance => _sharedInstance;

        /// <summary>현재 로컬 세이브 Row를 반환합니다.</summary>
        public TRow CurrentRow => Current;

        // INanooSaveSyncable 구현 (PlayNanooRuntime 전용)

        async Task<(bool success, bool hasRow, DateTime updatedAt)> INanooSaveSyncable.NanooLoadWithStateAsync()
        {
            var (success, hasRow, row) = await SupabaseSDK.TryLoadUserDataAttributedWithRowStateAsync<TRow>(defaultWhenFailed: null);
            if (!success) return (false, false, DateTime.MinValue);
            _nanooLastLoaded = row;
            if (!hasRow || row == null)
            {
                return (true, false, DateTime.MinValue);
            }
            var val = typeof(TRow)
                .GetField("updated_at", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(row)?.ToString();
            var updatedAt = DateTime.TryParse(val, out var t) ? t : DateTime.MinValue;
            return (true, true, updatedAt);
        }

        async Task<bool> INanooSaveSyncable.NanooPatchFromEmptyAsync(string nanooJson)
        {
            TRow nanooRow;
            try { nanooRow = NanooDeserializeJson(nanooJson); }
            catch (Exception e)
            {
                Debug.LogError($"{LogTag} 플레이나누 JSON 변환 실패 (NanooPatchFromEmptyAsync): {e.Message}");
                return false;
            }
            var ok = await SupabaseSDK.TryPatchUserDataDiffAsync(new TRow(), nanooRow);
            if (ok) await ReloadFromServerAsync();
            return ok;
        }

        async Task<bool> INanooSaveSyncable.NanooPatchFromLastLoadedAsync(string nanooJson)
        {
            TRow nanooRow;
            try { nanooRow = NanooDeserializeJson(nanooJson); }
            catch (Exception e)
            {
                Debug.LogError($"{LogTag} 플레이나누 JSON 변환 실패 (NanooPatchFromLastLoadedAsync): {e.Message}");
                return false;
            }
            var prev = _nanooLastLoaded ?? new TRow();
            var ok = await SupabaseSDK.TryPatchUserDataDiffAsync(prev, nanooRow);
            if (ok) await ReloadFromServerAsync();
            return ok;
        }

        /// <summary>
        /// PlayNANOO 데이터를 서버에 반영한 뒤, 서버 정본을 다시 읽어 로컬에 적용합니다.
        /// <para>
        /// PlayNANOO JSON을 그대로 <see cref="ApplyRow"/>하면 그 JSON에 없는 컬럼이 C# 타입 기본값(0·false·null)으로
        /// 덮여, DB 컬럼 DEFAULT로 채워진 값이 로컬에서 사라집니다. PATCH 이후의 서버 행이 정본이므로 그것을 읽어 적용합니다.
        /// </para>
        /// </summary>
        private async Task ReloadFromServerAsync()
        {
            var (success, hasRow, row) = await SupabaseSDK.TryLoadUserDataAttributedWithRowStateAsync<TRow>(
                defaultWhenFailed: null);

            if (!success || !hasRow || row == null)
            {
                Debug.LogWarning($"{LogTag} 플레이나누 반영 후 재조회에 실패했습니다. 다음 로드에서 다시 맞춰집니다.");
                return;
            }

            _nanooLastLoaded = row;
            ApplyRow(row);
            _hasLoadedOnce = true;   // 로컬이 서버 정본과 일치 — 이 시점부터 자동 저장 허용
        }

        // 플레이나누 필드 변환 (선택적 편의)
        private NanooFieldMap<TRow> _nanooMap;
        private bool _nanooMapBuilt;
        // 등록값은 static — 인스턴스 없이 PlayerSave.UseNanooConverters(...)로 호출 가능. (닫힌 제네릭 타입당 1개)
        private static Action<NanooFieldMap<TRow>> s_nanooConfigure;

        /// <summary>
        /// 플레이나누에서 특정 필드만 다른 형태로 저장/복원할 때, 그 필드 변환을 <b>코드에서</b> 등록합니다(상속·partial 불필요).
        /// 부트스트랩 등 <b>첫 로그인/동기화 전에</b> 한 번 호출하세요. 등록 안 한 필드는 자동 변환됩니다.
        /// <para>예: <c>PlayerSave.UseNanooConverters(map =&gt; map.Field(r =&gt; r.itemIds, toString, fromString))</c></para>
        /// </summary>
        public static void UseNanooConverters(Action<NanooFieldMap<TRow>> configure)
            => s_nanooConfigure = configure;

        private NanooFieldMap<TRow> GetNanooMap()
        {
            if (!_nanooMapBuilt)
            {
                var m = new NanooFieldMap<TRow>();
                s_nanooConfigure?.Invoke(m);  // UseNanooConverters로 등록한 변환(있으면)
                _nanooMap = m.IsEmpty ? null : m;
                _nanooMapBuilt = true;
            }
            return _nanooMap;
        }

        /// <summary>
        /// 플레이나누 Storage JSON을 Row로 역직렬화합니다.
        /// 등록 변환(<see cref="UseNanooConverters"/>)이 있으면 적용하고, 없으면 Newtonsoft.Json을 사용합니다.
        /// </summary>
        private TRow NanooDeserializeJson(string json)
        {
            var map = GetNanooMap();
            return map != null ? map.Deserialize(json) : Newtonsoft.Json.JsonConvert.DeserializeObject<TRow>(json);
        }

        /// <summary>
        /// Row를 플레이나누 Storage JSON으로 직렬화합니다.
        /// 등록 변환(<see cref="UseNanooConverters"/>)이 있으면 적용하고, 없으면 Newtonsoft.Json을 사용합니다.
        /// </summary>
        private string NanooSerializeJson(TRow row)
        {
            var map = GetNanooMap();
            return map != null ? map.Serialize(row) : Newtonsoft.Json.JsonConvert.SerializeObject(row);
        }

        /// <summary>
        /// PlayNANOO 스토리지에 기록할 "초기값" JSON을 만듭니다. 세이브 삭제 시 사용합니다.
        /// <para>
        /// <c>updated_at</c>을 반드시 채웁니다. 이 키가 없으면 동기화가 그 데이터를 "이관 전 순수 PlayNANOO 데이터"로 보고
        /// PlayNANOO를 영구히 우선시켜, 이후 쌓이는 진행도를 계속 초기값으로 덮어씁니다.
        /// </para>
        /// </summary>
        private string BuildDefaultsJsonForNanoo()
        {
            var row = new TRow();
            DataSchema.ApplyAutoDefaults(row);   // [AutoDefault] 컬렉션 초기값까지 반영
            var json = NanooSerializeJson(row);

            try
            {
                var jo = Newtonsoft.Json.Linq.JObject.Parse(json);
                jo["updated_at"] = DateTime.UtcNow.ToString("o");
                return jo.ToString(Newtonsoft.Json.Formatting.None);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{LogTag} 초기값 JSON에 updated_at을 넣지 못했습니다 — {e.Message}");
                return json;
            }
        }

        void INanooSaveSyncable.NanooApplyLastLoaded()
        {
            if (_nanooLastLoaded == null) return;
            ApplyRow(_nanooLastLoaded);
            _hasLoadedOnce = true;   // 서버에서 읽은 행을 그대로 적용 — 이 시점부터 자동 저장 허용
        }

        string INanooSaveSyncable.NanooGetLastLoadedJson()
            => _nanooLastLoaded != null ? NanooSerializeJson(_nanooLastLoaded) : null;

        async Task<bool> INanooSaveSyncable.TryLoadAsync() => (await LoadAsync()).IsSuccess;

        string INanooSaveSyncable.NanooCurrentJson => NanooSerializeJson(Current);


        /// <summary>
        /// 저장 쿨다운을 전역으로 설정합니다.
        /// <para>
        /// <paramref name="priority"/>가 <c>null</c>이면 모든 우선순위를 동일한 <paramref name="seconds"/>로 설정합니다.<br/>
        /// <paramref name="priority"/>를 지정하면 해당 우선순위의 쿨다운만 변경합니다.
        /// </para>
        /// </summary>
        /// <remarks>
        /// 유저 세이브 클래스는 프로젝트 전체에서 하나만 존재하므로 이 설정은 전역에 적용됩니다.
        /// 일반적으로는 <see cref="SupabaseSettings"/> Inspector에서 저장 주기를 지정하는 것을 권장합니다.
        /// </remarks>
        public void ConfigureCooldown(float seconds, DataSavePriority? priority = null)
        {
            var s = Mathf.Max(0f, seconds);
            if (priority == null)
            {
                // 모든 우선순위를 동일하게 설정
                SupabaseSDK.ConfigureUserSavePriorityCooldowns(s, s, s);
            }
            else
            {
                // 지정한 우선순위만 변경, 나머지는 현재 값 유지
                var urgent = priority.Value == DataSavePriority.Urgent ? s : UserSaveStaticSyncRegistry.GetPriorityCooldown(0);
                var normal = priority.Value == DataSavePriority.Normal ? s : UserSaveStaticSyncRegistry.GetPriorityCooldown(1);
                var lazy   = priority.Value == DataSavePriority.Lazy   ? s : UserSaveStaticSyncRegistry.GetPriorityCooldown(2);
                SupabaseSDK.ConfigureUserSavePriorityCooldowns(urgent, normal, lazy);
            }
        }

        /// <summary>
        /// throttle 캐시를 무시하고 지금 시점의 변경 여부를 신선하게 검사합니다.
        /// 즉시 저장 경로가 "보낼 것이 있는지"를 판정할 때 사용합니다.
        /// </summary>
        private bool HasFreshDirty()
        {
            _lastDeepCheckTime = float.MinValue;
            return HasDirty();
        }

        /// <summary>
        /// 쿨다운을 무시하고 즉시 전송을 요청합니다. 전송 완료를 기다리지 않습니다(fire-and-forget).
        /// <para>반복 호출해도 안전합니다. 이미 전송 중이면 완료 후 1회 재전송이 예약되고 성공을 반환하므로
        /// 중복 전송이 쌓이지 않습니다. 다만 호출할 때마다 값 비교를 새로 하므로 매 프레임 호출은 피하세요.</para>
        /// <para>보낼 변경분이 없으면 요청하지 않고 <see cref="SupabaseReason.UserSaveNoChanges"/> 사유의 실패를 반환합니다. 오류가 아니라 "보낼 것이 없었다"는 뜻입니다.</para>
        /// </summary>
        public SupabaseResult RequestSave()
        {
            EnsureRegistered();

            if (!HasFreshDirty())
                return SupabaseResult.Fail(SupabaseErrorCode.UserSaveNoChanges);

            return SupabaseSDK.RequestImmediateUserSaveStaticFlush(_syncKey)
                ? SupabaseResult.Ok
                : SupabaseResult.Fail(SupabaseErrorCode.UserSaveNotReady);
        }

        /// <summary>
        /// 쿨다운을 무시하고 즉시 전송한 뒤 완료까지 대기합니다. 앱 종료 등 중요한 시점에 사용하세요.
        /// <para>보낼 변경분이 없으면 네트워크 요청 없이 <see cref="SupabaseReason.UserSaveNoChanges"/> 사유의 실패를 반환합니다. 오류가 아니라 "보낼 것이 없었다"는 뜻입니다.</para>
        /// </summary>
        /// <param name="timeoutMs">전송 완료를 기다리는 최대 시간(밀리초). 초과 시 <see cref="SupabaseReason.UserSaveTimeout"/>.</param>
        public async Task<SupabaseResult> SaveNowAsync(int timeoutMs = 5000)
        {
            EnsureRegistered();

            if (!HasFreshDirty())
                return SupabaseResult.Fail(SupabaseErrorCode.UserSaveNoChanges);

            return await SupabaseSDK.TryFlushUserSaveImmediateAsync(_syncKey, timeoutMs)
                ? SupabaseResult.Ok
                : SupabaseResult.Fail(SupabaseErrorCode.UserSaveTimeout);
        }

        /// <summary>
        /// 로드한 Row를 Current와 _lastSynced에 적용하는 공유 헬퍼입니다.
        /// <see cref="LoadAsync"/>(서버 조회)와 PlayNANOO 이관 인터셉터(DB 재조회 없는 주입)가 함께 사용합니다.
        /// <para>
        /// 참조 타입 컬럼이 <paramref name="row"/>에서 null(SQL NULL)이고 로드 전 초기값(<c>_loadFallback</c>)이
        /// 캡처돼 있으면 그 값을 유지합니다. fallback이 없으면 <see cref="DataSchema.CopyInto{T}"/>와 동일하게 동작합니다.
        /// </para>
        /// </summary>
        private void ApplyRow(TRow row)
        {
            DataSchema.MergeServerOverFallback(Current, row, _loadFallback);
            DataSchema.ApplyAutoDefaults(Current);   // AutoList/AutoDict [AutoDefault] 주입 (병합이 새 인스턴스를 만들므로 매번 필요)
            _lastSynced = DataSchema.CloneRow(row);
            DataSchema.ApplyAutoDefaults(_lastSynced); // 비교 양쪽이 같은 기본값 기준을 갖도록
            _isDirty    = false;
            _scalarDirtyPri = PriNone;
            _refDirtyPri = PriNone;
            _refPriCheckedAt = float.MinValue;
            OnCurrentApplied();
        }

        /// <summary>
        /// Current에 Row가 적용될 때마다(로드·PlayNANOO 주입) 호출되는 확장점입니다.
        /// 생성기가 컬럼 기본값 시딩(<c>SeedDefault_*</c>)을 배선하는 데 사용합니다. 게임 코드는 재정의할 필요가 없습니다.
        /// </summary>
        protected virtual void OnCurrentApplied() { }

        /// <summary>
        /// DB에 본인 행이 존재하도록 보장합니다. 행이 없으면 DB 기본값으로 생성합니다(로컬 데이터는 변경하지 않음).
        /// </summary>
        public async Task<SupabaseResult> EnsureRowAsync()
        {
            EnsureRegistered();
            var r = await SupabaseSDK.EnsureMyRowAsync<TRow>();
            return r != null && r.IsSuccess
                ? SupabaseResult.Ok
                : SupabaseResult.Fail(r?.ErrorCode ?? SupabaseErrorCode.UserSaveLoadFailed);
        }

        /// <summary>
        /// DB에서 세이브를 로드해 <see cref="CurrentRow"/>에 적용합니다. 행이 없으면 생성 후 재로드합니다.
        /// 반환값(<see cref="SupabaseLoadResult"/>)의 <see cref="SupabaseLoadResult.IsNewUser"/>로 신규 유저 여부를 확인하고,
        /// 반환 시점에 적용이 끝나 있으므로 로드 후 처리는 <c>await</c> 다음에 이어서 작성합니다.
        /// </summary>
        /// <param name="includeUpdatedAt">true면 select에 <c>updated_at</c> 컬럼을 포함합니다.</param>
        public async Task<SupabaseLoadResult> LoadAsync(bool includeUpdatedAt = true)
        {
            EnsureRegistered();

            // 서버 조회 전에, 로드 전 개발자가 지정한 초기값(fallback)을 1회 스냅샷해 얼려둔다.
            // 이후 로드는 이 스냅샷 기준으로 병합하므로, 재로그인 시 이전 플레이 데이터가 부활하지 않는다.

            if (!_fallbackCaptured)
            {
                _loadFallback = DataSchema.CloneRow(Current);
                _fallbackCaptured = true;
            }

            var (success, hasRow, row) = await SupabaseSDK.TryLoadUserDataAttributedWithRowStateAsync<TRow>(
                defaultWhenFailed: null, includeUpdatedAt: includeUpdatedAt);

            if (!success) return SupabaseLoadResult.Fail(SupabaseErrorCode.UserSaveLoadFailed);


            // DB에 본인 행이 없던 신규 유저 여부. 반환값 IsNewUser로 노출한다.
            var isNewUser = !hasRow;

            if (!hasRow)
            {
                var ensured = await SupabaseSDK.EnsureMyRowAsync<TRow>();
                if (ensured == null || !ensured.IsSuccess)
                {
                    Debug.LogWarning($"{LogTag} TryLoadAsync: EnsureMyRowAsync 실패 — {ensured?.ErrorCode ?? "null"}");
                    return SupabaseLoadResult.Fail(ensured?.ErrorCode ?? SupabaseErrorCode.UserSaveLoadFailed);
                }

                bool hasRow2;
                (success, hasRow2, row) = await SupabaseSDK.TryLoadUserDataAttributedWithRowStateAsync<TRow>(
                    defaultWhenFailed: null, includeUpdatedAt: includeUpdatedAt);
                if (!success) return SupabaseLoadResult.Fail(SupabaseErrorCode.UserSaveLoadFailed);

                if (!hasRow2)
                {
                    Debug.LogWarning($"{LogTag} TryLoadAsync: 행 생성 후 재로드에서도 행을 찾을 수 없음.");
                    return SupabaseLoadResult.Fail(SupabaseErrorCode.UserSaveLoadFailed);
                }
            }

            ApplyRow(row);

            // 서버에 없던(SQL NULL) 컬렉션 컬럼에 로드 전 초기값(fallback)이 유지됐다면,
            // _lastSynced는 서버 정본 기준이므로 SaveIfChangedAsync가 그 변경분만 PATCH한다.
            // 변경분이 없으면(일반 로드) patch가 비어 HTTP를 보내지 않는다(no-op).
            // 변경분이 없으면(일반 로드) UserSaveNoChanges로 돌아오며, 이는 오류가 아니라 정상 경로다.
            var initSave = await SaveIfChangedAsync();
            if (!initSave.IsSuccess && initSave.Reason != SupabaseReason.UserSaveNoChanges)
                Debug.LogWarning($"{LogTag} 로드 후 초기값 저장 실패 — {initSave.ErrorCode ?? "null"}. 다음 저장에서 재시도됩니다.");

            _hasLoadedOnce = true;
            return SupabaseLoadResult.Success(isNewUser);
        }

        /// <summary>
        /// 본인 세이브를 삭제합니다(서버 행 DELETE + 로컬 상태를 기본값으로 리셋).
        /// 다음 <see cref="LoadAsync"/> 시 <c>ts_ensure_my_row</c>가 기본 행을 재생성하므로 실질적으로 "기본값 리셋"입니다.
        /// 계정 탈퇴가 아니라 세이브 데이터만 비웁니다.
        /// <para>먼저 로컬을 기본값으로 리셋한 뒤 서버를 삭제하므로, 삭제 도중 자동 저장이 옛 데이터를 되쓰지 않습니다.
        /// 저장이 활발한 순간(전투 중 등)보다 설정 화면 등 조용한 시점에 호출하세요.
        /// 실패 시 로컬은 기본값이지만 서버 데이터는 유지되므로 <see cref="LoadAsync"/>로 재동기화하세요.</para>
        /// <para>로드 전 초기값(fallback) 스냅샷도 함께 버립니다. 삭제 후 다시 초기값을 넣고 싶으면
        /// <see cref="LoadAsync"/> 전에 새로 세팅하세요.</para>
        /// </summary>
        public async Task<SupabaseResult> DeleteAsync()
        {
            EnsureRegistered();

            // 로컬을 먼저 리셋 — 삭제 await 도중 자동 저장이 돌아도 diff가 비어 PATCH가 안 나감(옛 데이터 되쓰기 방지).
            ResetLocalState();

            // 로드 전 초기값 스냅샷도 함께 버린다.
            // 남겨두면 다음 LoadAsync에서 재생성된 기본 행(컬렉션 컬럼은 SQL NULL) 위에
            // 옛 초기값이 그대로 병합돼, 삭제했는데 기본값이 아닌 값으로 되살아난다.
            // 로그아웃(ResetLocalState)과 달리 삭제는 "기본값으로 되돌리기"이므로 여기서만 비운다.
            _loadFallback     = null;
            _fallbackCaptured = false;

            var r = await SupabaseSDK.DeleteUserDataAsync<TRow>();
            if (r == null || !r.IsSuccess)
                return SupabaseResult.Fail(r?.ErrorCode ?? SupabaseErrorCode.UserSaveDeleteFailed);

            // PlayNANOO 병행 운영 중이면 그쪽 스토리지도 초기값으로 되돌린다.
            // 안 되돌리면 다음 로그인의 동기화가 옛 데이터를 그대로 다시 밀어 넣어 삭제가 무효가 된다.
            var resetNanoo = SupabaseSDK._nanooResetStorage;
            if (resetNanoo != null)
            {
                try { await resetNanoo(BuildDefaultsJsonForNanoo()); }
                catch (Exception e)
                {
                    Debug.LogWarning($"{LogTag} 플레이나누 스토리지 초기화 실패 — {e.Message}. "
                                   + "다음 로그인에서 옛 데이터가 복원될 수 있습니다.");
                }
            }

            return SupabaseResult.Ok;
        }

        /// <summary>
        /// 마지막 동기화 이후 변경된 필드만 즉시 PATCH합니다.
        /// <para>변경이 없으면 네트워크 요청 없이 <see cref="SupabaseReason.UserSaveNoChanges"/> 사유의 실패를 반환합니다. 오류가 아니라 "보낼 것이 없었다"는 뜻입니다.</para>
        /// </summary>
        public async Task<SupabaseResult> SaveIfChangedAsync()
        {
            EnsureRegistered();

            string tableName;
            Dictionary<string, object> patch;
            try
            {
                tableName = DataSchema.ResolveTableName<TRow>();
                patch     = DataSchema.BuildPatch(_lastSynced, Current);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{LogTag} 전송 형식 변환 실패 — {e.Message}");
                return SupabaseResult.Fail(SupabaseErrorCode.UserSaveSerializeFailed);
            }

            if (patch == null || patch.Count == 0)
            {
                _lastSynced = DataSchema.CloneRow(Current);
                DataSchema.ApplyAutoDefaults(_lastSynced); // 비교 양쪽이 같은 기본값 기준을 갖도록
                _isDirty    = false;
                return SupabaseResult.Fail(SupabaseErrorCode.UserSaveNoChanges);
            }

            var result = await SupabaseSDK.PatchUserDataAsync(
                tableName, patch, ensureRowFirst: true, setUpdatedAtIsoUtc: true);

            if (!result.IsSuccess)
            {
                Debug.LogWarning($"{LogTag} PATCH 전송 실패 — {result.ErrorCode}");
                return SupabaseResult.Fail(result.ErrorCode ?? SupabaseErrorCode.UserSaveFlushFailed);
            }

            _lastSynced = DataSchema.CloneRow(Current);
            DataSchema.ApplyAutoDefaults(_lastSynced); // 비교 양쪽이 같은 기본값 기준을 갖도록
            _isDirty    = false;
            return SupabaseResult.Ok;
        }


        /// <summary>
        /// 값이 변경되었음을 컬럼 우선순위와 함께 알립니다. 우선순위별 쿨다운이 지나면 자동 전송됩니다.
        /// 생성기가 스칼라 필드 setter에서 이 오버로드를 호출하도록 배선합니다 — 전체 행 diff 없이 O(1)입니다.
        /// </summary>
        protected void MarkDirty(DataSavePriority priority)
        {
            EnsureRegistered();
            _isDirty = true;
            if ((int)priority < _scalarDirtyPri) _scalarDirtyPri = (int)priority;
            SupabaseSDK.MarkUserSaveStaticDirty(_syncKey);
        }

        /// <summary>
        /// 우선순위 없이 변경을 알립니다. Normal로 간주합니다.
        /// 구버전 생성 파일·수동 호출 호환용입니다(생성기는 <see cref="MarkDirty(DataSavePriority)"/>를 emit).
        /// </summary>
        protected void MarkDirty() => MarkDirty(DataSavePriority.Normal);
    }

    // StaticUserSave<T> 서브클래스를 씬 로드 전에 자동으로 초기화합니다.
    // 서브클래스의 static 필드 초기화자(Instance = new())를 강제 실행해
    // SupabaseSDK._nanooSaveBridge 등록을 보장합니다.
    internal static class StaticUserSaveAutoInit
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InitAll()
        {
            var openGeneric = typeof(StaticUserSave<>);
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch { continue; }

                foreach (var type in types)
                {
                    if (type.IsAbstract) continue;
                    var baseType = type.BaseType;
                    if (baseType == null || !baseType.IsGenericType) continue;
                    if (baseType.GetGenericTypeDefinition() != openGeneric) continue;
                    System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(type.TypeHandle);
                }
            }
        }
    }
}
