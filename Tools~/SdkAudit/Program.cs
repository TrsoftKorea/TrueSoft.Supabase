using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

Console.OutputEncoding = Encoding.UTF8;

var root = FindRepoRoot();
if (root == null)
{
    Console.WriteLine("오류: 리포지토리 루트(Runtime/Core 를 가진 폴더)를 찾지 못했습니다.");
    return 1;
}

// 게임에 값을 돌려주지만 실패할 수 없는 멤버 — result 타입 규칙의 대상이 아니다.
// 네트워크를 타지 않는 순수 변환·등록 헬퍼만 들어간다. 새로 넣을 때는 정말 실패할 수 없는지 확인할 것.
var nonFailingMembers = new HashSet<string>(StringComparer.Ordinal)
{
    "ToRow",                          // 조회 결과 → 생성 클래스 행. 로컬 변환
    "RegisterPlayNanooInterceptors",  // 인터셉터 등록
    "UnregisterPlayNanooInterceptors",
    "RegisterNanooStorageReset",
    "RegisterIAPAppleInterceptor",
    "RegisterIAPGoogleInterceptor",
    "GetNanooSaveBridge",             // 등록된 브리지 반환. 없으면 null
    "UnregisterMailItemHandler",
};

var errors = new List<string>();
var warnings = new List<string>();

// ── 파싱 ────────────────────────────────────────────────────────────────────
var sources = Directory.EnumerateFiles(Path.Combine(root, "Runtime"), "*.cs", SearchOption.AllDirectories)
    .Select(p => new Source(p, File.ReadAllText(p)))
    .ToList();

