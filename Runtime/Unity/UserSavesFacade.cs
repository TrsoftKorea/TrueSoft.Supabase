using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TrueBase.Core.Auth;
using TrueBase.Core.Common;
using TrueBase.Core.Data;

namespace TrueBase.Unity
{
    /// <summary>
    /// 유저 세이브 로드·PATCH를 세션 관리와 함께 제공하는 파사드.
    /// 각 API는 세션 없는 오버로드(생성자에 전달한 <c>sessionGetter</c>에서 현재 세션을 가져옴)와
    /// 세션을 직접 지정하는 오버로드를 함께 제공합니다.
    /// </summary>
    internal sealed class UserSavesFacade
    {
        private readonly SupabaseUserDataService _userDataService;
        private readonly Func<SupabaseSession> _sessionGetter;

        /// <param name="userDataService">REST 호출을 수행할 서비스. null이면 예외.</param>
        /// <param name="sessionGetter">현재 세션 제공자. null이면 세션 없는 오버로드는 <c>session_null</c>로 실패합니다.</param>
        public UserSavesFacade(SupabaseUserDataService userDataService, Func<SupabaseSession> sessionGetter = null)
        {
            _userDataService = userDataService ?? throw new ArgumentNullException(nameof(userDataService));
            _sessionGetter = sessionGetter;
        }

        /// <summary>
        /// 로그인 직후 <c>user_data</c> 테이블에 본인 행이 존재하도록 보장합니다.
        /// DB RPC: <c>ts_ensure_my_row(table, user_id)</c>.
        /// </summary>
        public Task<SupabaseResult<bool>> EnsureMyRowAsync<T>()
        {
            var session = _sessionGetter?.Invoke();
            return EnsureMyRowAsync<T>(session);
        }

        /// <summary>세션을 직접 지정하는 오버로드. <paramref name="session"/>이 null이면 <c>session_null</c>로 실패합니다.</summary>
        public async Task<SupabaseResult<bool>> EnsureMyRowAsync<T>(SupabaseSession session)
        {
            if (session == null)
                return SupabaseResult<bool>.Fail("session_null");

            var accessToken = session.AccessToken;
            if (string.IsNullOrWhiteSpace(accessToken))
                return SupabaseResult<bool>.Fail("auth_not_signed_in");

            string tableName;
            try { tableName = DataSchema.ResolveTableName<T>(); }
            catch (Exception e) { return SupabaseResult<bool>.Fail("user_save_schema_invalid:" + e.Message); }

            return await _userDataService.EnsureMyRowAsync(accessToken, tableName, session.User?.PlayerUserId);
        }

        /// <summary>
        /// 변경된 값만 부분 저장(PATCH)합니다. <paramref name="tableName"/>은 대상 테이블을 명시합니다.
        /// </summary>
        public Task<SupabaseResult<bool>> PatchAsync(
            string tableName,
            Dictionary<string, object> patch,
            bool ensureRowFirst = true,
            bool setUpdatedAtIsoUtc = true)
        {
            var session = _sessionGetter?.Invoke();
            return PatchAsync(session, tableName, patch, ensureRowFirst, setUpdatedAtIsoUtc);
        }

        /// <summary>세션을 직접 지정하는 오버로드. <paramref name="session"/>이 null이면 <c>session_null</c>로 실패합니다.</summary>
        public async Task<SupabaseResult<bool>> PatchAsync(
            SupabaseSession session,
            string tableName,
            Dictionary<string, object> patch,
            bool ensureRowFirst = true,
            bool setUpdatedAtIsoUtc = true)
        {
            if (session == null)
                return SupabaseResult<bool>.Fail("session_null");

            var accessToken = session.AccessToken;
            var userId = session.User?.Id;

            if (string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(userId))
                return SupabaseResult<bool>.Fail("auth_not_signed_in");

            return await _userDataService.PatchAsync(
                accessToken: accessToken,
                accountId: userId,
                playerUserId: session.User.PlayerUserId,
                tableName: tableName,
                patch: patch,
                ensureRowFirst: ensureRowFirst,
                setUpdatedAtIsoUtc: setUpdatedAtIsoUtc);
        }

