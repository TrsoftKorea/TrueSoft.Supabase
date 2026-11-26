using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SdkAudit
{
    /// <summary>
    /// R22 가이드 페이지 꼴 통일 — 제목·함수 페이지 순서·에러/프로퍼티/반환 표 꼴·링크·용어.
    /// 규칙 원문은 <c>.claude/rules/docs.md</c> 13·14절. 2026-10-30 전체 168쪽을 세어 갈라진 꼴을 하나로 정했다.
    /// </summary>
    public static class DocStyleRules
    {
        private static readonly Regex FunctionLabel = new Regex(@"^\*\*(파라미터|반환|에러 코드)\*\*\s*$");
        private static readonly Regex ReturnLead = new Regex(@"^`\.[\w.]+`(은|는) |^`[^`]+`(을|를) 돌려줍니다");
        private static readonly Regex Link = new Regex(@"(?<!!)\[[^\]]*\]\(([^)\s]+)\)");
        private static readonly Regex InlineCode = new Regex(@"`[^`]*`");
        private static readonly Regex LabelColon = new Regex(@"^\*\*[^*]+:\*\*");
        private static readonly Regex Sentence = new Regex(@"(니다|세요)\.");
        private static readonly Regex ServerTask = new Regex(@"install\.sql|verify\.sql|SQL Editor|SQL 편집기|Edge Function 배포|functions deploy", RegexOptions.IgnoreCase);
        private static readonly Regex NoReturnSection = new Regex(@"^(event |void |(Task<)?SupabaseResult>? |Task )");

        // 쓰지 않는 말 → 대신 쓸 말. 정규식이 예외(기능 이름 「유저 데이터」, 고객센터의 「유저 ID」, 「이메일」)를 비켜 간다.
        private static readonly (Regex Pattern, string Use)[] Terms =
        {
            (new Regex("Retool"), "운영 콘솔"),
            (new Regex("어드민"), "운영 콘솔"),
            (new Regex(@"유저(?! 데이터| ID)"), "플레이어"),
            (new Regex(@"(?<!이)메일"), "우편"),
            // user_data 의 칸은 운영 콘솔 메뉴 이름 그대로 "데이터 필드"(2026-11-23 사용자 결정)
            (new Regex(@"세이브(의)? ?(같은 이름 ?)?(숫자 ?)?칸"), "데이터 필드"),
            (new Regex(@"숫자 칸"), "숫자 데이터 필드"),
        };

        public static void Run(AuditContext ctx)
        {
            var guideDir = Path.Combine(ctx.Root, "docs~", "guide");
            if (!Directory.Exists(guideDir))
                return;

            var sidebar = ReadSidebar(ctx);
            CheckUpdates(ctx, Path.Combine(guideDir, "updates.md"));
            CheckReturnTables(ctx, guideDir);
            foreach (var md in Directory.EnumerateFiles(guideDir, "*.md", SearchOption.AllDirectories))
            {
                CheckFile(ctx, md, sidebar);
                CheckCardNames(ctx, md, sidebar);
                CheckOverviewMethods(ctx, md);
                // 사이드바 항목을 일부만 읽어도 그 밖의 페이지는 제목 대조를 조용히 건너뛴다 — 빠진 페이지를 오류로 낸다.
                if (sidebar.Count > 0 && !sidebar.ContainsKey(ctx.Rel(md)))
                    ctx.Report.Error($"[R22 제목] {ctx.Rel(md)} 이 사이드바에 없습니다(또는 사이드바 항목을 읽지 못했습니다).");
            }
        }

        /// <summary>사이드바 링크 → 표시 이름. 페이지 파일의 저장소 기준 경로로 바꿔 둔다.</summary>
        private static Dictionary<string, string> ReadSidebar(AuditContext ctx)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            var config = Path.Combine(ctx.Root, "docs~", ".vitepress", "config.ts");
            if (!File.Exists(config))
            {
                ctx.Report.Warn("[R22 제목] docs~/.vitepress/config.ts 가 없어 제목·사이드바 대조를 건너뜁니다.");
                return map;
            }

            var text = File.ReadAllText(config);
            var start = text.IndexOf("sidebar:", StringComparison.Ordinal);
            if (start < 0)
            {
                // 사이드바를 못 찾으면 검사가 조용히 꺼진다 — 꺼졌다고 알린다.
                ctx.Report.Error("[R22 제목] config.ts 에서 sidebar 를 찾지 못해 제목·사이드바 대조를 못 했습니다.");
                return map;
            }

            CheckSidebarGroups(ctx, text.Substring(start));

            foreach (Match m in Regex.Matches(text.Substring(start), @"text:\s*'([^']+)',\s*link:\s*'/guide/([^']*)'"))
            {
                var link = m.Groups[2].Value;
                var page = "docs~/guide/" + (link.EndsWith("/") || link.Length == 0 ? link + "index.md" : link + ".md");
                if (!map.ContainsKey(page))
                    map[page] = m.Groups[1].Value;
            }
            // 따옴표·순서가 이 꼴과 다르면 0개를 읽고도 통과처럼 보인다 — 꺼졌다고 알린다.
            if (map.Count == 0)
                ctx.Report.Error("[R22 제목] config.ts 사이드바에서 항목을 하나도 읽지 못했습니다. 항목 꼴이 text: '이름', link: '/guide/…' 인지 봅니다.");
            return map;
        }

        private sealed class SidebarNode
        {
            public string Text;
            public bool HasLink, HasItems, Collapsed;
            public int Children;
            public string Link;
            public readonly List<string> ChildLinks = new List<string>();
        }

        /// <summary>
        /// 모음 페이지에 카드 목록이 있으면 사이드바 하위 항목이 전부 카드로 있어야 한다.
        /// 새 기능을 사이드바에만 더하고 카드를 빠뜨린 일이 두 번 있었다(2026-11-26 — API 색인 3개, 서비스 모음의 점검·버전).
        /// </summary>
        private static void CheckCardsComplete(AuditContext ctx, SidebarNode node)
        {
            if (string.IsNullOrEmpty(node.Link) || !node.Link.StartsWith("/guide/", StringComparison.Ordinal))
                return;
            var rel = node.Link.Substring("/guide/".Length);
            var page = Path.Combine(ctx.Root, "docs~", "guide", rel.EndsWith("/") || rel.Length == 0 ? rel + "index.md" : rel + ".md");
            if (!File.Exists(page))
                return; // 없는 페이지는 R22 제목이 잡는다
            var text = File.ReadAllText(page);
            if (!text.Contains("tb-cards", StringComparison.Ordinal))
                return;
            var cards = new HashSet<string>(CardLine.Matches(text).Select(m => "/guide/" + m.Groups["link"].Value), StringComparer.Ordinal);
            foreach (var child in node.ChildLinks)
                if (!cards.Contains(child))
                    ctx.Report.Error($"[R22 카드빠짐] {ctx.Rel(page)} 카드 목록에 사이드바 하위 항목 '{child}' 이 없습니다.");
        }

        private static readonly Regex CardLine = new Regex(@"^- \[\*\*(?<name>[^*]+)\*\*.*?\]\(/guide/(?<link>[^)#]*)(?:#[^)]*)?\)", RegexOptions.Multiline | RegexOptions.Compiled);

        /// <summary>모음 카드의 이름이 같은 페이지의 사이드바 이름과 같은지 본다 — 같은 곳을 두 이름으로 부르면 찾는 사람이 헷갈린다.</summary>
        private static void CheckCardNames(AuditContext ctx, string path, Dictionary<string, string> sidebar)
        {
            foreach (Match m in CardLine.Matches(File.ReadAllText(path)))
            {
                var link = m.Groups["link"].Value;
                var page = "docs~/guide/" + (link.EndsWith("/") || link.Length == 0 ? link + "index.md" : link + ".md");
                if (sidebar.TryGetValue(page, out var side) && side != m.Groups["name"].Value)
                    ctx.Report.Error($"[R22 카드이름] {ctx.Rel(path)} 카드 '{m.Groups["name"].Value}' 이 사이드바 이름 '{side}' 과 다릅니다.");
            }
        }

        private static readonly Regex SidebarKey = new Regex(@"\G(?<key>text|link|items|collapsed):\s*(?:'(?<value>[^']*)'|(?<value>true|false))?", RegexOptions.Compiled);

        /// <summary>
        /// 하위가 있는 항목은 눌러서 갈 페이지가 있고(link) 기본으로 접혀 있어야(collapsed: true) 한다.
        /// 중괄호로 객체를 따라가며 그 객체에 직접 달린 키만 본다 — 정규식 한 줄로는 하위 항목의 link 를 상위 것으로 착각한다.
        /// </summary>
        private static void CheckSidebarGroups(AuditContext ctx, string sidebarText)
        {
            var stack = new Stack<SidebarNode>();
            var inString = false;
            for (var i = 0; i < sidebarText.Length; i++)
            {
                var c = sidebarText[i];
                if (inString)
                {
                    inString = c != '\'';
                    continue;
                }

                if (c == '\'')
                {
                    inString = true;
                }
                else if (c == '{')
                {
                    stack.Push(new SidebarNode());
                }
                else if (c == '}')
                {
                    if (stack.Count == 0)
                        return; // sidebar 를 품은 객체가 닫혔다

                    var node = stack.Pop();
                    if (stack.Count > 0)
                    {
                        stack.Peek().Children++;
                        if (node.Link != null)
                            stack.Peek().ChildLinks.Add(node.Link);
                    }
                    if (node.HasItems)
                        CheckCardsComplete(ctx, node);
                    if (!node.HasItems)
                    {
                        if (node.Text == "개요")
                            ctx.Report.Error("[R22 사이드바] 하위 항목에 '개요'가 있습니다. 기능의 첫 페이지가 개요이므로 그 내용을 첫 페이지로 합칩니다.");
                        continue;
                    }
                    if (node.Children == 1)
                        ctx.Report.Error($"[R22 사이드바] '{node.Text}' 은 하위 항목이 하나뿐입니다. 묶지 않고 한 페이지로 합칩니다.");
                    if (!node.HasLink)
                        ctx.Report.Error($"[R22 사이드바] '{node.Text}' 은 하위 항목이 있는데 눌러서 갈 페이지(link)가 없습니다. 모음 페이지를 만들어 연결합니다.");
                    if (!node.Collapsed)
                        ctx.Report.Error($"[R22 사이드바] '{node.Text}' 은 하위 항목이 있는데 기본으로 접혀 있지 않습니다(collapsed: true).");
                }
                else if (stack.Count > 0 && char.IsLetter(c) && !char.IsLetterOrDigit(sidebarText[i - 1]))
                {
                    var m = SidebarKey.Match(sidebarText, i);
                    if (!m.Success)
                        continue;

                    var top = stack.Peek();
                    var value = m.Groups["value"].Value;
                    switch (m.Groups["key"].Value)
                    {
                        case "text": top.Text = value; break;
                        case "link": top.HasLink = true; top.Link = value; break;
                        case "items": top.HasItems = true; break;
                        case "collapsed": top.Collapsed = value == "true"; break;
                    }
                    i = m.Index + m.Length - 1;
                }
            }
        }

        private static readonly Regex ReturnsType = new Regex(@"`\.Data`는 `(?:IReadOnlyList<|List<)?(\w+)>?`|^`(\w+)`[을를] 돌려줍니다");
        private static readonly Regex TableName = new Regex(@"^\|\s*`(\w+)");

        /// <summary>
        /// 반환 설명이 가리키는 SDK 클래스의 공개 프로퍼티가 바로 아래 표에 다 있는지 본다.
        /// 우편 ID(<c>Mail.Id</c>)가 표에서 빠져 받기에 넘길 값을 문서로 알 수 없었다(2026-11-23 — 같은 빠짐이 13쪽).
        /// 표 대신 다른 페이지 표를 가리키면("표와 같습니다") 그쪽이 검사된다.
        /// </summary>
        private static void CheckReturnTables(AuditContext ctx, string guideDir)
        {
            var props = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var twice = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (_, c) in ctx.Classes)
            {
                if (!c.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword)) || c.Parent is ClassDeclarationSyntax)
                    continue;
                var names = c.Members.OfType<PropertyDeclarationSyntax>()
                    .Where(p => p.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword)) && !p.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword)))
                    .Select(p => p.Identifier.ValueText).ToList();
                if (names.Count == 0) continue;
                var name = c.Identifier.ValueText;
                if (props.ContainsKey(name)) twice.Add(name); else props[name] = names;
            }

            var tables = 0;
            foreach (var md in Directory.EnumerateFiles(guideDir, "*.md", SearchOption.AllDirectories))
            {
                var lines = File.ReadAllLines(md);
                for (var i = 0; i < lines.Length; i++)
                {
                    var m = ReturnsType.Match(lines[i]);
                    if (!m.Success) continue;
                    var type = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
                    if (!props.TryGetValue(type, out var want) || twice.Contains(type)) continue;
                    var at = $"{ctx.Rel(md)}:{i + 1}";

                    var j = i + 1;
                    while (j < lines.Length && !lines[j].StartsWith("|", StringComparison.Ordinal) && !lines[j].StartsWith("#", StringComparison.Ordinal) && !lines[j].StartsWith("**", StringComparison.Ordinal))
                        j++;
                    if (j >= lines.Length || !lines[j].StartsWith("|", StringComparison.Ordinal))
                    {
                        if (!lines[i].Contains("표와 같습니다"))
                            ctx.Report.Error($"[R22 반환빠짐] {at} `{type}` 의 값을 설명하는 표가 없습니다 — 프로퍼티 {want.Count}개를 `| 프로퍼티 | 타입 | 설명 |` 표로 적거나, 같은 값을 적은 페이지를 \"[…](/guide/…)의 표와 같습니다\"로 가리킵니다.");
                        continue;
                    }
                    var have = new HashSet<string>(StringComparer.Ordinal);
                    for (; j < lines.Length && lines[j].StartsWith("|", StringComparison.Ordinal); j++)
                    {
                        var n = TableName.Match(lines[j]);
                        if (n.Success) have.Add(n.Groups[1].Value);
                    }
                    tables++;
                    var missing = want.Where(p => !have.Contains(p)).ToList();
                    if (missing.Count > 0)
                        ctx.Report.Error($"[R22 반환빠짐] {at} `{type}` 표에 코드의 공개 값이 빠졌습니다: {string.Join(", ", missing)}");
                }
            }
            if (tables == 0)
                ctx.Report.Error("[R22 반환빠짐] 반환 값 표를 하나도 대조하지 못했습니다 — 반환 문구 꼴이나 클래스 읽기가 바뀌었는지 봅니다.");
        }

        private static readonly Regex UpdateItem =new Regex(@"^- \*\*[^*]+\*\* — \S");

        /// <summary>
        /// 업데이트 내역의 항목은 굵은 제목으로 시작한다 — "그래서 뭐가 바뀌었는데?"를 제목만 훑어 답하게(2026-11-23 사용자 요청).
        /// </summary>
        private static void CheckUpdates(AuditContext ctx, string path)
        {
            var rel = ctx.Rel(path);
            if (!File.Exists(path))
            {
                ctx.Report.Error($"[R22 내역제목] {rel} 이 없습니다 — 업데이트 내역 꼴을 검사하지 못했습니다.");
                return;
            }
            var lines = File.ReadAllLines(path);
            var items = 0;
            for (var i = 0; i < lines.Length; i++)
            {
                if (!lines[i].StartsWith("- ", StringComparison.Ordinal))
                    continue;
                items++;
                if (!UpdateItem.IsMatch(lines[i]))
                    ctx.Report.Error($"[R22 내역제목] {rel}:{i + 1} 항목은 `- **짧은 제목** — 설명` 꼴로 씁니다 — `{lines[i].Trim()}`");
            }
            if (items == 0)
                ctx.Report.Error($"[R22 내역제목] {rel} 에서 항목(`- `)을 하나도 못 찾았습니다.");
        }

        /// <summary>
        /// 기능 개요(하위 페이지가 있는 index.md, 모음 페이지 제외)는 맨 끝 절이 `## 메서드` 표다(docs.md 0절).
        /// </summary>
        private static void CheckOverviewMethods(AuditContext ctx, string path)
        {
            if (!string.Equals(Path.GetFileName(path), "index.md", StringComparison.Ordinal))
                return;
            var dir = Path.GetDirectoryName(path);
            if (string.Equals(Path.GetFullPath(dir), Path.GetFullPath(Path.Combine(ctx.Root, "docs~", "guide")), StringComparison.OrdinalIgnoreCase))
                return;
            if (!Directory.EnumerateFiles(dir, "*.md").Any(f => !string.Equals(Path.GetFileName(f), "index.md", StringComparison.Ordinal)))
                return;
            var lines = File.ReadAllLines(path);
            if (lines.Any(l => l.Contains("tb-cards")))
                return;

            var rel = ctx.Rel(path);
            var lastH2 = Array.FindLastIndex(lines, l => l.StartsWith("## ", StringComparison.Ordinal));
            if (lastH2 < 0 || lines[lastH2].Trim() != "## 메서드")
            {
                ctx.Report.Error($"[R22 개요메서드] {rel} 기능 개요의 맨 끝 절은 `## 메서드` 표입니다{(lastH2 >= 0 ? $" — 지금 마지막 절은 '{lines[lastH2].Trim()}'" : "")}.");
                return;
            }
            var header = lines.Skip(lastH2 + 1).FirstOrDefault(l => l.Trim().Length > 0) ?? "";
            if (!Regex.IsMatch(header, @"^\|\s*메서드\s*\|\s*설명\s*\|"))
                ctx.Report.Error($"[R22 개요메서드] {rel}:{lastH2 + 1} `## 메서드` 아래 첫 줄은 `| 메서드 | 설명 |` 표입니다.");
        }

        private static void CheckFile(AuditContext ctx, string path, Dictionary<string, string> sidebar)
        {
            var rel = ctx.Rel(path);
            var lines = File.ReadAllLines(path);
            var isCatalog = rel.EndsWith("api/fail-reasons.md", StringComparison.Ordinal);

            var inFence = false;
            var h1Line = -1;
            var csharpFences = new List<int>();
            var firstLabel = -1;
            string table = null; // 지금 보고 있는 표의 첫 머리 칸

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var at = $"{rel}:{i + 1}";

                // DB 설치·서버 함수 배포는 SDK 쪽이 게임마다 대신 한다. 가이드 독자(게임 개발자)에게 시키면
                // 할 수도 없는 일을 하라는 셈이라 코드 블록 안까지 막는다(2026-11-25 사용자 지적).
                var serverTask = ServerTask.Match(line);
                if (serverTask.Success)
                    ctx.Report.Error($"[R22 서버작업] {at} '{serverTask.Value}' — DB 설치·서버 함수 배포는 게임 개발자가 하지 않습니다. 운영 콘솔에서 하는 일로 쓰거나 뺍니다.");

                if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
                {
                    if (!inFence && line.TrimStart().StartsWith("```csharp", StringComparison.Ordinal))
                        csharpFences.Add(i);
                    inFence = !inFence;
                    continue;
                }
                if (inFence)
                    continue;

                if (h1Line < 0 && line.StartsWith("# ", StringComparison.Ordinal))
                    h1Line = i;
                if (firstLabel < 0 && FunctionLabel.IsMatch(line))
                    firstLabel = i;

                if (LabelColon.IsMatch(line))
                    ctx.Report.Error($"[R22 라벨쌍점] {at} 굵은 라벨 뒤에 쌍점을 붙이지 않습니다 — `{line.Trim()}`");

                // ── 표 ── 목록 안에 들여 쓴 표도 표다.
                if (line.TrimStart().StartsWith("|", StringComparison.Ordinal))
                {
                    var cells = line.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToArray();
                    var isSeparator = Regex.IsMatch(line.Trim(), @"^\|[\s:|-]+\|$");
                    if (table == null)
                        table = cells[0];
                    else if (!isSeparator)
                        CheckRow(ctx, at, table, cells, isCatalog);

                    if (line.Contains('—'))
                        ctx.Report.Error($"[R22 표줄표] {at} 표 칸 안에 줄표(—)를 쓰지 않습니다. 마침표로 끊습니다.");
                }
                else
                {
                    table = null;
                }

                if (line.Trim() == "**반환**")
                {
                    var next = lines.Skip(i + 1).FirstOrDefault(l => l.Trim().Length > 0) ?? "";
                    if (!ReturnLead.IsMatch(next))
                        ctx.Report.Error($"[R22 반환] {at} 반환 첫 줄은 \"`.Data`는 `타입`입니다.\" 꼴입니다 — `{Trunc(next)}`");
                }

                var prose = InlineCode.Replace(line, "");
                foreach (Match m in Link.Matches(prose))
                {
                    var target = m.Groups[1].Value;
                    if (!(target.StartsWith("/", StringComparison.Ordinal) || target.StartsWith("#", StringComparison.Ordinal)
                          || target.StartsWith("http", StringComparison.Ordinal) || target.StartsWith("mailto:", StringComparison.Ordinal)))
                        ctx.Report.Error($"[R22 링크] {at} 다른 페이지 링크는 /guide/ 로 시작합니다 — ({target})");
                }

                var words = Link.Replace(prose, "]");
                foreach (var (pattern, use) in Terms)
                    foreach (Match m in pattern.Matches(words))
                        ctx.Report.Error($"[R22 용어] {at} '{m.Value}' 대신 '{use}' 를 씁니다.");
            }

            if (h1Line < 0)
                return;

            var h1 = Regex.Replace(lines[h1Line].Substring(2), @"\s*\{#[^}]*\}\s*$", "").Trim();
            if (sidebar.TryGetValue(rel, out var side) && !h1.Contains(side))
                ctx.Report.Error($"[R22 제목] {rel}:{h1Line + 1} 제목 '{h1}' 에 사이드바 이름 '{side}' 이 들어 있지 않습니다.");

            // 함수 페이지 = 제목 바로 다음이 시그니처이거나, 파라미터·반환·에러 코드 라벨 앞에 시그니처가 있는 페이지
            // (절이 여러 개인 페이지·제목 뒤에 ::: 상자가 먼저 오는 페이지). 시그니처 다음 csharp 블록이 호출 예제다.
            var afterH1 = lines.Skip(h1Line + 1).Select((l, k) => (l, k: k + h1Line + 1)).FirstOrDefault(x => x.l.Trim().Length > 0);
            var sigIndex = afterH1.l != null && afterH1.l.TrimStart().StartsWith("```csharp", StringComparison.Ordinal)
                ? csharpFences.IndexOf(afterH1.k)
                : csharpFences.FindIndex(f => (firstLabel < 0 || f < firstLabel) && IsSignature(lines, f));
            if (sigIndex < 0 || (afterH1.k != csharpFences[sigIndex] && firstLabel < 0))
                return;

            var sigFence = csharpFences[sigIndex];
            var labelAfterSig = Array.FindIndex(lines, sigFence, l => FunctionLabel.IsMatch(l));
            if (sigIndex + 1 >= csharpFences.Count)
                ctx.Report.Error($"[R22 예제없음] {rel} 함수 페이지에 호출 예제가 없습니다.");
            else if (labelAfterSig >= 0 && csharpFences[sigIndex + 1] > labelAfterSig)
                ctx.Report.Error($"[R22 예제순서] {rel}:{csharpFences[sigIndex + 1] + 1} 호출 예제는 파라미터·반환·에러 코드 표보다 앞에 둡니다.");

            // 실사용 예시 = 게임 코드에서 실제로 부르는 모습. 표들 다음, ::: 상자 앞.
            var practical = Array.FindIndex(lines, sigFence, l => l.Trim() == "**실사용 예시**");
            var lastLabel = Array.FindLastIndex(lines, l => FunctionLabel.IsMatch(l));
            if (practical < 0)
                ctx.Report.Error($"[R22 실사용없음] {rel} 함수 페이지에 **실사용 예시** 가 없습니다. 에러 코드 표 다음에 게임 코드에서 부르는 모습을 넣습니다.");
            else if (lastLabel > practical)
                ctx.Report.Error($"[R22 실사용순서] {rel}:{practical + 1} **실사용 예시** 는 파라미터·반환·에러 코드 표 다음에 둡니다.");
            else if (!csharpFences.Any(f => f > practical && !lines.Skip(practical + 1).Take(f - practical - 1).Any(l => l.StartsWith("#") || l.StartsWith(":::"))))
                ctx.Report.Error($"[R22 실사용없음] {rel}:{practical + 1} **실사용 예시** 아래에 csharp 코드 블록이 없습니다.");

            // ::: 상자는 맨 끝 — 시그니처 뒤, 표·실사용 예시보다 앞에 오면 안 된다.
            var bodyEnd = Math.Max(lastLabel, practical);
            var earlyBox = Array.FindIndex(lines, sigFence, l => l.StartsWith(":::", StringComparison.Ordinal) && l.Trim() != ":::");
            if (earlyBox >= 0 && earlyBox < bodyEnd)
                ctx.Report.Error($"[R22 상자위치] {rel}:{earlyBox + 1} ::: 상자는 파라미터·반환·에러 코드 표와 실사용 예시 다음, 페이지 맨 끝에 둡니다.");

            // 시그니처 닫는 줄 다음 문단이 설명이다.
            var close = Array.FindIndex(lines, sigFence + 1, l => l.TrimStart().StartsWith("```", StringComparison.Ordinal));

            // 값을 돌려주는 함수는 **반환** 을 적는다(성공·실패만 알리는 SupabaseResult·이벤트·void 는 뺀다).
            if (close > sigFence)
            {
                var sig = string.Join(" ", lines.Skip(sigFence + 1).Take(close - sigFence - 1)).Trim();
                if (!NoReturnSection.IsMatch(sig) && !lines.Any(l => l.Trim() == "**반환**"))
                    ctx.Report.Error($"[R22 반환없음] {rel}:{sigFence + 2} 값을 돌려주는 함수인데 **반환** 이 없습니다 — `{Trunc(sig)}`");
            }
            if (close > 0)
            {
                var desc = string.Join(" ", lines.Skip(close + 1).SkipWhile(l => l.Trim().Length == 0).TakeWhile(l => l.Trim().Length > 0));
                var n = Sentence.Matches(desc).Count;
                if (n >= 4)
                    ctx.Report.Warn($"[R22 설명길이] {rel} 함수 설명이 {n}문장입니다. 1~3문장으로 줄이고 나머지는 아래 본문이나 ::: 상자로 옮깁니다.");
            }
        }

        private static void CheckRow(AuditContext ctx, string at, string header, string[] cells, bool isCatalog)
        {
            if (header == "Reason")
            {
                if (!isCatalog && !cells[0].StartsWith("`SupabaseReason.", StringComparison.Ordinal))
                    ctx.Report.Error($"[R22 에러표] {at} 첫 칸은 `SupabaseReason.이름` 입니다 — {cells[0]}");
                if (!Regex.IsMatch(cells[cells.Length - 1], @"(니다|세요)\.?$"))
                    ctx.Report.Error($"[R22 에러표] {at} 설명은 \"~니다\"·\"~세요\" 문장으로 씁니다 — {Trunc(cells[cells.Length - 1])}");
            }
            else if (header == "프로퍼티")
            {
                if (cells[0].StartsWith("`.", StringComparison.Ordinal))
                    ctx.Report.Error($"[R22 프로퍼티표] {at} 이름 칸에 점을 붙이지 않습니다 — {cells[0]}");
                if (cells.Length >= 3 && !cells[1].StartsWith("`", StringComparison.Ordinal) && !cells[1].StartsWith("[`", StringComparison.Ordinal))
                    ctx.Report.Error($"[R22 프로퍼티표] {at} 타입 칸은 코드 표시(`int`)로 씁니다 — {cells[1]}");
            }
        }

        // `Task<SupabaseResult> Supabase.Foo(` · `event Action Supabase.OnX` 꼴. 괄호 앞에 = 가 있으면 호출 예제(var x = …)다.
        private static readonly Regex SignatureLine = new Regex(@"^(event\s+)?[A-Za-z_][\w<>,\[\]?.\s]*\s+[A-Z]\w*(<\w+>)?\.\w+\s*(\(|$)");
        private static bool IsSignature(string[] lines, int fence)
        {
            var first = fence + 1 < lines.Length ? lines[fence + 1].Trim() : "";
            var head = first.Split('(')[0];
            return !head.Contains('=') && !head.StartsWith("var ", StringComparison.Ordinal) && SignatureLine.IsMatch(first);
        }

        private static string Trunc(string s) => s.Length > 40 ? s.Substring(0, 40) + "…" : s;
    }
}
