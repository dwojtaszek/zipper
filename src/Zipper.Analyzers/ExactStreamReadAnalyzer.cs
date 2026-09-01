using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Zipper.Analyzers;

/// <summary>
/// Diagnostic analyzer enforcing exact stream reads (ZIP003):
/// Flags inexact <c>Stream.Read</c> / <c>ReadAsync</c> usage in sink layers.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ExactStreamReadAnalyzer : ZipperAnalyzerBase
{
    public const string DiagnosticId = "ZIP003";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: "Prefer exact stream reads",
        messageFormat: "Prefer exact stream reads instead of inexact Read/ReadAsync",
        category: "Correctness",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Stream.Read/ReadAsync can return partial reads. Use exact read methods to guarantee full buffer fill.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    protected override void RegisterActions(AnalysisContext context)
    {

        context.RegisterSyntaxNodeAction(AnalyzeReadAccess, SyntaxKind.SimpleMemberAccessExpression);
    }

    private static void AnalyzeReadAccess(SyntaxNodeAnalysisContext context)
    {
        var memberAccess = (MemberAccessExpressionSyntax)context.Node;
        var memberName = memberAccess.Name.Identifier.Text;
        if (memberName != "Read" && memberName != "ReadAsync")
        {
            return;
        }

        var classDecl = memberAccess.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault();
        if (classDecl is null || !IsSinkLayer(classDecl.Identifier.Text))
        {
            return;
        }

        if (!IsStreamType(context))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Rule, memberAccess.GetLocation()));
    }

    private static bool IsStreamType(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not MemberAccessExpressionSyntax memberAccess)
        {
            return false;
        }

        var symbol = context.SemanticModel.GetSymbolInfo(memberAccess, context.CancellationToken).Symbol;
        if (symbol is not IMethodSymbol method)
        {
            return false;
        }

        var containing = method.ContainingType;
        if (containing is null)
        {
            return false;
        }

        var name = containing.ToDisplayString();
        return name == "System.IO.Stream"
            || name == "System.IO.FileStream"
            || name == "System.IO.BufferedStream"
            || name == "System.IO.MemoryStream";
    }

    private static bool IsSinkLayer(string typeName)
    {
        var upper = typeName.ToUpperInvariant();
        return upper.EndsWith("SINK", StringComparison.Ordinal)
            || upper.EndsWith("WRITER", StringComparison.Ordinal);
    }
}
