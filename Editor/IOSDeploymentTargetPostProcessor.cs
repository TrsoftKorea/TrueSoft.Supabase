#if UNITY_IOS
using System;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using UnityEngine;

namespace TrueBase.Editor
{
    /// <summary>
    /// iOS 빌드 후 Xcode 프로젝트의 IPHONEOS_DEPLOYMENT_TARGET을 자동으로 15.0 이상으로 설정합니다.
    /// StoreKit 2(JWS) 검증은 iOS 15 이상 필수이므로 SDK가 자동으로 적용합니다.
    /// 프로젝트 설정값이 이미 15.0 이상이면 변경하지 않습니다.
    /// </summary>
    internal static class IOSDeploymentTargetPostProcessor
    {
        private const string MinimumIOSVersion = "15.0";

        [PostProcessBuild(100)]
        public static void OnPostProcessBuild(BuildTarget buildTarget, string buildPath)
        {
            if (buildTarget != BuildTarget.iOS) return;

            var projPath = PBXProject.GetPBXProjectPath(buildPath);
            var proj     = new PBXProject();
            proj.ReadFromFile(projPath);

            bool changed = false;

            foreach (var targetGuid in new[]
            {
                proj.GetUnityMainTargetGuid(),
                proj.GetUnityFrameworkTargetGuid(),
            })
            {
                var current = proj.GetBuildPropertyForAnyConfig(targetGuid, "IPHONEOS_DEPLOYMENT_TARGET");
                if (!string.IsNullOrEmpty(current) && IsVersionAtLeast(current, MinimumIOSVersion))
                    continue;

                proj.SetBuildProperty(targetGuid, "IPHONEOS_DEPLOYMENT_TARGET", MinimumIOSVersion);
                changed = true;
            }

            if (changed)
            {
                proj.WriteToFile(projPath);
                Debug.Log($"[TrueBase.Supabase] IPHONEOS_DEPLOYMENT_TARGET을 {MinimumIOSVersion}으로 설정했습니다. (StoreKit 2 필수 사양)");
            }
        }

        private static bool IsVersionAtLeast(string version, string minimum)
        {
            try
            {
                // "15" → "15.0" 형태로 보정
                if (!version.Contains('.')) version += ".0";
                if (!minimum.Contains('.')) minimum += ".0";
                return Version.Parse(version) >= Version.Parse(minimum);
            }
            catch
            {
                return false;
            }
        }
    }
}
#endif // UNITY_IOS