        /// <inheritdoc cref="SupabaseUserDataService.LoadAttributedWithRowStateAsync{T}(string, string, bool)"/>
        public Task<SupabaseResult<DataColumnsLoadResult<T>>> LoadAttributedWithRowStateAsync<T>(
            bool includeUpdatedAt = true) where T : class, new()
        {
            var session = _sessionGetter?.Invoke();
            return LoadAttributedWithRowStateAsync<T>(session, includeUpdatedAt);
        }

        /// <summary>세션을 직접 지정하는 오버로드. <paramref name="session"/>이 null이면 <c>session_null</c>로 실패합니다.</summary>
        public async Task<SupabaseResult<DataColumnsLoadResult<T>>> LoadAttributedWithRowStateAsync<T>(
            SupabaseSession session,
            bool includeUpdatedAt = true) where T : class, new()
        {
            if (session == null)
                return SupabaseResult<DataColumnsLoadResult<T>>.Fail("session_null");

            var accessToken = session.AccessToken;
            var userId = session.User?.Id;

            if (string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(userId))
                return SupabaseResult<DataColumnsLoadResult<T>>.Fail("auth_not_signed_in");

            return await _userDataService.LoadAttributedWithRowStateAsync<T>(
                accessToken: accessToken,
                accountId: userId,
                includeUpdatedAt: includeUpdatedAt);
        }

        /// <summary>
        /// <see cref="DataSchema.BuildPatch{T}(T, T)"/>로 변경분만 PATCH합니다.
        /// </summary>
        public Task<SupabaseResult<bool>> PatchDiffAsync<T>(
            T previous,
            T current,
            bool ensureRowFirst = true,
            bool setUpdatedAtIsoUtc = true)
        {
            var session = _sessionGetter?.Invoke();
            return PatchDiffAsync(session, previous, current, ensureRowFirst, setUpdatedAtIsoUtc);
        }

        /// <summary>세션을 직접 지정하는 오버로드. <paramref name="session"/>이 null이면 <c>session_null</c>로 실패합니다.</summary>
        public async Task<SupabaseResult<bool>> PatchDiffAsync<T>(
            SupabaseSession session,
            T previous,
            T current,
            bool ensureRowFirst = true,
            bool setUpdatedAtIsoUtc = true)
        {
            if (session == null)
                return SupabaseResult<bool>.Fail("session_null");

            var accessToken = session.AccessToken;
            var userId = session.User?.Id;

            if (string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(userId))
                return SupabaseResult<bool>.Fail("auth_not_signed_in");

            return await _userDataService.PatchDiffAsync(
                accessToken: accessToken,
                accountId: userId,
                playerUserId: session.User.PlayerUserId,
                previous: previous,
                current: current,
                ensureRowFirst: ensureRowFirst,
                setUpdatedAtIsoUtc: setUpdatedAtIsoUtc);
        }

        /// <summary>본인 세이브 행을 삭제합니다. 테이블은 <typeparamref name="T"/>에서 해석합니다.</summary>
        public Task<SupabaseResult<bool>> DeleteMyRowAsync<T>()
        {
            var session = _sessionGetter?.Invoke();
            return DeleteMyRowAsync<T>(session);
        }

        /// <summary>세션을 직접 지정하는 오버로드. <paramref name="session"/>이 null이면 <c>session_null</c>로 실패합니다.</summary>
        public async Task<SupabaseResult<bool>> DeleteMyRowAsync<T>(SupabaseSession session)
        {
            if (session == null)
                return SupabaseResult<bool>.Fail("session_null");

            var accessToken = session.AccessToken;
            var userId = session.User?.Id;

            if (string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(userId))
                return SupabaseResult<bool>.Fail("auth_not_signed_in");

            string tableName;
            try { tableName = DataSchema.ResolveTableName<T>(); }
            catch (Exception e) { return SupabaseResult<bool>.Fail("resolve_table_failed:" + e.Message); }

            return await _userDataService.DeleteMyRowAsync(
                accessToken: accessToken,
                accountId: userId,
                tableName: tableName);
        }
    }
}
