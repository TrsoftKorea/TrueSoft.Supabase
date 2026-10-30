using System;
using System.Threading.Tasks;
using TrueBase.Core.Common;
using UnityEngine;
#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace TrueBase.Unity.Push
{
    /// <summary>
    /// 기기 알림 권한을 묻고 푸시 주소를 받는다. Android 는 Firebase 설치 ID(빌드 후처리가 넣은 TrueSoftPush),
    /// iOS 는 APNs 토큰(Plugins/iOS/TrueSoftPush.mm). 결과 알림을 받는 대신 상태를 짧게 물어본다(받는 쪽 GameObject 를 따로 두지 않으려고).
    /// </summary>
    internal static class PushNative
    {
        private const int PollMilliseconds = 200;
        // 권한 창에 플레이어가 답할 시간까지 포함한다.
        private const int TimeoutMilliseconds = 60_000;

        public readonly struct Result
        {
            public readonly string Token;
            public readonly string Platform;
            public readonly string ErrorCode;
            public readonly string Detail;

            public Result(string token, string platform, string errorCode, string detail)
            {
                Token = token;
                Platform = platform;
                ErrorCode = errorCode;
                Detail = detail;
            }
        }

        public static async Task<Result> GetTokenAsync()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                using var push = new AndroidJavaClass("com.truesoft.supabase.push.TrueSoftPush");
                push.CallStatic("start", activity);
                var started = Environment.TickCount;
                while (Environment.TickCount - started < TimeoutMilliseconds)
                {
                    await Task.Delay(PollMilliseconds);
                    var state = push.CallStatic<string>("state");
                    if (state == "ok") return new Result(push.CallStatic<string>("token"), "android", null, null);
                    if (state == "denied") return new Result(null, "android", SupabaseErrorCode.PushPermissionDenied, null);
                    if (state == "failed")
                    {
                        var error = push.CallStatic<string>("error");
                        // 빌드에 Firebase 값이 안 들어간 경우는 설정 문제다.
                        var code = error != null && error.Contains("Firebase") ? SupabaseErrorCode.PushNotConfigured : SupabaseErrorCode.PushTokenUnavailable;
                        return new Result(null, "android", code, error);
                    }
                }
                return new Result(null, "android", SupabaseErrorCode.PushTokenUnavailable, "timeout");
            }
            catch (AndroidJavaException e) when (e.Message.Contains("ClassNotFoundException") || e.Message.Contains("TrueSoftPush"))
            {
                // 푸시 부품이 빌드에 없다 — SupabaseSettings 의 푸시 알림 사용이 꺼진 채 빌드했다.
                return new Result(null, "android", SupabaseErrorCode.PushNotConfigured, e.Message);
            }
#elif UNITY_IOS && !UNITY_EDITOR
            TrueSoftPush_Start();
            var started = Environment.TickCount;
            while (Environment.TickCount - started < TimeoutMilliseconds)
            {
                await Task.Delay(PollMilliseconds);
                switch (TrueSoftPush_State())
                {
                    case 1: return new Result(TrueSoftPush_Token(), "ios", null, null);
                    case 2: return new Result(null, "ios", SupabaseErrorCode.PushPermissionDenied, TrueSoftPush_Error());
                    case 3: return new Result(null, "ios", SupabaseErrorCode.PushTokenUnavailable, TrueSoftPush_Error());
                }
            }
            return new Result(null, "ios", SupabaseErrorCode.PushTokenUnavailable, "timeout");
#else
            await Task.CompletedTask;
            return new Result(null, null, SupabaseErrorCode.PushUnsupportedPlatform, Application.platform.ToString());
#endif
        }

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void TrueSoftPush_Start();
        [DllImport("__Internal")] private static extern int TrueSoftPush_State();
        [DllImport("__Internal")] private static extern string TrueSoftPush_Token();
        [DllImport("__Internal")] private static extern string TrueSoftPush_Error();
#endif
    }
}
