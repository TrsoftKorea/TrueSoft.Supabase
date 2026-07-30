using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SdkAudit
{
    /// <summary>R3 문서 커버리지 · R4 시그니처 일치 · R5 문서 형식 통일 · R8 문서 값 정합성.</summary>
    public static class DocRules
    {
        private sealed class DocSignature
        {
            public string Name;
            public int Line;
            public int FenceLine;
            public List<string> ParamNames;
        }

        public static void Run(AuditContext ctx)
        {
            var guideDir = Path.Combine(ctx.Root, "docs~", "guide");
            if (!Directory.Exists(guideDir))
            {
                ctx.Report.Warn("docs~/guide 를 찾지 못해 문서 검사를 건너뜁니다.");
                return;
            }

            var codeMethods = ctx.PublicMethods
                .GroupBy(m => m.Identifier.ValueText, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

            foreach (var md in Directory.EnumerateFiles(guideDir, "*.md", SearchOption.AllDirectories))
                CheckFile(ctx, md, codeMethods, isGuide: true);

            foreach (var kv in ctx.PublicApi.OrderBy(x => x.Key, StringComparer.Ordinal))
                if (!ctx.DocApiNames.Contains(kv.Key))
                    ctx.Report.Warn($"[R3 미문서화] {kv.Value}.{kv.Key} 이 docs~/guide 어디에도 없습니다.");

            // 규칙 파일도 코드를 가리킨다. 게임 문서가 아니라 **내가 읽고 그대로 고치는** 파일이라
            // 여기가 틀리면 잘못된 수정으로 이어진다(실제로 컴파일을 깨뜨린 적이 있다).
            // 형식 규칙(R5·R10)은 VitePress 페이지가 아니므로 적용하지 않는다.
            foreach (var md in EnumerateRuleFiles(ctx.Root))
                CheckFile(ctx, md, codeMethods, isGuide: false);
        }

        /// <summary>코드를 가리키는 규칙 파일 — 프로젝트 CLAUDE.md 와 스킬.</summary>
        private static IEnumerable<string> EnumerateRuleFiles(string root)
        {
            var claudeMd = Path.Combine(root, "CLAUDE.md");
            if (File.Exists(claudeMd))
                yield return claudeMd;

            var skills = Path.Combine(root, ".claude", "skills");
            if (!Directory.Exists(skills))
                yield break;

            foreach (var md in Directory.EnumerateFiles(skills, "*.md", SearchOption.AllDirectories))
                yield return md;
        }

        /// <param name="isGuide">
        /// true 면 게임 가이드로 보고 형식 규칙(R5·R10)과 커버리지 집계까지 적용한다.
        /// false 면 규칙 파일이라 코드 참조 정합성(R3·R4·R8 사유)만 본다.
        /// </param>
        private static void CheckFile(AuditContext ctx, string path, Dictionary<string, List<MethodDeclarationSyntax>> codeMethods, bool isGuide)
        {
            var rel = ctx.Rel(path);
            var lines = File.ReadAllLines(path);
            var signatures = new List<DocSignature>();

            var inCsharp = false;
            var inFence = false;
            var fenceStartLine = -1;
            var frontmatterEnd = FrontmatterEnd(lines);
            // 이미지가 많은 단계별 절차 페이지는 단계 구분 --- 을 허용한다.
            var hasImages = lines.Any(l => l.Contains("!["));
            var h1Line = -1;
            var firstH2Line = -1;

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var trimmed = line.TrimStart();
                var at = $"{rel}:{i + 1}";

                if (trimmed.StartsWith("```", StringComparison.Ordinal))
                {
                    if (inFence)
                    {
                        inFence = false;
                        inCsharp = false;
                    }
                    else
                    {
                        inFence = true;
                        inCsharp = trimmed.StartsWith("```csharp", StringComparison.Ordinal);
                        fenceStartLine = i;
                    }
                    continue;
                }

                // ── R3 문서가 가리키는 API 는 산문·코드 어디에 있어도 실제로 존재해야 한다 ──
                // 한정자가 붙은 형태(TrueSoft.Supabase.git)·소문자 세그먼트·로그 태그([Supabase.Chat])는 멤버 참조가 아니다.
                foreach (Match hit in Regex.Matches(line, @"(?<![\w.\[])Supabase(?:IAP)?\.([A-Z]\w*)"))
                {
                    var name = hit.Groups[1].Value;
                    if (IsPlaceholderName(name))
                        continue;

                    if (isGuide)
                        ctx.DocApiNames.Add(name); // 커버리지 집계는 게임 가이드만. 규칙 파일 언급으로 가려지면 안 된다.
                    if (!ctx.PublicApi.ContainsKey(name))
                        ctx.Report.Error($"[R3 없는API] 문서가 Supabase.{name} 을 가리키지만 공개 멤버에 없습니다. ({at})");
                }

                // ── R8 문서에 노출된 사유 표기 ── 대문자로 시작하는 멤버만. SupabaseReason.cs 같은 파일명은 제외.
                foreach (Match hit in Regex.Matches(line, @"\bSupabaseReason\.([A-Z]\w*)"))
                {
                    var reason = hit.Groups[1].Value;
                    if (IsPlaceholderName(reason))
                        continue;

                    if (!ctx.ReasonMembers.Contains(reason))
                        ctx.Report.Error($"[R8 없는사유] 문서가 SupabaseReason.{reason} 을 가리키지만 enum 에 없습니다. ({at})");
                }

                // 규칙 파일은 SDK 내부 카탈로그를 설명하는 자리라 SupabaseErrorCode 를 써도 된다.
                if (isGuide && Regex.IsMatch(line, @"\bSupabaseErrorCode\b"))
                    ctx.Report.Error($"[R8 internal노출] SupabaseErrorCode 는 SDK 전용(internal)이라 게임 문서에 노출하지 않습니다. 게임은 SupabaseReason 으로 분기합니다. ({at})");

                if (inFence)
                {
                    if (!inCsharp)
                        continue;

                    // ── R5 시그니처에 수식어를 쓰지 않는다 ── 파사드 시그니처만 본다(예제 코드의 구현체 선언은 제외).
                    if (isGuide
                        && Regex.IsMatch(trimmed, @"^(public|internal|private|protected|static|async)\b")
                        && Regex.IsMatch(line, @"(?<![\w.\[])Supabase(?:IAP)?\.[A-Z]\w*\s*\("))
                        ctx.Report.Error($"[R5 시그니처수식어] 시그니처에서 public·static·async 등 수식어를 뺍니다. ({at})");

                    // ── R4 선언형 시그니처 수집 ──
                    var sig = ParseSignature(lines, i);
                    if (sig != null)
                    {
                        sig.FenceLine = fenceStartLine;
                        signatures.Add(sig);
                    }
                    continue;
                }

                // ── 이하 펜스 밖 ── 여기부터는 VitePress 페이지 형식 규칙이라 규칙 파일에는 적용하지 않는다.
                if (!isGuide || i < frontmatterEnd)
                    continue;

                // ── R5 GitHub 알림 문법 금지 ──
                if (Regex.IsMatch(trimmed, @"^>\s*\[!(NOTE|TIP|WARNING|IMPORTANT|CAUTION)\]"))
                    ctx.Report.Error($"[R5 알림문법] GitHub 알림 문법은 VitePress 가 렌더하지 못합니다. ::: 컨테이너를 쓰세요. ({at})");

                // ── R5 장식용 수평선 ──
                if (trimmed == "---")
                {
                    if (IsLastContentLine(lines, i))
                        ctx.Report.Error($"[R5 매달린구분선] 페이지가 --- 로 끝납니다. ({at})");
                    else if (!hasImages)
                        ctx.Report.Warn($"[R5 수평선] 본문 장식용 수평선은 ## 와 여백으로 대체합니다. ({at})");
                }

                if (trimmed.StartsWith("#", StringComparison.Ordinal))
                {
                    var level = trimmed.TakeWhile(c => c == '#').Count();
                    if (level == 1 && h1Line < 0)
                        h1Line = i;
                    if (level == 2 && firstH2Line < 0)
                        firstH2Line = i;

                    // ── R5 헤딩 부연 괄호 금지 ── 메서드 호출 표기 Dispose() 는 공백이 없어 걸리지 않는다.
                    var headingText = Regex.Replace(trimmed, @"\{#[\w-]+\}\s*$", "").TrimEnd();
                    if (Regex.IsMatch(headingText, @"\s\([^)]*\)"))
                        ctx.Report.Error($"[R5 헤딩괄호] 헤딩에 부연 괄호를 쓰지 않습니다. 별도 문장이나 콜아웃으로 옮기세요. ({at})");
                }
            }

            // ── R5 H1 아래 본문에는 ## 를 붙인다 ──
            if (isGuide && h1Line >= 0 && firstH2Line > h1Line)
                CheckOutlineGap(ctx, rel, lines, h1Line, firstH2Line);

            // ── R5 한 페이지에 시그니처 2개 이상 ──
            if (isGuide && signatures.Count > 1)
                ctx.Report.Warn($"[R5 시그니처다수] {rel} 에 시그니처가 {signatures.Count}개 있습니다({string.Join(", ", signatures.Select(s => s.Name))}). 메서드마다 페이지를 나눕니다.");

            // ── R10 헤딩 바로 다음에 시그니처가 와야 한다(코드 우선) ──
            if (isGuide)
                foreach (var sig in signatures)
                {
                    var prev = PreviousContentLine(lines, sig.FenceLine);
                    if (prev >= 0 && !lines[prev].TrimStart().StartsWith("#", StringComparison.Ordinal))
                        ctx.Report.Error($"[R10 코드우선] 헤딩과 시그니처 사이에 설명이 있습니다. 설명은 코드 아래로 옮기세요. ({rel}:{sig.FenceLine + 1})");
                }

            // ── R10 파라미터 표는 타입 열 없이 2열 ──
            // 첫 칸이 정확히 "파라미터"이고 다음 줄이 구분행인 **헤더 행**만 본다(셀 안의 단어에 걸리지 않도록).
            for (var i = 0; isGuide && i < lines.Length - 1; i++)
            {
                var t = lines[i].TrimStart();
                if (!t.StartsWith("|", StringComparison.Ordinal))
                    continue;

                var cells = t.Trim('|').Split('|');
                if (cells[0].Trim() != "파라미터")
                    continue;
                if (!Regex.IsMatch(lines[i + 1].Trim(), @"^\|[\s:|-]+\|$"))
                    continue;

                if (cells.Length != 2)
                    ctx.Report.Error($"[R10 파라미터표] 파라미터 표는 타입 열 없이 2열입니다(현재 {cells.Length}열). ({rel}:{i + 1})");
                break;
            }

            // ── R4·R8 시그니처와 파라미터 표를 코드와 대조 ──
            foreach (var sig in signatures)
            {
                if (!codeMethods.TryGetValue(sig.Name, out var overloads))
                    continue; // R3 에서 이미 보고

                if (!overloads.Any(o => ParamNames(o).SequenceEqual(sig.ParamNames, StringComparer.Ordinal)))
                {
                    var expected = string.Join(" | ", overloads.Select(o => "(" + string.Join(", ", ParamNames(o)) + ")"));
                    ctx.Report.Error($"[R4 시그니처] Supabase.{sig.Name} 문서 파라미터 ({string.Join(", ", sig.ParamNames)}) 가 코드 {expected} 와 다릅니다. ({rel}:{sig.Line})");
                    continue;
                }

                // 파라미터 표 대조는 함수 페이지 형식을 전제한다. 규칙 파일의 예시 표에는 적용하지 않는다.
                if (isGuide && signatures.Count == 1 && overloads.Count == 1)
                    CheckParamTable(ctx, rel, lines, overloads[0], sig);
            }
        }

        /// <summary>H1 과 첫 H2 사이에 코드블록·표·2단락 이상이 있으면 그 본문은 책갈피에 잡히지 않는다.</summary>
        private static void CheckOutlineGap(AuditContext ctx, string rel, string[] lines, int h1Line, int firstH2Line)
        {
            var hasBlock = false;
            var paragraphs = 0;
            var inParagraph = false;
            var inCallout = false;
            var inFence = false;

            for (var i = h1Line + 1; i < firstH2Line; i++)
            {
                var t = lines[i].TrimStart();

                if (t.StartsWith("```", StringComparison.Ordinal))
                {
                    inFence = !inFence;
                    if (inFence)
                        hasBlock = true;
                    continue;
                }
                if (inFence)
                    continue;

                if (t.StartsWith(":::", StringComparison.Ordinal))
                {
                    inCallout = !inCallout;
                    continue;
                }
                if (inCallout)
                    continue;

                if (t.Length == 0)
                {
                    inParagraph = false;
                    continue;
                }

                if (t.StartsWith("|", StringComparison.Ordinal))
                    hasBlock = true;

                if (!inParagraph)
                {
                    paragraphs++;
                    inParagraph = true;
                }
            }

            if (hasBlock || paragraphs >= 2)
                ctx.Report.Error($"[R5 책갈피누락] H1 바로 아래 본문에 ## 제목이 없어 우측 책갈피에서 빠집니다. ({rel}:{h1Line + 1})");
        }

        /// <summary>파라미터 표를 코드 시그니처와 대조한다.</summary>
        private static void CheckParamTable(AuditContext ctx, string rel, string[] lines, MethodDeclarationSyntax method, DocSignature sig)
        {
            var codeParams = method.ParameterList.Parameters
                .ToDictionary(p => p.Identifier.ValueText, p => p.Default?.Value.ToString(), StringComparer.Ordinal);

            var rows = FindParamTableRows(lines);
            if (rows.Count == 0)
            {
                if (codeParams.Count > 0)
                    ctx.Report.Warn($"[R8 파라미터표없음] Supabase.{sig.Name} 은 파라미터가 {codeParams.Count}개인데 문서에 파라미터 표가 없습니다. ({rel})");
                return;
            }

            foreach (var (name, cell, line) in rows)
            {
                if (!codeParams.TryGetValue(name, out var codeDefault))
                {
                    ctx.Report.Error($"[R8 없는파라미터] 파라미터 표의 {name} 이 Supabase.{sig.Name} 시그니처에 없습니다. ({rel}:{line})");
                    continue;
                }

                var docDefault = Regex.Match(cell, @"\(기본값:\s*`?([^)`]+)`?\s*\)");
                if (docDefault.Success == false)
                {
                    if (codeDefault != null)
                        ctx.Report.Warn($"[R8 기본값누락] {name} 은 코드 기본값이 {codeDefault} 인데 문서에 (기본값: ...) 표기가 없습니다. ({rel}:{line})");
                    continue;
                }

                if (codeDefault == null)
                {
                    ctx.Report.Error($"[R8 기본값불일치] 문서는 {name} 에 기본값이 있다고 하는데 코드에는 없습니다. ({rel}:{line})");
                    continue;
                }

                if (Normalize(docDefault.Groups[1].Value) != Normalize(codeDefault))
                    ctx.Report.Error($"[R8 기본값불일치] {name} 문서 기본값 {docDefault.Groups[1].Value.Trim()} != 코드 {codeDefault}. ({rel}:{line})");
            }

            var missing = codeParams.Keys.Where(k => rows.All(r => r.Name != k)).ToList();
            if (missing.Count > 0)
                ctx.Report.Warn($"[R8 파라미터누락] Supabase.{sig.Name} 의 {string.Join(", ", missing)} 이 파라미터 표에 없습니다. 이름만으로 의미가 명확하면 무시하세요. ({rel})");
        }

        /// <summary>헤더에 '파라미터'가 있는 표의 데이터 행을 (첫 칸 이름, 둘째 칸, 줄번호)로 돌려준다.</summary>
        private static List<(string Name, string Cell, int Line)> FindParamTableRows(string[] lines)
        {
            var rows = new List<(string, string, int)>();
            for (var i = 0; i < lines.Length; i++)
            {
                if (!lines[i].TrimStart().StartsWith("|", StringComparison.Ordinal))
                    continue;
                if (!lines[i].Contains("파라미터"))
                    continue;

                // 헤더 다음 구분행을 지나 데이터 행을 읽는다.
                for (var j = i + 2; j < lines.Length; j++)
                {
                    var t = lines[j].TrimStart();
                    if (!t.StartsWith("|", StringComparison.Ordinal))
                        break;

                    var cells = t.Trim('|').Split('|');
                    if (cells.Length < 2)
                        continue;

                    var name = cells[0].Trim().Trim('`').Trim();
                    if (name.Length > 0)
                        rows.Add((name, cells[1], j + 1));
                }
                break;
            }
            return rows;
        }

        /// <summary>선언형 시그니처면 파라미터 이름을 뽑고, 호출 예제면 null.</summary>
        private static DocSignature ParseSignature(string[] lines, int index)
        {
            var line = lines[index];
            var decl = Regex.Match(line, @"^\s*(?<ret>[A-Za-z_][\w<>,\[\]\?\. ]*?)\s+Supabase(?:IAP)?\.(?<name>[A-Za-z_]\w*)\s*\(");
            if (!decl.Success || line.Contains('=') || IsStatementKeyword(decl.Groups["ret"].Value.Trim()))
                return null;

            var names = ParseDocParamNames(lines, index, decl.Index + decl.Length - 1);
            if (names == null)
                return null;

            return new DocSignature { Name = decl.Groups["name"].Value, Line = index + 1, ParamNames = names };
        }

        private static List<string> ParamNames(MethodDeclarationSyntax m) =>
            m.ParameterList.Parameters.Select(p => p.Identifier.ValueText).ToList();

        /// <summary>숫자 리터럴 접미사와 자릿수 구분자를 없애 문서 표기(2)와 코드(2f)를 같게 본다.</summary>
        private static string Normalize(string v)
        {
            var s = v.Trim().Trim('`').Replace("_", "").Replace(" ", "");
            return Regex.IsMatch(s, @"^\d+(\.\d+)?[fFdDmM]$") ? s.Substring(0, s.Length - 1) : s;
        }

        private static int FrontmatterEnd(string[] lines)
        {
            if (lines.Length == 0 || lines[0].Trim() != "---")
                return 0;
            for (var i = 1; i < lines.Length; i++)
                if (lines[i].Trim() == "---")
                    return i + 1;
            return 0;
        }

        /// <summary>주어진 줄 위쪽에서 가장 가까운 내용 있는 줄. 없으면 -1.</summary>
        private static int PreviousContentLine(string[] lines, int index)
        {
            for (var i = index - 1; i >= 0; i--)
                if (lines[i].Trim().Length > 0)
                    return i;
            return -1;
        }

        private static bool IsLastContentLine(string[] lines, int index)
        {
            for (var i = index + 1; i < lines.Length; i++)
                if (lines[i].Trim().Length > 0)
                    return false;
            return true;
        }

        /// <summary>
        /// 규칙 문서가 형식을 설명할 때 쓰는 자리표시자(<c>Supabase.Xxx()</c>·<c>SupabaseReason.멤버명</c>·<c>Supabase.Try*</c>).
        /// 실제 멤버 이름이 아니므로 실존 검사에서 뺀다.
        /// </summary>
        private static bool IsPlaceholderName(string name) =>
            name == "Try" || Regex.IsMatch(name, @"^Xx*(Async)?$");

        private static bool IsStatementKeyword(string token) =>
            token is "await" or "var" or "return" or "if" or "else" or "using" or "new" or "yield";

        /// <summary>여는 괄호부터 짝이 맞는 닫는 괄호까지 읽어 파라미터 이름만 뽑는다.</summary>
        private static List<string> ParseDocParamNames(string[] lines, int startLine, int openParenIndex)
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

        private static List<string> SplitParams(string inside)
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

        private static void AddName(List<string> names, string chunk)
        {
            // "int timeoutMs = 10_000" → timeoutMs
            var head = chunk.Split('=')[0].Trim();
            if (head.Length == 0)
                return;
            var m = Regex.Match(head, @"(\w+)\s*$");
            if (m.Success)
                names.Add(m.Groups[1].Value);
        }
    }
}
