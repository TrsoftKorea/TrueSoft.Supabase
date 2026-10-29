using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace SdkAudit
{
    /// <summary>
    /// R17 설치 누락. 운영 콘솔 서버(admin-api)와 Edge Function 이 부르는 DB 함수·표는 install.sql 에 있어야 한다.
    /// 라이브 DB 에만 손으로 만들어 두면 새 게임에 설치할 때 그 화면이 "함수가 없다"로 실패하고, 권한 정리(22절)에서도
    /// 빠져 클라이언트에 열린 채로 남는다 — 실제로 운영 함수 8개가 그렇게 빠져 있었고 그중 3개가 로그인 없이 불렸다.
    /// <para>verify.sql 의 "클라이언트가 불러도 되는 함수" 목록도 install.sql 의 grant 와 손으로 맞추는 복제라 함께 대조한다.</para>
    /// </summary>
    public static class SqlCoverageRules
    {
        private static readonly Regex RpcCall = new(@"\brpc\(\s*""(?<name>[A-Za-z_][\w]*)""");
        // storage.from("버킷") 은 표가 아니다.
        private static readonly Regex FromCall = new(@"(?<!storage)\.from\(\s*""(?<name>[A-Za-z_][\w]*)""\s*\)");
        private static readonly Regex DefinedFunction = new(@"create\s+(?:or\s+replace\s+)?function\s+(?:public\.)?""?(?<name>\w+)""?\s*\(", RegexOptions.IgnoreCase);
        private static readonly Regex DefinedRelation = new(@"create\s+(?:or\s+replace\s+)?(?:unlogged\s+)?(?:table|view|materialized\s+view)\s+(?:if\s+not\s+exists\s+)?(?:public\.)?""?(?<name>\w+)""?", RegexOptions.IgnoreCase);
        private static readonly Regex ClientGrant = new(@"grant\s+execute\s+on\s+function\s+public\.(?<name>\w+)\s*\([^)]*\)\s+to\s+(?<roles>[^;]+);", RegexOptions.IgnoreCase);
        // public. 을 붙인 정의만 표로 센다 — install.sql 은 전부 그렇게 쓰고, 주석·문자열 속 "CREATE TABLE AS" 같은 글자를 표로 오인하지 않게.
        // unlogged(기록을 남기지 않는 표, 요청 제한의 요청 수)도 표다.
        private static readonly Regex DefinedTable = new(@"create\s+(?:unlogged\s+)?table\s+(?:if\s+not\s+exists\s+)?public\.""?(?<name>[A-Za-z_][A-Za-z0-9_]*)""?", RegexOptions.IgnoreCase);

        public static void Run(AuditContext ctx)
        {
            var sqlPath = Path.Combine(ctx.Root, "Samples~", "DatabaseSetup", "SQL", "player", "install.sql");
            if (!File.Exists(sqlPath))
                return;
            var sql = File.ReadAllText(sqlPath);
            var sqlRel = ctx.Rel(sqlPath);

            var functions = DefinedFunction.Matches(sql).Select(m => m.Groups["name"].Value.ToLowerInvariant()).ToHashSet();
            var relations = DefinedRelation.Matches(sql).Select(m => m.Groups["name"].Value.ToLowerInvariant()).ToHashSet();

            var callerDirs = new[]
            {
                Path.Combine(ctx.Root, "Admin~", "supabase", "functions"),
                Path.Combine(ctx.Root, "Samples~", "DatabaseSetup", "EdgeFunctions"),
            };
            foreach (var dir in callerDirs.Where(Directory.Exists))
            {
                foreach (var path in Directory.GetFiles(dir, "*.ts", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
                {
                    var text = File.ReadAllText(path);
                    var rel = ctx.Rel(path);
                    foreach (var name in RpcCall.Matches(text).Select(m => m.Groups["name"].Value).Distinct())
                    {
                        if (!functions.Contains(name.ToLowerInvariant()))
                            ctx.Report.Error($"[R17 설치누락] 함수 `{name}` 를 부르는데 {sqlRel} 에 정의가 없습니다 — 라이브에만 있으면 새 게임 설치 때 실패합니다. ({rel})");
                    }
                    foreach (var name in FromCall.Matches(text).Select(m => m.Groups["name"].Value).Distinct())
                    {
                        if (!relations.Contains(name.ToLowerInvariant()))
                            ctx.Report.Error($"[R17 설치누락] 표 `{name}` 를 읽는데 {sqlRel} 에 정의가 없습니다 — 라이브에만 있으면 새 게임 설치 때 실패합니다. ({rel})");
                    }
                }
            }

            VerifyAllowList(ctx, sql, sqlRel);
        }

        private static void VerifyAllowList(AuditContext ctx, string sql, string sqlRel)
        {
            var verifyPath = Path.Combine(ctx.Root, "Samples~", "DatabaseSetup", "SQL", "player", "verify.sql");
            if (!File.Exists(verifyPath))
                return;
            var verifyRel = ctx.Rel(verifyPath);
            var verifyText = File.ReadAllText(verifyPath);

            VerifyTableList(ctx, sql, sqlRel, verifyText, verifyRel);

            var granted = ClientGrant.Matches(sql)
                .Where(m => Regex.IsMatch(m.Groups["roles"].Value, @"\b(authenticated|anon)\b", RegexOptions.IgnoreCase))
                .Select(m => m.Groups["name"].Value)
                .ToHashSet(StringComparer.Ordinal);

            var block = Regex.Match(verifyText, @"'grant:function'[\s\S]*?proname\s+not\s+in\s*\(([\s\S]*?)\)");
            if (!block.Success)
            {
                ctx.Report.Error($"[R17 검증목록] {verifyRel} 에서 함수 허용 목록(`proname not in (…)`)을 찾지 못했습니다. 모양이 바뀌었으면 검사기도 고치세요.");
                return;
            }
            var allowed = Regex.Matches(block.Groups[1].Value, @"'(\w+)'").Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

            foreach (var name in granted.Except(allowed).OrderBy(n => n, StringComparer.Ordinal))
                ctx.Report.Error($"[R17 검증목록] `{name}` 는 {sqlRel} 이 클라이언트에 여는데 {verifyRel} 허용 목록에 없습니다 — 설치 검증이 '권한 과다'로 잘못 표시합니다.");
            foreach (var name in allowed.Except(granted).OrderBy(n => n, StringComparer.Ordinal))
                ctx.Report.Error($"[R17 검증목록] `{name}` 는 {verifyRel} 허용 목록에 있는데 {sqlRel} 이 클라이언트에 열지 않습니다 — 실제로 열리면 검증이 못 잡습니다.");
        }

        /// <summary>
        /// install.sql 이 만드는 표는 전부 verify.sql 의 "표·RLS" 목록에 있어야 한다. 손으로 맞추는 복제라 표를 새로 만들 때
        /// 빠진다 — 2026-10-28 에 50개 중 25개가 빠져 있어, 그 표가 설치됐는지·RLS 가 켜졌는지를 설치 확인이 보지 못했다.
        /// </summary>
        private static void VerifyTableList(AuditContext ctx, string sql, string sqlRel, string verifyText, string verifyRel)
        {
            var created = DefinedTable.Matches(sql).Select(m => m.Groups["name"].Value.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
            var block = Regex.Match(verifyText, @"'table\+rls'[\s\S]*?\)\s*as\s+exp\(name\)");
            if (!block.Success)
            {
                ctx.Report.Error($"[R17 검증목록] {verifyRel} 에서 표 목록(`'table+rls'` … `as exp(name)`)을 찾지 못했습니다. 모양이 바뀌었으면 검사기도 고치세요.");
                return;
            }
            var listed = Regex.Matches(block.Value, @"\(\s*'(\w+)'\s*\)").Select(m => m.Groups[1].Value.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);

            foreach (var name in created.Except(listed).OrderBy(n => n, StringComparer.Ordinal))
                ctx.Report.Error($"[R17 검증표] 표 `{name}` 를 {sqlRel} 이 만드는데 {verifyRel} 의 표 목록에 없습니다 — 설치 확인이 그 표가 빠졌는지·RLS 가 꺼졌는지 보지 못합니다.");
            foreach (var name in listed.Except(created).OrderBy(n => n, StringComparer.Ordinal))
                ctx.Report.Error($"[R17 검증표] 표 `{name}` 는 {verifyRel} 표 목록에 있는데 {sqlRel} 이 만들지 않습니다 — 설치 확인이 늘 missing 으로 표시합니다.");
        }
    }
}
