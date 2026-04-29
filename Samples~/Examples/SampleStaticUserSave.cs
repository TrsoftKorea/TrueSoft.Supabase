using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using Truesoft.Supabase.Core.Data;
using SupabaseSdk = global::Truesoft.Supabase.Unity.Supabase;

namespace Truesoft.SupabaseUnity.Samples
{
    /// <summary>
    /// OpenAPI 유저 데이터 생성기가 출력하는 <c>public static class</c> + 내부 Row 패턴의 수동 예시입니다.
    /// 컬럼 구성은 <see cref="ExampleSupabaseScenarios"/> 의 세이브 데모와 동일하게 맞춰 두었습니다.
    /// </summary>
    public static class SampleStaticUserSave
    {
        private const string SyncKey = "Truesoft.SupabaseUnity.Samples.SampleStaticUserSave";
        private static readonly SampleStaticUserSaveRow Current = new SampleStaticUserSaveRow();
        private static SampleStaticUserSaveRow LastSynced = new SampleStaticUserSaveRow();
        private static bool IsDirty;
        private static bool IsRegistered;

        static SampleStaticUserSave()
        {
            EnsureRegistered();
        }

        private static void EnsureRegistered()
        {
            if (IsRegistered)
                return;

            SupabaseSdk.RegisterUserSaveStaticSync(SyncKey, HasDirty, FlushDirtyAsync, ResetLocalState);
            IsRegistered = true;
        }

        public static void ConfigureCooldown(float seconds)
        {
            SupabaseSdk.ConfigureUserSaveAutoSyncCooldown(seconds);
        }

        public static bool TryRequestImmediateSave()
        {
            EnsureRegistered();
            return SupabaseSdk.RequestImmediateUserSaveStaticFlush(SyncKey);
        }

        public static Task<bool> TryFlushNowAsync(int timeoutMs = 5000)
        {
            EnsureRegistered();
            return SupabaseSdk.TryFlushUserSaveImmediateAsync(SyncKey, timeoutMs);
        }

        /// <summary>
        /// 서버에서 로드합니다. 본인 행이 없으면(<c>HasRow == false</c>) <paramref name="initialLevel"/>·<paramref name="initialCoins"/>로 채웁니다.
        /// </summary>
        public static async Task<bool> TryLoadFromServerAsync(int initialLevel, int initialCoins, bool includeUpdatedAt = true)
        {
            EnsureRegistered();
            var (success, hasRow, row) = await SupabaseSdk.TryLoadUserDataAttributedWithRowStateAsync<SampleStaticUserSaveRow>(
                defaultWhenFailed: null,
                includeUpdatedAt: includeUpdatedAt);
            if (!success)
                return false;

            if (!hasRow)
            {
                row.level = initialLevel;
                row.coins = initialCoins;
                row.updated_at = null;
            }

            CopyInto(Current, row);
            LastSynced = CloneRow(row);
            IsDirty = false;
            return true;
        }

        /// <summary>
        /// 생성기 <c>TryLoadAsync</c>와 동일: 행이 없으면 타입 기본값 Row로 채웁니다. 게임 초기값은 이후 프로퍼티로 설정하세요.
        /// </summary>
        public static async Task<bool> TryLoadAsync(bool includeUpdatedAt = true)
        {
            EnsureRegistered();
            var (success, _, row) = await SupabaseSdk.TryLoadUserDataAttributedWithRowStateAsync<SampleStaticUserSaveRow>(
                defaultWhenFailed: null,
                includeUpdatedAt: includeUpdatedAt);
            if (!success)
                return false;

            CopyInto(Current, row);
            LastSynced = CloneRow(row);
            IsDirty = false;
            return true;
        }