var classes = new List<(Source Src, ClassDeclarationSyntax Node)>();
foreach (var s in sources)
    foreach (var c in CSharpSyntaxTree.ParseText(s.Text).GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
        classes.Add((s, c));

// ── R1 공개 표면 ────────────────────────────────────────────────────────────
// 게임에 공개하는 파사드는 internal 멤버를 두지 않고, Try 접두어를 쓰지 않고, 항상 result 타입을 돌려준다.
var entryPoints = new[] { "Supabase", "SupabaseIAP" };
var publicApi = new Dictionary<string, string>(StringComparer.Ordinal); // 멤버명 -> 소유 클래스
var publicMethods = new List<MethodDeclarationSyntax>();

foreach (var (src, cls) in classes.Where(c => entryPoints.Contains(c.Node.Identifier.ValueText)))
{
    var owner = cls.Identifier.ValueText;

    foreach (var m in cls.Members)
    {
        var name = MemberName(m);
        if (name == null)
            continue;

        var where = $"{src.Rel(root)}:{LineOf(src, m)}";

        if (Has(m, SyntaxKind.InternalKeyword))
        {
            // 규칙은 Supabase 파사드를 명시한다. 다른 진입점은 같은 원칙이 적용되는지 판단이 필요해 경고로 둔다.
            var msg = $"{owner}.{name} 이 internal 입니다. 내부 배선은 SupabaseSDK 를 직접 참조하세요. ({where})";
            if (owner == "Supabase")
                errors.Add("[R1 internal] " + msg);
            else
                warnings.Add($"[R1 internal] {msg} — 규칙이 Supabase 파사드만 명시하고 있어 이 진입점에 적용되는지 확인이 필요합니다.");
            continue;
        }

        if (!Has(m, SyntaxKind.PublicKeyword))
            continue;

        publicApi[name] = owner;

        if (m is not MethodDeclarationSyntax method)
            continue;

        publicMethods.Add(method);

        if (name.StartsWith("Try", StringComparison.Ordinal))
            errors.Add($"[R1 Try접두어] {owner}.{name} — 공개 메서드에 Try 접두어를 쓰지 않습니다. ({where})");

        var ret = method.ReturnType.ToString();
        if (!IsResultType(ret) && !nonFailingMembers.Contains(name))
            errors.Add($"[R1 반환타입] {owner}.{name} 이 result 타입이 아닌 {ret} 을 돌려줍니다. 실패할 수 없는 멤버라면 검사기의 nonFailingMembers 에 근거와 함께 넣으세요. ({where})");
    }
}

if (publicApi.Count == 0)
    errors.Add("[R1] 파사드에서 공개 멤버를 찾지 못했습니다. 검사기가 파일 구조를 못 읽고 있습니다.");

// ── R2 리셋 대칭성 ──────────────────────────────────────────────────────────
// 파사드 백킹 필드를 하나 추가하면 리셋 블록에도 넣어야 한다. 빠뜨려도 컴파일은 통과한다.
var facadesWithReset = new Dictionary<string, string>(StringComparer.Ordinal);
foreach (var (src, cls) in classes)
{
    if (!cls.Identifier.ValueText.EndsWith("Facade", StringComparison.Ordinal))
        continue;
    var reset = cls.Members.OfType<MethodDeclarationSyntax>().FirstOrDefault(x => x.Identifier.ValueText == "Reset");
    if (reset != null)
        facadesWithReset[cls.Identifier.ValueText] = $"{src.Rel(root)}:{LineOf(src, reset)}";
}

var sdk = classes.FirstOrDefault(c => c.Node.Identifier.ValueText == "SupabaseSDK");
if (sdk.Node == null)
{
    errors.Add("[R2] SupabaseSDK 클래스를 찾지 못했습니다.");
}
else
{
    var facadeFields = new List<(string Field, string Type)>();
    foreach (var f in sdk.Node.Members.OfType<FieldDeclarationSyntax>())
    {
        var type = f.Declaration.Type.ToString();
        if (!type.EndsWith("Facade", StringComparison.Ordinal))
            continue;
        foreach (var v in f.Declaration.Variables)
            facadeFields.Add((v.Identifier.ValueText, type));
    }

    if (facadeFields.Count == 0)
    {
        errors.Add("[R2] SupabaseSDK 에서 파사드 백킹 필드를 찾지 못했습니다.");
    }
    else
    {
        // 필드를 가장 많이 null 로 되돌리는 메서드를 리셋 블록으로 본다.
        var nullers = sdk.Node.Members.OfType<MethodDeclarationSyntax>()
            .Select(m => (Name: m.Identifier.ValueText, Body: m.Body?.ToString() ?? ""))
            .Select(x => (x.Name, x.Body, Nulled: facadeFields.Where(f => NullsField(x.Body, f.Field)).Select(f => f.Field).ToList()))
            .Where(x => x.Nulled.Count > 0)
            .OrderByDescending(x => x.Nulled.Count)
            .ToList();

        if (nullers.Count == 0)
        {
            errors.Add("[R2] 파사드 필드를 정리하는 메서드가 없습니다.");
        }
        else
        {
            var reset = nullers[0];

            foreach (var f in facadeFields)
                if (!reset.Nulled.Contains(f.Field))
                    errors.Add($"[R2 리셋누락] {f.Type} {f.Field} 가 리셋 블록({reset.Name})에서 정리되지 않습니다.");

            foreach (var f in facadeFields.Where(f => facadesWithReset.ContainsKey(f.Type)))
                if (!CallsReset(reset.Body, f.Field))
                    errors.Add($"[R2 Reset미호출] {f.Type} 은 Reset() 을 갖는데 리셋 블록({reset.Name})에서 호출되지 않습니다. 필드만 null 로 두면 내부 상태(구독·커서 등)가 남습니다.");

            // 리셋 블록이 아닌데 일부만 정리하는 메서드 — 세션 종속 상태를 빠뜨렸을 수 있다(판단 필요).
            foreach (var m in nullers.Skip(1))
            {
                var skipped = facadeFields
                    .Where(f => facadesWithReset.ContainsKey(f.Type) && !m.Nulled.Contains(f.Field))
                    .Select(f => f.Type)
                    .ToList();
                if (skipped.Count > 0)
                    warnings.Add($"[R2 부분정리] {m.Name} 이 파사드 일부만 정리합니다({string.Join(", ", m.Nulled)}). Reset() 을 가진 {string.Join(", ", skipped)} 은 여기서 정리되지 않습니다 — 이 시점에 남아도 되는 상태인지 확인하세요.");
            }
        }
    }
}

// Reset() 을 갖지만 아무도 부르지 않는 파사드 — 죽은 코드.
var allText = string.Join("\n", sources.Select(s => s.Text));
foreach (var kv in facadesWithReset)
{
    var fields = Regex.Matches(allText, @"\b" + Regex.Escape(kv.Key) + @"\s+(_\w+)\s*;")
        .Select(m => m.Groups[1].Value).Distinct().ToList();
    if (fields.Count > 0 && !fields.Any(f => CallsReset(allText, f)))
        errors.Add($"[R2 죽은Reset] {kv.Key}.Reset() 이 어디에서도 호출되지 않습니다. ({kv.Value})");
}

// ── R3·R4 문서 정합성 ──────────────────────────────────────────────────────
var guideDir = Path.Combine(root, "docs~", "guide");
if (!Directory.Exists(guideDir))
{
    warnings.Add("docs~/guide 를 찾지 못해 문서 검사를 건너뜁니다.");
}
else
{
    var docApiNames = new HashSet<string>(StringComparer.Ordinal);

    // 코드 메서드: 이름 -> 오버로드별 파라미터 이름 목록
    var codeParams = publicMethods
        .GroupBy(m => m.Identifier.ValueText, StringComparer.Ordinal)
        .ToDictionary(
            g => g.Key,
            g => g.Select(m => m.ParameterList.Parameters.Select(p => p.Identifier.ValueText).ToList()).ToList(),
            StringComparer.Ordinal);

    foreach (var md in Directory.EnumerateFiles(guideDir, "*.md", SearchOption.AllDirectories))
    {
        var rel = Rel(root, md);
        var lines = File.ReadAllLines(md);
        var inCsharp = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();

            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                inCsharp = !inCsharp && trimmed.StartsWith("```csharp", StringComparison.Ordinal);
                continue;
            }

            // R3 — 문서가 가리키는 API 는 산문·코드 어디에 있어도 실제로 존재해야 한다.
            // 앞에 한정자가 붙은 형태(TrueSoft.Supabase.git)와 소문자 세그먼트는 멤버 참조가 아니다.
            foreach (Match hit in Regex.Matches(line, @"(?<![\w.])Supabase(?:IAP)?\.([A-Z]\w*)"))
            {
                var name = hit.Groups[1].Value;
                docApiNames.Add(name);
                if (!publicApi.ContainsKey(name))
                    errors.Add($"[R3 없는API] 문서가 Supabase.{name} 을 가리키지만 공개 멤버에 없습니다. ({rel}:{i + 1})");
            }

            // R4 — csharp 블록의 선언형 시그니처만 파라미터를 대조한다.
            if (!inCsharp)
                continue;

            var decl = Regex.Match(line, @"^\s*(?<ret>[A-Za-z_][\w<>,\[\]\?\. ]*?)\s+Supabase(?:IAP)?\.(?<name>[A-Za-z_]\w*)\s*\(");
            if (!decl.Success || line.Contains('=') || IsStatementKeyword(decl.Groups["ret"].Value.Trim()))
                continue;

            var mname = decl.Groups["name"].Value;
            if (!codeParams.TryGetValue(mname, out var overloads))
                continue; // R3 에서 이미 보고

            var docNames = ParseDocParamNames(lines, i, decl.Index + decl.Length - 1);
            if (docNames == null)
                continue;

            if (!overloads.Any(o => o.SequenceEqual(docNames, StringComparer.Ordinal)))
            {
                var expected = string.Join(" | ", overloads.Select(o => "(" + string.Join(", ", o) + ")"));
                errors.Add($"[R4 시그니처] Supabase.{mname} 문서 파라미터 ({string.Join(", ", docNames)}) 가 코드 {expected} 와 다릅니다. ({rel}:{i + 1})");
            }
        }
    }

    foreach (var kv in publicApi.OrderBy(x => x.Key, StringComparer.Ordinal))
        if (!docApiNames.Contains(kv.Key))
            warnings.Add($"[R3 미문서화] {kv.Value}.{kv.Key} 이 docs~/guide 어디에도 없습니다.");
}

