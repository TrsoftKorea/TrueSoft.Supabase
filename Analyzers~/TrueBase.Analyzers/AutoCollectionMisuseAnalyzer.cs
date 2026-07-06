using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace TrueBase.Analyzers
{
    /// <summary>
    /// AutoList/AutoDict 계열을 List/IList로 변환하면 안전 인덱서(자동 확장·비파괴 읽기)가
    /// 사라지는 것을 컴파일 타임에 경고합니다.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class AutoCollectionMisuseAnalyzer : DiagnosticAnalyzer
    {
        public const string CastRuleId = "TB0001";

        private static readonly DiagnosticDescriptor CastRule = new DiagnosticDescriptor(
            id: CastRuleId,
            title: "자동 확장 컬렉션을 List로 변환",
            messageFormat: "'{0}'을(를) '{1}'(으)로 변환하면 안전 인덱서가 사라져 범위 밖 접근이 예외가 됩니다. 변수·파라미터 타입을 자동 확장 컬렉션 그대로 유지하세요.",
            category: "TrueBase.Usage",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: "AutoList/AutoList2D/AutoDict/AutoDict2D를 List<T>·IList<T>·IReadOnlyList<T>로 캐스팅·대입·인자 전달하면 자동 확장과 비파괴 읽기가 사라집니다.");

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
            => ImmutableArray.Create(CastRule);

        // 자동 확장 컬렉션 (TrueBase.Core.Data)
        private static readonly string[] AutoTypeMetadataNames =
        {
            "TrueBase.Core.Data.AutoList`1",
            "TrueBase.Core.Data.AutoList2D`1",
            "TrueBase.Core.Data.AutoDict`2",
            "TrueBase.Core.Data.AutoDict2D`3",
        };

        // 변환 시 안전 인덱서를 잃는 대상 타입(인덱서 보유)
        private static readonly string[] UnsafeTargetMetadataNames =
        {
            "System.Collections.Generic.List`1",
            "System.Collections.Generic.IList`1",
            "System.Collections.Generic.IReadOnlyList`1",
        };

        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.RegisterCompilationStartAction(OnCompilationStart);
        }

        private static void OnCompilationStart(CompilationStartAnalysisContext ctx)
        {
            var comp = ctx.Compilation;

            var autoTypes = Resolve(comp, AutoTypeMetadataNames);
            if (autoTypes.IsEmpty) return; // SDK 미참조 → 분석 비활성

            var unsafeTargets = Resolve(comp, UnsafeTargetMetadataNames);
            if (unsafeTargets.IsEmpty) return;

            ctx.RegisterOperationAction(opCtx =>
            {
                var conv = (IConversionOperation)opCtx.Operation;
                var from = conv.Operand?.Type;
                var to = conv.Type;
                if (from == null || to == null) return;
                if (SymbolEqualityComparer.Default.Equals(from, to)) return;
                if (!IsAutoType(from, autoTypes)) return;
                if (!IsUnsafeTarget(to, unsafeTargets)) return;

                opCtx.ReportDiagnostic(Diagnostic.Create(
                    CastRule, conv.Syntax.GetLocation(), from.Name, to.Name));
            }, OperationKind.Conversion);
        }

        private static ImmutableArray<INamedTypeSymbol> Resolve(Compilation comp, string[] metadataNames)
            => metadataNames
                .Select(comp.GetTypeByMetadataName)
                .Where(s => s != null)
                .Select(s => s.OriginalDefinition)
                .ToImmutableArray();

        private static bool IsAutoType(ITypeSymbol type, ImmutableArray<INamedTypeSymbol> autoTypes)
        {
            for (var t = type as INamedTypeSymbol; t != null; t = t.BaseType)
            {
                var def = t.OriginalDefinition;
                foreach (var a in autoTypes)
                    if (SymbolEqualityComparer.Default.Equals(def, a)) return true;
            }
            return false;
        }

        private static bool IsUnsafeTarget(ITypeSymbol type, ImmutableArray<INamedTypeSymbol> targets)
        {
            if (!(type is INamedTypeSymbol named)) return false;
            var def = named.OriginalDefinition;
            foreach (var t in targets)
                if (SymbolEqualityComparer.Default.Equals(def, t)) return true;
            return false;
        }
    }
}
