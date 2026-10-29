#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

namespace TrueBase.Editor
{
    /// <summary>
    /// iOS 빌드에 고객센터 창(<c>Supabase.OpenCustomerCenterAsync</c>)이 쓰는 것을 넣습니다.
    /// 창이 WKWebView 라 WebKit 을 링크하고, 그림을 붙일 때 iOS 가 "사진 찍기"도 내보이므로
    /// 카메라 사용 문구가 없으면 기본 문구를 넣습니다 — 문구 없이 카메라를 고르면 앱이 바로 꺼집니다.
    /// 게임이 이미 문구를 넣어 두었으면 그대로 둡니다.
    /// </summary>
    public static class CustomerCenterBuildPostProcessor
    {
        [PostProcessBuild(101)]
        public static void OnPostprocessBuild(BuildTarget target, string pathToBuiltProject)
        {
            if (target != BuildTarget.iOS)
                return;

            var pbxPath = PBXProject.GetPBXProjectPath(pathToBuiltProject);
            var project = new PBXProject();
            project.ReadFromFile(pbxPath);
            project.AddFrameworkToProject(project.GetUnityFrameworkTargetGuid(), "WebKit.framework", false);
            project.WriteToFile(pbxPath);

            var plistPath = Path.Combine(pathToBuiltProject, "Info.plist");
            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);
            if (!plist.root.values.ContainsKey("NSCameraUsageDescription"))
            {
                plist.root.SetString("NSCameraUsageDescription", "고객센터 문의에 붙일 사진을 찍는 데 사용합니다.");
                plist.WriteToFile(plistPath);
            }
        }
    }
}
#endif
