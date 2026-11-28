using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Zipper.Analyzers;

/// <summary>
/// Diagnostic analyzer enforcing the load-file three-stage seam invariant (ZIP001):
/// Composer -> Serializer -> Emitter. Direct stream/writer writes bypassing
/// <see cref="ILoadFileSerializer"/> inside load-file composer and serializer types are flagged.
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
        defaultSeverity: DiagnosticSeverity.Warning,
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
        var memberName = memberAccess.Name.Identifier.Text;
        if (memberName != "Write" && memberName != "WriteLine" && memberName != "WriteAsync" && memberName != "WriteLineAsync")
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

    /// <summary>
    /// Composers and load-file serializers must not write. The Emitter is the I/O
    /// authority, so writes there are allowed. EmailSerializer and FileDataSerializer
    /// are not load-file types.
    /// </summary>
    private static bool IsLoadFileTargetLayer(string className)
    {
        var upper = className.ToUpperInvariant();
        if (upper.Contains("COMPOSER"))
        {
            return true;
        }

        if (!upper.Contains("SERIALIZER"))
        {
            return false;
        }

        return !upper.Contains("EMAIL") && !upper.Contains("FILEDATA");
    }
}
