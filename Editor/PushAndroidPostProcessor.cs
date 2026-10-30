#if UNITY_ANDROID
using System.IO;
using System.Security;
using System.Xml;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEngine;

namespace TrueBase.Editor
{
    /// <summary>
    /// Android 빌드에 푸시 알림 부품을 넣는다(SupabaseSettings "푸시 알림 사용"일 때만).
    ///  1. firebase-messaging 의존성(unityLibrary build.gradle)
    ///  2. 기기 등록 코드(Editor/Push 의 .java.txt → unityLibrary/src/main/java)
    ///  3. 등록을 받는 서비스와 설치 ID 등록 사용 표시(AndroidManifest)
    ///  4. Firebase 값 문자열 리소스 — 앱이 뜰 때 기본 Firebase 가 저절로 시작되게(알림으로 앱이 새로 뜰 때도 필요)
    /// 게임에 Firebase 유니티 기본 패키지가 있으면 4 는 그쪽 것을 쓴다(같은 이름을 두 번 선언하면 빌드가 깨진다).
    /// Firebase 유니티 메시징이 있으면 빌드를 멈춘다 — 이유는 OnPostGenerateGradleAndroidProject 주석.
    /// 푸시를 끄면 지난 빌드가 남긴 2·4 를 지운다 — Unity 는 빌드 폴더를 다시 쓰므로 안 지우면 Firebase 없이 컴파일하다 깨진다.
    /// </summary>
    internal sealed class PushAndroidPostProcessor : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 2;

        // firebase-messaging 25.1.0 부터 설치 ID 등록(register·onRegistered)이 있다. 25.1.3 = BoM 34.19.0, 최소 API 23.
        // 26.x 는 최소 API 24 로 올라가 더 많은 기기를 버린다.
        private const string MessagingDependency = "com.google.firebase:firebase-messaging:25.1.3";
        private const string AndroidNs = "http://schemas.android.com/apk/res/android";
        private const string ServiceName = "com.truesoft.supabase.push.TrueSoftPushService";
        private const string ValuesFile = "truesoft_firebase.xml";

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            var settings = PushBuildSettings.Load();
            var javaDir = Path.Combine(path, "src", "main", "java", "com", "truesoft", "supabase", "push");
            var valuesPath = Path.Combine(path, "src", "main", "res", "values", ValuesFile);

            if (settings == null || !settings.pushEnabled)
            {
                if (Directory.Exists(javaDir)) Directory.Delete(javaDir, true);
                if (File.Exists(valuesPath)) File.Delete(valuesPath);
                return;
            }

            // Firebase 유니티 메시징은 같은 신호(MESSAGING_EVENT)를 받는 서비스를 따로 넣는다 — 안드로이드는 그중 하나에만
            // 전달해 어느 쪽 알림·등록이 끊길지 모른다. 그 패키지의 메시징 부품은 24.x 라 설치 ID 등록도 없다(12.10.1 확인).
            if (PushBuildSettings.HasFirebaseUnityMessaging())
                throw new BuildFailedException("[TrueSoft.Supabase] 게임에 Firebase 유니티 메시징이 있어 SDK 푸시 알림과 함께 쓸 수 없습니다. "
                    + "SupabaseSettings 의 푸시 알림 사용을 끄거나 Firebase 메시징 패키지를 빼 주세요.");

            InjectDependency(path);
            if (PushBuildSettings.HasFirebaseUnityApp())
            {
                if (File.Exists(valuesPath)) File.Delete(valuesPath);
            }
            else
                WriteFirebaseValues(settings, valuesPath);

