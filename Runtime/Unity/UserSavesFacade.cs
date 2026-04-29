using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Truesoft.Supabase.Core.Auth;
using Truesoft.Supabase.Core.Common;
using Truesoft.Supabase.Core.Data;

namespace Truesoft.Supabase.Unity
{
    public sealed class UserSavesFacade
    {
        private readonly SupabaseUserDataService _userDataService;
        private readonly Func<SupabaseSession> _sessionGetter;

        public UserSavesFacade(SupabaseUserDataService userDataService, Func<SupabaseSession> sessionGetter = null)
        {
            _userDataService = userDataService ?? throw new ArgumentNullException(nameof(userDataService));
            _sessionGetter = sessionGetter;
        }

        /// <summary>
        /// 로그인 직후 <typeparamref name="T"/>의 <see cref="DataTableAttribute"/> 테이블에 본인 행이 존재하도록 보장합니다.
        /// DB RPC: <c>ts_ensure_my_row(table, user_id)</c>.
        /// </summary>
        public Task<SupabaseResult<bool>> EnsureMyRowAsync<T>()
        {
            var session = _sessionGetter?.Invoke();
            return EnsureMyRowAsync<T>(session);
        }

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

        /// <summary>
        /// 프로젝트별 명시 컬럼을 select로 지정해 로드합니다.
        /// </summary>
        public Task<SupabaseResult<T>> LoadColumnsAsync<T>(string tableName, string selectColumnsCsv) where T : class, new()
        {
            var session = _sessionGetter?.Invoke();
            return LoadColumnsAsync<T>(session, tableName, selectColumnsCsv);
        }

        public async Task<SupabaseResult<T>> LoadColumnsAsync<T>(
            SupabaseSession session,
            string tableName,
            string selectColumnsCsv) where T : class, new()
        {
            if (session == null)
                return SupabaseResult<T>.Fail("session_null");

            var accessToken = session.AccessToken;
            var userId = session.User?.Id;

            if (string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(userId))
                return SupabaseResult<T>.Fail("auth_not_signed_in");

            return await _userDataService.LoadColumnsAsync<T>(
                accessToken: accessToken,
                accountId: userId,
                tableName: tableName,
                selectColumnsCsv: selectColumnsCsv);
        }

        /// <inheritdoc cref="SupabaseUserDataService.LoadColumnsWithRowStateAsync{T}(string, string, string, string)"/>
        public Task<SupabaseResult<DataColumnsLoadResult<T>>> LoadColumnsWithRowStateAsync<T>(
            string tableName,
            string selectColumnsCsv) where T : class, new()
        {
            var session = _sessionGetter?.Invoke();
            return LoadColumnsWithRowStateAsync<T>(session, tableName, selectColumnsCsv);
        }

        public async Task<SupabaseResult<DataColumnsLoadResult<T>>> LoadColumnsWithRowStateAsync<T>(
            SupabaseSession session,
            string tableName,
            string selectColumnsCsv) where T : class, new()
        {
            if (session == null)
                return SupabaseResult<DataColumnsLoadResult<T>>.Fail("session_null");

            var accessToken = session.AccessToken;
            var userId = session.User?.Id;

            if (string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(userId))
                return SupabaseResult<DataColumnsLoadResult<T>>.Fail("auth_not_signed_in");

            return await _userDataService.LoadColumnsWithRowStateAsync<T>(
                accessToken: accessToken,
                accountId: userId,
                tableName: tableName,
                selectColumnsCsv: selectColumnsCsv);
        }

        /// <summary>
        /// <see cref="DataColumnAttribute"/>로 표시한 컬럼만 모아 로드합니다.
        /// 대상 타입에 <see cref="DataTableAttribute"/>가 필요합니다.
        /// </summary>
        public Task<SupabaseResult<T>> LoadAttributedAsync<T>(bool includeUpdatedAt = true) where T : class, new()
        {
            var session = _sessionGetter?.Invoke();
            return LoadAttributedAsync<T>(session, includeUpdatedAt);
        }

        public async Task<SupabaseResult<T>> LoadAttributedAsync<T>(SupabaseSession session, bool includeUpdatedAt = true) where T : class, new()
        {
            if (session == null)
                return SupabaseResult<T>.Fail("session_null");

            var accessToken = session.AccessToken;
            var userId = session.User?.Id;

            if (string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(userId))
                return SupabaseResult<T>.Fail("auth_not_signed_in");

            return await _userDataService.LoadAttributedAsync<T>(
                accessToken: accessToken,
                accountId: userId,
                includeUpdatedAt: includeUpdatedAt);
        }

        /// <inheritdoc cref="SupabaseUserDataService.LoadAttributedWithRowStateAsync{T}(string, string, bool)"/>
        public Task<SupabaseResult<DataColumnsLoadResult<T>>> LoadAttributedWithRowStateAsync<T>(
            bool includeUpdatedAt = true) where T : class, new()
        {
            var session = _sessionGetter?.Invoke();
            return LoadAttributedWithRowStateAsync<T>(session, includeUpdatedAt);
        }

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
        /// 대상 타입에 <see cref="DataTableAttribute"/>가 필요합니다.
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
    }
}
