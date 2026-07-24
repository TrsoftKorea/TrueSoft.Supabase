using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace TrueBase.Editor
{
    /// <summary>
    /// 클래스 생성기 창들이 공유하는 에디터 헬퍼.
    /// 유저 데이터·Remote Config·리더보드 생성기가 함께 쓰는 GUI 스타일, 타입 파싱, CSV·우선순위 유틸을 모읍니다.
    /// </summary>
    internal static class GeneratorEditorCommon
    {
        /// <summary>Secret API 키(EditorPrefs 저장). 여러 생성기가 같은 키를 공유합니다.</summary>
        public const string PrefsKeySecret = "TrueBase.UserSaveClassGenerator.SecretKey";

        /// <summary>EditorPrefs에 저장된 Secret 키를 읽습니다. 없으면 빈 문자열.</summary>
        public static string GetSecretKey() => EditorPrefs.GetString(PrefsKeySecret, "");

        // ── GUI 스타일 ────────────────────────────────────────────────────────

        private static GUIStyle _ambiguousStyle;
        /// <summary>타입 추정이 불확실한 항목 표시용(노란 계열).</summary>
        public static GUIStyle AmbiguousStyle
        {
            get
            {
                if (_ambiguousStyle == null)
                    _ambiguousStyle = new GUIStyle(EditorStyles.label)
                    {
                        normal = { textColor = EditorGUIUtility.isProSkin
                            ? new Color(1f, 0.75f, 0.1f) : new Color(0.65f, 0.35f, 0f) }
                    };
                return _ambiguousStyle;
            }
        }

        private static GUIStyle _errorStyle;
        /// <summary>해석 불가 타입(오타·네임스페이스 누락) 표시용(빨강 계열).</summary>
        public static GUIStyle ErrorStyle
        {
            get
            {
                if (_errorStyle == null)
                    _errorStyle = new GUIStyle(EditorStyles.label)
                    {
                        normal = { textColor = EditorGUIUtility.isProSkin
                            ? new Color(1f, 0.4f, 0.4f) : new Color(0.75f, 0.1f, 0.1f) }
                    };
                return _errorStyle;
            }
        }

        // ── 저장 우선순위 (Normal=1, Urgent=0, Lazy=2) ────────────────────────

        private static readonly string[] s_priorityOptions = { "보통", "짧게", "길게" };
        private static readonly int[]    s_priorityValues  = {  1,      0,      2     };

        public static string PriorityLabel(int p)
        {
            var i = Array.IndexOf(s_priorityValues, p);
            return i >= 0 ? s_priorityOptions[i] : s_priorityOptions[0];
        }

        public static int ParsePriority(string s, int fallback)
        {
            var i = Array.IndexOf(s_priorityOptions, s);
            if (i >= 0) return s_priorityValues[i];
            if (int.TryParse(s, out var n) && Array.IndexOf(s_priorityValues, n) >= 0) return n;
            return fallback;
        }

        // ── 타입 파싱 ─────────────────────────────────────────────────────────

        /// <summary>명확한 ClrType 문자열에서 FieldTypeCategory를 결정합니다(isAmbiguous=false인 경우만).</summary>
        public static FieldTypeCategory ResolveTypeCategory(string rawClrType)
        {
            switch (rawClrType?.Trim())
            {
                case "bool":                                         return FieldTypeCategory.Boolean;
                case "int": case "short": case "long": case "ulong": return FieldTypeCategory.Integer;
                case "float": case "double":                         return FieldTypeCategory.Float;
                case "string":                                       return FieldTypeCategory.String;
                case "DateTimeOffset": case "DateTime":
                case "DateOnly": case "TimeOnly":                    return FieldTypeCategory.String;
                default:                                             return FieldTypeCategory.Unknown;
            }
        }

        /// <summary>Dictionary&lt;K, V&gt; 형식이면 K·V를 파싱합니다. 중첩 제네릭도 처리합니다.</summary>
        public static bool TryParseDictionaryTypes(string customType, out string keyType, out string valueType)
        {
            keyType = "string";
            valueType = "object";
            if (string.IsNullOrWhiteSpace(customType)) return false;

            var s = customType.Trim();
            if (!s.StartsWith("Dictionary<", StringComparison.Ordinal) || !s.EndsWith(">", StringComparison.Ordinal))
                return false;

            var inner = s.Substring("Dictionary<".Length, s.Length - "Dictionary<".Length - 1);
            var depth = 0;
            for (var i = 0; i < inner.Length; i++)
            {
                if (inner[i] == '<') depth++;
                else if (inner[i] == '>') depth--;
                else if (inner[i] == ',' && depth == 0)
                {
                    keyType = inner.Substring(0, i).Trim();
                    valueType = inner.Substring(i + 1).Trim();
                    return true;
                }
            }
            return false;
        }

        /// <summary>List&lt;T&gt; 형식이면 요소 타입을 파싱합니다.</summary>
        public static bool TryParseListType(string customType, out string elementType)
        {
            elementType = "int";
            if (string.IsNullOrWhiteSpace(customType)) return false;
            var s = customType.Trim();
            if (!s.StartsWith("List<", StringComparison.Ordinal) || !s.EndsWith(">", StringComparison.Ordinal))
                return false;
            elementType = s.Substring(5, s.Length - 6).Trim();
            return !string.IsNullOrEmpty(elementType);
        }

        /// <summary>T[] 형식이면 요소 타입을 파싱합니다.</summary>
        public static bool TryParseArrayType(string customType, out string elementType)
        {
            elementType = "int";
            if (string.IsNullOrWhiteSpace(customType)) return false;
            var s = customType.Trim();
            if (!s.EndsWith("[]", StringComparison.Ordinal)) return false;
            elementType = s.Substring(0, s.Length - 2).Trim();
            return !string.IsNullOrEmpty(elementType);
        }

        /// <summary>
        /// 타입이 "미지정"인지 — 정제하지 않은 jsonb 상태. <c>/* refine manually */</c> 플레이스홀더나
        /// Dictionary value가 object이면 true. 이 상태로는 소스 생성을 막습니다.
        /// </summary>
        public static bool IsUnspecifiedType(string clrType)
        {
            if (string.IsNullOrWhiteSpace(clrType)) return true;
            var t = clrType.Trim();
            if (t.IndexOf("/*", StringComparison.Ordinal) >= 0) return true;
            if (TryParseDictionaryTypes(t, out _, out var valueType)
                && string.Equals(valueType?.Trim(), "object", StringComparison.Ordinal))
                return true;
            return false;
        }

        // ── CSV ───────────────────────────────────────────────────────────────

        public static bool ParseBool(string s, bool fallback)
        {
            switch (s.Trim().ToLowerInvariant())
            {
                case "1": case "true": case "y": case "yes": case "o": return true;
                case "0": case "false": case "n": case "no": case "x": return false;
                default: return fallback;
            }
        }

        /// <summary>셀에 콤마·따옴표·줄바꿈이 있으면 큰따옴표로 감싸고 내부 <c>"</c>는 <c>""</c>로 이스케이프합니다.</summary>
        public static string CsvEscape(string s)
        {
            s = s ?? "";
            if (s.IndexOf(',') >= 0 || s.IndexOf('"') >= 0 || s.IndexOf('\n') >= 0 || s.IndexOf('\r') >= 0)
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        /// <summary>CSV 한 줄을 셀 목록으로 파싱합니다(콤마 구분, 큰따옴표 인용, <c>""</c>→<c>"</c>).</summary>
        public static List<string> ParseCsvLine(string line)
        {
            var result = new List<string>();
            var sb = new System.Text.StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i];
                if (inQuotes)
                {
                    if (ch == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else sb.Append(ch);
                }
                else
                {
                    if (ch == '"') inQuotes = true;
                    else if (ch == ',') { result.Add(sb.ToString()); sb.Clear(); }
                    else sb.Append(ch);
                }
            }
            result.Add(sb.ToString());
            return result;
        }

        /// <summary>CSV 임포트 결과(미매칭 행·타입 해석 실패)를 한 팝업으로 알립니다. 문제가 없으면 조용히 넘어갑니다.</summary>
        public static void ReportImportIssues(string dialogTitle, int applied, List<string> unknown, List<string> unresolved)
        {
            if (unknown.Count == 0 && unresolved.Count == 0) return;

            var sb = new System.Text.StringBuilder();
            sb.Append($"{applied}개 행을 적용했습니다.");
            if (unknown.Count > 0)
            {
                var shown = unknown.Take(10).ToArray();
                sb.Append($"\n\n일치하는 항목이 없어 건너뜀({unknown.Count}): ")
                  .Append(string.Join(", ", shown))
                  .Append(unknown.Count > shown.Length ? " …" : "");
            }
            if (unresolved.Count > 0)
            {
                var shown = unresolved.Take(10).ToArray();
                sb.Append($"\n\n에디터에서 찾지 못한 타입({unresolved.Count}): ")
                  .Append(string.Join(", ", shown))
                  .Append(unresolved.Count > shown.Length ? " …" : "")
                  .Append("\n철자가 맞다면 그대로 생성해도 됩니다. 오타라면 컴파일 시 에러가 납니다.");
            }
            EditorUtility.DisplayDialog(dialogTitle, sb.ToString(), "확인");
        }
    }
}