            Directory.CreateDirectory(javaDir);
            File.WriteAllText(Path.Combine(javaDir, "TrueSoftPush.java"), PushBuildSettings.ReadTemplate("TrueSoftPush.java.txt"));
            File.WriteAllText(Path.Combine(javaDir, "TrueSoftPushService.java"), PushBuildSettings.ReadTemplate("TrueSoftPushService.java.txt"));
            InjectManifest(path);
            RaiseKotlinPin(path);
        }

        // 구글 로그인 후처리(GoogleLoginProGuardPostBuild, 순서 1)가 최상위 build.gradle 에서 kotlin-stdlib 를 1.8.22 로 강제 고정한다.
        // firebase-messaging 25.1.3 은 kotlin-stdlib 2.0.21 을 요구해(메이븐 POM 으로 확인) 그대로 두면 낮은 판으로 깔려
        // 기기에서 실행 중 오류가 날 수 있다. 푸시를 켠 게임만 그 고정을 2.0.21 로 올린다 — 빌드 도구가 못 받아들이면
        // 빌드가 실패해 바로 드러난다(실행 중 오류보다 낫다). 확인 못 함: 실제 빌드(2022.3·6000).
        private const string KotlinForFirebase = "2.0.21";

        private static void RaiseKotlinPin(string unityLibraryPath)
        {
            var rootBuildGradle = Path.Combine(unityLibraryPath, "..", "build.gradle");
            if (!File.Exists(rootBuildGradle)) return;
            var content = File.ReadAllText(rootBuildGradle);
            var raised = content;
            foreach (var artifact in new[] { "kotlin-stdlib", "kotlin-stdlib-jdk7", "kotlin-stdlib-jdk8" })
                raised = raised.Replace($"force 'org.jetbrains.kotlin:{artifact}:1.8.22'", $"force 'org.jetbrains.kotlin:{artifact}:{KotlinForFirebase}'");
            if (raised != content)
            {
                File.WriteAllText(rootBuildGradle, raised);
                Debug.Log($"[TrueSoft.Supabase] 푸시 알림(Firebase)에 맞춰 kotlin-stdlib 고정을 {KotlinForFirebase} 로 올렸습니다.");
            }
            // 위 글자와 다른 꼴로 고정돼 있으면(구글 로그인 후처리 문구가 바뀌었거나 게임이 직접 고정) 낮은 판이 그대로 깔린다.
            else if (content.Contains("force 'org.jetbrains.kotlin:kotlin-stdlib") && !content.Contains($"kotlin-stdlib:{KotlinForFirebase}'"))
            {
                Debug.LogWarning($"[TrueSoft.Supabase] 최상위 build.gradle 이 kotlin-stdlib 를 다른 판으로 고정하고 있어 푸시 알림(Firebase)이 요구하는 {KotlinForFirebase} 로 올리지 못했습니다. 기기에서 실행 중 오류가 날 수 있습니다.");
            }
        }

        private static void InjectDependency(string path)
        {
            var buildGradle = Path.Combine(path, "build.gradle");
            if (!File.Exists(buildGradle)) return;
            var content = File.ReadAllText(buildGradle);
            if (content.Contains("firebase-messaging")) return;
            const string depsBlock = "dependencies {";
            var idx = content.IndexOf(depsBlock, System.StringComparison.Ordinal);
            if (idx < 0)
            {
                Debug.LogError("[TrueSoft.Supabase] build.gradle 에 dependencies 블록이 없어 푸시 부품을 넣지 못했습니다.");
                return;
            }
            var insertAt = idx + depsBlock.Length;
            content = content.Substring(0, insertAt)
                + "\n    // TrueSoft Supabase SDK - Push\n    implementation '" + MessagingDependency + "'\n"
                + content.Substring(insertAt);
            File.WriteAllText(buildGradle, content);
        }

        private static void WriteFirebaseValues(TrueBase.Unity.SupabaseSettings s, string valuesPath)
        {
            if (string.IsNullOrWhiteSpace(s.firebaseAndroidAppId) || string.IsNullOrWhiteSpace(s.firebaseApiKey)
                || string.IsNullOrWhiteSpace(s.firebaseProjectId) || string.IsNullOrWhiteSpace(s.firebaseSenderId))
            {
                Debug.LogError("[TrueSoft.Supabase] 푸시 알림을 켰지만 SupabaseSettings 의 Firebase 값(프로젝트 ID·Android 앱 ID·웹 API 키·발신자 ID)이 비어 있습니다. Android 푸시가 동작하지 않습니다.");
                return;
            }
            string Item(string name, string value) =>
                $"    <string name=\"{name}\" translatable=\"false\">{SecurityElement.Escape(value.Trim())}</string>\n";
            Directory.CreateDirectory(Path.GetDirectoryName(valuesPath));
            File.WriteAllText(valuesPath,
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<!-- TrueSoft Supabase SDK - Push: SupabaseSettings 의 Firebase 값 -->\n<resources>\n"
                + Item("google_app_id", s.firebaseAndroidAppId)
                + Item("google_api_key", s.firebaseApiKey)
                + Item("project_id", s.firebaseProjectId)
                + Item("gcm_defaultSenderId", s.firebaseSenderId)
                + "</resources>\n");
        }

        private static void InjectManifest(string path)
        {
            var manifestPath = Path.Combine(path, "src", "main", "AndroidManifest.xml");
            if (!File.Exists(manifestPath)) return;
            var doc = new XmlDocument();
            doc.Load(manifestPath);
            var manifest = doc.DocumentElement;
            if (manifest == null) return;
            var app = manifest["application"];
            if (app == null)
            {
                app = doc.CreateElement("application");
                manifest.AppendChild(app);
            }

            var hasService = false;
            foreach (XmlElement e in app.GetElementsByTagName("service"))
                if (e.GetAttribute("name", AndroidNs) == ServiceName) hasService = true;
            if (!hasService)
            {
                var service = doc.CreateElement("service");
                service.SetAttribute("name", AndroidNs, ServiceName);
                service.SetAttribute("exported", AndroidNs, "false");
                var filter = doc.CreateElement("intent-filter");
                var action = doc.CreateElement("action");
                action.SetAttribute("name", AndroidNs, "com.google.firebase.MESSAGING_EVENT");
                filter.AppendChild(action);
                service.AppendChild(filter);
                app.AppendChild(service);
            }

            var hasMeta = false;
            foreach (XmlElement e in app.GetElementsByTagName("meta-data"))
                if (e.GetAttribute("name", AndroidNs) == "firebase_messaging_installation_id_enabled") hasMeta = true;
            if (!hasMeta)
            {
                var meta = doc.CreateElement("meta-data");
                meta.SetAttribute("name", AndroidNs, "firebase_messaging_installation_id_enabled");
                meta.SetAttribute("value", AndroidNs, "true");
                app.AppendChild(meta);
            }
            doc.Save(manifestPath);
        }
    }
}
#endif
