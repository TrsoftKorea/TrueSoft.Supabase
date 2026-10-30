#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using UnityEngine;

namespace TrueBase.Editor
{
    /// <summary>
    /// iOS 빌드에 푸시 알림 권한(Push Notifications capability · aps-environment)과 UserNotifications 를 넣고,
    /// 앱 델리게이트의 원격 알림 콜백을 켠다(SupabaseSettings "푸시 알림 사용"일 때만). 개발 빌드는 애플의 개발 서버용, 나머지는 운영 서버용으로 표시한다 —
    /// 배포용으로 서명할 때 Xcode 가 프로비저닝 프로파일의 값으로 맞춘다.
    /// </summary>
    public static class PushIosPostProcessor
    {
        [PostProcessBuild(102)]
        public static void OnPostprocessBuild(BuildTarget target, string pathToBuiltProject)
        {
            if (target != BuildTarget.iOS)
                return;
            var settings = PushBuildSettings.Load();
            if (settings == null || !settings.pushEnabled)
                return;

            var pbxPath = PBXProject.GetPBXProjectPath(pathToBuiltProject);
            var project = new PBXProject();
            project.ReadFromFile(pbxPath);
            project.AddFrameworkToProject(project.GetUnityFrameworkTargetGuid(), "UserNotifications.framework", false);
            project.WriteToFile(pbxPath);

            // 애플 로그인 후처리(AppleSignInBuildPostProcessor)와 같은 권한 파일에 더한다.
            var capManager = new ProjectCapabilityManager(pbxPath, "TrueSoftSupabase.entitlements", "Unity-iPhone");
            capManager.AddPushNotifications(EditorUserBuildSettings.development);
            capManager.WriteToFile();

            EnableRemoteNotificationCallbacks(pathToBuiltProject);
        }

        // 유니티 앱 델리게이트는 이 스위치가 1일 때만 기기 토큰을 TrueSoftPush.mm 에 넘긴다. 기본값은 0이고
        // 유니티는 옛 NotificationServices API 를 쓸 때만 켜 준다 — 안 켜면 아이폰 등록이 늘 시간 초과로 끝난다.
        const string RemoteNotificationsOff = "#define UNITY_USES_REMOTE_NOTIFICATIONS 0";
        const string RemoteNotificationsOn = "#define UNITY_USES_REMOTE_NOTIFICATIONS 1";

        static void EnableRemoteNotificationCallbacks(string pathToBuiltProject)
        {
            var path = Path.Combine(pathToBuiltProject, "Classes", "Preprocessor.h");
            var text = File.Exists(path) ? File.ReadAllText(path) : null;
            if (text != null && text.Contains(RemoteNotificationsOn))
                return;
            if (text == null || !text.Contains(RemoteNotificationsOff))
            {
                Debug.LogWarning("[TrueSoft.Push] Classes/Preprocessor.h 에서 UNITY_USES_REMOTE_NOTIFICATIONS 를 찾지 못했습니다 — 아이폰 푸시 등록이 시간 초과로 끝날 수 있습니다.");
                return;
            }
            File.WriteAllText(path, text.Replace(RemoteNotificationsOff, RemoteNotificationsOn));
        }
    }
}
#endif
