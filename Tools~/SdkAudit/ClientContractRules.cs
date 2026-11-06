using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SdkAudit
{
    /// <summary>
    /// R23 옛 빌드 호환. 플레이어 폰에 깔린 게임은 업데이트 전까지 옛 SDK 로 서버를 부른다. 서버는 적용하는 즉시 전원에게
    /// 바뀌므로, 함수를 지우거나 인자를 바꾸면 그 빌드의 기능만 조용히 실패한다.
    /// <para>기준 목록(client-contract.txt)은 지금까지 나간 SDK 가 부르는 것을 더하기만 한다. 코드에서 호출이 사라져도 줄은 남는다 —
    /// 그 호출을 하는 옛 빌드가 아직 있을 수 있어서다. 더는 없다고 판단하면 지우지 말고 retired 와 이유를 적는다.</para>
    /// <para>못 보는 것: 응답 내용의 변화, 함수 안쪽 동작, 게임별 세이브 칸, 라이브 DB 와 install.sql 의 차이.</para>
    /// </summary>
    public static class ClientContractRules
    {
        public const string ContractRelPath = "Tools~/SdkAudit/client-contract.txt";

        // 코드에서 뽑은 것이 이보다 적으면 뽑기가 고장 난 것이다. 그대로 두면 빠진 호출이 조용히 검사에서 사라진다.
        private const int MinExtractedRpcs = 40;

        private static readonly Regex RpcCallLiteral = new(@"""(ts_[a-z0-9_]+)""");
        private static readonly Regex RpcUrl = new(@"/rest/v1/rpc/(ts_[a-z0-9_]+)");
        private static readonly Regex ArgToken = new(@"\b(p_[a-z0-9_]+)\b");
        private static readonly Regex EdgeConst = new(@"FunctionName\w*\s*=\s*""([a-z][a-z0-9-]+)""");
        private static readonly Regex EdgeUrl = new(@"functions/v1/([a-z][a-z0-9-]+)");

        // SDK 가 표를 직접 읽는 파일. 기본 이름은 설정으로 바뀔 수 있고 옛 판은 이름이 달라 깃 기록에서 뽑지 않는다.
        private static readonly Dictionary<string, string> TableFiles = new(StringComparer.Ordinal)
        {
            ["SupabaseMailboxService.cs"] = "mails",
            ["SupabasePublicProfileService.cs"] = "user_profiles",
            ["SupabaseRemoteConfigService.cs"] = "remote_config",
            ["SupabaseUserSessionService.cs"] = "user_sessions",
        };

        // ── 코드에서 호출 뽑기 ───────────────────────────────────────────────

        /// <summary>소스에서 DB 함수·서버 함수 호출을 뽑는다. 줄 꼴은 기준 목록과 같다.</summary>
        public static SortedSet<string> ExtractCalls(IEnumerable<(string Name, string Text)> sources, bool includeTables)
        {
            var found = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var (name, text) in sources)
            {
                foreach (Match m in EdgeConst.Matches(text)) found.Add("edge " + m.Groups[1].Value);
                foreach (Match m in EdgeUrl.Matches(text)) found.Add("edge " + m.Groups[1].Value);

                if (text.Contains("ts_"))
                    ExtractRpcs(text, found);

                if (includeTables && TableFiles.TryGetValue(Path.GetFileName(name), out var table))
                    ExtractTable(table, text, found);
            }
            return found;
        }

        private static void ExtractRpcs(string text, SortedSet<string> found)
        {
            var root = CSharpSyntaxTree.ParseText(text).GetRoot();
            var classBodies = root.DescendantNodes().OfType<ClassDeclarationSyntax>().ToList();

            foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                var body = (SyntaxNode)method.Body ?? method.ExpressionBody;
                if (body == null)
                    continue;
                var src = body.ToFullString();

                var hits = new List<(string Rpc, int At, bool IsUrl)>();
                foreach (Match m in RpcUrl.Matches(src)) hits.Add((m.Groups[1].Value, m.Index, true));
                // 글자 그대로의 함수 이름은 RPC 호출의 인자로 넘길 때만 센다. 로그 문구·사유 문자열의 ts_ 를 호출로 읽지 않게.
                foreach (var call in body.DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    if (!(call.Expression is MemberAccessExpressionSyntax ma ? ma.Name.Identifier.ValueText : (call.Expression as IdentifierNameSyntax)?.Identifier.ValueText ?? "").Contains("Rpc"))
                        continue;
                    foreach (var arg in call.ArgumentList.Arguments)
                    {
                        if (arg.Expression is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.StringLiteralExpression)
                            && Regex.IsMatch(lit.Token.ValueText, "^ts_[a-z0-9_]+$"))
                            hits.Add((lit.Token.ValueText, call.Span.End - body.FullSpan.Start, false));
                    }
                }
                hits = hits.OrderBy(h => h.At).ToList();

                for (var i = 0; i < hits.Count; i++)
                {
                    // 호출 형은 본문을 먼저 만들고, 주소 형은 주소 뒤에 만드는 코드가 많다. 함수 하나에 호출이 하나면 메서드 전체가 그 호출 몫이다.
                    string segment;
                    if (hits.Count == 1)
                        segment = src;
                    else if (hits[i].IsUrl)
                        segment = src.Substring(hits[i].At, (i + 1 < hits.Count ? hits[i + 1].At : src.Length) - hits[i].At);
                    else
                    {
                        var from = i > 0 ? hits[i - 1].At : 0;
                        segment = src.Substring(from, hits[i].At - from);
                    }

                    // 본문을 만드는 도우미(BuildXxxBody)를 부르면 그 안의 인자까지 본다.
                    var helperText = string.Concat(Regex.Matches(segment, @"\b(Build\w*)\s*\(").Select(h => h.Groups[1].Value).Distinct()
                        .SelectMany(helper => root.DescendantNodes().OfType<MethodDeclarationSyntax>().Where(x => x.Identifier.ValueText == helper))
                        .Select(x => x.ToFullString()));

                    var args = ArgToken.Matches(segment + helperText).Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal)
                        .OrderBy(a => a, StringComparer.Ordinal);
                    found.Add($"rpc {hits[i].Rpc} {string.Join(",", args)}".TrimEnd());
                }
            }
        }

        private static void ExtractTable(string table, string text, SortedSet<string> found)
        {
            var columns = new SortedSet<string>(StringComparer.Ordinal);
            foreach (Match m in Regex.Matches(text, @"SelectColumns\s*=\s*((?:""[^""]*""\s*\+?\s*)+);"))
                foreach (Match part in Regex.Matches(m.Groups[1].Value, @"""([^""]*)"""))
                    AddColumnList(part.Groups[1].Value, columns);
            foreach (Match m in Regex.Matches(text, @"[?&]select=([a-z0-9_,]+)"))
                AddColumnList(m.Groups[1].Value, columns);
            foreach (Match m in Regex.Matches(text, @"[?&]([a-z_][a-z0-9_]*)=(?:eq|neq|gt|gte|lt|lte|in|is)\."))
                columns.Add(m.Groups[1].Value);
            foreach (Match m in Regex.Matches(text, @"(?:on_conflict|order)=([a-z_][a-z0-9_]*)"))
                columns.Add(m.Groups[1].Value);
            if (columns.Count > 0)
                found.Add($"table {table} {string.Join(",", columns)}");
        }

        private static void AddColumnList(string csv, SortedSet<string> into)
        {
            foreach (var c in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (Regex.IsMatch(c, "^[a-z_][a-z0-9_]*$"))
                    into.Add(c);
        }

        // ── install.sql 읽기 ─────────────────────────────────────────────────

        private sealed class SqlFunction
        {
            public string TypesKey;
            public List<(string Name, bool HasDefault)> Params = new();
        }

        /// <summary>install.sql 을 위에서 아래로 따라가 끝났을 때 남는 함수 판들을 돌려준다.</summary>
        public static Dictionary<string, List<(string TypesKey, List<(string Name, bool HasDefault)> Params)>> ParseFunctions(string sql)
        {
            var clean = StripLineComments(sql);
            var state = new Dictionary<string, List<SqlFunction>>(StringComparer.Ordinal);
            var head = new Regex(@"\b(create\s+(?:or\s+replace\s+)?function|drop\s+function(?:\s+if\s+exists)?)\s+(?:public\.)?""?([a-z_][a-z0-9_]*)""?\s*(\(|;|\s+cascade)", RegexOptions.IgnoreCase);
            foreach (Match m in head.Matches(clean))
            {
                var isDrop = m.Groups[1].Value.StartsWith("drop", StringComparison.OrdinalIgnoreCase);
                var name = m.Groups[2].Value.ToLowerInvariant();
                if (m.Groups[3].Value != "(")
                {
                    if (isDrop) state.Remove(name);
                    continue;
                }

                var inner = ReadParens(clean, m.Groups[3].Index);
                var parsed = ParseParams(inner);
                if (!state.TryGetValue(name, out var list))
                    state[name] = list = new List<SqlFunction>();
                list.RemoveAll(f => f.TypesKey == parsed.TypesKey);
                if (!isDrop)
                    list.Add(parsed);
            }
            return state.Where(p => p.Value.Count > 0).ToDictionary(
                p => p.Key,
                p => p.Value.Select(f => (f.TypesKey, f.Params)).ToList(),
                StringComparer.Ordinal);
        }

        private static string StripLineComments(string sql) => Regex.Replace(sql, @"--[^\n]*", "");

        private static string ReadParens(string text, int open)
        {
            var depth = 0;
            for (var i = open; i < text.Length; i++)
            {
                if (text[i] == '(') depth++;
                else if (text[i] == ')' && --depth == 0)
                    return text.Substring(open + 1, i - open - 1);
            }
            return "";
        }

        private static SqlFunction ParseParams(string inner)
        {
            var fn = new SqlFunction();
            var types = new List<string>();
            foreach (var raw in SplitTopLevel(inner))
            {
                var part = Regex.Replace(raw.Trim(), @"\s+", " ");
                if (part.Length == 0)
                    continue;
                if (Regex.IsMatch(part, @"^out\s", RegexOptions.IgnoreCase))
                    continue;
                part = Regex.Replace(part, @"^(in|inout|variadic)\s", "", RegexOptions.IgnoreCase);

                var hasDefault = Regex.IsMatch(part, @"\s(default\s|=\s*)", RegexOptions.IgnoreCase);
                var noDefault = Regex.Split(part, @"\s(?:default\s|=\s*)", RegexOptions.IgnoreCase)[0].Trim();
                var m = Regex.Match(noDefault, @"^""?([a-z_][a-z0-9_]*)""?\s+(.+)$", RegexOptions.IgnoreCase);
                // 이름 없는 인자(drop 의 형식 목록): 인자 전체가 형식이다.
                if (m.Success && m.Groups[1].Value.StartsWith("p_", StringComparison.OrdinalIgnoreCase))
                {
                    fn.Params.Add((m.Groups[1].Value.ToLowerInvariant(), hasDefault));
                    types.Add(NormalizeType(m.Groups[2].Value));
                }
                else
                    types.Add(NormalizeType(noDefault));
            }
            fn.TypesKey = string.Join(",", types);
            return fn;
        }

        private static IEnumerable<string> SplitTopLevel(string s)
        {
            var depth = 0;
            var start = 0;
            var inQuote = false;
            for (var i = 0; i < s.Length; i++)
            {
                var c = s[i];
                if (c == '\'') inQuote = !inQuote;
                if (inQuote) continue;
                if (c == '(' || c == '[') depth++;
                else if (c == ')' || c == ']') depth--;
                else if (c == ',' && depth == 0)
                {
                    yield return s.Substring(start, i - start);
                    start = i + 1;
                }
            }
            yield return s.Substring(start);
        }

        private static string NormalizeType(string t)
        {
            t = Regex.Replace(t.Trim().ToLowerInvariant(), @"\s+", " ").Replace("public.", "");
            return t switch
            {
                "int" or "int4" => "integer",
                "int8" => "bigint",
                "bool" => "boolean",
                "float8" or "double precision" => "double precision",
                "timestamp with time zone" => "timestamptz",
                _ => t,
            };
        }

        private static Dictionary<string, HashSet<string>> ParseTables(string sql)
        {
            var clean = StripLineComments(sql);
            var tables = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (Match m in Regex.Matches(clean, @"create\s+(?:unlogged\s+)?table\s+(?:if\s+not\s+exists\s+)?public\.""?([a-z_][a-z0-9_]*)""?\s*\(", RegexOptions.IgnoreCase))
            {
                var name = m.Groups[1].Value.ToLowerInvariant();
                if (!tables.TryGetValue(name, out var cols))
                    tables[name] = cols = new HashSet<string>(StringComparer.Ordinal);
                var inner = ReadParens(clean, m.Index + m.Length - 1);
                foreach (var raw in SplitTopLevel(inner))
                {
                    var c = Regex.Match(raw.Trim(), @"^""?([a-z_][a-z0-9_]*)""?\s", RegexOptions.IgnoreCase);
                    if (c.Success && !Regex.IsMatch(c.Groups[1].Value, "^(primary|unique|check|foreign|constraint|exclude)$", RegexOptions.IgnoreCase))
                        cols.Add(c.Groups[1].Value.ToLowerInvariant());
                }
            }
            foreach (Match m in Regex.Matches(clean, @"alter\s+table\s+(?:if\s+exists\s+)?(?:only\s+)?public\.""?([a-z_][a-z0-9_]*)""?\s+([^;]+);", RegexOptions.IgnoreCase))
            {
                if (!tables.TryGetValue(m.Groups[1].Value.ToLowerInvariant(), out var cols))
                    continue;
                var action = m.Groups[2].Value;
                foreach (Match a in Regex.Matches(action, @"add\s+column\s+(?:if\s+not\s+exists\s+)?""?([a-z_][a-z0-9_]*)", RegexOptions.IgnoreCase)) cols.Add(a.Groups[1].Value.ToLowerInvariant());
                foreach (Match d in Regex.Matches(action, @"drop\s+column\s+(?:if\s+exists\s+)?""?([a-z_][a-z0-9_]*)", RegexOptions.IgnoreCase)) cols.Remove(d.Groups[1].Value.ToLowerInvariant());
                var r = Regex.Match(action, @"rename\s+column\s+""?([a-z_][a-z0-9_]*)""?\s+to\s+""?([a-z_][a-z0-9_]*)", RegexOptions.IgnoreCase);
                if (r.Success && cols.Remove(r.Groups[1].Value.ToLowerInvariant())) cols.Add(r.Groups[2].Value.ToLowerInvariant());
            }
            return tables;
        }

        // ── 기준 목록 ────────────────────────────────────────────────────────

        private static List<string> ReadContract(string path, out HashSet<string> retired, out List<string> badRetired)
        {
            retired = new HashSet<string>(StringComparer.Ordinal);
            badRetired = new List<string>();
            var active = new List<string>();
            foreach (var raw in File.ReadAllLines(path))
            {
                var hash = raw.IndexOf('#');
                var line = (hash >= 0 ? raw.Substring(0, hash) : raw).Trim();
                if (line.Length == 0)
                    continue;
                if (line.StartsWith("retired ", StringComparison.Ordinal))
                {
                    var entry = line.Substring("retired ".Length).Trim();
                    retired.Add(entry);
                    if (hash < 0 || raw.Substring(hash + 1).Trim().Length == 0)
                        badRetired.Add(entry);
                    continue;
                }
                active.Add(line);
            }
            return active;
        }

        public static void Run(AuditContext ctx)
        {
            var path = Path.Combine(ctx.Root, ContractRelPath.Replace('/', Path.DirectorySeparatorChar));
            var sqlPath = Path.Combine(ctx.Root, "Samples~", "DatabaseSetup", "SQL", "player", "install.sql");
            if (!File.Exists(sqlPath))
                return;
            if (!File.Exists(path))
            {
                ctx.Report.Error($"[R23 기준없음] {ContractRelPath} 가 없습니다 — 옛 빌드 호환을 볼 기준이 없습니다.");
                return;
            }

            var active = ReadContract(path, out var retired, out var badRetired);
            foreach (var b in badRetired)
                ctx.Report.Error($"[R23 이유없음] `{b}` 를 retired 로 표시했는데 # 뒤에 이유가 없습니다 — 왜 더는 옛 빌드가 없다고 봤는지 적으세요. ({ContractRelPath})");

            var known = new HashSet<string>(active, StringComparer.Ordinal);
            known.UnionWith(retired);

            var extracted = ExtractCalls(ctx.RuntimeSources.Select(s => (s.Path, s.Text)), includeTables: true);
            if (extracted.Count(e => e.StartsWith("rpc ", StringComparison.Ordinal)) < MinExtractedRpcs)
                ctx.Report.Error($"[R23 뽑기고장] 코드에서 찾은 DB 함수 호출이 {MinExtractedRpcs}개보다 적습니다. 호출 모양이 바뀌었으면 검사기를 고치세요. 그대로 두면 빠진 호출을 못 봅니다.");
            foreach (var e in extracted)
            {
                if (!known.Contains(e))
                    ctx.Report.Error($"[R23 기준누락] 코드가 부르는 `{e}` 가 기준 목록에 없습니다. 이 빌드가 나가면 지켜야 할 약속이니 " +
                                     "`dotnet run --project Tools~/SdkAudit -- --update-contract` 로 더하세요.");
            }

            var edgeDir = Path.Combine(ctx.Root, "Samples~", "DatabaseSetup", "EdgeFunctions");
            foreach (var problem in FindBroken(active, File.ReadAllText(sqlPath), edgeDir))
                ctx.Report.Error(problem);
        }

        private static IEnumerable<string> FindBroken(IEnumerable<string> active, string sql, string edgeDir)
        {
            var functions = ParseFunctions(sql);
            var tables = ParseTables(sql);
            var granted = ParseClientGrants(sql);
            foreach (var entry in active)
            {
                var problem = CheckEntry(entry, functions, granted, tables, edgeDir);
                if (problem != null)
                    yield return $"[R23 옛빌드호환] `{entry}` — {problem}. 이미 깔린 게임이 이 호출로 실패합니다. 옛 형태를 남겨 함께 받으세요. " +
                                 $"정말 그 빌드가 없으면 {ContractRelPath} 의 그 줄을 `retired … # 이유` 로 바꾸세요.";
            }
        }

        // 위에서 아래로 따라가며 마지막 상태를 본다. 정의 때 회수하고 24절에서 다시 주는 함수가 많고, 이 순서가 뒤집히면 클라이언트가 못 부른다.
        private static HashSet<string> ParseClientGrants(string sql)
        {
            var granted = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match m in Regex.Matches(StripLineComments(sql),
                         @"\b(grant|revoke)\s+(?:execute|all(?:\s+privileges)?)\s+on\s+function\s+public\.(\w+)\s*\([^)]*\)\s+(?:to|from)\s+([^;]+);", RegexOptions.IgnoreCase))
            {
                if (!Regex.IsMatch(m.Groups[3].Value, @"\b(authenticated|anon)\b", RegexOptions.IgnoreCase))
                    continue;
                var name = m.Groups[2].Value.ToLowerInvariant();
                if (m.Groups[1].Value.Equals("grant", StringComparison.OrdinalIgnoreCase)) granted.Add(name);
                else granted.Remove(name);
            }
            return granted;
        }

        /// <summary>
        /// 라이브에 적용하기 직전 검사. install.sql 뒤에 적용할 SQL 을 이어 붙인 상태로 기준 목록을 대조한다.
        /// 작업이 끝날 때 도는 검사는 이미 적용된 뒤라 늦고, install.sql 을 아직 안 고친 적용도 여기서는 걸린다.
        /// </summary>
        public static int CheckMigration(string root, string migrationPath)
        {
            var contract = Path.Combine(root, ContractRelPath.Replace('/', Path.DirectorySeparatorChar));
            var sql = File.ReadAllText(Path.Combine(root, "Samples~", "DatabaseSetup", "SQL", "player", "install.sql"))
                      + "\n" + File.ReadAllText(migrationPath);
            var edgeDir = Path.Combine(root, "Samples~", "DatabaseSetup", "EdgeFunctions");
            var problems = FindBroken(ReadContract(contract, out _, out _), sql, edgeDir).ToList();
            foreach (var p in problems)
                Console.WriteLine(p);
            return problems.Count == 0 ? 0 : 2;
        }

        private static string CheckEntry(
            string entry,
            Dictionary<string, List<(string TypesKey, List<(string Name, bool HasDefault)> Params)>> functions,
            HashSet<string> granted,
            Dictionary<string, HashSet<string>> tables,
            string edgeDir)
        {
            var parts = entry.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            switch (parts[0])
            {
                case "rpc":
                {
                    var name = parts[1];
                    var keys = parts.Length > 2 ? parts[2].Split(',').ToHashSet(StringComparer.Ordinal) : new HashSet<string>(StringComparer.Ordinal);
                    if (!functions.TryGetValue(name, out var versions))
                        return "install.sql 에 이 함수가 없습니다";
                    if (!granted.Contains(name))
                        return "클라이언트(authenticated·anon) 실행 권한이 없습니다";
                    // 서버는 보낸 인자 이름이 모두 있고 기본값 없는 인자가 다 채워진 판을 고른다.
                    var match = versions.Any(v => keys.All(k => v.Params.Any(p => p.Name == k))
                                                  && v.Params.Where(p => !p.HasDefault).All(p => keys.Contains(p.Name)));
                    if (!match)
                    {
                        var have = string.Join(" / ", versions.Select(v => "(" + string.Join(", ", v.Params.Select(p => p.HasDefault ? p.Name + "?" : p.Name)) + ")"));
                        return $"이 인자로 부를 수 있는 판이 없습니다. 지금 판: {have}";
                    }
                    return null;
                }
                case "edge":
                    return Directory.Exists(Path.Combine(edgeDir, parts[1])) ? null : "서버 함수 폴더가 없습니다";
                case "table":
                {
                    if (!tables.TryGetValue(parts[1], out var cols))
                        return "install.sql 에 이 표가 없습니다";
                    var missing = (parts.Length > 2 ? parts[2].Split(',') : Array.Empty<string>()).Where(c => !cols.Contains(c)).ToList();
                    return missing.Count == 0 ? null : "칸이 없습니다: " + string.Join(", ", missing);
                }
                default:
                    return "알 수 없는 줄입니다";
            }
        }

        // ── 기준 목록 더하기 ─────────────────────────────────────────────────

        /// <summary>지금 코드가 부르는데 목록에 없는 줄을 더한다. 지우지는 않는다.</summary>
        public static int UpdateContract(string root)
        {
            var path = Path.Combine(root, ContractRelPath.Replace('/', Path.DirectorySeparatorChar));
            var sources = Directory.EnumerateFiles(Path.Combine(root, "Runtime"), "*.cs", SearchOption.AllDirectories)
                .Select(p => (p, File.ReadAllText(p)));
            var extracted = ExtractCalls(sources, includeTables: true);
            var known = new HashSet<string>(StringComparer.Ordinal);
            if (File.Exists(path))
            {
                known.UnionWith(ReadContract(path, out var retired, out _));
                known.UnionWith(retired);
            }

            var added = extracted.Where(e => !known.Contains(e)).ToList();
            if (added.Count > 0)
                File.AppendAllText(path, string.Join("\n", added) + "\n");
            Console.WriteLine($"기준 목록에 {added.Count}줄 더했습니다.");
            foreach (var a in added)
                Console.WriteLine("  " + a);
            return 0;
        }

        /// <summary>깃 기록의 SDK 판들이 불렀던 호출을 모아 출력한다. 기준 목록을 처음 만들거나 다시 대조할 때 쓴다.</summary>
        public static int DumpHistory(string root)
        {
            var all = new SortedDictionary<string, (string First, string Last)>(StringComparer.Ordinal);
            // 대부분의 커밋은 파일 몇 개만 바꾼다. 같은 내용(blob)은 한 번만 파싱해야 수백 커밋을 금방 돈다.
            var byBlob = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
            var commits = Git(root, "log --reverse --format=%h|%ad --date=short -- Runtime").Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in commits)
            {
                var hash = line.Split('|')[0];
                var date = line.Split('|')[1];
                foreach (var entry in Git(root, $"ls-tree -r {hash} -- Runtime").Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var tab = entry.IndexOf('\t');
                    var file = entry.Substring(tab + 1).Trim('"');
                    if (!file.EndsWith(".cs", StringComparison.Ordinal))
                        continue;
                    var blob = entry.Substring(0, tab).Split(' ')[2];
                    if (!byBlob.TryGetValue(blob, out var calls))
                        byBlob[blob] = calls = ExtractCalls(new[] { (file, Git(root, $"cat-file blob {blob}")) }, includeTables: false);
                    foreach (var e in calls)
                        all[e] = all.TryGetValue(e, out var seen) ? (seen.First, date) : (date, date);
                }
            }
            foreach (var kv in all)
                Console.WriteLine($"{kv.Key}\t{kv.Value.First}\t{kv.Value.Last}");
            return 0;
        }

        private static string Git(string root, string arguments)
        {
            using var p = Process.Start(new ProcessStartInfo("git", arguments)
            {
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
            });
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            return output.Replace("\r\n", "\n");
        }
    }
}
