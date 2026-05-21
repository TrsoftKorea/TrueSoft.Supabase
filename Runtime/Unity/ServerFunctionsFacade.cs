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
    public sealed class ServerFunctionsFacade
    {
        private readonly SupabaseEdgeFunctionsService _functionsService;
        private readonly Func<SupabaseSession> _sessionGetter;

        public ServerFunctionsFacade(SupabaseEdgeFunctionsService functionsService, Func<SupabaseSession> sessionGetter = null)
        {
            _functionsService = functionsService ?? throw new ArgumentNullException(nameof(functionsService));
            _sessionGetter = sessionGetter;
        }

        public Task<SupabaseResult<SupabaseFunctionResponse>> InvokeRawAsync(
            string functionName,
            object requestBody = null,
            bool requireAuth = true)
        {
            var accessToken = ResolveAccessToken(requireAuth, out var errorCode);
            if (errorCode != null)
                return Task.FromResult(SupabaseResult<SupabaseFunctionResponse>.Fail(errorCode));

            return _functionsService.InvokeRawAsync(functionName, accessToken, requestBody);
        }

        public Task<SupabaseResult<TResponse>> InvokeAsync<TResponse>(
            string functionName,
            object requestBody = null,
            bool requireAuth = true)
        {
            var accessToken = ResolveAccessToken(requireAuth, out var errorCode);
            if (errorCode != null)
                return Task.FromResult(SupabaseResult<TResponse>.Fail(errorCode));

            return _functionsService.InvokeAsync<TResponse>(functionName, accessToken, requestBody);
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

