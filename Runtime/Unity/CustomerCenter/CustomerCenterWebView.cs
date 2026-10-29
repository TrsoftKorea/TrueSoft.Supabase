using System.Threading.Tasks;
using UnityEngine;
#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace TrueBase.Unity.CustomerCenter
{
    /// <summary>
    /// 고객센터 창을 띄운다. Android·iOS 는 게임 위 창(Android 전체 화면·iOS 시트, Plugins/Android·iOS 의 TrueSoftCustomerCenter),
    /// 그 밖(에디터 등)은 기본 브라우저. 게임 안 창은 닫힐 때까지 기다린다 — 닫은 뒤 새 답변 표시를 바로 고칠 수 있게.
    /// 닫힘 알림을 받는 대신 열려 있는지 짧게 물어본다(받는 쪽 GameObject 를 따로 두지 않으려고).
    /// </summary>
    internal static class CustomerCenterWebView
    {
        private const int PollMilliseconds = 300;

#if UNITY_ANDROID && !UNITY_EDITOR
        private const string JavaClass = "com.truesoft.supabase.TrueSoftCustomerCenter";

        private static void OpenNative(string url)
        {
            using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
            using var cls = new AndroidJavaClass(JavaClass);
            cls.CallStatic("open", activity, url);
        }

        private static bool IsOpenNative()
        {
            using var cls = new AndroidJavaClass(JavaClass);
            return cls.CallStatic<bool>("isOpen");
        }
#elif UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void TrueSoftCustomerCenter_Open(string url);
        // 네이티브 쪽은 C++ bool(1바이트)을 돌려준다 — 기본 마샬링(4바이트)과 맞춘다.
        [DllImport("__Internal")]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool TrueSoftCustomerCenter_IsOpen();

        private static void OpenNative(string url) => TrueSoftCustomerCenter_Open(url);
        private static bool IsOpenNative() => TrueSoftCustomerCenter_IsOpen();
#endif

        /// <summary>창을 띄우고 닫힐 때까지 기다린다. 게임 안 창이 없는 곳은 브라우저로 열고 바로 끝난다.</summary>
        /// <param name="inAppUrl">게임 안 창 주소(닫기 단추가 보이는 꼴).</param>
        /// <param name="browserUrl">브라우저로 열 때의 주소.</param>
        /// <returns>창이 떴으면 true. 게임 안 창이 끝내 안 뜨면 false.</returns>
        public static async Task<bool> OpenAsync(string inAppUrl, string browserUrl)
        {
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
            OpenNative(inAppUrl);
            // 창은 띄우기가 끝나야 "열림"이 된다. 열린 것이 보일 때까지 기다리되(최대 6초) 끝내 안 뜨면
            // (다른 화면이 떠 있어 띄우기가 실패한 경우) 실패로 끝낸다.
            var shown = false;
            for (var i = 0; i < 20 && !(shown = IsOpenNative()); i++)
                await Task.Delay(PollMilliseconds);
            if (!shown)
                return false;
            while (IsOpenNative())
                await Task.Delay(PollMilliseconds);
            return true;
#else
            Application.OpenURL(browserUrl);
            await Task.CompletedTask;
            return true;
#endif
        }
    }
}
