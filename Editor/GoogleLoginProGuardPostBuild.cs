#if UNITY_ANDROID
using System;
using System.IO;
using UnityEditor.Android;

namespace TrueBase.Editor
{
    /// <summary>
    /// Android 빌드 시 Gradle 프로젝트에 자동으로 주입합니다.
    ///  1. proguard-user.txt — googleloginplugin keep 규칙
    ///  2. build.gradle — CredentialManager 의존성 (EDM 미설치 프로젝트 대응)
    /// </summary>
    internal class GoogleLoginProGuardPostBuild : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 1;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            InjectProGuardRule(path);
            InjectGradleDependencies(path);
        }


        private static void InjectProGuardRule(string path)
        {
            const string marker = "com.truesoft.googleloginplugin";
            const string rule =
                "\n# TrueSoft Supabase SDK - Google Login\n" +
                "-keep class com.truesoft.googleloginplugin.** { *; }\n";

            var file = Path.Combine(path, "proguard-user.txt");
            if (File.Exists(file))
            {
                if (!File.ReadAllText(file).Contains(marker))
                    File.AppendAllText(file, rule);
            }
            else
            {
                File.WriteAllText(file, rule);
            }
        }


        private static void InjectGradleDependencies(string path)
        {
            const string credentialsMarker = "androidx.credentials";
            const string depsBlock        = "dependencies {";
            const string injection =
                "\n    // TrueSoft Supabase SDK - Google Login (CredentialManager)\n" +
                "    implementation 'androidx.credentials:credentials:1.3.0'\n" +
                "    implementation 'androidx.credentials:credentials-play-services-auth:1.3.0'\n" +
                "    implementation 'com.google.android.libraries.identity.googleid:googleid:1.1.1'\n";

            var buildGradle = Path.Combine(path, "build.gradle");
            if (!File.Exists(buildGradle)) return;

            var content = File.ReadAllText(buildGradle);

            // EDM 등으로 이미 포함된 경우 건너뜀
            if (content.Contains(credentialsMarker)) return;

            var idx = content.IndexOf(depsBlock, StringComparison.Ordinal);
            if (idx < 0) return;

            var insertAt = idx + depsBlock.Length;
            content = content.Substring(0, insertAt) + injection + content.Substring(insertAt);
            File.WriteAllText(buildGradle, content);
        }
    }
}
#endif
