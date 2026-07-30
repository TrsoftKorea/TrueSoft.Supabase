using System;
using System.IO;
using System.Text;
using SdkAudit;

Console.OutputEncoding = Encoding.UTF8;

// 검사기 자신의 회귀 테스트. 규칙을 고친 뒤 다른 규칙의 탐지가 깨지지 않았는지 본다.
if (args.Length > 0 && args[0] == "--selftest")
    return SelfTest.Run();

var root = FindRepoRoot();
if (root == null)
{
    Console.WriteLine("오류: 리포지토리 루트(Runtime/Core 를 가진 폴더)를 찾지 못했습니다.");
    return 1;
}

var ctx = new AuditContext(root);

// 순서가 있다. CodeRules 가 공개 표면을 채우고, DocRules 가 문서 참조를 채운 뒤,
// 둘을 모두 아는 상태에서만 미참조 판정을 할 수 있다.
CodeRules.Run(ctx);
DocRules.Run(ctx);
CodeRules.UnusedPublicApi(ctx);
CodeRules.Consumers(ctx);
SqlRules.Run(ctx);

var errors = ctx.Report.Errors;
var warnings = ctx.Report.Warnings;

Console.WriteLine($"SDK 규칙 검사 — 공개 멤버 {ctx.PublicApi.Count}개 · 런타임 소스 {ctx.RuntimeSources.Count}개 · 샘플 소스 {ctx.SampleSources.Count}개");
Console.WriteLine();

foreach (var w in warnings)
    Console.WriteLine("  경고: " + w);
if (warnings.Count > 0)
    Console.WriteLine();

if (errors.Count == 0)
{
    Console.WriteLine("  ✔ R1 공개 표면 · R2 리셋 대칭성 · R3 문서 커버리지 · R4 시그니처 · R5 문서 형식 · R6 샘플 · R7 미참조 · R8 문서 값 · R9 설치 순서 · R10 문서 구조 · R11 명명 규칙 · R12 소비 게임 통과.");
    Console.WriteLine();
    Console.WriteLine(warnings.Count == 0 ? "결과: OK" : $"결과: OK (경고 {warnings.Count}건)");
    return 0;
}

foreach (var e in errors)
    Console.WriteLine("  오류: " + e);
Console.WriteLine();
Console.WriteLine($"결과: 실패 — 오류 {errors.Count}건" + (warnings.Count > 0 ? $", 경고 {warnings.Count}건" : ""));
return 1;

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null)
    {
        if (Directory.Exists(Path.Combine(dir.FullName, "Runtime", "Core")))
            return dir.FullName;
        dir = dir.Parent;
    }
    return null;
}
