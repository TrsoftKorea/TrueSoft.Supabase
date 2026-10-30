using System;
using System.IO;
using TrueBase.Unity;
using UnityEditor;
using UnityEngine;

namespace TrueBase.Editor
{
    /// <summary>
    /// 푸시 빌드 후처리(Android·iOS)가 함께 읽는 값. SupabaseSettings 에서 "푸시 알림 사용"을 켠 게임만 푸시 부품을 넣는다 —
    /// 안 쓰는 게임까지 Firebase 를 넣으면 앱이 커지고, 이미 Firebase 를 쓰는 게임과 부딪힐 수 있다.
    /// </summary>
    internal static class PushBuildSettings
    {
        public static SupabaseSettings Load()
        {
            var s = Resources.Load<SupabaseSettings>("SupabaseSettings");
            if (s != null) return s;
            foreach (var guid in AssetDatabase.FindAssets("t:SupabaseSettings"))
            {
                s = AssetDatabase.LoadAssetAtPath<SupabaseSettings>(AssetDatabase.GUIDToAssetPath(guid));
                if (s != null) return s;
            }
            return null;
        }

        /// <summary>SDK 의 Editor/Push 에 둔 파일 내용. 패키지가 어디 설치돼 있든(Git·로컬·임베드) 이 어셈블리의 패키지에서 찾는다.</summary>
        public static string ReadTemplate(string fileName)
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(PushBuildSettings).Assembly);
            var root = package != null ? package.resolvedPath : Path.GetFullPath("Packages/com.truesoft.supabase");
            var path = Path.Combine(root, "Editor", "Push", fileName);
            if (!File.Exists(path)) throw new FileNotFoundException($"[TrueSoft.Supabase] 푸시 부품 파일이 없습니다: {path}");
            return File.ReadAllText(path);
        }

        /// <summary>게임에 Firebase 유니티 메시징이 있는지. 있으면 SDK 푸시와 함께 쓸 수 없다(Android 후처리가 빌드를 멈춘다).</summary>
        public static bool HasFirebaseUnityMessaging() => HasType("Firebase.Messaging.FirebaseMessaging");

        /// <summary>게임에 Firebase 유니티 기본 패키지(분석 등)가 있는지. 있으면 Firebase 설정값은 그 패키지가 넣은 것을 쓴다.</summary>
        public static bool HasFirebaseUnityApp() => HasType("Firebase.FirebaseApp");

        private static bool HasType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                if (assembly.GetType(fullName) != null)
                    return true;
            return false;
        }
    }
}
