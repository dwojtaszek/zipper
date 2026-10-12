using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Zipper.Analyzers;

/// <summary>
/// Diagnostic analyzer enforcing deterministic formatting APIs (ZIP002):
/// flags parameterless <c>ToString()</c> on non-string value types and <c>string.Format</c>
/// without an invariant <c>IFormatProvider</c> inside formatter/composer/emitter layers to avoid culture-sensitive formatting.
/// Calls to <c>string.Format</c> must supply a provider known to use invariant formatting (<see cref="System.Globalization.CultureInfo.InvariantCulture"/>).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DeterministicFormattableAnalyzer : ZipperAnalyzerBase
{
    public const string DiagnosticId = "ZIP002";
    private const string FormatProviderInterface = "System.IFormatProvider";

    private static readonly DiagnosticDescriptor ToStringRule = new(
        id: DiagnosticId,
        title: "Prefer deterministic formatting APIs",
        messageFormat: "Use deterministic formatting APIs instead of {0}",
        category: "Determinism",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Formatting APIs that depend on the current culture or mutable state should be avoided in deterministic formatter layers.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(ToStringRule);

    protected override void RegisterActions(AnalysisContext context)
    {

        context.RegisterSyntaxNodeAction(AnalyzeToString, SyntaxKind.InvocationExpression);
        context.RegisterSyntaxNodeAction(AnalyzeStringFormat, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeToString(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not InvocationExpressionSyntax invocation)
        {
            return;
        }

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return;
        }

        if (memberAccess.Name.Identifier.Text != "ToString")
        {
            return;
        }

        if (invocation.ArgumentList.Arguments.Count != 0)
        {
            return;
        }

        var classDecl = invocation.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault();
        if (classDecl is null || !IsFormatterLayer(classDecl.Identifier.Text))
        {
            return;
        }

        var symbolInfo = context.SemanticModel.GetSymbolInfo(memberAccess, context.CancellationToken);
        if (symbolInfo.Symbol is not IMethodSymbol method)
        {
            return;
        }

        var containing = method.ContainingType;
        if (containing is null || containing.SpecialType == SpecialType.System_String)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(ToStringRule, invocation.GetLocation(), "ToString()"));
    }

    private static void AnalyzeStringFormat(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not InvocationExpressionSyntax invocation)
        {
            return;
        }

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return;
        }

        if (memberAccess.Name.Identifier.Text != "Format")
        {
            return;
        }

        var exprText = memberAccess.Expression.ToString();
        if (exprText != "string" && exprText != "String" && exprText != "System.String")
        {
            return;
        }

        var classDecl = invocation.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault();
        if (classDecl is null || !IsFormatterLayer(classDecl.Identifier.Text))
        {
            return;
        }

        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken);
        var symbol = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
        if (symbol is IMethodSymbol method)
        {
            var providerParam = method.Parameters.FirstOrDefault(p => IsFormatProvider(p.Type));
            if (providerParam is not null)
            {
                var providerArg = GetArgumentForParameter(invocation, providerParam);
                if (providerArg is not null && IsInvariantProvider(context.SemanticModel, providerArg.Expression))
                {
                    return;
                }
            }
        }

        context.ReportDiagnostic(Diagnostic.Create(ToStringRule, invocation.GetLocation(), "string.Format"));
    }

    private static ArgumentSyntax? GetArgumentForParameter(
        InvocationExpressionSyntax invocation,
        IParameterSymbol parameter)
    {
        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            if (argument.NameColon is { } nameColon)
            {
                if (string.Equals(nameColon.Name.Identifier.Text, parameter.Name, StringComparison.Ordinal))
                {
                    return argument;
                }
            }
        }

        if (parameter.Ordinal < invocation.ArgumentList.Arguments.Count)
        {
            var positionalArg = invocation.ArgumentList.Arguments[parameter.Ordinal];
            if (positionalArg.NameColon is null)
            {
                return positionalArg;
            }
        }

        return null;
    }

    private static bool IsInvariantProvider(SemanticModel semanticModel, ExpressionSyntax expression)
    {
        var symbol = semanticModel.GetSymbolInfo(expression).Symbol;
        if (symbol is IPropertySymbol prop)
        {
            if (prop.Name == "InvariantCulture"
                && prop.ContainingType?.ToDisplayString() == "System.Globalization.CultureInfo")
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsFormatProvider(ITypeSymbol type)
    {
        if (type.OriginalDefinition.ToDisplayString() == FormatProviderInterface)
        {
            return true;
        }

        return type.AllInterfaces.Any(i => i.OriginalDefinition.ToDisplayString() == FormatProviderInterface);
    }

    private static bool IsFormatterLayer(string typeName)
    {
        var upper = typeName.ToUpperInvariant();
        return upper.Contains("FORMATTER")
            || (upper.Contains("LOADFILE") && upper.Contains("COMPOSER"));
    }
}
