using System.IO;
using TrueBase.Unity;
using TrueBase.Unity.Config;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TrueBase.Editor
{
    /// <summary>
    /// <c>TrueSoft &gt; Supabase</c> 메뉴 — SDK 초기 설정 도구.
    /// <c>SupabaseSettings</c> 에셋과 씬의 <c>SupabaseRuntime</c> 오브젝트를 만듭니다.
    /// </summary>
    public static class SupabaseSetupMenu
    {
        // 에셋은 반드시 Resources 하위에 있어야 런타임 자동 부트스트랩이 로드할 수 있음.
        private const string DefaultFolder = "Assets/Resources";
        private const string AssetName = "SupabaseSettings.asset";

        /// <summary><c>Assets/Resources/SupabaseSettings.asset</c>을 만들고(이미 있으면 재사용) 선택·핑합니다.</summary>
        [MenuItem("TrueSoft/Supabase/설정 에셋 만들기")]
        public static void CreateSettingsAsset()
        {
            var settings = GetOrCreateSettingsAsset();
            Selection.activeObject = settings;
            EditorGUIUtility.PingObject(settings);
        }

        /// <summary>
        /// 현재 씬에 <c>SupabaseRuntime</c> 컴포넌트를 가진 오브젝트를 만들고 Settings 에셋을 연결합니다.
        /// 씬에 이미 있으면 새로 만들지 않고 그 오브젝트를 선택만 합니다.
        /// </summary>
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

        /// <summary><c>Assets/Resources/SupabaseSettings.asset</c>을 로드하고, 없으면 새로 만들어 반환합니다.</summary>
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