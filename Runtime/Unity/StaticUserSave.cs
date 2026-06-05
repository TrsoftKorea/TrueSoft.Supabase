using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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
    public abstract class StaticUserSave<TRow> where TRow : class, new()
    {
        // ── 단일 서브클래스 강제 ──────────────────────────────────────────────
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

        protected readonly TRow   Current;
        private            TRow   _lastSynced;
        private            bool   _isDirty;
        private            bool   _isRegistered;
        protected readonly string _syncKey;
        protected readonly string LogTag;

        /// <summary>syncKey를 <c>typeof(TRow).FullName</c>으로 자동 설정합니다.</summary>
        protected StaticUserSave() : this(typeof(TRow).FullName) { }

        /// <summary>syncKey를 직접 지정합니다. 여러 인스턴스가 같은 TRow를 공유하는 경우 사용합니다.</summary>
        protected StaticUserSave(string syncKey)
        {
            SingletonGuard.Assert(GetType());
            _sharedInstance = this;

            if (string.IsNullOrWhiteSpace(syncKey))
                throw new ArgumentException("syncKey must not be empty.", nameof(syncKey));

            _syncKey    = syncKey.Trim();
            LogTag      = $"[StaticUserSave<{typeof(TRow).Name}>]";
            Current     = new TRow();
            _lastSynced = new TRow();

            EnsureRegistered();
        }

        private void EnsureRegistered()
        {
            if (_isRegistered) return;
            Supabase.RegisterUserSaveStaticSync(
                _syncKey, HasDirty, FlushDirtyAsync, ResetLocalState,
                () => TryLoadAsync(),
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

        // ── 레지스트리 콜백 ───────────────────────────────────────────────────
        private bool HasDirty() => _isDirty;

        private async Task<bool> FlushDirtyAsync()
        {
            if (!_isDirty) return true;

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
            return true;
        }

        private void ResetLocalState()
        {
            DataSchema.CopyInto(Current, new TRow());
            _lastSynced = new TRow();
            _isDirty    = false;
        }

        // ── 이벤트 ───────────────────────────────────────────────────────────
        /// <summary>
        /// <see cref="TryLoadAsync"/> 성공 후 발행됩니다.
        /// 로드 완료 후 게임 데이터를 적용하거나 UI를 갱신할 때 사용합니다.
        /// </summary>
        public event Action OnLoaded;

        // ── 공유 인스턴스 ─────────────────────────────────────────────────────

        /// <summary>
        /// 이 TRow 타입에 등록된 유일한 StaticUserSave 인스턴스를 반환합니다.
        /// <para>PlayNanooRuntime&lt;TRow&gt; 등 외부에서 세이브 인스턴스를 참조할 때 사용합니다.</para>
        /// </summary>
        public static StaticUserSave<TRow> SharedInstance => _sharedInstance;

        /// <summary>현재 로컬 세이브 Row를 반환합니다.</summary>
        public TRow CurrentRow => Current;

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// 저장 쿨다운을 전역으로 설정합니다.
        /// <para>
        /// <paramref name="priority"/>가 <c>null</c>이면 모든 우선순위를 동일한 <paramref name="seconds"/>로 설정합니다.<br/>
        /// <paramref name="priority"/>를 지정하면 해당 우선순위의 쿨다운만 변경합니다.
        /// </para>
        /// <remarks>
        /// 유저 세이브 클래스는 프로젝트 전체에서 하나만 존재하므로 이 설정은 전역에 적용됩니다.
        /// 일반적으로는 <see cref="SupabaseSettings"/> Inspector에서 저장 주기를 지정하는 것을 권장합니다.
        /// </remarks>
        /// </summary>
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

        public bool TryRequestImmediateSave()
        {
            EnsureRegistered();
            return Supabase.RequestImmediateUserSaveStaticFlush(_syncKey);
        }

        public Task<bool> TryFlushNowAsync(int timeoutMs = 5000)
        {
            EnsureRegistered();
            return Supabase.TryFlushUserSaveImmediateAsync(_syncKey, timeoutMs);
        }

        /// <summary>
        /// 외부에서 로드한 Row를 Current와 _lastSynced에 적용합니다.
        /// PlayNanoo 이관 등 DB 재조회 없이 데이터를 주입할 때 사용합니다.
        /// </summary>
        public void ApplyRow(TRow row)
        {
            DataSchema.CopyInto(Current, row);
            _lastSynced = DataSchema.CloneRow(row);
            _isDirty    = false;
            OnLoaded?.Invoke();
        }

        public async Task<bool> TryEnsureRowAsync()
        {
            EnsureRegistered();
            var r = await Supabase.EnsureMyRowAsync<TRow>();
            return r != null && r.IsSuccess;
        }

        public async Task<bool> TryLoadAsync(bool includeUpdatedAt = true)
        {
            EnsureRegistered();
            var (success, hasRow, row) = await Supabase.TryLoadUserDataAttributedWithRowStateAsync<TRow>(
                defaultWhenFailed: null, includeUpdatedAt: includeUpdatedAt);

            if (!success) return false;

            if (!hasRow)
            {
                var ensured = await Supabase.EnsureMyRowAsync<TRow>();
                if (ensured == null || !ensured.IsSuccess)
                {
                    Debug.LogWarning($"{LogTag} TryLoadAsync: EnsureMyRowAsync 실패 — {ensured?.ErrorMessage ?? "null"}");
                    return false;
                }

                bool hasRow2;
                (success, hasRow2, row) = await Supabase.TryLoadUserDataAttributedWithRowStateAsync<TRow>(
                    defaultWhenFailed: null, includeUpdatedAt: includeUpdatedAt);
                if (!success) return false;

                if (!hasRow2)
                {
                    Debug.LogWarning($"{LogTag} TryLoadAsync: 행 생성 후 재로드에서도 행을 찾을 수 없음.");
                    return false;
                }
            }

            DataSchema.CopyInto(Current, row);
            _lastSynced = DataSchema.CloneRow(row);
            _isDirty    = false;
            OnLoaded?.Invoke();
            return true;
        }

        public async Task<bool> TrySaveIfChangedAsync()
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
                return false;
            }

            if (patch == null || patch.Count == 0)
            {
                _lastSynced = DataSchema.CloneRow(Current);
                _isDirty    = false;
                return true;
            }

            var result = await SupabaseSDK.PatchUserDataAsync(
                tableName, patch, ensureRowFirst: true, setUpdatedAtIsoUtc: true);

            if (!result.IsSuccess)
            {
                Debug.LogWarning($"{LogTag} PATCH 전송 실패 — {result.ErrorMessage}");
                return false;
            }

            _lastSynced = DataSchema.CloneRow(Current);
            _isDirty    = false;
            return true;
        }

        // ── 서브클래스용 ──────────────────────────────────────────────────────
        protected void MarkDirty()
        {
            EnsureRegistered();
            _isDirty = true;
            Supabase.MarkUserSaveStaticDirty(_syncKey);
        }
    }
}