// ── 리포트 ─────────────────────────────────────────────────────────────────
errors = errors.Distinct().ToList();
warnings = warnings.Distinct().ToList();

Console.WriteLine($"SDK 규칙 검사 — 공개 멤버 {publicApi.Count}개 · 파사드 Reset {facadesWithReset.Count}개 · 소스 {sources.Count}개");
Console.WriteLine();
foreach (var w in warnings)
    Console.WriteLine("  경고: " + w);
if (warnings.Count > 0)
    Console.WriteLine();
if (errors.Count == 0)
{
    Console.WriteLine("  ✔ R1 공개 표면 · R2 리셋 대칭성 · R3 문서 커버리지 · R4 시그니처 일치 통과.");
    Console.WriteLine();
    Console.WriteLine(warnings.Count == 0 ? "결과: OK" : $"결과: OK (경고 {warnings.Count}건)");
    return 0;
}
foreach (var e in errors)
    Console.WriteLine("  오류: " + e);
Console.WriteLine();
Console.WriteLine($"결과: 실패 — 오류 {errors.Count}건" + (warnings.Count > 0 ? $", 경고 {warnings.Count}건" : ""));
return 1;

// ── 헬퍼 ───────────────────────────────────────────────────────────────────
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

static string Rel(string root, string path) =>
    Path.GetRelativePath(root, path).Replace('\\', '/');

