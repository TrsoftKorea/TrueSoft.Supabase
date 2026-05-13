using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Truesoft.Supabase.Core.Data;
using UnityEngine;

namespace Truesoft.Supabase.Unity
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
    ///     [DataTable("basic")]
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
    /// </summary>
    public abstract class StaticUserSave<TRow> where TRow : class, new()
    {
        protected readonly TRow   Current;
        private            TRow   _lastSynced;
        private            bool   _isDirty;
        private            bool   _isRegistered;
        private  readonly  string _syncKey;
        protected readonly string LogTag;

        /// <summary>syncKey를 <c>typeof(TRow).FullName</c>으로 자동 설정합니다.</summary>
        protected StaticUserSave() : this(typeof(TRow).FullName) { }

        /// <summary>syncKey를 직접 지정합니다. 여러 인스턴스가 같은 TRow를 공유하는 경우 사용합니다.</summary>
        protected StaticUserSave(string syncKey)
        {
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
            Supabase.RegisterUserSaveStaticSync(_syncKey, HasDirty, FlushDirtyAsync, ResetLocalState);
            _isRegistered = true;
        }

        // ── 레지스트리 콜백 ───────────────────────────────────────────────────
        private bool HasDirty() => _isDirty;

        private async Task<bool> FlushDirtyAsync()
        {
            if (!_isDirty) return true;

            var ok = await Supabase.TryPatchUserDataDiffAsync(
                _lastSynced, Current,
                ensureRowFirst: true, setUpdatedAtIsoUtc: true);

            if (!ok) return false;

            _lastSynced = DataSchema.CloneRow(Current);
            _isDirty    = false;
            return true;
        }

        private void ResetLocalState()
        {
            DataSchema.CopyInto(Current, new TRow());
            _lastSynced = new TRow();
            _isDirty    = false;
        }

        // ── Public API ────────────────────────────────────────────────────────
        public void ConfigureCooldown(float seconds) =>
            Supabase.ConfigureUserSaveAutoSyncCooldown(seconds);

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
