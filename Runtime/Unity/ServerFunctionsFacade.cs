using System;
using System.Threading.Tasks;
using TrueBase.Core.Auth;
using TrueBase.Core.Common;
using TrueBase.Core.Data;

namespace TrueBase.Unity
{
    /// <summary>
    /// Edge Function 호출 퍼사드.
    /// requireAuth=true면 현재 세션(access_token)이 필요합니다.
    /// </summary>
    internal sealed class ServerFunctionsFacade
    {
        private readonly SupabaseEdgeFunctionsService _functionsService;
        private readonly Func<SupabaseSession> _sessionGetter;

        public ServerFunctionsFacade(SupabaseEdgeFunctionsService functionsService, Func<SupabaseSession> sessionGetter = null)
        {
            _functionsService = functionsService ?? throw new ArgumentNullException(nameof(functionsService));
            _sessionGetter = sessionGetter;
        }

        public async Task<SupabaseResult<SupabaseFunctionResponse>> InvokeRawAsync(
            string functionName,
            object requestBody = null,
            bool requireAuth = true)
        {
            var gate = requireAuth ? await SupabaseSDK.RefreshIfNearExpiryAsync() : null;
            if (gate != null)
                return SupabaseResult<SupabaseFunctionResponse>.Fail(gate);

            var accessToken = ResolveAccessToken(requireAuth, out var errorCode);
            if (errorCode != null)
                return SupabaseResult<SupabaseFunctionResponse>.Fail(errorCode);

            return await _functionsService.InvokeRawAsync(functionName, accessToken, requestBody);
        }

        public async Task<SupabaseResult<TResponse>> InvokeAsync<TResponse>(
            string functionName,
            object requestBody = null,
            bool requireAuth = true)
        {
            // 결제 확인처럼 결제 창에서 돌아온 직후 부르는 호출이 많다 — 그사이 끝난 토큰을 보내지 않게 먼저 갱신한다.
            var gate = requireAuth ? await SupabaseSDK.RefreshIfNearExpiryAsync() : null;
            if (gate != null)
                return SupabaseResult<TResponse>.Fail(gate);

            var accessToken = ResolveAccessToken(requireAuth, out var errorCode);
            if (errorCode != null)
                return SupabaseResult<TResponse>.Fail(errorCode);

            return await _functionsService.InvokeAsync<TResponse>(functionName, accessToken, requestBody);
        }

        public Task<SupabaseResult<TResponse>> InvokeAsSessionAsync<TResponse>(
            SupabaseSession session,
            string functionName,
            object requestBody = null)
        {
            if (session == null || string.IsNullOrWhiteSpace(session.AccessToken))
                return Task.FromResult(SupabaseResult<TResponse>.Fail("auth_not_signed_in"));

            return _functionsService.InvokeAsync<TResponse>(functionName, session.AccessToken, requestBody);
        }

        private string ResolveAccessToken(bool requireAuth, out string errorCode)
        {
            errorCode = null;

            if (requireAuth == false)
                return null;

            var session = _sessionGetter?.Invoke();
            if (session == null || string.IsNullOrWhiteSpace(session.AccessToken))
            {
                errorCode = "auth_not_signed_in";
                return null;
            }

            return session.AccessToken;
        }
    }
}

