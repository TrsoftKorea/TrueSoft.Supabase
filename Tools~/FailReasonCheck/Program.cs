using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
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

var root = FindRepoRoot();

// 5) (경고) 방출/참조 스캔 — C#·SQL 어디에서도 방출되지 않는 죽은 사유.
//    서버가 던지는 사유는 C# 에 안 적혀 있어도 게임까지 흘러오므로 SQL 도 함께 본다.
var sqlCodes = CollectSqlErrorCodes(root, warnings);

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
        if (sqlCodes.Contains(kv.Value))
            continue; // 서버가 던지는 사유

        bool used = sources.Any(txt =>
            txt.Contains("SupabaseErrorCode." + kv.Key) || txt.Contains("\"" + kv.Value + "\""));
        if (!used)
            warnings.Add($"[미사용] SupabaseErrorCode.{kv.Key} (\"{kv.Value}\") 가 C#·SQL 어디에서도 방출되지 않습니다. 죽은 사유일 수 있습니다.");
    }
}

// 6) SQL 대조 — 클라이언트에 열린 RPC가 던지는 사유가 카탈로그에 있는가.
//    없으면 게임에서 SupabaseReason.Unknown 으로 떨어져 분기할 수 없다.
{
    var known = new HashSet<string>(consts.Values, StringComparer.Ordinal);
    foreach (var (func, code) in sqlCodes.Emissions)
        if (!known.Contains(code))
            errors.Add($"[SQL→카탈로그] public.{func} 이 던지는 \"{code}\" 가 SupabaseErrorCode 에 없습니다. 게임에서 SupabaseReason.Unknown 으로 떨어집니다.");
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

/// <summary>
/// install.sql 에서 클라이언트에 열린 RPC 가 던지는 사유 코드를 모은다.
/// 관리 함수(service_role 전용)는 SDK 가 볼 일이 없어 제외한다.
/// </summary>
static SqlErrorCodes CollectSqlErrorCodes(string root, List<string> warnings)
{
    var result = new SqlErrorCodes();
    if (root == null)
        return result;

    var sqlPath = Path.Combine(root, "Samples~", "DatabaseSetup", "SQL", "player", "install.sql");
    if (!File.Exists(sqlPath))
    {
        warnings.Add("install.sql 을 찾지 못해 SQL 사유 대조를 건너뜁니다.");
        return result;
    }

    var sql = File.ReadAllText(sqlPath);

    var clientFuncs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (Match g in Regex.Matches(sql,
                 @"grant\s+execute\s+on\s+function\s+(?:public\.)?(\w+)\s*\([^)]*\)\s*to\s+([^;]+);",
                 RegexOptions.IgnoreCase))
    {
        var roles = g.Groups[2].Value;
        if (roles.IndexOf("authenticated", StringComparison.OrdinalIgnoreCase) >= 0 ||
            roles.IndexOf("anon", StringComparison.OrdinalIgnoreCase) >= 0)
            clientFuncs.Add(g.Groups[1].Value);
    }

    if (clientFuncs.Count == 0)
    {
        warnings.Add("install.sql 에서 클라이언트 grant 를 찾지 못해 SQL 사유 대조를 건너뜁니다.");
        return result;
    }

    foreach (var (name, body) in EnumerateFunctionBodies(sql))
    {
        if (!clientFuncs.Contains(name))
            continue;

        foreach (Match r in Regex.Matches(body, @"raise\s+exception\s+'([^']+)'", RegexOptions.IgnoreCase))
        {
            // 클라이언트는 ExtractRpcErrorCode 로 첫 ':' 앞만 취한다.
            var raw = r.Groups[1].Value;
            var colon = raw.IndexOf(':');
            var code = (colon > 0 ? raw.Substring(0, colon) : raw).Trim();

            // snake_case 식별자만 사유 코드다. 사람이 읽는 문장은 관리 함수용이라 제외.
            if (!Regex.IsMatch(code, @"^[a-z][a-z0-9_]*$"))
                continue;

            if (result.Add(code))
                result.Emissions.Add((name, code));
        }
    }

    return result;
}

/// <summary>
/// install.sql 에서 (함수 이름, 본문) 쌍을 뽑는다. 본문은 달러 인용 구간이라
/// 여는 태그($$ 또는 $tag$)와 같은 태그가 다시 나올 때까지가 한 함수다.
/// </summary>
static IEnumerable<(string Name, string Body)> EnumerateFunctionBodies(string sql)
{
    foreach (Match m in Regex.Matches(sql,
                 @"create\s+(?:or\s+replace\s+)?function\s+(?:public\.)?(\w+)\s*\(",
                 RegexOptions.IgnoreCase))
    {
        var open = Regex.Match(sql.Substring(m.Index), @"\$(\w*)\$");
        if (!open.Success)
            continue;

        var tag = open.Value;
        var bodyStart = m.Index + open.Index + tag.Length;
        var end = sql.IndexOf(tag, bodyStart, StringComparison.Ordinal);
        if (end < 0)
            continue;

        yield return (m.Groups[1].Value, sql.Substring(bodyStart, end - bodyStart));
    }
}

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

/// <summary>SQL 에서 모은 사유 코드 집합과, 어느 함수가 던졌는지 기록.</summary>
sealed class SqlErrorCodes
{
    private readonly HashSet<string> _codes = new HashSet<string>(StringComparer.Ordinal);

    public List<(string Func, string Code)> Emissions { get; } = new List<(string, string)>();

    public bool Add(string code) => _codes.Add(code);
    public bool Contains(string code) => _codes.Contains(code);
}
