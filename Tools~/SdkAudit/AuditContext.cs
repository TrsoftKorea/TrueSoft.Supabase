using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SdkAudit
{
    /// <summary>파싱한 소스 하나.</summary>
    public readonly struct Source
    {
        public readonly string Path;
        public readonly string Text;

        public Source(string path, string text)
        {
            Path = path;
            Text = text;
        }
    }

    /// <summary>오류·경고를 모으는 곳. 오류만 종료 코드에 영향을 준다.</summary>
    public sealed class Report
    {
        private readonly List<string> _errors = new List<string>();
        private readonly List<string> _warnings = new List<string>();

        public void Error(string message) => _errors.Add(message);
        public void Warn(string message) => _warnings.Add(message);

        public IReadOnlyList<string> Errors => _errors.Distinct().ToList();
        public IReadOnlyList<string> Warnings => _warnings.Distinct().ToList();
    }

    /// <summary>한 타입의 공개 표면. 타입 자체가 public 이어야 어셈블리 밖에서 부를 수 있다.</summary>
    public sealed class TypeSurface
    {
        public bool IsPublicType;
        public HashSet<string> PublicMembers { get; } = new HashSet<string>(StringComparer.Ordinal);
    }

    /// <summary>규칙들이 공유하는 파싱 결과.</summary>
    public sealed class AuditContext
    {
        public string Root { get; }
        public Report Report { get; } = new Report();

        public List<Source> RuntimeSources { get; } = new List<Source>();
        public List<Source> SampleSources { get; } = new List<Source>();
        public List<(Source Src, ClassDeclarationSyntax Node)> Classes { get; } = new List<(Source, ClassDeclarationSyntax)>();

        /// <summary>게임에 공개하는 파사드 멤버: 이름 -> 소유 클래스.</summary>
        public Dictionary<string, string> PublicApi { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>파사드 공개 메서드 선언(오버로드 포함).</summary>
        public List<MethodDeclarationSyntax> PublicMethods { get; } = new List<MethodDeclarationSyntax>();

        /// <summary>타입 이름 -> 그 타입의 공개 표면. 샘플이 부르는 정적 진입점 검증에 쓴다.</summary>
        public Dictionary<string, TypeSurface> TypeMembers { get; } = new Dictionary<string, TypeSurface>(StringComparer.Ordinal);

        /// <summary>SupabaseReason enum 멤버 이름.</summary>
        public HashSet<string> ReasonMembers { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>문서에 등장한 파사드 멤버 이름. R3 커버리지 판정에 쓴다.</summary>
        public HashSet<string> DocApiNames { get; } = new HashSet<string>(StringComparer.Ordinal);

        public static readonly string[] EntryPoints = { "Supabase", "SupabaseIAP" };

        public AuditContext(string root)
        {
            Root = root;

            RuntimeSources.AddRange(ReadTree(Path.Combine(root, "Runtime")));
            SampleSources.AddRange(ReadTree(Path.Combine(root, "Samples~")));

            foreach (var s in RuntimeSources)
                foreach (var c in CSharpSyntaxTree.ParseText(s.Text).GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
                    Classes.Add((s, c));

            var reasonFile = Path.Combine(root, "Runtime", "Core", "Models", "SupabaseReason.cs");
            if (File.Exists(reasonFile))
            {
                var enumDecl = CSharpSyntaxTree.ParseText(File.ReadAllText(reasonFile)).GetRoot()
                    .DescendantNodes().OfType<EnumDeclarationSyntax>()
                    .FirstOrDefault(e => e.Identifier.ValueText == "SupabaseReason");
                if (enumDecl != null)
                    foreach (var m in enumDecl.Members)
                        ReasonMembers.Add(m.Identifier.ValueText);
            }
        }

        private static IEnumerable<Source> ReadTree(string dir)
        {
            if (!Directory.Exists(dir))
                return Enumerable.Empty<Source>();

            return Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories)
                .Select(p => new Source(p, File.ReadAllText(p)))
                .ToList();
        }

        public string Rel(string path) => Path.GetRelativePath(Root, path).Replace('\\', '/');

        public string Where(Source src, SyntaxNode node) => $"{Rel(src.Path)}:{LineOf(src, node)}";

        public static int LineOf(Source src, SyntaxNode node) =>
            src.Text.Take(node.SpanStart).Count(ch => ch == '\n') + 1;
    }
}
