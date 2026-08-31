#if UNITY_ANDROID
using System;
using System.IO;
using UnityEditor.Android;

namespace TrueBase.Editor
{
    /// <summary>
    /// Android 빌드 시 Gradle 프로젝트에 자동으로 주입합니다.
    ///  1. proguard-user.txt — googleloginplugin keep 규칙
    ///  2. build.gradle(unityLibrary 모듈) — CredentialManager 의존성 (EDM 미설치 프로젝트 대응)
    ///  3. build.gradle(최상위 프로젝트) — androidx.core·Kotlin stdlib 버전 강제 고정
    /// </summary>
    internal class GoogleLoginProGuardPostBuild : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 1;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            InjectProGuardRule(path);
            InjectGradleDependencies(path);
            InjectRootResolutionStrategy(path);
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

            // 1.3.0 이상은 Unity 2022.3(JDK 11 고정)에서 R8 단계가 깨지는 사례가 보고돼
            // 1.2.2(2024-04, 마지막 1.2.x 안정 버전)로 낮춰 둔다 — GoogleLoginDependencies.xml과 동일 버전 유지.
            const string depsBlock = "dependencies {";
            const string injection =
                "\n    // TrueSoft Supabase SDK - Google Login (CredentialManager)\n" +
                "    implementation 'androidx.credentials:credentials:1.2.2'\n" +
                "    implementation 'androidx.credentials:credentials-play-services-auth:1.2.2'\n" +
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

        /// <summary>
        /// androidx.core·androidx.core-ktx·Kotlin stdlib 버전을 최상위 build.gradle에서 강제 고정한다.
        /// Unity 2022.3(JDK 11 고정) 환경에서 androidx.core 1.15.0(AGP 8 요구)이 R8 단계를 깨뜨리는
        /// 사례가 실사용자 빌드에서 확인됨 — 개별 라이브러리 버전을 낮추는 것만으로는 다른 의존성이
        /// 여전히 최신 core를 요구하면 Gradle이 그쪽을 택해 무력화된다. resolutionStrategy.force는
        /// 누가 어떤 버전을 요청하든 지정한 버전으로 덮어써 이 경합 자체를 없앤다.
        /// EDM 설치 여부와 무관하게 항상 적용한다 — EDM이 해석한 버전도 이 강제 고정 대상이다.
        /// </summary>
        private static void InjectRootResolutionStrategy(string unityLibraryPath)
        {
            const string marker = "TrueSoft Supabase SDK - androidx.core force";
            var rootBuildGradle = Path.Combine(unityLibraryPath, "..", "build.gradle");
            if (!File.Exists(rootBuildGradle)) return;

            var content = File.ReadAllText(rootBuildGradle);
            if (content.Contains(marker)) return;

            const string block =
                "\n// " + marker + " — Unity 2022.3(JDK 11) 툴체인이 androidx.core 1.15.0+(AGP 8 요구)을\n" +
                "// 처리하지 못해 R8 단계에서 깨지는 것을 막는다. 실사용자 빌드로 검증된 조합.\n" +
                "allprojects {\n" +
                "    configurations.all {\n" +
                "        resolutionStrategy {\n" +
                "            force 'androidx.core:core:1.13.1'\n" +
                "            force 'androidx.core:core-ktx:1.13.1'\n" +
                "            force 'org.jetbrains.kotlin:kotlin-stdlib:1.8.22'\n" +
                "            force 'org.jetbrains.kotlin:kotlin-stdlib-jdk7:1.8.22'\n" +
                "            force 'org.jetbrains.kotlin:kotlin-stdlib-jdk8:1.8.22'\n" +
                "        }\n" +
                "    }\n" +
                "}\n";

            File.AppendAllText(rootBuildGradle, block);
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
