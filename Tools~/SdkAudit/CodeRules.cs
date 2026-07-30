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
    /// <summary>R1 공개 표면 · R2 리셋 대칭성 · R6 샘플 최신성 · R7 미사용 공개 API.</summary>
    public static class CodeRules
    {
        // 값을 돌려주지만 실패할 수 없는 멤버 — R1 반환타입 규칙의 대상이 아니다.
        // 네트워크를 타지 않는 순수 변환·등록 헬퍼만 들어간다. 새로 넣을 때는 정말 실패할 수 없는지 확인할 것.
        private static readonly HashSet<string> NonFailingMembers = new HashSet<string>(StringComparer.Ordinal)
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

        public static void Run(AuditContext ctx)
        {
            PublicSurface(ctx);
            ResetSymmetry(ctx);
            Samples(ctx);
        }

        // ── R1 ─────────────────────────────────────────────────────────────
        private static void PublicSurface(AuditContext ctx)
        {
            foreach (var (src, cls) in ctx.Classes.Where(c => AuditContext.EntryPoints.Contains(c.Node.Identifier.ValueText)))
            {
                var owner = cls.Identifier.ValueText;

                foreach (var m in cls.Members)
                {
                    var name = MemberName(m);
                    if (name == null)
                        continue;

                    var where = ctx.Where(src, m);

                    if (Has(m, SyntaxKind.InternalKeyword))
                    {
                        // 규칙은 Supabase 파사드를 명시한다. 다른 진입점은 같은 원칙이 적용되는지 판단이 필요해 경고로 둔다.
                        var msg = $"{owner}.{name} 이 internal 입니다. 내부 배선은 SupabaseSDK 를 직접 참조하세요. ({where})";
                        if (owner == "Supabase")
                            ctx.Report.Error("[R1 internal] " + msg);
                        else
                            ctx.Report.Warn($"[R1 internal] {msg} — 규칙이 Supabase 파사드만 명시하고 있어 이 진입점에 적용되는지 확인이 필요합니다.");
                        continue;
                    }

                    if (!Has(m, SyntaxKind.PublicKeyword))
                        continue;

                    ctx.PublicApi[name] = owner;

                    if (m is not MethodDeclarationSyntax method)
                        continue;

                    ctx.PublicMethods.Add(method);

                    if (name.StartsWith("Try", StringComparison.Ordinal))
                        ctx.Report.Error($"[R1 Try접두어] {owner}.{name} — 공개 메서드에 Try 접두어를 쓰지 않습니다. ({where})");

                    var ret = method.ReturnType.ToString();
                    if (!IsResultType(ret) && !NonFailingMembers.Contains(name))
                        ctx.Report.Error($"[R1 반환타입] {owner}.{name} 이 result 타입이 아닌 {ret} 을 돌려줍니다. 실패할 수 없는 멤버라면 검사기의 NonFailingMembers 에 근거와 함께 넣으세요. ({where})");

                    // ── R11 게임 대면 절대 시각은 DateTimeOffset ── DateTime 은 오프셋 정보가 없어 기기 시간대에 휘둘린다.
                    if (Regex.IsMatch(ret, @"(?<!\w)DateTime(?!Offset)\b"))
                        ctx.Report.Error($"[R11 시각타입] {owner}.{name} 이 DateTime 을 돌려줍니다. 게임 대면 절대 시각은 DateTimeOffset 을 씁니다. ({where})");

                    foreach (var p in method.ParameterList.Parameters)
                        if (Regex.IsMatch(p.Type?.ToString() ?? "", @"(?<!\w)DateTime(?!Offset)\b"))
                            ctx.Report.Error($"[R11 시각타입] {owner}.{name} 의 파라미터 {p.Identifier.ValueText} 가 DateTime 입니다. DateTimeOffset 을 씁니다. ({where})");
                }
            }

            if (ctx.PublicApi.Count == 0)
                ctx.Report.Error("[R1] 파사드에서 공개 멤버를 찾지 못했습니다. 검사기가 파일 구조를 못 읽고 있습니다.");

            // 샘플이 부르는 정적 클래스는 파사드만이 아니다(SupabaseBridge 등). 타입별 공개 멤버를 모아 둔다.
            foreach (var (_, cls) in ctx.Classes)
            {
                var reachable = Has(cls, SyntaxKind.PublicKeyword);
                if (!ctx.TypeMembers.TryGetValue(cls.Identifier.ValueText, out var members))
                    ctx.TypeMembers[cls.Identifier.ValueText] = members = new TypeSurface();

                members.IsPublicType |= reachable;
                foreach (var m in cls.Members.Where(m => Has(m, SyntaxKind.PublicKeyword)))
                {
                    var name = MemberName(m);
                    if (name != null)
                        members.PublicMembers.Add(name);
                }
            }
        }

        // ── R2 ─────────────────────────────────────────────────────────────
        private static void ResetSymmetry(AuditContext ctx)
        {
            var facadesWithReset = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (src, cls) in ctx.Classes)
            {
                if (!cls.Identifier.ValueText.EndsWith("Facade", StringComparison.Ordinal))
                    continue;
                var resetMethod = cls.Members.OfType<MethodDeclarationSyntax>().FirstOrDefault(x => x.Identifier.ValueText == "Reset");
                if (resetMethod != null)
                    facadesWithReset[cls.Identifier.ValueText] = ctx.Where(src, resetMethod);
            }

            var sdk = ctx.Classes.FirstOrDefault(c => c.Node.Identifier.ValueText == "SupabaseSDK");
            if (sdk.Node == null)
            {
                ctx.Report.Error("[R2] SupabaseSDK 클래스를 찾지 못했습니다.");
                return;
            }

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
                ctx.Report.Error("[R2] SupabaseSDK 에서 파사드 백킹 필드를 찾지 못했습니다.");
                return;
            }

            // 필드를 가장 많이 null 로 되돌리는 메서드를 리셋 블록으로 본다. 이름이 바뀌어도 따라간다.
            var nullers = sdk.Node.Members.OfType<MethodDeclarationSyntax>()
                .Select(m => (Name: m.Identifier.ValueText, Body: m.Body?.ToString() ?? ""))
                .Select(x => (x.Name, x.Body, Nulled: facadeFields.Where(f => NullsField(x.Body, f.Field)).Select(f => f.Field).ToList()))
                .Where(x => x.Nulled.Count > 0)
                .OrderByDescending(x => x.Nulled.Count)
                .ToList();

            if (nullers.Count == 0)
            {
                ctx.Report.Error("[R2] 파사드 필드를 정리하는 메서드가 없습니다.");
                return;
            }

            var reset = nullers[0];

            foreach (var f in facadeFields)
                if (!reset.Nulled.Contains(f.Field))
                    ctx.Report.Error($"[R2 리셋누락] {f.Type} {f.Field} 가 리셋 블록({reset.Name})에서 정리되지 않습니다.");

            foreach (var f in facadeFields.Where(f => facadesWithReset.ContainsKey(f.Type)))
                if (!CallsReset(reset.Body, f.Field))
                    ctx.Report.Error($"[R2 Reset미호출] {f.Type} 은 Reset() 을 갖는데 리셋 블록({reset.Name})에서 호출되지 않습니다. 필드만 null 로 두면 내부 상태(구독·커서 등)가 남습니다.");

            // 리셋 블록이 아닌데 일부만 정리하는 메서드 — 세션 종속 상태를 빠뜨렸을 수 있다(판단 필요).
            foreach (var m in nullers.Skip(1))
            {
                var skipped = facadeFields
                    .Where(f => facadesWithReset.ContainsKey(f.Type) && !m.Nulled.Contains(f.Field))
                    .Select(f => f.Type)
                    .ToList();
                if (skipped.Count > 0)
                    ctx.Report.Warn($"[R2 부분정리] {m.Name} 이 파사드 일부만 정리합니다({string.Join(", ", m.Nulled)}). Reset() 을 가진 {string.Join(", ", skipped)} 은 여기서 정리되지 않습니다 — 이 시점에 남아도 되는 상태인지 확인하세요.");
            }

            // Reset() 을 갖지만 아무도 부르지 않는 파사드 — 죽은 코드.
            var allText = string.Join("\n", ctx.RuntimeSources.Select(s => s.Text));
            foreach (var kv in facadesWithReset)
            {
                var fields = Regex.Matches(allText, @"\b" + Regex.Escape(kv.Key) + @"\s+(_\w+)\s*;")
                    .Select(m => m.Groups[1].Value).Distinct().ToList();
                if (fields.Count > 0 && !fields.Any(f => CallsReset(allText, f)))
                    ctx.Report.Error($"[R2 죽은Reset] {kv.Key}.Reset() 이 어디에서도 호출되지 않습니다. ({kv.Value})");
            }
        }

        // ── R6 샘플 최신성 ─────────────────────────────────────────────────
        // Samples~ 는 import 전까지 컴파일되지 않아 API 가 바뀌어도 조용히 썩는다.
        private static void Samples(AuditContext ctx)
        {
            if (ctx.SampleSources.Count == 0)
            {
                ctx.Report.Warn("[R6] Samples~ 를 찾지 못해 샘플 검사를 건너뜁니다.");
                return;
            }

            foreach (var src in ctx.SampleSources)
            {
                var lines = src.Text.Replace("\r\n", "\n").Split('\n');
                for (var i = 0; i < lines.Length; i++)
                {
                    // ── R11 진입점에 별칭을 두지 않는다 ── 문서·공개 API가 전부 Supabase.Xxx() 라 별칭을 섞으면 배우는 사람이 혼란스럽다.
                    var alias = Regex.Match(lines[i], @"^\s*using\s+(\w+)\s*=\s*[\w.:]*\b(Supabase|SupabaseIAP|SupabaseBridge)\s*;");
                    if (alias.Success)
                        ctx.Report.Error($"[R11 별칭] 진입점 {alias.Groups[2].Value} 에 별칭 {alias.Groups[1].Value} 를 두지 않습니다. 직접 호출하세요. ({ctx.Rel(src.Path)}:{i + 1})");

                    // 로그 태그 "[Supabase.Chat]" 는 멤버 참조가 아니다.
                    // 샘플은 어셈블리 밖이라 타입과 멤버가 **둘 다** public 이어야 부를 수 있다.
                    foreach (Match hit in Regex.Matches(lines[i], @"(?<![\w.\[])([A-Z]\w*)\.([A-Z]\w*)"))
                    {
                        var type = hit.Groups[1].Value;
                        var name = hit.Groups[2].Value;
                        if (!ctx.TypeMembers.TryGetValue(type, out var surface))
                            continue; // SDK 가 선언한 타입이 아니다

                        var at = $"{ctx.Rel(src.Path)}:{i + 1}";
                        if (!surface.IsPublicType)
                            ctx.Report.Error($"[R6 샘플] 샘플이 {type}.{name} 을 부르지만 {type} 이 internal 이라 어셈블리 밖에서 보이지 않습니다. ({at})");
                        else if (!surface.PublicMembers.Contains(name))
                            ctx.Report.Error($"[R6 샘플] 샘플이 {type}.{name} 을 부르지만 공개 멤버가 아닙니다. ({at})");
                    }
                }
            }
        }

        // ── R7 미사용 공개 API ─────────────────────────────────────────────
        // 문서·샘플·SDK 어디에서도 참조되지 않는 공개 멤버는 죽은 API 후보다.
        // DocApiNames 가 채워진 뒤에 돌려야 하므로 문서 규칙 다음에 호출한다.
        public static void UnusedPublicApi(AuditContext ctx)
        {
            var facadeFiles = new HashSet<string>(
                ctx.Classes.Where(c => AuditContext.EntryPoints.Contains(c.Node.Identifier.ValueText))
                    .Select(c => c.Src.Path),
                StringComparer.OrdinalIgnoreCase);

            var runtimeOutsideFacade = string.Join("\n",
                ctx.RuntimeSources.Where(s => !facadeFiles.Contains(s.Path)).Select(s => s.Text));
            var sampleText = string.Join("\n", ctx.SampleSources.Select(s => s.Text));

            foreach (var kv in ctx.PublicApi.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                if (ctx.DocApiNames.Contains(kv.Key))
                    continue;

                var token = new Regex(@"\b" + Regex.Escape(kv.Key) + @"\b");
                if (token.IsMatch(sampleText) || token.IsMatch(runtimeOutsideFacade))
                    continue;

                ctx.Report.Warn($"[R7 미참조] {kv.Value}.{kv.Key} 이 문서·샘플·SDK 어디에서도 참조되지 않습니다. 죽은 API 후보입니다.");
            }
        }

        // ── 헬퍼 ───────────────────────────────────────────────────────────
        private static bool Has(MemberDeclarationSyntax m, SyntaxKind kind) =>
            m.Modifiers.Any(t => t.IsKind(kind));

        private static string MemberName(MemberDeclarationSyntax m) => m switch
        {
            MethodDeclarationSyntax x => x.Identifier.ValueText,
            PropertyDeclarationSyntax x => x.Identifier.ValueText,
            EventDeclarationSyntax x => x.Identifier.ValueText,
            EventFieldDeclarationSyntax x => x.Declaration.Variables.First().Identifier.ValueText,
            _ => null,
        };

        // 게임에 돌려줄 수 있는 반환 타입: result 계층 또는 그것을 감싼 Task.
        private static bool IsResultType(string ret)
        {
            ret = ret.Trim();
            while (ret.StartsWith("Task<", StringComparison.Ordinal) && ret.EndsWith(">", StringComparison.Ordinal))
                ret = ret.Substring(5, ret.Length - 6).Trim();

            var outer = (ret.Contains('<') ? ret.Substring(0, ret.IndexOf('<')) : ret).Trim();
            return outer is "SupabaseResult" or "SupabaseLoadResult" or "SupabaseSignInResult";
        }

        private static bool NullsField(string body, string field) =>
            Regex.IsMatch(body, @"(^|[^\w])" + Regex.Escape(field) + @"\s*=\s*null\s*;");

        private static bool CallsReset(string body, string field) =>
            Regex.IsMatch(body, Regex.Escape(field) + @"\s*\??\s*\.\s*Reset\s*\(");
    }
}
