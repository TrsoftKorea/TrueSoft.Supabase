#if UNITY_ANDROID
using System.IO;
using UnityEditor.Android;

namespace TrueBase.Editor
{
    /// <summary>
    /// Android 빌드 시 Gradle 프로젝트에 googleloginplugin ProGuard keep 규칙을 자동으로 주입합니다.
    /// </summary>
    internal class GoogleLoginProGuardPostBuild : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 1;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            const string marker  = "com.truesoft.googleloginplugin";
            const string keepRule =
                "\n# TrueSoft Supabase SDK - Google Login\n" +
                "-keep class com.truesoft.googleloginplugin.** { *; }\n";

            var proguardFile = Path.Combine(path, "proguard-user.txt");

            if (File.Exists(proguardFile))
            {
                var content = File.ReadAllText(proguardFile);
                if (!content.Contains(marker))
                    File.AppendAllText(proguardFile, keepRule);
            }
            else
            {
                File.WriteAllText(proguardFile, keepRule);
            }
        }
    }
}
#endif
