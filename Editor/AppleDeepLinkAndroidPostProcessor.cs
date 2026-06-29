#if UNITY_ANDROID
using System.IO;
using System.Xml;
using UnityEditor;
using UnityEditor.Android;
using UnityEngine;

namespace TrueBase.Editor
{
    /// <summary>
    /// Android 빌드 시, 런처 액티비티에 딥링크 intent-filter(스킴 = 앱 패키지 이름)를 자동으로 추가하고
    /// <c>launchMode=singleTop</c>으로 설정합니다. Android Apple 로그인
    /// (<c>Supabase.TrySignInWithAppleAsync</c>)의 콜백 딥링크 수신에 필요합니다.
    /// 스킴이 패키지 이름이라 전역 고유하고, <c>singleTop</c>은 기존 동작을 거의 바꾸지 않아 항상 적용합니다.
    /// </summary>
    public sealed class AppleDeepLinkAndroidPostProcessor : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 100;

        private const string AndroidNs = "http://schemas.android.com/apk/res/android";

        // 런타임(SupabaseSDK.AppleAndroidRedirectHost)과 동일해야 합니다.
        private const string RedirectHost = "login-callback";

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            // 딥링크 스킴 = 앱 패키지 이름(전역 고유). 런타임도 Application.identifier로 같은 값을 사용합니다.
            var scheme = PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.Android);
            if (string.IsNullOrEmpty(scheme))
                return;

            var host = RedirectHost;

            var manifestPath = Path.Combine(path, "src", "main", "AndroidManifest.xml");
            if (!File.Exists(manifestPath))
                return;

            var doc = new XmlDocument();
            doc.Load(manifestPath);

            var launcher = FindLauncherActivity(doc);
            if (launcher == null)
            {
                Debug.LogWarning("[TrueSoft.Supabase] 런처 액티비티를 찾지 못해 Apple 딥링크 intent-filter를 추가하지 못했습니다.");
                return;
            }

            // 딥링크 콜백이 새 인스턴스를 만들지 않고 기존(맨 위) 액티비티로 전달되도록 합니다.
            // singleTop은 "맨 위면 재사용"만 바꾸므로 기존 동작에 미치는 영향이 거의 없습니다.
            launcher.SetAttribute("launchMode", AndroidNs, "singleTop");

            // 이미 같은 스킴이 있으면 중복 추가하지 않습니다.
            foreach (XmlElement data in launcher.GetElementsByTagName("data"))
            {
                if (data.GetAttribute("scheme", AndroidNs) == scheme)
                    return;
            }

            var filter = doc.CreateElement("intent-filter");
            AppendNamed(doc, filter, "action", "android.intent.action.VIEW");
            AppendNamed(doc, filter, "category", "android.intent.category.DEFAULT");
            AppendNamed(doc, filter, "category", "android.intent.category.BROWSABLE");

            var dataEl = doc.CreateElement("data");
            dataEl.SetAttribute("scheme", AndroidNs, scheme);
            dataEl.SetAttribute("host", AndroidNs, host);
            filter.AppendChild(dataEl);

            launcher.AppendChild(filter);
            doc.Save(manifestPath);

            Debug.Log($"[TrueSoft.Supabase] Apple 딥링크 intent-filter를 추가했습니다: {scheme}://{host}");
        }

        private static XmlElement FindLauncherActivity(XmlDocument doc)
        {
            foreach (XmlElement activity in doc.GetElementsByTagName("activity"))
            {
                foreach (XmlElement filter in activity.GetElementsByTagName("intent-filter"))
                {
                    var hasMain = false;
                    var hasLauncher = false;
                    foreach (XmlElement action in filter.GetElementsByTagName("action"))
                        if (action.GetAttribute("name", AndroidNs) == "android.intent.action.MAIN")
                            hasMain = true;
                    foreach (XmlElement category in filter.GetElementsByTagName("category"))
                        if (category.GetAttribute("name", AndroidNs) == "android.intent.category.LAUNCHER")
                            hasLauncher = true;

                    if (hasMain && hasLauncher)
                        return activity;
                }
            }
            return null;
        }

        private static void AppendNamed(XmlDocument doc, XmlElement parent, string tag, string nameValue)
        {
            var el = doc.CreateElement(tag);
            el.SetAttribute("name", AndroidNs, nameValue);
            parent.AppendChild(el);
        }
    }
}
#endif
