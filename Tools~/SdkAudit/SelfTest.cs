using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SdkAudit
{
    /// <summary>
    /// 검사기 자신의 회귀 테스트. 위반만 모아둔 임시 리포지토리를 만들어 돌리고,
    /// **각 규칙이 실제로 발동하는지** 확인한다.
    /// <para>
    /// 검사기가 커지면 규칙을 고치다 다른 규칙의 탐지를 조용히 망가뜨릴 수 있다.
    /// 실제로 소비 게임 검사(R12)를 처음 붙였을 때 경로 필터가 너무 넓어 아무것도 잡지 못했는데,
    /// "통과"만 보고는 알 수 없었다. 통과가 곧 동작을 뜻하지는 않는다.
    /// </para>
    /// </summary>
    public static class SelfTest
    {
        /// <summary>각 규칙이 픽스처에서 최소 한 번은 나와야 한다.</summary>
        private static readonly string[] ExpectedTags =
        {
            "[R1 internal]", "[R1 Try접두어]", "[R1 반환타입]",
            "[R2 리셋누락]",
            "[R3 없는API]",
            "[R4 시그니처]",
            "[R5 알림문법]", "[R5 헤딩괄호]", "[R5 매달린구분선]",
            "[R6 샘플]",
            "[R8 없는사유]", "[R8 internal노출]",
            "[R9 설치순서]",
            "[R10 코드우선]",
            "[R11 시각타입]", "[R11 별칭]",
            "[R12 소비게임]",
        };

        public static int Run()
        {
            var dir = Path.Combine(Path.GetTempPath(), "sdkaudit-selftest-" + Guid.NewGuid().ToString("N"));
            try
            {
                Build(dir);

                var ctx = new AuditContext(dir);
                CodeRules.Run(ctx);
                DocRules.Run(ctx);
                CodeRules.UnusedPublicApi(ctx);
                CodeRules.Consumers(ctx);
                SqlRules.Run(ctx);

                var found = ctx.Report.Errors.Concat(ctx.Report.Warnings).ToList();
                var missing = ExpectedTags.Where(tag => !found.Any(f => f.StartsWith(tag, StringComparison.Ordinal))).ToList();

                Console.WriteLine($"검사기 자체 테스트 — 기대 규칙 {ExpectedTags.Length}개 · 발동 {ExpectedTags.Length - missing.Count}개");
                Console.WriteLine();

                if (missing.Count == 0)
                {
                    Console.WriteLine("  ✔ 모든 규칙이 픽스처에서 발동했습니다.");
                    Console.WriteLine();
                    Console.WriteLine("결과: OK");
                    return 0;
                }

                foreach (var tag in missing)
                    Console.WriteLine($"  오류: {tag} 이 픽스처에서 발동하지 않았습니다. 탐지가 깨졌거나 픽스처가 규칙과 어긋납니다.");

                Console.WriteLine();
                Console.WriteLine($"결과: 실패 — 미발동 {missing.Count}건");
                return 1;
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { }
            }
        }

        /// <summary>위반만 모아둔 최소 리포지토리를 만든다.</summary>
        private static void Build(string root)
        {
            // Core — 사유 카탈로그. R8 이 여기 있는 이름과 대조한다.
            Write(root, "Runtime/Core/Models/SupabaseReason.cs", @"
namespace TrueBase.Core.Common
{
    internal enum SupabaseReason { None, Unknown, RealReason }
}");

            // 파사드 — R1(internal·Try 접두어·bare value)·R11(DateTime) 위반을 모아 둔다.
            Write(root, "Runtime/Unity/Supabase.cs", @"
using System;
using System.Threading.Tasks;

namespace TrueBase.Unity
{
    public static class Supabase
    {
        internal static void Wiring() { }
        public static SupabaseResult TryDoAsync() => null;
        public static string GetNameAsync() => null;
        public static DateTime GetWhen() => default;
        public static SupabaseResult RealApiAsync() => null;
    }
}");

            // 리셋 블록에서 한 파사드를 빠뜨린다 → R2.
            Write(root, "Runtime/Unity/SupabaseSDK.cs", @"
namespace TrueBase.Unity
{
    internal static class SupabaseSDK
    {
        private static ChatFacade _chat;
        private static MailFacade _mail;

        public static void Initialize()
        {
            _chat = null;
        }
    }
}");
            Write(root, "Runtime/Unity/ChatFacade.cs", "namespace TrueBase.Unity { internal sealed class ChatFacade { } }");
            Write(root, "Runtime/Unity/MailFacade.cs", "namespace TrueBase.Unity { internal sealed class MailFacade { } }");

            // 샘플 — R6(없는 멤버)·R11(별칭).
            Write(root, "Samples~/Example.cs", @"
using Sb = TrueBase.Unity.Supabase;

class Example { void M() { Supabase.NoSuchMemberAsync(); } }");

            // 문서 — R3·R4·R5·R8·R10 위반.
            Write(root, "docs~/guide/broken.md", @"# 깨진 페이지 (부연 괄호)

설명이 코드보다 먼저 온다.

```csharp
SupabaseResult Supabase.RealApiAsync(int wrongParam)
```

`Supabase.GhostApiAsync()` 는 없는 API다.
`SupabaseReason.GhostReason` 은 없는 사유다.
`SupabaseErrorCode` 는 게임 문서에 노출하면 안 된다.

> [!NOTE]
> 알림 문법은 렌더되지 않는다.

---");

            // SQL — R9(앞 절이 뒤 절 객체를 참조).
            Write(root, "Samples~/DatabaseSetup/SQL/player/install.sql", @"
create index if not exists early_idx on public.late_table (id);

create table if not exists public.late_table (
  id uuid primary key
);");

            // 소비 게임 — R12.
            Write(root, "consumer/Assets/Scripts/Game.cs", "class Game { void M() { Supabase.GhostForGameAsync(); } }");
            Write(root, "Tools~/SdkAudit/consumers.txt", "consumer");
        }

        private static void Write(string root, string relative, string content)
        {
            var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, content.TrimStart('\n'));
        }
    }
}
