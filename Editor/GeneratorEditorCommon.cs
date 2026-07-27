using System;
using System.Collections.Generic;
using System.Linq;
using TrueBase.Unity;
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

        private static readonly string[] s_priorityOptions = { "보통", "짧게", "길게" }; // UI 표시(한글)
        private static readonly int[]    s_priorityValues  = {  1,      0,      2     };

        /// <summary>UI 표시용 한글 라벨.</summary>
        public static string PriorityLabel(int p)
        {
            var i = Array.IndexOf(s_priorityValues, p);
            return i >= 0 ? s_priorityOptions[i] : s_priorityOptions[0];
        }

        // 저장주기 숫자화 이전에 쓰던 문구 → 숫자. 옛 CSV를 열 때 최신 형식(숫자)으로 변환합니다.
        private static readonly Dictionary<string, int> s_legacyPriorityTokens = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "보통", 1 }, { "Normal", 1 },
            { "짧게", 0 }, { "fast",   0 },
            { "길게", 2 }, { "slow",   2 },
        };

        /// <summary>
        /// CSV의 저장주기 셀을 파싱합니다. 표준은 숫자(0=짧게·1=보통·2=길게)이며, 옛 형식 문구(보통/짧게/길게·Normal/fast/slow)는
        /// 최신 숫자로 변환해 인식합니다. 어느 쪽도 아니면 보통(1). 옛 형식 문구를 만나면 <paramref name="legacyConverted"/>=true.
        /// </summary>
        public static int ParsePriority(string s, out bool legacyConverted)
        {
            legacyConverted = false;
            var t = (s ?? string.Empty).Trim();
            if (int.TryParse(t, out var n) && (n == 0 || n == 1 || n == 2)) return n;
            if (s_legacyPriorityTokens.TryGetValue(t, out var v)) { legacyConverted = true; return v; }
            return 1; // 알 수 없는 값 → 보통
        }

        // 헤더 한글화 이전에 쓰던 영문 헤더 문구(두 생성기 공용, RC는 이 중 field·type·include 사용).
        private static readonly HashSet<string> s_legacyEnglishHeaderTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "column", "field", "type", "priority", "default", "include",
        };

        /// <summary>첫 셀이 헤더 한글화 이전의 영문 헤더 문구면 true — 옛 CSV 여부 판별용.</summary>
        public static bool IsLegacyHeaderRow(string firstCell) => s_legacyEnglishHeaderTokens.Contains((firstCell ?? "").Trim());

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

        // 헤더 행 판별용 토큰(현재 한글 + 헤더 한글화 전 영문). CSV는 열 순서로 매칭하므로
        // 어느 언어의 헤더든 첫 셀이 여기 포함되면 헤더 행으로 보고 건너뜁니다.
        private static readonly HashSet<string> s_userSaveHeaderTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "컬럼", "필드명", "타입", "저장주기", "기본값", "포함",       // 현재(한글)
            "column", "field", "type", "priority", "default", "include", // 이전(영문)
        };
        private static readonly HashSet<string> s_rcHeaderTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "필드", "타입", "포함",       // 현재(한글)
            "field", "type", "include",   // 이전(영문)
        };

        /// <summary>유저 데이터 CSV의 첫 행이 헤더인지 — 한글 또는 이전 영문 헤더 문구면 true.</summary>
        public static bool IsUserSaveHeaderRow(string firstCell) => s_userSaveHeaderTokens.Contains((firstCell ?? "").Trim());

        /// <summary>Remote Config CSV의 첫 행이 헤더인지 — 한글 또는 이전 영문 헤더 문구면 true.</summary>
        public static bool IsRcHeaderRow(string firstCell) => s_rcHeaderTokens.Contains((firstCell ?? "").Trim());

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

        // ── 생성기 연결 설정 UI ─────────────────────────────────────────────────

        private static string _secretKeyDraft;
        private static bool _secretWarned;

        /// <summary>
        /// 가져오기 실패 예외를 사람이 읽기 쉬운 안내로 바꿉니다. 흔한 원인(키·URL·네트워크)을 짚어주고,
        /// 원문 메시지는 뒤에 덧붙여 디버깅에도 쓸 수 있게 합니다.
        /// </summary>
        public static string DescribeFetchError(Exception e)
        {
            var raw = string.IsNullOrWhiteSpace(e?.Message) ? "알 수 없는 오류" : e.Message.Trim();
            var s = raw.ToLowerInvariant();

            string hint = null;
            if (s.Contains("http 401") || s.Contains("http 403") || s.Contains("jwt") || s.Contains("apikey") || s.Contains("api 키가 비어"))
                hint = "Secret 키가 올바른지 확인하세요. 창 상단에서 다시 입력할 수 있습니다.";
            else if (s.Contains("http 404"))
                hint = "요청한 테이블·RPC가 프로젝트에 있는지, 프로젝트 URL이 맞는지 확인하세요.";
            else if (s.Contains("http 5"))
                hint = "서버가 오류를 반환했습니다. 잠시 후 다시 시도하세요.";
            else if (s.Contains("timeout") || s.Contains("timed out") || s.Contains("시간 초과"))
                hint = "요청이 시간 초과됐습니다. 네트워크와 프로젝트 URL을 확인하세요.";
            else if (s.Contains("resolve") || s.Contains("unknown host") || s.Contains("cannot connect")
                     || s.Contains("connection") || s.Contains("curl error") || s.Contains("네트워크"))
                hint = "서버에 연결하지 못했습니다. 프로젝트 URL과 네트워크 연결을 확인하세요.";
            else if (s.Contains("url이 비어") || s.Contains("프로젝트 url"))
                hint = "프로젝트 URL이 비어 있습니다. '설정 열기'에서 입력하세요.";

            return hint == null ? raw : hint + "\n\n원문: " + raw;
        }

        /// <summary>
        /// 생성기 창 상단의 공통 연결 설정 바. 설정 에셋을 창 안에서 만들거나 선택하고,
        /// Secret 키를 쓰는 생성기는 여기서 바로 입력·수정할 수 있습니다.
        /// 필드 목록을 가져올 준비(설정 에셋 존재 + URL + 필요 시 Secret 키)가 됐으면 true.
        /// </summary>
        /// <param name="needsSecret">Secret 키로 인증하는 생성기(유저 데이터·원격 설정)면 true. 리더보드는 publishable 키를 써서 false.</param>
        public static bool DrawConnectionSetup(SupabaseSettings settings, bool needsSecret)
        {
            if (settings == null)
            {
                EditorGUILayout.HelpBox("SupabaseSettings 에셋이 없습니다. 먼저 만들어야 필드 목록을 가져올 수 있습니다.", MessageType.Warning);
                if (GUILayout.Button("설정 에셋 만들기", GUILayout.Height(24)))
                    SupabaseSetupMenu.CreateSettingsAsset();
                return false;
            }

            var ready = true;

            if (needsSecret)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (_secretKeyDraft == null) _secretKeyDraft = GetSecretKey();
                    EditorGUI.BeginChangeCheck();
                    _secretKeyDraft = EditorGUILayout.PasswordField("Secret 키", _secretKeyDraft);
                    if (EditorGUI.EndChangeCheck())
                    {
                        EditorPrefs.SetString(PrefsKeySecret, _secretKeyDraft ?? "");
                        if (!string.IsNullOrWhiteSpace(_secretKeyDraft) && !_secretWarned)
                        {
                            _secretWarned = true; // 입력 중 글자마다 로그가 찍히지 않도록 세션당 한 번만
                            Debug.LogWarning("[Supabase] Secret 키가 EditorPrefs에 저장됩니다. 공유 PC 환경에서는 주의하세요.");
                        }
                    }
                    if (GUILayout.Button(new GUIContent("설정 열기", "SupabaseSettings 에셋을 선택합니다 — URL·타임아웃 등을 고칠 때"), GUILayout.Width(70)))
                        PingSettings(settings);
                }
                if (string.IsNullOrWhiteSpace(GetSecretKey()))
                {
                    EditorGUILayout.HelpBox("Secret 키를 입력해야 필드 목록을 가져올 수 있습니다.", MessageType.Info);
                    ready = false;
                }
            }
            else
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("설정 에셋", AssetDatabase.GetAssetPath(settings), EditorStyles.miniLabel);
                    if (GUILayout.Button(new GUIContent("설정 열기", "SupabaseSettings 에셋을 선택합니다 — URL·publishable 키 등을 고칠 때"), GUILayout.Width(70)))
                        PingSettings(settings);
                }
            }

            if (string.IsNullOrWhiteSpace(settings.projectUrl))
            {
                EditorGUILayout.HelpBox("프로젝트 URL이 비어 있습니다. '설정 열기'에서 입력하세요.", MessageType.Warning);
                ready = false;
            }

            return ready;
        }

        /// <summary>가져오기 실패 메시지를 창 안에 보여주고 '다시 시도' 버튼을 제공합니다. 메시지가 없으면 아무것도 그리지 않습니다.</summary>
        public static void DrawFetchError(string error, Action retry)
        {
            if (string.IsNullOrEmpty(error)) return;
            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(error, MessageType.Error);
            if (retry != null && GUILayout.Button("다시 시도", GUILayout.Height(22)))
                retry();
        }

        private static void PingSettings(SupabaseSettings settings)
        {
            Selection.activeObject = settings;
            EditorGUIUtility.PingObject(settings);
        }
    }
}
