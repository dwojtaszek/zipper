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

    private static readonly HashSet<string> DirectWriteMethods = new(StringComparer.Ordinal)
    {
        "Write",
        "WriteLine",
        "WriteAsync",
        "WriteLineAsync",
    };

    private static readonly HashSet<string> FileIoWriteMethods = new(StringComparer.Ordinal)
    {
        "WriteAllText",
        "WriteAllTextAsync",
        "WriteAllBytes",
        "WriteAllBytesAsync",
        "WriteAllLines",
        "WriteAllLinesAsync",
        "AppendAllText",
        "AppendAllTextAsync",
        "AppendAllLines",
        "AppendAllLinesAsync",
        "Create",
        "OpenWrite",
    };

    private static void AnalyzeWriteAccess(SyntaxNodeAnalysisContext context)
    {
        var memberAccess = (MemberAccessExpressionSyntax)context.Node;
        var memberName = memberAccess.Name.Identifier.Text;
        var isDirectWrite = DirectWriteMethods.Contains(memberName);
        var isFileIoWrite = !isDirectWrite && FileIoWriteMethods.Contains(memberName);
        if (!isDirectWrite && !isFileIoWrite)
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

        if (isFileIoWrite && !IsSystemIoFileMember(context, memberAccess))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Rule, memberAccess.GetLocation()));
    }

    private static bool IsSystemIoFileMember(
        SyntaxNodeAnalysisContext context,
        MemberAccessExpressionSyntax memberAccess)
    {
        var symbol = context.SemanticModel.GetSymbolInfo(memberAccess, context.CancellationToken).Symbol;
        return symbol is IMethodSymbol method
            && string.Equals(method.ContainingType?.ToDisplayString(), "System.IO.File", StringComparison.Ordinal);
    }

    /// <summary>
    /// Composers and load-file serializers must not write. The Emitter is the I/O
    /// authority, so writes there are allowed. The exact names EmailSerializer and
    /// FileDataSerializer are not load-file types.
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

        return upper is not "EMAILSERIALIZER" and not "FILEDATASERIALIZER";
    }
}
