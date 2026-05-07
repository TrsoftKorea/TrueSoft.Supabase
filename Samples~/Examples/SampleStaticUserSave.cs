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
    /// <para><b>사용 방법</b></para>
    /// <para>1. <see cref="SampleStaticUserSaveRow"/>의 <c>[DataTable("custom_saves")]</c>를 실제 테이블명으로 바꿉니다.</para>
    /// <para>2. <see cref="SampleStaticUserSaveRow"/> 필드를 프로젝트 컬럼에 맞게 수정하고, <c>[DataColumn]</c>을 붙입니다.</para>
    /// <para>3. <see cref="TryLoadAsync"/>로 불러오고, <see cref="TrySaveIfChangedAsync"/>로 변경분만 저장합니다.</para>
    /// <para>행이 없는 신규 유저는 <see cref="TryLoadAsync"/> 반환값 <c>hasRow == false</c>로 확인 후 초기값을 직접 설정하세요.</para>
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
        /// 로그인 직후 한 번 호출합니다. 행이 없는 신규 유저의 경우 DB 기본값으로 행을 생성합니다.
        /// </summary>
        public static async Task<bool> TryEnsureRowAsync()
        {
            EnsureRegistered();
            var r = await SupabaseSdk.EnsureMyRowAsync<SampleStaticUserSaveRow>();
            return r != null && r.IsSuccess;
        }

        /// <summary>
        /// 서버에서 로드합니다. 행이 없는 신규 유저는 C# 타입 기본값으로 채워지며,
        /// 최초 저장 시 <c>ts_ensure_my_row</c>가 DB 기본값으로 행을 생성합니다.
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

            return new SampleStaticUserSaveRow
            {
                level = src.level,
                coins = src.coins,
            };
        }

        private static void CopyInto(SampleStaticUserSaveRow dst, SampleStaticUserSaveRow src)
        {
            if (dst == null || src == null)
                return;
            dst.level = src.level;
            dst.coins = src.coins;
        }

        /// <summary>
        /// [DataTable("테이블명")] 에 실제 테이블명을 "data_" 없이 입력하세요. 접두사는 자동으로 붙습니다.
        /// 예: [DataTable("custom_saves")] → DB 테이블 "data_custom_saves"
        /// </summary>
        [DataTable("custom_saves")]
        [Serializable]
        private sealed class SampleStaticUserSaveRow
        {
            [DataColumn("level")] public int level;
            [DataColumn("coins")] public int coins;
        }
    }
}
