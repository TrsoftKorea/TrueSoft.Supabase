using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using TrueBase.Unity;

namespace TrueBase.Editor
{
    /// <summary>
    /// Play 모드에서 등록된 유저 세이브(PlayerSave 등)의 현재 값을 보고 편집·저장·재로드하는 디버그 창.
    /// [DataColumn] 필드를 리플렉션으로 자동 표시하므로 생성된 어떤 세이브 클래스든 동작합니다.
    /// </summary>
    public class SupabaseUserDataDebugWindow : EditorWindow
    {
        private Vector2 _scroll;
        private readonly Dictionary<string, string> _jsonDrafts = new Dictionary<string, string>();

        [MenuItem("TrueSoft/Supabase/유저 데이터 디버그")]
        private static void Open()
        {
            var w = GetWindow<SupabaseUserDataDebugWindow>();
            w.titleContent = new GUIContent("Supabase 유저 데이터");
            w.minSize = new Vector2(320, 200);
            w.Show();
        }

        // Play 중 값이 외부에서 바뀌어도 창이 갱신되도록
        private void OnInspectorUpdate() => Repaint();

        private void OnGUI()
        {
            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "Play 모드에서만 동작합니다.\n로그인 → 데이터 로드 후 값을 편집·저장할 수 있습니다.",
                    MessageType.Info);
                return;
            }

            var entries = StaticUserSaveDebug.Entries;
            if (entries.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "등록된 세이브가 없습니다.\n세이브 클래스(PlayerSave 등)가 초기화되고 로드되면 표시됩니다.",
                    MessageType.Info);
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            for (var i = 0; i < entries.Count; i++)
            {
                DrawEntry(entries[i]);
                EditorGUILayout.Space(10);
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawEntry(StaticUserSaveDebug.Entry entry)
        {
            EditorGUILayout.LabelField(entry.SyncKey, EditorStyles.boldLabel);

            var row = entry.GetRow?.Invoke();
            if (row == null)
            {
                EditorGUILayout.HelpBox("아직 로드되지 않았습니다.", MessageType.None);
                return;
            }

            using (new EditorGUI.IndentLevelScope())
            {
                foreach (var f in GetDataColumnFields(row.GetType()))
                {
                    if (f.Name == "updated_at")
                    {
                        EditorGUILayout.LabelField(f.Name, f.GetValue(row)?.ToString() ?? "(null)");
                        continue;
                    }

                    if (IsScalar(f.FieldType))
                    {
                        EditorGUI.BeginChangeCheck();
                        var newVal = DrawScalar(f, f.GetValue(row));
                        if (EditorGUI.EndChangeCheck())
                        {
                            f.SetValue(row, newVal);
                            entry.MarkDirty?.Invoke();
                        }
                    }
                    else
                    {
                        DrawJsonField(entry, row, f);
                    }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("즉시 저장")) _ = Supabase.TrySaveAllAsync();
                if (GUILayout.Button("재로드")) _ = entry.ReloadAsync?.Invoke();
            }
        }

        // ── 필드 그리기 ───────────────────────────────────────────────────────────

        private static bool IsScalar(Type t)
            => t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(ulong)
            || t == typeof(float) || t == typeof(double) || t == typeof(bool) || t == typeof(string)
            || t.IsEnum;

        private static object DrawScalar(FieldInfo f, object value)
        {
            var t = f.FieldType;
            var label = f.Name;
            if (t == typeof(int))    return EditorGUILayout.IntField(label, value is int i ? i : 0);
            if (t == typeof(short))  return (short)EditorGUILayout.IntField(label, value is short s ? s : 0);
            if (t == typeof(long))   return EditorGUILayout.LongField(label, value is long l ? l : 0L);
            if (t == typeof(ulong))  return (ulong)Math.Max(0L, EditorGUILayout.LongField(label, value is ulong u ? (long)u : 0L));
            if (t == typeof(float))  return EditorGUILayout.FloatField(label, value is float fl ? fl : 0f);
            if (t == typeof(double)) return EditorGUILayout.DoubleField(label, value is double d ? d : 0d);
            if (t == typeof(bool))   return EditorGUILayout.Toggle(label, value is bool b && b);
            if (t == typeof(string)) return EditorGUILayout.TextField(label, value as string ?? "");
            if (t.IsEnum)            return EditorGUILayout.EnumPopup(label, (Enum)(value ?? Activator.CreateInstance(t)));
            return value;
        }

        // 컬렉션·클래스 등은 JSON 텍스트로 편집하고 "적용"으로 반영(타이핑 도중 파싱 오류 방지)
        private void DrawJsonField(StaticUserSaveDebug.Entry entry, object row, FieldInfo f)
        {
            var key = entry.SyncKey + "." + f.Name;
            if (!_jsonDrafts.TryGetValue(key, out var draft))
            {
                draft = Serialize(f.GetValue(row));
                _jsonDrafts[key] = draft;
            }

            EditorGUILayout.LabelField(f.Name + "  (JSON)");
            draft = EditorGUILayout.TextArea(draft, GUILayout.MinHeight(38));
            _jsonDrafts[key] = draft;

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("적용", GUILayout.Width(60)))
                {
                    try
                    {
                        f.SetValue(row, JsonConvert.DeserializeObject(draft, f.FieldType));
                        entry.MarkDirty?.Invoke();
                    }
                    catch (Exception e)
                    {
                        EditorUtility.DisplayDialog("JSON 파싱 실패", e.Message, "확인");
                    }
                }
                if (GUILayout.Button("되돌리기", GUILayout.Width(70)))
                    _jsonDrafts[key] = Serialize(f.GetValue(row));
            }
        }

        private static string Serialize(object value)
        {
            try { return JsonConvert.SerializeObject(value); }
            catch { return "(직렬화 불가)"; }
        }

        private static IEnumerable<FieldInfo> GetDataColumnFields(Type t)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var f in t.GetFields(flags))
                if (HasDataColumn(f))
                    yield return f;
        }

        private static bool HasDataColumn(FieldInfo f)
        {
            foreach (var a in f.GetCustomAttributes(false))
                if (a.GetType().Name == "DataColumnAttribute")
                    return true;
            return false;
        }
    }
}
