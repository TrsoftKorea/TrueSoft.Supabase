using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SdkAudit
{
    /// <summary>
    /// R9 설치 순서. install.sql 은 빈 프로젝트에 위에서 아래로 한 번 실행된다.
    /// 뒤 절에서 만드는 객체를 앞 절이 참조하면 신규 프로젝트에서만 실패하고, 라이브 DB 에서는 드러나지 않는다.
    /// </summary>
    public static class SqlRules
    {
        // 참조 형태. sch 그룹이 있고 public 이 아니면 다른 스키마이므로 검사하지 않는다.
        private static readonly (string Kind, string Pattern)[] References =
        {
            ("외래키",   @"references\s+(?:(?<sch>\w+)\.)?(?<obj>\w+)"),
            ("인덱스",   @"create\s+(?:unique\s+)?index\s+(?:if\s+not\s+exists\s+)?[\w""]+\s+on\s+(?:(?<sch>\w+)\.)?(?<obj>\w+)"),
            ("정책",     @"create\s+policy\s+[^;]*?\son\s+(?:(?<sch>\w+)\.)?(?<obj>\w+)"),
            ("트리거",   @"create\s+trigger\s+\w+[^;]*?\son\s+(?:(?<sch>\w+)\.)?(?<obj>\w+)"),
            ("트리거함수", @"execute\s+(?:function|procedure)\s+(?:(?<sch>\w+)\.)?(?<obj>\w+)"),
            // alter table if exists 는 신규 설치에서 no-op 인 마이그레이션 구문이라 선행 정의가 필요 없다.
            ("alter",    @"alter\s+table\s+(?!if\s+exists)(?:(?<sch>\w+)\.)?(?<obj>\w+)"),
            ("grant함수", @"\bon\s+function\s+(?:(?<sch>\w+)\.)?(?<obj>\w+)"),
            ("grant테이블", @"\bon\s+table\s+(?:(?<sch>\w+)\.)?(?<obj>\w+)"),
            ("주석",     @"comment\s+on\s+(?:table|view|column|function)\s+(?:(?<sch>\w+)\.)?(?<obj>\w+)"),
            ("시드",     @"insert\s+into\s+(?:(?<sch>\w+)\.)?(?<obj>\w+)"),
        };

        private static readonly string[] Definitions =
        {
            @"create\s+(?:unlogged\s+)?table\s+(?:if\s+not\s+exists\s+)?(?:(?<sch>\w+)\.)?(?<obj>\w+)",
            @"create\s+(?:or\s+replace\s+)?function\s+(?:(?<sch>\w+)\.)?(?<obj>\w+)",
            @"create\s+(?:or\s+replace\s+)?(?:materialized\s+)?view\s+(?:if\s+not\s+exists\s+)?(?:(?<sch>\w+)\.)?(?<obj>\w+)",
            @"create\s+type\s+(?:(?<sch>\w+)\.)?(?<obj>\w+)",
            @"create\s+sequence\s+(?:if\s+not\s+exists\s+)?(?:(?<sch>\w+)\.)?(?<obj>\w+)",
        };

        public static void Run(AuditContext ctx)
        {
            var path = Path.Combine(ctx.Root, "Samples~", "DatabaseSetup", "SQL", "player", "install.sql");
            if (!File.Exists(path))
            {
                ctx.Report.Warn("install.sql 을 찾지 못해 설치 순서 검사를 건너뜁니다.");
                return;
            }

            var rel = ctx.Rel(path);
            var raw = File.ReadAllText(path);
            var sections = Regex.Matches(raw, @"(?m)^--\s*(\d{2})\.\s*(.+)$")
                .Select(m => (Offset: m.Index, Label: $"{m.Groups[1].Value}. {m.Groups[2].Value.Trim()}"))
                .OrderBy(x => x.Offset)
                .ToList();

            // 함수 본문은 설치 시점에 해석되지 않는다(런타임 해석). 본문 안의 참조는 순서와 무관하므로 지운다.
            var sql = MaskComments(MaskDollarBodies(raw));

            var defs = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var pattern in Definitions)
            {
                foreach (Match m in Regex.Matches(sql, pattern, RegexOptions.IgnoreCase))
                {
                    if (OtherSchema(m))
                        continue;
                    var name = m.Groups["obj"].Value;
                    if (!defs.TryGetValue(name, out var first) || m.Index < first)
                        defs[name] = m.Index;
                }
            }

            if (defs.Count == 0)
            {
                ctx.Report.Warn("[R9] install.sql 에서 객체 정의를 찾지 못했습니다. 검사기가 파일을 못 읽고 있습니다.");
                return;
            }

            var undefined = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (kind, pattern) in References)
            {
                foreach (Match m in Regex.Matches(sql, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline))
                {
                    if (OtherSchema(m))
                        continue;

                    var name = m.Groups["obj"].Value;
                    var at = m.Groups["obj"].Index;

                    if (!defs.TryGetValue(name, out var defAt))
                    {
                        // 다른 절에서 만들지 않는 이름(확장 함수·롤 등)은 정의가 없다. 명시적으로 public 을 붙인 것만 의심한다.
                        if (m.Groups["sch"].Success && undefined.Add(name))
                            ctx.Report.Warn($"[R9 정의없음] {kind} 가 public.{name} 을 참조하지만 install.sql 에 정의가 없습니다. ({rel}:{LineOf(raw, at)})");
                        continue;
                    }

                    if (defAt > at)
                        ctx.Report.Error($"[R9 설치순서] {SectionOf(sections, at)} 의 {kind} 가 {SectionOf(sections, defAt)} 에서 만드는 {name} 을 참조합니다. 신규 프로젝트 설치가 실패합니다. ({rel}:{LineOf(raw, at)})");
                }
            }

            CronScheduled(ctx, rel, raw);
        }

        /// <summary>
        /// R13 크론 미등록. 주석에 cron 이 부른다고 적힌 함수인데 install.sql 에 스케줄이 없으면 아무도 부르지 않는다.
        /// 정의는 멀쩡하고 호출자만 없어서 문법·타입 어디에도 안 걸린다 — 실제로 리더보드 회차 전환이 이 상태였다.
        /// </summary>
        private static void CronScheduled(AuditContext ctx, string rel, string raw)
        {
            // 스케줄 본문은 달러 인용이라 마스킹 전 원문에서 찾는다. cron.unschedule 은 "cron." 뒤가 schedule 이 아니라 안 걸린다.
            var scheduled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in Regex.Matches(raw, @"cron\.schedule\b", RegexOptions.IgnoreCase))
            {
                var end = raw.IndexOf(';', m.Index);
                if (end < 0)
                    end = Math.Min(raw.Length, m.Index + 500);
                foreach (Match f in Regex.Matches(raw.Substring(m.Index, end - m.Index), @"public\.(\w+)", RegexOptions.IgnoreCase))
                    scheduled.Add(f.Groups[1].Value);
            }

            foreach (Match m in Regex.Matches(
                raw,
                @"comment\s+on\s+function\s+public\.(?<fn>\w+)\s*\([^)]*\)\s*is\s*'(?<body>(?:[^']|'')*)'",
                RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                if (!Regex.IsMatch(m.Groups["body"].Value, "cron", RegexOptions.IgnoreCase))
                    continue;

                var fn = m.Groups["fn"].Value;
                if (scheduled.Contains(fn))
                    continue;

                ctx.Report.Error($"[R13 크론미등록] {fn} 의 주석은 cron 이 부른다고 적혀 있는데 install.sql 에 cron.schedule 이 없습니다. 아무도 부르지 않습니다. ({rel}:{LineOf(raw, m.Index)})");
            }
        }

        private static bool OtherSchema(Match m)
        {
            var sch = m.Groups["sch"];
            return sch.Success && !sch.Value.Equals("public", StringComparison.OrdinalIgnoreCase);
        }

        private static string SectionOf(List<(int Offset, string Label)> sections, int offset)
        {
            var label = sections.LastOrDefault(s => s.Offset <= offset).Label;
            return label == null ? "머리말" : $"{label}절";
        }

        private static int LineOf(string text, int offset) =>
            text.Take(offset).Count(ch => ch == '\n') + 1;

        /// <summary>달러 인용($$ … $$, $tag$ … $tag$) 구간을 같은 길이의 공백으로 바꾼다. 오프셋이 보존된다.</summary>
        private static string MaskDollarBodies(string sql)
        {
            var sb = new StringBuilder(sql);
            var opener = new Regex(@"\$\w*\$");
            var i = 0;
            while (i < sql.Length)
            {
                if (sql[i] != '$')
                {
                    i++;
                    continue;
                }

                var open = opener.Match(sql, i);
                if (!open.Success || open.Index != i)
                {
                    i++;
                    continue;
                }

                var tag = open.Value;
                var end = sql.IndexOf(tag, i + tag.Length, StringComparison.Ordinal);
                if (end < 0)
                    break;

                for (var c = i; c < end + tag.Length; c++)
                    if (sb[c] != '\n')
                        sb[c] = ' ';
                i = end + tag.Length;
            }
            return sb.ToString();
        }

        /// <summary>줄 주석과 블록 주석을 같은 길이의 공백으로 바꾼다.</summary>
        private static string MaskComments(string sql)
        {
            var sb = new StringBuilder(sql);
            for (var i = 0; i < sb.Length - 1; i++)
            {
                if (sb[i] == '-' && sb[i + 1] == '-')
                {
                    for (var c = i; c < sb.Length && sb[c] != '\n'; c++)
                        sb[c] = ' ';
                }
                else if (sb[i] == '/' && sb[i + 1] == '*')
                {
                    var end = sql.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    if (end < 0)
                        end = sb.Length - 2;
                    for (var c = i; c < end + 2 && c < sb.Length; c++)
                        if (sb[c] != '\n')
                            sb[c] = ' ';
                    i = end + 1;
                }
            }
            return sb.ToString();
        }
    }
}
