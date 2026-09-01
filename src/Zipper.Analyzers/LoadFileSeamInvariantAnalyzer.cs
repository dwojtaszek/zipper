using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Zipper.Analyzers;

/// <summary>
/// Diagnostic analyzer enforcing the load-file three-stage seam invariant (ZIP001):
/// Composer -> Serializer -> Emitter. Direct stream/writer writes bypassing
/// <see cref="ILoadFileSerializer"/> inside load-file composer/emitter layers are flagged.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LoadFileSeamInvariantAnalyzer : ZipperAnalyzerBase
{
    public const string DiagnosticId = "ZIP001";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: "Direct stream/write access bypasses ILoadFileSerializer",
        messageFormat: "Direct stream/write access bypasses ILoadFileSerializer in load-file pipeline",
        category: "Architecture",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Write operations in load-file generation must route through ILoadFileSerializer; bypassing violates the Composer->Serializer->Emitter seam.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    protected override void RegisterActions(AnalysisContext context)
    {

        context.RegisterSyntaxNodeAction(AnalyzeWriteAccess, SyntaxKind.SimpleMemberAccessExpression);
    }

    private static void AnalyzeWriteAccess(SyntaxNodeAnalysisContext context)
    {
        var memberAccess = (MemberAccessExpressionSyntax)context.Node;
        if (memberAccess.Name.Identifier.Text != "Write")
        {
            return;
        }

        var classDecl = memberAccess.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault();
        if (classDecl is null)
        {
            return;
        }

        var className = classDecl.Identifier.Text;
        if (!IsLoadFileTargetLayer(className))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Rule, memberAccess.GetLocation()));
    }

    private static bool IsLoadFileTargetLayer(string className)
    {
        var upper = className.ToUpperInvariant();
        return upper.Contains("COMPOSER")
            || upper.Contains("EMITTER")
            || upper.Contains("SERIALIZER");
    }
}
