using System.IO;
using TrueBase.Unity;
using TrueBase.Unity.Config;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TrueBase.Editor
{
    public static class SupabaseSetupMenu
    {
        private const string DefaultFolder = "Assets/Resources";
        private const string AssetName = "SupabaseSettings.asset";

        [MenuItem("TrueSoft/Supabase/설정 에셋 만들기")]
        public static void CreateSettingsAsset()
        {
            var settings = GetOrCreateSettingsAsset();
            Selection.activeObject = settings;
            EditorGUIUtility.PingObject(settings);
        }

        [MenuItem("TrueSoft/Supabase/씬에 런타임 오브젝트 만들기")]
        public static void CreateRuntimeObjectInScene()
        {
            var existing = Object.FindFirstObjectByType<SupabaseRuntime>();
            if (existing != null)
            {
                Selection.activeObject = existing.gameObject;
                EditorGUIUtility.PingObject(existing.gameObject);
                Debug.Log("[Supabase] 현재 씬에 Runtime 오브젝트가 이미 있습니다.");
                return;
            }

            var settings = GetOrCreateSettingsAsset();

            var go = new GameObject("SupabaseSDK");
            Undo.RegisterCreatedObjectUndo(go, "Supabase Runtime 오브젝트 만들기");

            var runtime = go.AddComponent<SupabaseRuntime>();
            var so = new SerializedObject(runtime);
            var settingsProp = so.FindProperty("settings");
            settingsProp.objectReferenceValue = settings;
            so.ApplyModifiedPropertiesWithoutUndo();

            Selection.activeObject = go;
            EditorGUIUtility.PingObject(go);
            EditorSceneManager.MarkSceneDirty(go.scene);

            Debug.Log("[Supabase] 씬에 Runtime 오브젝트를 만들었습니다: " + go.name);
        }

        private static SupabaseSettings GetOrCreateSettingsAsset()
        {
            if (Directory.Exists(DefaultFolder) == false)
                Directory.CreateDirectory(DefaultFolder);

            var assetPath = Path.Combine(DefaultFolder, AssetName).Replace("\\", "/");

            var existing = AssetDatabase.LoadAssetAtPath<SupabaseSettings>(assetPath);
            if (existing != null)
            {
                Debug.Log("[Supabase] Settings 에셋이 이미 있습니다: " + assetPath);
                return existing;
            }

            var settings = ScriptableObject.CreateInstance<SupabaseSettings>();
            AssetDatabase.CreateAsset(settings, assetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[Supabase] Settings 에셋을 만들었습니다: " + assetPath);
            return settings;
        }
    }
}