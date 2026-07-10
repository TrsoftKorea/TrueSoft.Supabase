using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using TrueBase.Core.Common;
using TrueBase.Core.Data;
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
    public abstract class StaticUserSave<TRow> : INanooSaveSyncable where TRow : class, new()
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

            if (string.IsNullOrWhiteSpace(syncKey))
                throw new ArgumentException("syncKey must not be empty.", nameof(syncKey));

            _syncKey    = syncKey.Trim();
            LogTag      = $"[StaticUserSave<{typeof(TRow).Name}>]";
            Current     = new TRow();
            _lastSynced = new TRow();
            _hasReferenceColumns = DataSchema.HasReferenceColumns<TRow>();

            EnsureRegistered();
        }

        private void EnsureRegistered()
        {
            if (_isRegistered) return;
            Supabase.RegisterUserSaveStaticSync(
                _syncKey, HasDirty, FlushDirtyAsync, ResetLocalState,
                async () => (await LoadAsync()).IsSuccess,
                GetDirtyCooldown);
            _isRegistered = true;
        }

        /// <summary>
        /// dirty 필드 중 가장 높은 우선순위(Urgent에 가까운)의 쿨다운(초)을 반환합니다.
        /// <see cref="SupabaseSettings"/>의 유저 세이브 저장 주기 설정값을 사용합니다.
        /// </summary>
        private float GetDirtyCooldown()
        {
            // DataSchema는 Core.Data.DataSavePriority를 반환하므로 int 경유로 변환합니다.
            var coreP = DataSchema.GetHighestDirtyPriority(_lastSynced, Current);
            var p     = coreP.HasValue ? (int)coreP.Value : 1; // 기본 Normal(1)
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
            _isDirty = false;

            var ok = await Supabase.TryPatchUserDataDiffAsync(
                _lastSynced, snapshot,
                ensureRowFirst: true, setUpdatedAtIsoUtc: true);

            if (!ok)
            {
                // 전송 실패 시 dirty 복원 — 다음 flush 때 재전송
                _isDirty = true;
                return false;
            }

            _lastSynced = snapshot;
            // 방금 동기화했으므로 값 비교 캐시를 초기화(새 _lastSynced 기준으로 재평가)
            _lastDeepResult = false;
            _lastDeepCheckTime = float.MinValue;
            return true;
        }

        private void ResetLocalState()
        {
            DataSchema.CopyInto(Current, new TRow());
            DataSchema.ApplyAutoDefaults(Current);
            _lastSynced = new TRow();
            _isDirty    = false;
        }

        /// <summary>
        /// <see cref="LoadAsync"/> 성공 후 발행됩니다.
        /// 로드 완료 후 게임 데이터를 적용하거나 UI를 갱신할 때 사용합니다.
        /// </summary>
        public event Action OnLoaded;


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
            var (success, hasRow, row) = await Supabase.TryLoadUserDataAttributedWithRowStateAsync<TRow>(defaultWhenFailed: null);
            if (!success) return (false, false, DateTime.MinValue);
            _nanooLastLoaded = row;
            if (!hasRow || row == null) return (true, false, DateTime.MinValue);
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
            var ok = await Supabase.TryPatchUserDataDiffAsync(new TRow(), nanooRow);
            if (ok) ApplyRow(nanooRow);
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
            var ok = await Supabase.TryPatchUserDataDiffAsync(prev, nanooRow);
            if (ok) ApplyRow(nanooRow);
            return ok;
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

        void INanooSaveSyncable.NanooApplyLastLoaded()
        {
            if (_nanooLastLoaded != null) ApplyRow(_nanooLastLoaded);
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
                Supabase.ConfigureUserSavePriorityCooldowns(s, s, s);
            }
            else
            {
                // 지정한 우선순위만 변경, 나머지는 현재 값 유지
                var urgent = priority.Value == DataSavePriority.Urgent ? s : UserSaveStaticSyncRegistry.GetPriorityCooldown(0);
                var normal = priority.Value == DataSavePriority.Normal ? s : UserSaveStaticSyncRegistry.GetPriorityCooldown(1);
                var lazy   = priority.Value == DataSavePriority.Lazy   ? s : UserSaveStaticSyncRegistry.GetPriorityCooldown(2);
                Supabase.ConfigureUserSavePriorityCooldowns(urgent, normal, lazy);
            }
        }

        /// <summary>
        /// 쿨다운을 무시하고 즉시 전송을 요청합니다. 전송 완료를 기다리지 않습니다(fire-and-forget).
        /// 이미 전송 중이면 완료 후 1회 재전송이 예약됩니다.
        /// </summary>
        public SupabaseResult RequestImmediateSave()
        {
            EnsureRegistered();
            return Supabase.RequestImmediateUserSaveStaticFlush(_syncKey)
                ? SupabaseResult.Ok
                : SupabaseResult.Fail(SupabaseFailReason.UserSaveFlushFailed);
        }

        /// <summary>
        /// 쿨다운을 무시하고 즉시 전송한 뒤 완료까지 대기합니다. 앱 종료 등 중요한 시점에 사용하세요.
        /// </summary>
        /// <param name="timeoutMs">전송 완료를 기다리는 최대 시간(밀리초). 초과 시 실패를 반환합니다.</param>
        public async Task<SupabaseResult> FlushNowAsync(int timeoutMs = 5000)
        {
            EnsureRegistered();
            return await Supabase.TryFlushUserSaveImmediateAsync(_syncKey, timeoutMs)
                ? SupabaseResult.Ok
                : SupabaseResult.Fail(SupabaseFailReason.UserSaveFlushFailed);
        }

        /// <summary>
        /// 외부에서 로드한 Row를 Current와 _lastSynced에 적용합니다.
        /// PlayNANOO 이관 등 DB 재조회 없이 데이터를 주입할 때 사용합니다.
        /// </summary>
        public void ApplyRow(TRow row)
        {
            DataSchema.CopyInto(Current, row);
            DataSchema.ApplyAutoDefaults(Current);   // AutoList/AutoDict [AutoDefault] 주입 (CopyInto가 새 인스턴스를 만들므로 매번 필요)
            _lastSynced = DataSchema.CloneRow(row);
            _isDirty    = false;
            OnLoaded?.Invoke();
        }

        /// <summary>
        /// DB에 본인 행이 존재하도록 보장합니다. 행이 없으면 DB 기본값으로 생성합니다(로컬 데이터는 변경하지 않음).
        /// </summary>
        public async Task<SupabaseResult> EnsureRowAsync()
        {
            EnsureRegistered();
            var r = await Supabase.EnsureMyRowAsync<TRow>();
            return r != null && r.IsSuccess
                ? SupabaseResult.Ok
                : SupabaseResult.Fail(r?.ErrorMessage ?? SupabaseFailReason.UserSaveLoadFailed);
        }

        /// <summary>
        /// DB에서 세이브를 로드해 <see cref="CurrentRow"/>에 적용합니다. 행이 없으면 생성 후 재로드합니다.
        /// 성공 시 <see cref="OnLoaded"/>가 발행됩니다.
        /// </summary>
        /// <param name="includeUpdatedAt">true면 select에 <c>updated_at</c> 컬럼을 포함합니다.</param>
        public async Task<SupabaseResult> LoadAsync(bool includeUpdatedAt = true)
        {
            EnsureRegistered();
            var (success, hasRow, row) = await Supabase.TryLoadUserDataAttributedWithRowStateAsync<TRow>(
                defaultWhenFailed: null, includeUpdatedAt: includeUpdatedAt);

            if (!success) return SupabaseResult.Fail(SupabaseFailReason.UserSaveLoadFailed);

            if (!hasRow)
            {
                var ensured = await Supabase.EnsureMyRowAsync<TRow>();
                if (ensured == null || !ensured.IsSuccess)
                {
                    Debug.LogWarning($"{LogTag} TryLoadAsync: EnsureMyRowAsync 실패 — {ensured?.ErrorMessage ?? "null"}");
                    return SupabaseResult.Fail(ensured?.ErrorMessage ?? SupabaseFailReason.UserSaveLoadFailed);
                }

                bool hasRow2;
                (success, hasRow2, row) = await Supabase.TryLoadUserDataAttributedWithRowStateAsync<TRow>(
                    defaultWhenFailed: null, includeUpdatedAt: includeUpdatedAt);
                if (!success) return SupabaseResult.Fail(SupabaseFailReason.UserSaveLoadFailed);

                if (!hasRow2)
                {
                    Debug.LogWarning($"{LogTag} TryLoadAsync: 행 생성 후 재로드에서도 행을 찾을 수 없음.");
                    return SupabaseResult.Fail(SupabaseFailReason.UserSaveLoadFailed);
                }
            }

            DataSchema.CopyInto(Current, row);
            DataSchema.ApplyAutoDefaults(Current);
            _lastSynced = DataSchema.CloneRow(row);
            _isDirty    = false;
            OnLoaded?.Invoke();
            return SupabaseResult.Ok;
        }

        /// <summary>
        /// 마지막 동기화 이후 변경된 필드만 즉시 PATCH합니다. 변경이 없으면 네트워크 요청 없이 성공을 반환합니다.
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
                Debug.LogWarning($"{LogTag} BuildPatch 실패 — {e.Message}");
                return SupabaseResult.Fail(SupabaseFailReason.UserSaveFlushFailed);
            }

            if (patch == null || patch.Count == 0)
            {
                _lastSynced = DataSchema.CloneRow(Current);
                _isDirty    = false;
                return SupabaseResult.Ok;
            }

            var result = await SupabaseSDK.PatchUserDataAsync(
                tableName, patch, ensureRowFirst: true, setUpdatedAtIsoUtc: true);

            if (!result.IsSuccess)
            {
                Debug.LogWarning($"{LogTag} PATCH 전송 실패 — {result.ErrorMessage}");
                return SupabaseResult.Fail(result.ErrorMessage ?? SupabaseFailReason.UserSaveFlushFailed);
            }

            _lastSynced = DataSchema.CloneRow(Current);
            _isDirty    = false;
            return SupabaseResult.Ok;
        }


        /// <summary>
        /// 값이 변경되었음을 알립니다. 우선순위별 쿨다운이 지나면 자동으로 전송됩니다.
        /// 스칼라 필드 setter에서 값이 실제로 바뀔 때 호출하세요(컬렉션·클래스 필드는 값 비교로 자동 감지).
        /// </summary>
        protected void MarkDirty()
        {
            EnsureRegistered();
            _isDirty = true;
            Supabase.MarkUserSaveStaticDirty(_syncKey);
        }
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
