using System;
using System.Collections.Generic;
using System.Linq;
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
    /// public sealed class GameUserSave : StaticUserSave&lt;GameSaveRow&gt;
    /// {
    ///     public static readonly GameUserSave Instance = new("com.mygame.GameUserSave");
    ///     private GameUserSave(string key) : base(key) { }
    ///
    ///     public int Level
    ///     {
    ///         get => Current.level;
    ///         set { if (Current.level == value) return; Current.level = value; MarkDirty(); }
    ///     }
    /// }
    /// </code>
    /// </para>
    /// <para><paramref name="syncKey"/>는 등록된 세이브 간 고유해야 합니다. 권장 형식: "Namespace.ClassName"</para>
    /// </summary>
    public abstract class StaticUserSave<TRow> where TRow : class, new()
    {
        protected readonly TRow   Current;
        private            TRow   _lastSynced;
        private            bool   _isDirty;
        private            bool   _isRegistered;
        private  readonly  string _syncKey;
        protected readonly string LogTag;

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
                if (ensured == null || !ensured.IsSuccess) return false;

                (success, _, row) = await Supabase.TryLoadUserDataAttributedWithRowStateAsync<TRow>(
                    defaultWhenFailed: null, includeUpdatedAt: includeUpdatedAt);
                if (!success) return false;
            }

            DataSchema.CopyInto(Current, row);
            _lastSynced = DataSchema.CloneRow(row);
            _isDirty    = false;
            return true;
        }

        public async Task<bool> TrySaveIfChangedAsync()
        {
            EnsureRegistered();

            Dictionary<string, object> patch;
            try   { patch = DataSchema.BuildPatch(_lastSynced, Current); }
            catch (Exception e)
            {
                Debug.LogWarning($"{LogTag} BuildPatch 실패 — {e.Message}");
                return false;
            }

            var hasDiff = patch != null && patch.Count > 0;
            var keys    = hasDiff
                ? string.Join(", ", patch.Keys.OrderBy(k => k, StringComparer.Ordinal))
                : "(없음)";
            Debug.Log($"{LogTag} 저장 시도 — hasDiff={hasDiff}, 컬럼={keys}");

            if (!hasDiff)
            {
                _lastSynced = DataSchema.CloneRow(Current);
                _isDirty    = false;
                Debug.Log($"{LogTag} PATCH 전송 생략 (변경 없음).");
                return true;
            }

            var ok = await Supabase.TryPatchUserDataDiffAsync(
                _lastSynced, Current,
                ensureRowFirst: true, setUpdatedAtIsoUtc: true);

            if (!ok)
            {
                Debug.LogWarning($"{LogTag} PATCH 전송 실패.");
                return false;
            }

            _lastSynced = DataSchema.CloneRow(Current);
            _isDirty    = false;
            Debug.Log($"{LogTag} PATCH 전송 완료.");
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