static bool Has(MemberDeclarationSyntax m, SyntaxKind kind) =>
    m.Modifiers.Any(t => t.IsKind(kind));

static string MemberName(MemberDeclarationSyntax m) => m switch
{
    MethodDeclarationSyntax x => x.Identifier.ValueText,
    PropertyDeclarationSyntax x => x.Identifier.ValueText,
    EventDeclarationSyntax x => x.Identifier.ValueText,
    EventFieldDeclarationSyntax x => x.Declaration.Variables.First().Identifier.ValueText,
    _ => null,
};

static int LineOf(Source src, SyntaxNode node) =>
    src.Text.Take(node.SpanStart).Count(ch => ch == '\n') + 1;

// 게임에 돌려줄 수 있는 반환 타입: result 계층 또는 그것을 감싼 Task.
static bool IsResultType(string ret)
{
    ret = ret.Trim();
    while (ret.StartsWith("Task<", StringComparison.Ordinal) && ret.EndsWith(">", StringComparison.Ordinal))
        ret = ret.Substring(5, ret.Length - 6).Trim();

    var outer = (ret.Contains('<') ? ret.Substring(0, ret.IndexOf('<')) : ret).Trim();
    return outer is "SupabaseResult" or "SupabaseLoadResult" or "SupabaseSignInResult";
}

static bool NullsField(string body, string field) =>
    Regex.IsMatch(body, @"(^|[^\w])" + Regex.Escape(field) + @"\s*=\s*null\s*;");

static bool CallsReset(string body, string field) =>
    Regex.IsMatch(body, Regex.Escape(field) + @"\s*\??\s*\.\s*Reset\s*\(");

static bool IsStatementKeyword(string token) =>
    token is "await" or "var" or "return" or "if" or "else" or "using" or "new" or "yield";

// 여는 괄호부터 짝이 맞는 닫는 괄호까지 읽어 파라미터 이름만 뽑는다.
static List<string> ParseDocParamNames(string[] lines, int startLine, int openParenIndex)
{
    var inside = new StringBuilder();
    var depth = 0;
    for (var i = startLine; i < lines.Length && i < startLine + 40; i++)
    {
        var from = i == startLine ? openParenIndex : 0;
        for (var c = from; c < lines[i].Length; c++)
        {
            var ch = lines[i][c];
            if (ch == '(')
            {
                depth++;
                if (depth == 1)
                    continue;
            }
            else if (ch == ')')
            {
                depth--;
                if (depth == 0)
                    return SplitParams(inside.ToString());
            }
            if (depth >= 1)
                inside.Append(ch);
        }
        inside.Append(' ');
    }
    return null; // 닫히지 않음 — 시그니처가 아니다
}

static List<string> SplitParams(string inside)
{
    var names = new List<string>();
    var depth = 0;
    var current = new StringBuilder();
    foreach (var ch in inside)
    {
        if (ch is '<' or '[' or '(') depth++;
        else if (ch is '>' or ']' or ')') depth--;

        if (ch == ',' && depth == 0)
        {
            AddName(names, current.ToString());
            current.Clear();
            continue;
        }
        current.Append(ch);
    }
    AddName(names, current.ToString());
    return names;
}

static void AddName(List<string> names, string chunk)
{
    // "int timeoutMs = 10_000" → timeoutMs
    var head = chunk.Split('=')[0].Trim();
    if (head.Length == 0)
        return;
    var m = Regex.Match(head, @"(\w+)\s*$");
    if (m.Success)
        names.Add(m.Groups[1].Value);
}

readonly struct Source
{
    public readonly string Path;
    public readonly string Text;
    public Source(string path, string text) { Path = path; Text = text; }
    public string Rel(string root) => System.IO.Path.GetRelativePath(root, Path).Replace('\\', '/');
}
