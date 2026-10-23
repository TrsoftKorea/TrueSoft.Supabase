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
    /// 다르게 동작하므로, 같은 이름의 구간이 글자 그대로 같은지 대조한다.
    /// </summary>
    public static class EdgeFunctionRules
    {
        private static readonly Regex Begin = new(@"^//\s*═══\s*BEGIN\s+(?<name>[\w-]+)\s*═══");
        private static readonly Regex End = new(@"^//\s*═══\s*END\s+(?<name>[\w-]+)\s*═══");

        public static void Run(AuditContext ctx)
        {
            var dir = Path.Combine(ctx.Root, "Samples~", "DatabaseSetup", "EdgeFunctions");
            if (!Directory.Exists(dir))
                return;

            var blocks = new Dictionary<string, List<(string File, string Text)>>(StringComparer.Ordinal);
            foreach (var path in Directory.GetFiles(dir, "*.ts", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
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
    }
}
