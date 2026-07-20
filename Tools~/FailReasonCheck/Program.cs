using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TrueBase.Core.Common;

var errors = new List<string>();
var warnings = new List<string>();

// enum 멤버 (None·Unknown 제외)
var enumNames = Enum.GetNames(typeof(SupabaseReason))
    .Where(n => n != nameof(SupabaseReason.None) && n != nameof(SupabaseReason.Unknown))
    .ToList();

// SupabaseErrorCode 문자열 상수: 이름 -> 값
var consts = typeof(SupabaseErrorCode)
    .GetFields(BindingFlags.Public | BindingFlags.Static)
    .Where(f => f.IsLiteral && f.FieldType == typeof(string))
    .ToDictionary(f => f.Name, f => (string)f.GetRawConstantValue());

// 1) enum 멤버 ↔ 상수 이름 1:1
foreach (var e in enumNames)
    if (!consts.ContainsKey(e))
        errors.Add($"[enum→상수] SupabaseReason.{e} 에 대응하는 SupabaseErrorCode.{e} 상수가 없습니다.");
foreach (var c in consts.Keys)
    if (!enumNames.Contains(c))
        errors.Add($"[상수→enum] SupabaseErrorCode.{c} 에 대응하는 SupabaseReason.{c} enum 멤버가 없습니다.");

// 2) map 정합성: FromErrorCode(상수값) == 동명 enum 멤버
foreach (var kv in consts)
{
    if (!enumNames.Contains(kv.Key)) continue; // 1)에서 이미 보고
    var mapped = SupabaseReasonMap.FromErrorCode(kv.Value).ToString();
    if (mapped != kv.Key)
        errors.Add($"[map] FromErrorCode(\"{kv.Value}\") → {mapped} (기대: {kv.Key}). map이 상수/enum과 어긋납니다.");
}

// 3) 에러코드 문자열 중복
foreach (var g in consts.GroupBy(kv => kv.Value).Where(g => g.Count() > 1))
    errors.Add($"[중복] 에러코드 \"{g.Key}\" 가 여러 상수에서 사용됨: {string.Join(", ", g.Select(x => x.Key))}");

// 4) None/빈문자열/미정의 매핑 동작
if (SupabaseReasonMap.FromErrorCode(null) != SupabaseReason.None)
    errors.Add("[map] FromErrorCode(null) 이 None 이 아닙니다.");
if (SupabaseReasonMap.FromErrorCode("") != SupabaseReason.None)
    errors.Add("[map] FromErrorCode(\"\") 이 None 이 아닙니다.");
if (SupabaseReasonMap.FromErrorCode("__정의되지_않은_에러코드__") != SupabaseReason.Unknown)
    errors.Add("[map] 미정의 문자열이 Unknown 으로 매핑되지 않습니다.");

// 5) (경고) 방출/참조 스캔 — Runtime·Editor 어디에도 없는 죽은 사유
var root = FindRepoRoot();
if (root == null)
{
    warnings.Add("리포지토리 루트(Runtime/Core)를 찾지 못해 방출 스캔을 건너뜁니다.");
}
else
{
    var defFiles = new HashSet<string> { "SupabaseReason.cs", "SupabaseErrorCode.cs" };
    var scanDirs = new[] { "Runtime", "Editor" }
        .Select(d => Path.Combine(root, d))
        .Where(Directory.Exists);

    var sources = scanDirs
        .SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories))
        .Where(p => !defFiles.Contains(Path.GetFileName(p)))
        .Select(File.ReadAllText)
        .ToList();

    foreach (var kv in consts)
    {
        bool used = sources.Any(txt =>
            txt.Contains("SupabaseErrorCode." + kv.Key) || txt.Contains("\"" + kv.Value + "\""));
        if (!used)
            warnings.Add($"[미사용] SupabaseErrorCode.{kv.Key} (\"{kv.Value}\") 가 Runtime·Editor 어디에서도 방출/참조되지 않습니다. 죽은 사유일 수 있습니다(서버 전용 사유면 무시).");
    }
}

// 리포트
Console.WriteLine($"에러코드 카탈로그 검증 — enum {enumNames.Count}개 · 상수 {consts.Count}개");
Console.WriteLine();
foreach (var w in warnings)
    Console.WriteLine("  경고: " + w);
if (errors.Count == 0)
{
    Console.WriteLine("  ✔ 3자(enum · 상수 · map) 정합성 통과.");
    Console.WriteLine();
    Console.WriteLine(warnings.Count == 0 ? "결과: OK" : $"결과: OK (경고 {warnings.Count}건)");
    return 0;
}
foreach (var e in errors)
    Console.WriteLine("  오류: " + e);
Console.WriteLine();
Console.WriteLine($"결과: 실패 — 오류 {errors.Count}건");
return 1;

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null)
    {
        if (Directory.Exists(Path.Combine(dir.FullName, "Runtime", "Core")))
            return dir.FullName;
        dir = dir.Parent;
    }
    return null;
}
