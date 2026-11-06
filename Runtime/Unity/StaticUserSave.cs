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

        private TRow _nanooLastLoaded;  // NanooLoadWithStateAsync 캐시 (PlayNanoo 동기화용)

        protected readonly TRow   Current;
        private            TRow   _lastSynced;
        private            bool   _isDirty;
        private            bool   _isRegistered;
        private readonly   bool   _hasReferenceColumns;  // 컬렉션·클래스 컬럼 보유 여부(값 비교 감지용)
        private            float  _lastDeepCheckTime = float.MinValue;  // 값 비교 throttle 타임스탬프
        private            bool   _lastDeepResult;                      // 캐시된 값 비교 결과
        private            bool   _hasLoadedOnce;                       // 최초 로드 완료 여부(로드 전 auto-flush 차단용)
        private            string _newUserCarriedForAccount;            // PlayNANOO 동기화가 행을 처음 만든 계정. 게임의 다음 로드에 IsNewUser 로 넘긴다
        private            string _newUserAfterDeleteAccount;           // 삭제가 기본 행을 다시 읽어 온 계정. 기기에도 적어 둔다 — 플레이어 진행이 저장되면 지운다
        private            bool   _reopenAfterDelete;                   // 이번 로그인에서 로드한 뒤 지웠다. 삭제가 실패해도 남아 다시 지울 때 저장을 연다
        private            Task<SupabaseResult> _deleteInFlight;        // 진행 중인 삭제. 두 번 불러도 삭제는 한 번만 돈다
        private            TRow   _loadFallback;                        // 로드 전에 세팅한 초기값 스냅샷(1회 캡처)
        private            bool   _fallbackCaptured;                    // _loadFallback 캡처 여부
        private            int    _resetCount;                          // 로컬을 비운 횟수. 보내거나 읽는 사이 로그아웃·계정 교체·삭제가 있었는지 가른다
        private            int    _sendsInFlight;                       // 응답을 기다리는 저장 수. 삭제가 이걸 기다린 뒤 나간다
        protected readonly string _syncKey;
        protected readonly string LogTag;

        /// <summary>syncKey를 <c>typeof(TRow).FullName</c>으로 자동 설정합니다.</summary>
        protected StaticUserSave() : this(typeof(TRow).FullName) { }

        /// <summary>syncKey를 직접 지정합니다. 여러 인스턴스가 같은 TRow를 공유하는 경우 사용합니다.</summary>
        protected StaticUserSave(string syncKey)
        {
            SingletonGuard.Assert(GetType());
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
                _syncKey, HasDirty, FlushDirtyAsync, ResetForSessionChange,
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

        // 저장 요약 로그에 쓸 칸 이름을 담을 곳. 전송 코드가 만든 목록을 받으므로 로그 때문에 값을 다시 비교하지 않는다.
        private static List<string> NewSentColumnsForLog() =>
            SupabaseSDK.ApiResultLogsEnabled ? new List<string>() : null;

        // 실제로 보낸 칸으로 가른다. 변경 표시로 보면 실패 뒤 재시도의 긴급 값을 보통으로, 되돌린 긴급 값을 긴급으로 센다.
        private static UserSaveSendCause GetAutoSendCause(List<(string Column, DataSavePriority Priority)> columns)
        {
            if (columns.Count == 0)
                return UserSaveSendCause.AutoNormal;

            var highest = DataSavePriority.Lazy;
            foreach (var column in columns)
            {
                if (column.Priority < highest)
                    highest = column.Priority;
            }

            return highest switch
            {
                DataSavePriority.Urgent => UserSaveSendCause.AutoUrgent,
                DataSavePriority.Lazy => UserSaveSendCause.AutoLazy,
                _ => UserSaveSendCause.AutoNormal
            };
        }

        /// <param name="requestedBy">저장을 요청한 쪽. 자동 저장이면 null(보낸 칸의 우선순위로 가른다).</param>
        private static void RecordSendResult(
            SupabaseResult<bool> result,
            UserSaveSendCause? requestedBy,
            List<string> sentColumns)
        {
            if (!SupabaseSDK.ApiResultLogsEnabled)
                return;

            if (!result.IsSuccess)
            {
                UserSaveStaticSyncRegistry.RecordSendFailure();
            }
            else if (result.Data)
            {
                var columns = DataSchema.GetColumnPriorities<TRow>(sentColumns);
                UserSaveStaticSyncRegistry.RecordSend(requestedBy ?? GetAutoSendCause(columns), columns);
            }
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

        private async Task<SupabaseResult> FlushDirtyAsync(UserSaveSendCause? requestedBy)
        {
            // flush 시점에는 throttle을 무시하고 신선하게 검사 — 즉시 저장(앱 종료 등)에서
            // 컬렉션 제자리 수정이 throttle 캐시 때문에 누락되지 않도록 합니다.
            _lastDeepCheckTime = float.MinValue;
            if (!HasDirty()) return SupabaseResult.Ok;

            // await 이전에 현재 값을 스냅샷하고 dirty를 먼저 해제합니다.
            // 이렇게 하면 네트워크 대기 중 게임 코드가 새 값을 쓰고 MarkDirty()를 호출해도
            // dirty 플래그가 올바르게 유지됩니다.
            var snapshot = DataSchema.CloneRow(Current);
            DataSchema.ApplyAutoDefaults(snapshot); // 클론은 [AutoDefault]가 다시 안 걸려 있음 — diff·다음 _lastSynced 기준으로 쓰이기 전에 재주입
            _isDirty = false;
            _scalarDirtyPri = PriNone; // 스냅샷 이후의 스칼라 변경은 await 중 MarkDirty가 다시 채운다
            _refPriCheckedAt = float.MinValue; // 참조 우선순위도 새 스냅샷 기준으로 재평가

            var resetCount = _resetCount;
            var sentColumns = NewSentColumnsForLog();
            SupabaseResult<bool> ok;
            _sendsInFlight++;
            try
            {
                ok = await SupabaseSDK.TryPatchUserDataDiffAsync(
                    _lastSynced, snapshot,
                    ensureRow: true, setUpdatedAtIsoUtc: true, sentColumns: sentColumns);
            }
            finally
            {
                _sendsInFlight--;
            }

            RecordSendResult(ok, requestedBy, sentColumns);

            // 보내는 사이 로컬이 비워졌다 — 이 결과는 옛 상태 몫이라 새 상태에 적지 않는다.
            if (resetCount != _resetCount)
                return SupabaseResult.Ok;

            if (!ok)
            {
                // 전송 실패 시 dirty 복원 — 다음 flush 때 재전송
                _isDirty = true;
                return ok;
            }

            CommitSynced(snapshot);
            return SupabaseResult.Ok;
        }

        private void CommitSynced(TRow snapshot)
        {
            _lastSynced = snapshot;
            // 삭제 뒤 진행이 저장됐다 — 이제 와서 신규 유저로 알리면 게임의 신규 처리가 그 진행을 덮는다.
            if (_newUserAfterDeleteAccount != null)
                ClearNewUserAfterDelete(_newUserAfterDeleteAccount);
            // 방금 동기화했으므로 값 비교·참조 우선순위 캐시를 초기화(새 _lastSynced 기준으로 재평가)
            _lastDeepResult = false;
            _lastDeepCheckTime = float.MinValue;
            _refDirtyPri = PriNone;
            _refPriCheckedAt = float.MinValue;
        }

        // 로그아웃·계정 교체 때 불린다. 삭제가 비우는 로컬(ResetLocalState)과 달리 이번 로그인에 묶인 표시도 함께 버린다.
        // 기기에 적어 둔 신규 표시는 남긴다 — 같은 계정으로 돌아오면 그 로드에 넘겨야 한다.
        private void ResetForSessionChange()
        {
            _reopenAfterDelete = false;
            _newUserAfterDeleteAccount = null;
            ResetLocalState();
        }

        private string NewUserAfterDeleteKey(string accountId)
            => $"TrueBase.UserSave.NewUserAfterDelete.{_syncKey}.{accountId}";

        private void MarkNewUserAfterDelete()
        {
            var accountId = SupabaseSDK.CurrentAccountId;
            if (string.IsNullOrEmpty(accountId)) return;
            _newUserAfterDeleteAccount = accountId;
            PlayerPrefs.SetInt(NewUserAfterDeleteKey(accountId), 1);
            PlayerPrefs.Save();
        }

        private void ClearNewUserAfterDelete(string accountId)
        {
            _newUserAfterDeleteAccount = null;
            if (string.IsNullOrEmpty(accountId) || !PlayerPrefs.HasKey(NewUserAfterDeleteKey(accountId))) return;
            PlayerPrefs.DeleteKey(NewUserAfterDeleteKey(accountId));
            PlayerPrefs.Save();
        }

        private void ResetLocalState()
        {
            _resetCount++;
            DataSchema.CopyInto(Current, new TRow());
            DataSchema.ApplyAutoDefaults(Current);
            _lastSynced = new TRow();
            DataSchema.ApplyAutoDefaults(_lastSynced); // 비교 양쪽이 같은 기본값 기준을 갖도록
            _isDirty       = false;
            _hasLoadedOnce = false;  // 재로그인 시 다시 로드 완료 전까지 auto-flush 차단
            // _loadFallback/_fallbackCaptured는 유지 — fallback은 앱 수명 설정이라
            // 로그아웃 후 재로그인에도 동일 적용(Current가 리셋돼도 스냅샷으로 재적용).
        }

        /// <summary>현재 로컬 세이브 Row를 반환합니다.</summary>
        public TRow CurrentRow => Current;

        // INanooSaveSyncable 구현 (PlayNanooRuntime 전용)

        async Task<(bool success, bool hasRow, DateTimeOffset updatedAt)> INanooSaveSyncable.NanooLoadWithStateAsync()
        {
            var (success, hasRow, row) = await SupabaseSDK.TryLoadUserDataAttributedWithRowStateAsync<TRow>(defaultWhenFailed: null);
            if (!success) return (false, false, DateTimeOffset.MinValue);
            _nanooLastLoaded = row;
            if (!hasRow || row == null)
            {
                return (true, false, DateTimeOffset.MinValue);
            }
            var map = GetNanooMap();
            if (map != null && map.HasCompareBy)
                return (true, true, map.CompareSelector(row));

            var compareField = map?.CompareMemberName ?? "updated_at";
            var field = typeof(TRow).GetField(compareField, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null)
            {
                Debug.LogWarning($"{LogTag} 플레이나누 비교 필드 '{compareField}'를 {typeof(TRow).Name}에서 찾지 못했습니다. 필드 이름을 확인하세요.");
                return (true, true, DateTimeOffset.MinValue);
            }
            var val = field.GetValue(row)?.ToString();
            return (true, true, NanooTimestamp.Parse(val, map?.CompareFallbackUtcOffsetHours ?? 0));
        }

        DateTimeOffset INanooSaveSyncable.NanooParseCompareTimestamp(string nanooJson)
        {
            if (string.IsNullOrEmpty(nanooJson)) return DateTimeOffset.MinValue;
            var map = GetNanooMap();

            var jVal = Newtonsoft.Json.Linq.JObject.Parse(nanooJson)[map?.CompareJsonKey ?? "updated_at"];
            if (jVal == null)
                // 비교 필드 없음 = 이관 전 순수 PlayNANOO 데이터 → PlayNANOO 항상 우선
                return DateTimeOffset.MaxValue;

            if (map != null && map.HasCompareBy)
            {
                // CompareBy(예: r => DateTime.FromOADate(r.field))가 등록돼 있으면 원시 JSON 값만으로는
                // 그 변환을 재현할 수 없다 — TRow로 역직렬화해 CompareSelector를 그대로 태운다.
                try
                {
                    var row = NanooDeserializeJson(nanooJson);
                    if (row != null) return map.CompareSelector(row);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"{LogTag} 플레이나누 비교 필드 변환 실패: {e.Message}");
                }
                return DateTimeOffset.MaxValue;
            }

            var val = jVal.ToString();
            if (string.IsNullOrEmpty(val))
                return DateTimeOffset.MaxValue;
            var parsed = NanooTimestamp.Parse(val, map?.CompareFallbackUtcOffsetHours ?? 0);
            return parsed == DateTimeOffset.MinValue ? DateTimeOffset.MaxValue : parsed;
        }

        async Task<bool> INanooSaveSyncable.NanooPatchFromEmptyAsync(string nanooJson)
        {
            TRow nanooRow;
            try { nanooRow = NanooDeserializeJson(nanooJson); }
            catch (Exception e)
            {
                LogNanooConvertFailure("NanooPatchFromEmptyAsync", e, nanooJson);
                return false;
            }
            var sentColumns = NewSentColumnsForLog();
            var ok = await SupabaseSDK.TryPatchUserDataDiffAsync(new TRow(), nanooRow, sentColumns: sentColumns);
            RecordSendResult(ok, UserSaveSendCause.Sdk, sentColumns);
            return ok && await ReloadFromServerAsync();
        }

        async Task<bool> INanooSaveSyncable.NanooPatchFromLastLoadedAsync(string nanooJson)
        {
            TRow nanooRow;
            try { nanooRow = NanooDeserializeJson(nanooJson); }
            catch (Exception e)
            {
                LogNanooConvertFailure("NanooPatchFromLastLoadedAsync", e, nanooJson);
                return false;
            }
            var prev = _nanooLastLoaded ?? new TRow();
            var sentColumns = NewSentColumnsForLog();
            var ok = await SupabaseSDK.TryPatchUserDataDiffAsync(prev, nanooRow, sentColumns: sentColumns);
            RecordSendResult(ok, UserSaveSendCause.Sdk, sentColumns);
            return ok && await ReloadFromServerAsync();
        }

        // 기기 로그는 한 줄이 길면 잘릴 수 있어 원문을 조각내 남긴다. 조각 크기는 여유를 둔 값이다.
        private const int NanooJsonLogChunkLength = 1000;

        /// <summary>
        /// 플레이나누 데이터를 세이브로 바꾸지 못했을 때, 실패한 키(예외 메시지)와 받은 JSON 원문 전체를 남긴다.
        /// 다른 사람 기기에서 난 오류는 나중에 재현할 길이 없어, 실패한 순간의 원문이 유일한 단서다. 성공하면 아무것도 남기지 않는다.
        /// </summary>
        private void LogNanooConvertFailure(string where, Exception e, string json)
        {
            var length = json?.Length ?? 0;
            var chunks = length == 0 ? 0 : (length + NanooJsonLogChunkLength - 1) / NanooJsonLogChunkLength;
            Debug.LogError($"{LogTag} 플레이나누 JSON 변환 실패 ({where}): {e.Message}\n" +
                           $"받은 JSON 원문 {length}자를 아래 {chunks}개 로그로 남깁니다.");
            for (var i = 0; i < chunks; i++)
            {
                var start = i * NanooJsonLogChunkLength;
                var part  = json.Substring(start, Math.Min(NanooJsonLogChunkLength, length - start));
                Debug.LogWarning($"{LogTag} 플레이나누 JSON 원문 ({i + 1}/{chunks}): {part}");
            }
        }

        /// <summary>
        /// PlayNANOO 데이터를 서버에 반영한 뒤, 서버 정본을 다시 읽어 로컬에 적용합니다.
        /// <para>
        /// PlayNANOO JSON을 그대로 <see cref="ApplyRow"/>하면 그 JSON에 없는 컬럼이 C# 타입 기본값(0·false·null)으로
        /// 덮여, DB 컬럼 DEFAULT로 채워진 값이 로컬에서 사라집니다. PATCH 이후의 서버 행이 정본이므로 그것을 읽어 적용합니다.
        /// </para>
        /// </summary>
        /// <returns>로컬이 서버 정본과 일치하게 됐으면 true. 호출부는 false 를 반영 실패로 취급해 플레이나누 쓰기를 열지 않는다.</returns>
        private async Task<bool> ReloadFromServerAsync()
        {
            var resetCount = _resetCount;
            var (success, hasRow, row) = await SupabaseSDK.TryLoadUserDataAttributedWithRowStateAsync<TRow>(
                defaultWhenFailed: null);

            if (!success || !hasRow || row == null || resetCount != _resetCount)
            {
                // 로컬이 서버와 어긋났다(기본값이거나, 로그아웃 없이 바꾼 이전 계정의 데이터일 수 있다).
                // _hasLoadedOnce 를 여기서 올리지 않는다. 이미 true 였어도 false 를 돌려줘 이번 로그인의 플레이나누 쓰기를 막게 한다.
                Debug.LogWarning($"{LogTag} 플레이나누 반영 후 재조회에 실패했습니다. 로컬이 서버와 어긋난 상태라 " +
                                 $"이번 로그인에서는 플레이나누 저장을 막습니다.");
                return false;
            }

            _nanooLastLoaded = row;
            ApplyRow(row);
            _hasLoadedOnce = true;   // 로컬이 서버 정본과 일치 — 이 시점부터 자동 저장 허용
            return true;
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
        /// <para>
        /// 이 메서드를 호출해 뭐라도 등록했다면 <see cref="NanooFieldMap{TRow}.CompareBy{T}"/>도 반드시 함께 등록해야 합니다 —
        /// 안 하면 첫 사용 시점에 <see cref="InvalidOperationException"/>이 발생합니다. 이 메서드를 아예 안 부르는 경우는
        /// 예외가 아니며, 생성기가 항상 넣어주는 <c>updated_at</c> 컬럼을 기본 비교 기준으로 씁니다.
        /// </para>
        /// </summary>
        public static void UseNanooConverters(Action<NanooFieldMap<TRow>> configure)
            => s_nanooConfigure = configure;

        private NanooFieldMap<TRow> GetNanooMap()
        {
            if (!_nanooMapBuilt)
            {
                var m = new NanooFieldMap<TRow>();
                s_nanooConfigure?.Invoke(m);  // UseNanooConverters로 등록한 변환(있으면)

                // Field()만 등록하고 CompareBy()를 빠뜨리면 updated_at으로 조용히 넘어가던 게 실제 사고 원인이었다
                // (해당 Row엔 의미 있는 updated_at이 없는 경우). UseNanooConverters를 아예 안 쓰는 게임은
                // 여기 안 걸린다 — 그쪽은 생성기가 항상 넣어주는 진짜 updated_at 컬럼을 쓰는 게 맞기 때문이다.
                if (!m.IsEmpty && !m.HasCompareBy)
                {
                    var ownerName = typeof(TRow).DeclaringType?.Name ?? typeof(TRow).Name;
                    throw new InvalidOperationException(
                        $"{ownerName}.UseNanooConverters(...)에 .CompareBy(...)가 없습니다. " +
                        "PlayNANOO·SDK 중 최신 데이터를 가릴 기준 필드를 명시적으로 지정하세요. " +
                        "예: map.CompareBy(r => r.updated_at)");
                }

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
            if (map != null) return map.Deserialize(json);
            try
            {
                return Newtonsoft.Json.JsonConvert.DeserializeObject<TRow>(json);
            }
            catch (Exception e)
            {
                // 예외 메시지에 키가 없을 때가 있어("Input string was not in a correct format") 어느 키인지 찾아 붙인다.
                var detail = NanooFieldMap<TRow>.DescribeFailingField(Newtonsoft.Json.Linq.JObject.Parse(json));
                if (detail == null) throw;
                throw new FormatException(detail, e);
            }
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

        async Task<bool> INanooSaveSyncable.TryLoadAsync()
        {
            var r = await LoadAsync();
            // 로그인 직후 동기화가 게임보다 먼저 행을 만들면, 게임의 첫 LoadAsync 는 행이 이미 있어 신규 유저를 못 본다.
            // 이번 로드가 행을 처음 만든 것이었다면 그 사실을 이 계정에 묶어 게임의 다음 로드에 넘긴다.
            if (r.IsSuccess && r.IsNewUser)
                _newUserCarriedForAccount = SupabaseSDK.CurrentAccountId;
            return r.IsSuccess;
        }

        string INanooSaveSyncable.NanooCurrentJson => NanooSerializeJson(Current);

        bool INanooSaveSyncable.NanooHasServerData => _hasLoadedOnce;


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
        /// <para>아직 로드하지 않았으면 <see cref="SupabaseReason.UserSaveNotReady"/> 사유의 실패를 반환합니다.</para>
        /// </summary>
        public SupabaseResult RequestSave()
        {
            EnsureRegistered();

            if (!_hasLoadedOnce)
                return SupabaseResult.Fail(SupabaseErrorCode.UserSaveNotReady);

            if (!HasFreshDirty())
                return SupabaseResult.Fail(SupabaseErrorCode.UserSaveNoChanges);

            return SupabaseSDK.RequestImmediateUserSaveStaticFlush(_syncKey)
                ? SupabaseResult.Ok
                : SupabaseResult.Fail(SupabaseErrorCode.UserSaveNotReady);
        }

        /// <summary>
        /// 쿨다운을 무시하고 즉시 전송한 뒤 완료까지 대기합니다. 앱 종료 등 중요한 시점에 사용하세요.
        /// <para>보낼 변경분이 없으면 네트워크 요청 없이 <see cref="SupabaseReason.UserSaveNoChanges"/> 사유의 실패를 반환합니다. 오류가 아니라 "보낼 것이 없었다"는 뜻입니다.</para>
        /// <para>아직 로드하지 않았으면 <see cref="SupabaseReason.UserSaveNotReady"/> 사유의 실패를 반환합니다.</para>
        /// <para>전송이 실패하면 그 사유(중복 로그인·네트워크 오류 등)를 그대로 돌려줍니다.</para>
        /// </summary>
        /// <param name="timeoutMs">전송 완료를 기다리는 최대 시간(밀리초). 초과 시 <see cref="SupabaseReason.UserSaveTimeout"/>.</param>
        public async Task<SupabaseResult> SaveNowAsync(int timeoutMs = 5000)
        {
            EnsureRegistered();

            if (!_hasLoadedOnce)
                return SupabaseResult.Fail(SupabaseErrorCode.UserSaveNotReady);

            if (!HasFreshDirty())
                return SupabaseResult.Fail(SupabaseErrorCode.UserSaveNoChanges);

            return await SupabaseSDK.TryFlushUserSaveImmediateAsync(_syncKey, timeoutMs);
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

            var resetCount = _resetCount;
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

            // 읽는 사이 로그아웃·계정 교체·삭제가 있었다 — 옛 상태의 값을 새 상태에 적용하면 다른 계정 줄에 섞여 저장된다.
            if (resetCount != _resetCount)
                return SupabaseLoadResult.Fail(SupabaseErrorCode.UserSaveLoadFailed);

            ApplyRow(row);

            // 아래 초기값 저장이 표시를 지우기 전에 읽어 둔다 — 이 저장은 플레이어 진행이 아니다.
            var loadAccountId = SupabaseSDK.CurrentAccountId;
            var afterDelete = !string.IsNullOrEmpty(loadAccountId) && PlayerPrefs.HasKey(NewUserAfterDeleteKey(loadAccountId));

            // 서버에 없던(SQL NULL) 컬렉션 컬럼에 로드 전 초기값(fallback)이 유지됐다면,
            // _lastSynced는 서버 정본 기준이므로 SaveIfChangedAsync가 그 변경분만 PATCH한다.
            // 변경분이 없으면(일반 로드) patch가 비어 HTTP를 보내지 않는다(no-op).
            // 변경분이 없으면(일반 로드) UserSaveNoChanges로 돌아오며, 이는 오류가 아니라 정상 경로다.
            var initSave = await SendChangesAsync(UserSaveSendCause.Sdk);
            if (!initSave.IsSuccess && initSave.Reason != SupabaseReason.UserSaveNoChanges)
                Debug.LogWarning($"{LogTag} 로드 후 초기값 저장 실패 — {initSave.ErrorCode ?? "null"}. 다음 저장에서 재시도됩니다.");

            if (resetCount != _resetCount)
                return SupabaseLoadResult.Fail(SupabaseErrorCode.UserSaveLoadFailed);

            _hasLoadedOnce = true;
            _reopenAfterDelete = false;

            if (afterDelete)
            {
                isNewUser = true;
                ClearNewUserAfterDelete(loadAccountId);
            }

            // PlayNANOO 동기화가 이 계정의 행을 먼저 만든 경우 — 게임이 처음 부르는 로드에서 신규 유저로 알린다(1회).
            // 계정에 묶어 둬서, 로그아웃 후 다른 계정으로 들어온 로드에는 넘어가지 않는다.
            var carried = _newUserCarriedForAccount;
            _newUserCarriedForAccount = null;
            if (!isNewUser && carried != null && carried == SupabaseSDK.CurrentAccountId)
                isNewUser = true;

            return SupabaseLoadResult.Success(isNewUser);
        }

        private const int DeleteWaitForSendsMs = 5000;

        /// <summary>
        /// 본인 세이브를 삭제합니다(서버 행 DELETE + 로컬 상태를 기본값으로 리셋).
        /// 이미 로드한 상태였으면 <c>ts_ensure_my_row</c>로 기본 행을 다시 만들어 읽어 오므로, 끝나면 바로 이어서 저장할 수 있습니다.
        /// 계정 탈퇴가 아니라 세이브 데이터만 비웁니다.
        /// <para>먼저 로컬을 기본값으로 리셋한 뒤 서버를 삭제하므로, 삭제 도중 자동 저장이 옛 데이터를 되쓰지 않습니다.
        /// 저장이 활발한 순간(전투 중 등)보다 설정 화면 등 조용한 시점에 호출하세요.
        /// 실패 시 로컬은 기본값이지만 서버 데이터는 유지되므로 <see cref="LoadAsync"/>로 재동기화하세요.
        /// 지운 뒤 다시 읽기만 실패해도 실패를 반환합니다 — 같은 방법으로 다시 로드하면 됩니다.</para>
        /// <para>로드 전 초기값(fallback) 스냅샷도 함께 버립니다. 로드하기 전에 지웠으면 다음 <see cref="LoadAsync"/> 전에 새로 세팅하고,
        /// 로드한 뒤에 지웠으면 삭제가 끝난 다음 값을 넣으세요.</para>
        /// </summary>
        public async Task<SupabaseResult> DeleteAsync()
        {
            // 진행 중인 삭제를 기다린다. 따로 돌면 뒤 호출이 앞 호출을 초기화로 착각해 저장을 다시 열지 못한다.
            if (_deleteInFlight != null)
                return await _deleteInFlight;

            _deleteInFlight = DeleteCoreAsync();
            try { return await _deleteInFlight; }
            finally { _deleteInFlight = null; }
        }

        private async Task<SupabaseResult> DeleteCoreAsync()
        {
            EnsureRegistered();

            // 앞 삭제가 실패해 이미 닫힌 저장도 다시 열어야 하므로, 지금 로드 상태가 아니라 이번 로그인 기준으로 본다.
            _reopenAfterDelete |= _hasLoadedOnce;

            // 로컬을 먼저 리셋 — 삭제 await 도중 자동 저장이 돌아도 diff가 비어 PATCH가 안 나감(옛 데이터 되쓰기 방지).
            ResetLocalState();
            var resetCount = _resetCount;

            // 로드 전 초기값 스냅샷도 함께 버린다.
            // 남겨두면 다음 LoadAsync에서 재생성된 기본 행(컬렉션 컬럼은 SQL NULL) 위에
            // 옛 초기값이 그대로 병합돼, 삭제했는데 기본값이 아닌 값으로 되살아난다.
            // 로그아웃(ResetLocalState)과 달리 삭제는 "기본값으로 되돌리기"이므로 여기서만 비운다.
            _loadFallback     = null;
            _fallbackCaptured = false;

            // 이미 나간 저장이 끝난 뒤 지운다. 삭제보다 늦게 도착하면 지운 줄을 다시 만들 수 있기 때문이다.
            // 응답이 오래 안 오면 더 기다리지 않는다 — 그때는 서비스 쪽 삭제 표시가 되살림을 막는다.
            var waited = System.Diagnostics.Stopwatch.StartNew();
            while (_sendsInFlight > 0 && waited.ElapsedMilliseconds < DeleteWaitForSendsMs)
                await Task.Delay(16);

            // 기다리는 사이 로그아웃·계정 교체가 있었다. 삭제는 그때의 로그인으로 나가므로 그대로 보내면 다른 계정 세이브가 지워진다.
            if (resetCount != _resetCount)
                return SupabaseResult.Fail(SupabaseErrorCode.UserSaveDeleteFailed);

            var r = await SupabaseSDK.DeleteUserDataAsync<TRow>();
            if (r == null || !r.IsSuccess)
                return SupabaseResult.Fail(r?.ErrorCode ?? SupabaseErrorCode.UserSaveDeleteFailed);

            // 지우는 사이 계정이 바뀌었으면 앞 계정 줄은 지워졌다. 이어지는 일은 지금 계정에 닿으므로 하지 않는다.
            if (resetCount != _resetCount)
                return SupabaseResult.Ok;

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

            // 플레이 중에 지웠으면 기본 행을 읽어 와 저장을 다시 연다. 리셋한 로컬은 C# 기본값이라 DB 기본값과 다를 수 있어,
            // 읽지 않고 저장만 열면 틀린 값이 서버에 올라간다. 게임이 다시 로드하지 않아도 이후 진행이 사라지지 않게 하려는 것이다.
            if (!_reopenAfterDelete || resetCount != _resetCount)
                return SupabaseResult.Ok;

            // 읽기 전에 적는다. 기본 행이 만들어진 뒤 읽기가 실패하면 다음 로드는 행이 있어 신규 유저를 알 수 없다.
            MarkNewUserAfterDelete();

            var loaded = await LoadAsync();
            if (!loaded.IsSuccess)
                return SupabaseResult.Fail(loaded.ErrorCode ?? SupabaseErrorCode.UserSaveLoadFailed);

            // 이 로드는 게임이 부른 것이 아니다 — 게임이 다음에 부르는 로드가 신규 유저 후처리를 놓치지 않게 넘긴다.
            // 앱을 다시 켠 뒤의 로드는 이미 행이 있어 스스로 알 수 없으므로 기기에 적어 둔다.
            // 방금 로드가 표시를 소비했으므로 다시 적는다.
            MarkNewUserAfterDelete();

            return SupabaseResult.Ok;
        }

        /// <summary>
        /// 마지막 동기화 이후 변경된 필드만 즉시 PATCH합니다.
        /// <para>변경이 없으면 네트워크 요청 없이 <see cref="SupabaseReason.UserSaveNoChanges"/> 사유의 실패를 반환합니다. 오류가 아니라 "보낼 것이 없었다"는 뜻입니다.</para>
        /// <para>아직 로드하지 않았으면 <see cref="SupabaseReason.UserSaveNotReady"/> 사유의 실패를 반환합니다. 로드 전 값을 보내면 서버 값을 덮기 때문입니다.</para>
        /// </summary>
        public Task<SupabaseResult> SaveIfChangedAsync()
        {
            EnsureRegistered();

            return _hasLoadedOnce
                ? SendChangesAsync(UserSaveSendCause.Game)
                : Task.FromResult(SupabaseResult.Fail(SupabaseErrorCode.UserSaveNotReady));
        }

        private async Task<SupabaseResult> SendChangesAsync(UserSaveSendCause cause)
        {
            string tableName;
            Dictionary<string, object> patch;
            TRow snapshot;
            try
            {
                tableName = DataSchema.ResolveTableName<TRow>();
                // 보낸 값을 기준으로 삼는다. 끝난 뒤 Current 를 베끼면 보내는 사이 바뀐 값이 "이미 저장됨"이 돼 사라진다.
                snapshot  = DataSchema.CloneRow(Current);
                DataSchema.ApplyAutoDefaults(snapshot); // 비교 양쪽이 같은 기본값 기준을 갖도록
                patch     = DataSchema.BuildPatch(_lastSynced, snapshot);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{LogTag} 전송 형식 변환 실패 — {e.Message}");
                return SupabaseResult.Fail(SupabaseErrorCode.UserSaveSerializeFailed);
            }

            if (patch == null || patch.Count == 0)
            {
                _lastSynced = snapshot;
                _isDirty    = false;
                return SupabaseResult.Fail(SupabaseErrorCode.UserSaveNoChanges);
            }

            var sentColumns = SupabaseSDK.ApiResultLogsEnabled ? new List<string>(patch.Keys) : null;

            // 먼저 해제해야 보내는 사이 게임이 부른 MarkDirty 가 지워지지 않는다.
            var resetCount = _resetCount;
            _isDirty = false;
            _scalarDirtyPri = PriNone;
            _refPriCheckedAt = float.MinValue;

            SupabaseResult<bool> result;
            _sendsInFlight++;
            try
            {
                result = await SupabaseSDK.PatchUserDataAsync(
                    tableName, patch, ensureRow: true, setUpdatedAtIsoUtc: true);
            }
            finally
            {
                _sendsInFlight--;
            }

            RecordSendResult(result, cause, sentColumns);

            // 보내는 사이 로컬이 비워졌으면 옛 상태 몫이라 새 상태에 적지 않는다.
            var stale = resetCount != _resetCount;

            if (!result.IsSuccess)
            {
                if (!stale) _isDirty = true;
                Debug.LogWarning($"{LogTag} PATCH 전송 실패 — {result.ErrorCode}");
                return SupabaseResult.Fail(result.ErrorCode ?? SupabaseErrorCode.UserSaveFlushFailed);
            }

            if (!stale) CommitSynced(snapshot);
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
