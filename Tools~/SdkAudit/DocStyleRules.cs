using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

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

        // 쓰지 않는 말 → 대신 쓸 말. 정규식이 예외(기능 이름 「유저 데이터」, 고객센터의 「유저 ID」, 「이메일」)를 비켜 간다.
        private static readonly (Regex Pattern, string Use)[] Terms =
        {
            (new Regex("Retool"), "운영 콘솔"),
            (new Regex("어드민"), "운영 콘솔"),
            (new Regex(@"유저(?! 데이터| ID)"), "플레이어"),
            (new Regex(@"(?<!이)메일"), "우편"),
        };

        public static void Run(AuditContext ctx)
        {
            var guideDir = Path.Combine(ctx.Root, "docs~", "guide");
            if (!Directory.Exists(guideDir))
                return;

            var sidebar = ReadSidebar(ctx);
            foreach (var md in Directory.EnumerateFiles(guideDir, "*.md", SearchOption.AllDirectories))
            {
                CheckFile(ctx, md, sidebar);
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

            // 시그니처 닫는 줄 다음 문단이 설명이다.
            var close = Array.FindIndex(lines, sigFence + 1, l => l.TrimStart().StartsWith("```", StringComparison.Ordinal));
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
