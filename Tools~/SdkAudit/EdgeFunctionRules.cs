using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace SdkAudit
{
    /// <summary>
    /// R16 공통 구간. Edge Function 은 함수마다 따로 배포돼 파일을 나눠 쓸 수 없어서, 같은 코드를 함수마다
    /// 복사해 둔다(`═══ BEGIN 이름 ═══` ~ `═══ END 이름 ═══`). 한쪽만 고치면 배포된 함수끼리 조용히
    /// 다르게 동작하므로, 같은 이름의 구간이 글자 그대로 같은지 대조한다. 콘솔 서버(Admin~/supabase/functions)
    /// 사본도 함께 본다.
    /// </summary>
    public static class EdgeFunctionRules
    {
        private static readonly Regex Begin = new(@"^//\s*═══\s*BEGIN\s+(?<name>[\w-]+)\s*═══");
        private static readonly Regex End = new(@"^//\s*═══\s*END\s+(?<name>[\w-]+)\s*═══");
        private static readonly Regex HandlerFirstLine = new(@"Deno\.serve\([^\n]*\{\s*\n\s*(?<line>[^\n]*)");
        private static readonly Regex CountCall = new(@"^countCall\(""(?<name>[\w-]+)"",\s*req\);");

        public static void Run(AuditContext ctx)
        {
            var dir = Path.Combine(ctx.Root, "Samples~", "DatabaseSetup", "EdgeFunctions");
            if (!Directory.Exists(dir))
                return;
            var dirs = new[] { dir, Path.Combine(ctx.Root, "Admin~", "supabase", "functions") }.Where(Directory.Exists).ToArray();

            var blocks = new Dictionary<string, List<(string File, string Text)>>(StringComparer.Ordinal);
            foreach (var path in dirs.SelectMany(d => Directory.GetFiles(d, "*.ts", SearchOption.AllDirectories)).OrderBy(p => p, StringComparer.Ordinal))
            {
                var rel = ctx.Rel(path);
                var lines = File.ReadAllText(path).Replace("\r\n", "\n").Split('\n');
                for (var i = 0; i < lines.Length; i++)
                {
                    var b = Begin.Match(lines[i]);
                    if (!b.Success)
                        continue;
                    var name = b.Groups["name"].Value;
                    var endAt = -1;
                    for (var j = i + 1; j < lines.Length; j++)
                    {
                        var e = End.Match(lines[j]);
                        if (e.Success && e.Groups["name"].Value == name) { endAt = j; break; }
                    }
                    if (endAt < 0)
                    {
                        ctx.Report.Error($"[R16 공통구간] `{name}` 구간의 END 표시가 없습니다. ({rel}:{i + 1})");
                        continue;
                    }
                    if (!blocks.TryGetValue(name, out var list))
                        blocks[name] = list = new List<(string, string)>();
                    list.Add(($"{rel}:{i + 1}", string.Join("\n", lines, i, endAt - i + 1)));
                    i = endAt;
                }
            }

            ServerConfigKeys(ctx, dir);
            ServerConfigServices(ctx);
            UsageFeatureInfo(ctx);
            if (blocks.ContainsKey("usage-count"))
                UsageCount(ctx, dirs);

            foreach (var (name, list) in blocks)
            {
                // 첫 사본을 기준으로 삼는다. 어느 쪽이 맞는지는 사람이 정한다 — 다른 줄만 알려 준다.
                var baseline = list[0];
                foreach (var other in list.Skip(1))
                {
                    if (other.Text == baseline.Text)
                        continue;
                    var a = baseline.Text.Split('\n');
                    var b = other.Text.Split('\n');
                    var line = Enumerable.Range(0, Math.Min(a.Length, b.Length)).FirstOrDefault(k => a[k] != b[k], Math.Min(a.Length, b.Length));
                    ctx.Report.Error($"[R16 공통구간] `{name}` 구간이 {baseline.File} 와 다릅니다 — 구간 안 {line + 1}번째 줄부터. 한쪽만 고쳤습니다. ({other.File})");
                }
            }
        }

        /// <summary>
        /// 서버 함수 호출 수는 함수마다 넣은 공통 구간 usage-count 가 센다 — 새 함수에 빠뜨리면 그 함수는 콘솔
        /// "서버 함수 호출"에 아예 안 나오고 아무 신호도 없다. 함수 폴더마다 구간이 있고, 핸들러 첫 줄에서 자기
        /// 폴더 이름으로 countCall 을 부르는지 본다(다른 이름이면 다른 함수 몫으로 쌓인다).
        /// </summary>
        private static void UsageCount(AuditContext ctx, IEnumerable<string> dirs)
        {
            foreach (var fnDir in dirs.SelectMany(Directory.GetDirectories).OrderBy(p => p, StringComparer.Ordinal))
            {
                var name = Path.GetFileName(fnDir);
                var index = Path.Combine(fnDir, "index.ts");
                if (name.StartsWith("_", StringComparison.Ordinal) || !File.Exists(index))
                    continue; // 공유 폴더
                var text = File.ReadAllText(index).Replace("\r\n", "\n");
                var rel = ctx.Rel(index);
                if (!Regex.IsMatch(text, @"^//\s*═══\s*BEGIN\s+usage-count\s*═══", RegexOptions.Multiline))
                {
                    ctx.Report.Error($"[R16 호출세기] `{name}` 에 공통 구간 usage-count 가 없습니다 — 이 함수 호출이 콘솔 서버 상태에 안 잡힙니다. 다른 함수에서 구간을 복사해 넣으세요. ({rel})");
                    continue;
                }
                var first = HandlerFirstLine.Match(text);
                var call = first.Success ? CountCall.Match(first.Groups["line"].Value.Trim()) : Match.Empty;
                if (!call.Success)
                    ctx.Report.Error($"[R16 호출세기] `{name}` 의 Deno.serve 핸들러 첫 줄이 countCall(\"{name}\", req); 가 아닙니다 — 앞에서 끝나는 요청은 안 세어집니다. ({rel})");
                else if (call.Groups["name"].Value != name)
                    ctx.Report.Error($"[R16 호출세기] `{name}` 가 countCall(\"{call.Groups["name"].Value}\", …) 로 다른 이름을 셉니다 — 다른 함수 몫으로 쌓입니다. ({rel})");
            }
        }

        /// <summary>
        /// 운영 콘솔에서 넣는 서버 연결값의 키 목록은 네 곳에 있다 — DB 허용 목록(ts_server_config_keys),
        /// 콘솔 서버의 형식 검사(SERVER_CONFIG_VALID), 콘솔 화면(STORE_KEYS), 엣지 함수의 serverConfig("…") 호출.
        /// DB 목록에 없는 키는 저장이 거절되고, 화면에 없는 키는 운영자가 넣을 길이 없다.
        /// </summary>
        private static void ServerConfigKeys(AuditContext ctx, string edgeDir)
        {
            var sqlPath = Path.Combine(ctx.Root, "Samples~", "DatabaseSetup", "SQL", "player", "install.sql");
            if (!File.Exists(sqlPath))
                return;
            var fn = Regex.Match(File.ReadAllText(sqlPath), @"function\s+public\.ts_server_config_keys\(\)[\s\S]*?\$\$([\s\S]*?)\$\$");
            if (!fn.Success)
                return; // 이 기능이 없는 저장소(검사기 픽스처 등)
            var sqlKeys = Regex.Matches(fn.Groups[1].Value, @"'([A-Z0-9_]+)'").Select(m => m.Groups[1].Value).ToHashSet();
            var sqlRel = ctx.Rel(sqlPath);

            void Compare(string relPath, string blockPattern, string what)
            {
                var path = Path.Combine(ctx.Root, relPath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(path))
                {
                    ctx.Report.Error($"[R16 연결값키] {relPath} 를 찾지 못해 키 목록을 대조하지 못했습니다. 경로가 바뀌었으면 검사기도 고치세요.");
                    return;
                }
                var block = Regex.Match(File.ReadAllText(path), blockPattern);
                if (!block.Success)
                {
                    ctx.Report.Error($"[R16 연결값키] {relPath} 에서 {what} 를 찾지 못했습니다. 검사기가 목록을 못 읽고 있습니다.");
                    return;
                }
                var keys = Regex.Matches(block.Groups[1].Value, @"\b([A-Z][A-Z0-9]*(?:_[A-Z0-9]+)+)\b").Select(m => m.Groups[1].Value).ToHashSet();
                foreach (var k in sqlKeys.Except(keys))
                    ctx.Report.Error($"[R16 연결값키] `{k}` 가 {sqlRel} 의 ts_server_config_keys 에는 있는데 {what} 에 없습니다. ({relPath})");
                foreach (var k in keys.Except(sqlKeys))
                    ctx.Report.Error($"[R16 연결값키] `{k}` 가 {what} 에는 있는데 {sqlRel} 의 ts_server_config_keys 에 없습니다 — 저장이 거절됩니다. ({relPath})");
            }

            Compare("Admin~/supabase/functions/admin-api/index.ts", @"SERVER_CONFIG_VALID[^\n]*=\s*\{([\s\S]*?)\n\};", "콘솔 서버 SERVER_CONFIG_VALID");
            Compare("Admin~/src/pages/Settings.tsx", @"STORE_KEYS[^\n]*=\s*\[([\s\S]*?)\n\]", "콘솔 화면 STORE_KEYS");

            foreach (var path in Directory.GetFiles(edgeDir, "*.ts", SearchOption.AllDirectories))
            {
                foreach (Match m in Regex.Matches(File.ReadAllText(path), @"serverConfig(?:List)?\(""([A-Z0-9_]+)""\)"))
                {
                    if (!sqlKeys.Contains(m.Groups[1].Value))
                        ctx.Report.Error($"[R16 연결값키] 엣지 함수가 읽는 `{m.Groups[1].Value}` 가 {sqlRel} 의 ts_server_config_keys 에 없습니다 — 콘솔 값이 항상 빈 채로 읽힙니다. ({ctx.Rel(path)})");
                }
            }
        }

        /// <summary>
        /// 서비스 연결의 묶음(사용 켜기/끄기 단위) 이름도 네 곳에 있다 — 표 제약(server_config_services check),
        /// 켜고 끄는 DB 함수(ts_admin_server_config_service_set), 콘솔 서버(settings.setServiceEnabled), 콘솔 화면(StoreGroup).
        /// 한 곳이라도 빠지면 그 묶음의 사용 스위치가 오류를 낸다.
        /// </summary>
        private static void ServerConfigServices(AuditContext ctx)
        {
            var sqlPath = Path.Combine(ctx.Root, "Samples~", "DatabaseSetup", "SQL", "player", "install.sql");
            if (!File.Exists(sqlPath))
                return;
            var sql = File.ReadAllText(sqlPath);
            if (!sql.Contains("server_config_services"))
                return; // 이 기능이 없는 저장소(검사기 픽스처 등)
            var sqlRel = ctx.Rel(sqlPath);

            HashSet<string>? Read(string text, string pattern, string what, string rel)
            {
                var m = Regex.Match(text, pattern);
                if (!m.Success)
                {
                    ctx.Report.Error($"[R16 연결묶음] {rel} 에서 {what} 를 찾지 못했습니다. 검사기가 목록을 못 읽고 있습니다.");
                    return null;
                }
                return Regex.Matches(m.Groups[1].Value, @"['""]([a-z][a-z0-9_]*)['""]").Select(x => x.Groups[1].Value).ToHashSet();
            }

            string? ReadFile(string relPath)
            {
                var path = Path.Combine(ctx.Root, relPath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(path))
                    return File.ReadAllText(path);
                ctx.Report.Error($"[R16 연결묶음] {relPath} 를 찾지 못해 묶음 목록을 대조하지 못했습니다. 경로가 바뀌었으면 검사기도 고치세요.");
                return null;
            }

            var lists = new List<(string What, string Rel, HashSet<string>? Names)>
            {
                ("표 제약 server_config_services", sqlRel,
                    Read(sql, @"check\s*\(\s*service\s+in\s*\(([^)]*)\)", "server_config_services 의 check", sqlRel)),
                ("DB 함수 ts_admin_server_config_service_set", sqlRel,
                    Read(sql, @"p_service\s+not\s+in\s*\(([^)]*)\)", "ts_admin_server_config_service_set 의 목록", sqlRel)),
            };
            const string apiRel = "Admin~/supabase/functions/admin-api/index.ts";
            var api = ReadFile(apiRel);
            if (api != null)
                lists.Add(("콘솔 서버 settings.setServiceEnabled", apiRel,
                    Read(api, @"""settings\.setServiceEnabled""[\s\S]*?\[([^\]]*)\]\.includes", "settings.setServiceEnabled 의 목록", apiRel)));
            const string uiRel = "Admin~/src/pages/Settings.tsx";
            var ui = ReadFile(uiRel);
            if (ui != null)
                lists.Add(("콘솔 화면 StoreGroup", uiRel,
                    Read(ui, @"type\s+StoreGroup\s*=([^\n]*)", "StoreGroup", uiRel)));

            var baseline = lists[0];
            if (baseline.Names == null)
                return;
            foreach (var other in lists.Skip(1))
            {
                if (other.Names == null)
                    continue;
                foreach (var n in baseline.Names.Except(other.Names))
                    ctx.Report.Error($"[R16 연결묶음] `{n}` 가 {baseline.What} 에는 있는데 {other.What} 에 없습니다 — 그 묶음의 사용 스위치가 오류를 냅니다. ({other.Rel})");
                foreach (var n in other.Names.Except(baseline.Names))
                    ctx.Report.Error($"[R16 연결묶음] `{n}` 가 {other.What} 에는 있는데 {baseline.What} 에 없습니다. ({other.Rel})");
            }
        }

        /// <summary>
        /// 사용량 기능 묶음의 이름은 DB(ts_usage_feature)가 정하고 설명은 콘솔 화면(FEATURE_INFO)에 있다.
        /// 한쪽만 고치면 그 묶음의 설명이 말없이 사라지거나, 없는 묶음의 설명만 남는다.
        /// </summary>
        private static void UsageFeatureInfo(AuditContext ctx)
        {
            var sqlPath = Path.Combine(ctx.Root, "Samples~", "DatabaseSetup", "SQL", "player", "install.sql");
            if (!File.Exists(sqlPath))
                return;
            var fn = Regex.Match(File.ReadAllText(sqlPath), @"function\s+public\.ts_usage_feature\([^)]*\)[\s\S]*?\$\$([\s\S]*?)\$\$");
            if (!fn.Success)
                return; // 이 기능이 없는 저장소(검사기 픽스처 등)

            const string relPath = "Admin~/src/pages/ServerStatus.tsx";
            var path = Path.Combine(ctx.Root, relPath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                ctx.Report.Error($"[R16 기능설명] {relPath} 를 찾지 못해 기능 설명을 대조하지 못했습니다. 경로가 바뀌었으면 검사기도 고치세요.");
                return;
            }
            var block = Regex.Match(File.ReadAllText(path), @"FEATURE_INFO[^\n]*=\s*\{([\s\S]*?)\n\}");
            if (!block.Success)
            {
                ctx.Report.Error($"[R16 기능설명] {relPath} 에서 FEATURE_INFO 를 찾지 못했습니다. 검사기가 목록을 못 읽고 있습니다.");
                return;
            }

            var names = Regex.Matches(fn.Groups[1].Value, @"\b(?:then|else)\s+'([^']+)'").Select(m => m.Groups[1].Value).ToHashSet();
            var infos = Regex.Matches(block.Groups[1].Value, @"^\s*'([^']+)':", RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToHashSet();
            foreach (var name in names.Except(infos))
                ctx.Report.Error($"[R16 기능설명] `{name}` 가 ts_usage_feature 에는 있는데 FEATURE_INFO 에 설명이 없습니다. ({relPath})");
            foreach (var name in infos.Except(names))
                ctx.Report.Error($"[R16 기능설명] `{name}` 의 설명이 FEATURE_INFO 에 있는데 ts_usage_feature 에 그런 묶음이 없습니다. ({relPath})");
        }
    }
}
