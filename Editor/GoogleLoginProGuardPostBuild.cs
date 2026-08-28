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
            // EDM(External Dependency Manager)이 설치돼 있으면 GoogleLoginDependencies.xml을
            // 이미 해석해 반영했다고 신뢰하고 건너뛴다. build.gradle 텍스트에 "androidx.credentials"가
            // 있는지로 판단하던 예전 방식은, EDM이 의존성을 텍스트 선언이 아니라 실제 AAR 파일 배치로
            // 해석하는 경우(흔한 동작) 이 문자열이 안 남아 True Positive를 못 잡고 이중 선언으로
            // 이어져 R8 단계에서 androidx.core 클래스 중복(버전 충돌)을 일으킬 수 있었다.
            if (IsExternalDependencyManagerInstalled()) return;

            const string depsBlock = "dependencies {";
            const string injection =
                "\n    // TrueSoft Supabase SDK - Google Login (CredentialManager)\n" +
                "    implementation 'androidx.credentials:credentials:1.3.0'\n" +
                "    implementation 'androidx.credentials:credentials-play-services-auth:1.3.0'\n" +
                "    implementation 'com.google.android.libraries.identity.googleid:googleid:1.1.1'\n";

            var buildGradle = Path.Combine(path, "build.gradle");
            if (!File.Exists(buildGradle)) return;

            var content = File.ReadAllText(buildGradle);

            // 이미 들어가 있으면(다른 경로로든) 건너뜀
            if (content.Contains("androidx.credentials")) return;

            var idx = content.IndexOf(depsBlock, StringComparison.Ordinal);
            if (idx < 0) return;

            var insertAt = idx + depsBlock.Length;
            content = content.Substring(0, insertAt) + injection + content.Substring(insertAt);
            File.WriteAllText(buildGradle, content);
        }

        /// <summary>External Dependency Manager for Unity(Play Services Resolver)가 프로젝트에 설치돼 있는지 확인합니다.</summary>
        private static bool IsExternalDependencyManagerInstalled()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetType("GooglePlayServices.PlayServicesResolver") != null)
                    return true;
            }
            return false;
        }
    }
}
#endif
