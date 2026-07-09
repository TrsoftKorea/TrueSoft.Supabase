using System;
using UnityEngine;
using TrueBase.Core.Auth;
using TrueBase.Core.Data;
using TrueBase.Core.Http;
using TrueBase.Unity;

namespace TrueBase.Unity.Config
{
    /// <summary>
    /// <see cref="SupabaseSettings"/>로부터 Core 계층 서비스들을 생성해 <see cref="SupabaseSDK"/>에 주입하는 부트스트랩입니다.
    /// <see cref="SupabaseRuntime"/> 또는 자동 부트스트랩이 사용합니다.
    /// </summary>
    public sealed class SupabaseUnityBootstrap
    {
        /// <summary>현재 부트스트랩이 사용 중인 프로젝트 URL (동일 URL로 재초기화될 때 기존 로그인 세션 유지에 사용).</summary>
        public string ProjectUrl { get; private set; }
        public bool EnableApiResultLogs { get; private set; } = true;

        public SupabaseAuthService AuthService { get; private set; }
        public SupabaseUserDataService UserDataService { get; private set; }
        public SupabaseRemoteConfigService RemoteConfigService { get; private set; }
        public SupabasePublicProfileService PublicProfileService { get; private set; }
        public SupabaseEdgeFunctionsService EdgeFunctionsService { get; private set; }
        public SupabaseUserSessionService UserSessionService { get; private set; }
        public SupabaseAnonymousRecoveryService AnonymousRecoveryService { get; private set; }
        public SupabaseServerTimeService ServerTimeService { get; private set; }
        public SupabaseMailboxService MailboxService { get; private set; }
        /// <summary><see cref="SupabaseSettings.duplicateSessionPollSeconds"/>.</summary>
        public float DuplicateSessionPollSeconds { get; private set; }

        /// <summary><see cref="SupabaseSettings.duplicateSessionActionCheckCooldownSeconds"/>.</summary>
        public float DuplicateSessionActionCheckCooldownSeconds { get; private set; }

        /// <summary><see cref="SupabaseSettings.withdrawalRequestDelayDays"/>.</summary>
        public float WithdrawalRequestDelayDays { get; private set; }

        public string DefaultServerCode { get; private set; } = "GLOBAL";

        /// <summary>
        /// 설정을 검증하고 모든 서비스를 생성한 뒤 <see cref="SupabaseSDK.Initialize"/>를 호출합니다.
        /// </summary>
        /// <param name="settings">로드된 <see cref="SupabaseSettings"/> 에셋. null이거나 projectUrl·publishableKey가 유효하지 않으면 예외를 던집니다.</param>
        public void Initialize(SupabaseSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            var options = settings.ToOptions();

            // 설정 유효성 검사
            if (string.IsNullOrWhiteSpace(options.ProjectURL) ||
                !Uri.TryCreate(options.ProjectURL, UriKind.Absolute, out _))
                throw new ArgumentException($"[Supabase] 유효하지 않은 projectUrl: '{options.ProjectURL}'");

            if (string.IsNullOrWhiteSpace(options.PublishableKey))
                throw new ArgumentException("[Supabase] publishableKey가 비어 있습니다.");

            if (options.TimeoutSeconds < 1 || options.TimeoutSeconds > 300)
                Debug.LogWarning($"[Supabase] TimeoutSeconds({options.TimeoutSeconds})가 권장 범위(1-300)를 벗어납니다.");

            ProjectUrl = options.ProjectURL ?? string.Empty;
            EnableApiResultLogs = settings.enableApiResultLogs;

            var http = new UnitySupabaseHttpClient(options.TimeoutSeconds);
            var json = new UnitySupabaseJsonSerializer();

            AuthService = new SupabaseAuthService(
                options.ProjectURL,
                options.PublishableKey,
                http,
                json);

            UserDataService = new SupabaseUserDataService(
                options.ProjectURL,
                options.PublishableKey,
                http,
                json);

            RemoteConfigService = new SupabaseRemoteConfigService(
                options.ProjectURL,
                options.PublishableKey,
                http,
                "remote_config");

            PublicProfileService = new SupabasePublicProfileService(
                options.ProjectURL,
                options.PublishableKey,
                http,
                json,
                "user_profiles",
                displayNamesTable: "display_names",
                defaultServerCode: options.DefaultServerCode);

            EdgeFunctionsService = new SupabaseEdgeFunctionsService(
                options.ProjectURL,
                options.PublishableKey,
                http,
                json);

            UserSessionService = new SupabaseUserSessionService(
                options.ProjectURL,
                options.PublishableKey,
                http,
                json,
                "user_sessions");

            AnonymousRecoveryService = new SupabaseAnonymousRecoveryService(
                options.ProjectURL,
                options.PublishableKey,
                http,
                json);

            ServerTimeService = new SupabaseServerTimeService(
                options.ProjectURL,
                options.PublishableKey,
                http,
                json);

            MailboxService = new SupabaseMailboxService(
                options.ProjectURL,
                options.PublishableKey,
                http,
                "mails");

            DuplicateSessionPollSeconds = settings.duplicateSessionPollSeconds;
            DuplicateSessionActionCheckCooldownSeconds = options.DuplicateSessionActionCheckCooldownSeconds;
            WithdrawalRequestDelayDays = options.WithdrawalRequestDelayDays;
            DefaultServerCode = string.IsNullOrWhiteSpace(options.DefaultServerCode) ? "GLOBAL" : options.DefaultServerCode.Trim();
            SupabaseSDK.Initialize(this);
        }
    }
}