        /// <summary>
        /// 마지막 서버 스냅샷 대비 변경된 컬럼만 PATCH합니다. 변경이 없으면 네트워크 요청을 보내지 않습니다.
        /// 콘솔에 변경 여부·전송 생략/완료를 로그로 남깁니다.
        /// </summary>
        public static async Task<bool> TrySaveIfChangedAsync()
        {
            EnsureRegistered();

            Dictionary<string, object> patch;
            try
            {
                patch = DataSchema.BuildPatch(LastSynced, Current);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SampleStaticUserSave] 저장: BuildPatch 실패 — " + e.Message);
                return false;
            }

            var hasDiff = patch != null && patch.Count > 0;
            var keys = hasDiff ? string.Join(", ", patch.Keys.OrderBy(k => k, StringComparer.Ordinal)) : "(없음)";
            Debug.Log($"[SampleStaticUserSave] 저장 시도 — 서버 스냅샷 대비 변경 있음: {hasDiff}, diff 컬럼: {keys}");

            if (!hasDiff)
            {
                LastSynced = CloneRow(Current);
                IsDirty = false;
                Debug.Log("[SampleStaticUserSave] PATCH 전송 생략 (변경된 유저 컬럼 없음, updated_at만 쓰는 갱신도 없음).");
                return true;
            }

            var ok = await SupabaseSdk.TryPatchUserDataDiffAsync(
                LastSynced,
                Current,
                ensureRowFirst: true,
                setUpdatedAtIsoUtc: true);
            if (!ok)
            {
                Debug.LogWarning("[SampleStaticUserSave] PATCH 전송 실패(TryPatchUserDataDiffAsync false).");
                return false;
            }

            LastSynced = CloneRow(Current);
            IsDirty = false;
            Debug.Log("[SampleStaticUserSave] PATCH 전송 완료(HTTP 성공).");
            return true;
        }

        public static int Level
        {
            get => Current.level;
            set
            {
                if (Equals(Current.level, value))
                    return;
                Current.level = value;
                MarkDirty();
            }
        }

        public static int Coins
        {
            get => Current.coins;
            set
            {
                if (Equals(Current.coins, value))
                    return;
                Current.coins = value;
                MarkDirty();
            }
        }

        public static string UpdatedAt
        {
            get => Current.updated_at;
            set
            {
                if (Equals(Current.updated_at, value))
                    return;
                Current.updated_at = value;
                MarkDirty();
            }
        }

        private static void MarkDirty()
        {
            EnsureRegistered();
            IsDirty = true;
            SupabaseSdk.MarkUserSaveStaticDirty(SyncKey);
        }

        private static bool HasDirty() => IsDirty;

        private static async Task<bool> FlushDirtyAsync()
        {
            if (!IsDirty)
                return true;

            var ok = await SupabaseSdk.TryPatchUserDataDiffAsync(
                LastSynced,
                Current,
                ensureRowFirst: true,
                setUpdatedAtIsoUtc: true);
            if (!ok)
                return false;

            LastSynced = CloneRow(Current);
            IsDirty = false;
            return true;
        }

        private static void ResetLocalState()
        {
            CopyInto(Current, new SampleStaticUserSaveRow());
            LastSynced = new SampleStaticUserSaveRow();
            IsDirty = false;
        }

        private static SampleStaticUserSaveRow CloneRow(SampleStaticUserSaveRow src)
        {
            if (src == null)
                return new SampleStaticUserSaveRow();

            var copy = new SampleStaticUserSaveRow();
            copy.level = src.level;
            copy.coins = src.coins;
            copy.updated_at = src.updated_at;
            return copy;
        }

        private static void CopyInto(SampleStaticUserSaveRow dst, SampleStaticUserSaveRow src)
        {
            if (dst == null || src == null)
                return;
            dst.level = src.level;
            dst.coins = src.coins;
            dst.updated_at = src.updated_at;
        }

        [Serializable]
        private sealed class SampleStaticUserSaveRow
        {
            [DataColumn("level")] public int level;
            [DataColumn("coins")] public int coins;
            [DataColumn("updated_at")] public string updated_at;
        }
    }
}
