#if UNITY_IOS
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

namespace TrueBase.Editor
{
    /// <summary>
    /// iOS 빌드 시 Xcode 프로젝트에 "Sign in with Apple" Capability(entitlement)를 자동으로 추가합니다.
    /// 네이티브 Apple 로그인(<c>Supabase.TrySignInWithAppleAsync</c>)에 필요합니다.
    /// </summary>
    public static class AppleSignInBuildPostProcessor
    {
        [PostProcessBuild(100)]
        public static void OnPostprocessBuild(BuildTarget target, string pathToBuiltProject)
        {
            if (target != BuildTarget.iOS)
                return;

            var pbxPath = PBXProject.GetPBXProjectPath(pathToBuiltProject);
            const string entitlementFileName = "TrueSoftSupabase.entitlements";

            // 메인 앱 타겟에 Sign in with Apple capability를 추가합니다.
            var capManager = new ProjectCapabilityManager(
                pbxPath,
                entitlementFileName,
                "Unity-iPhone");

            capManager.AddSignInWithApple();
            capManager.WriteToFile();
        }
    }
}
#endif
