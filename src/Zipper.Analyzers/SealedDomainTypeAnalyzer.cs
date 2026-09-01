using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Zipper.Analyzers;

/// <summary>
/// Diagnostic analyzer enforcing sealed record/value-type patterns (ZIP004):
/// Encourages sealed keyword for types that should not be inherited.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SealedDomainTypeAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "ZIP004";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: "Prefer sealed for domain types",
        messageFormat: "Consider sealing {0} to prevent unintended inheritance",
        category: "Architecture",
        defaultSeverity: DiagnosticSeverity.Hidden,
        isEnabledByDefault: true,
        description: "Domain types that are not designed for inheritance should be marked sealed to preserve architectural invariants.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeTypeDeclarations, SyntaxKind.ClassDeclaration);
    }

    private static void AnalyzeTypeDeclarations(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not ClassDeclarationSyntax classDecl)
        {
            return;
        }

        if (classDecl.Modifiers.Any(SyntaxKind.SealedKeyword))
        {
            return;
        }

        if (!IsDomainType(classDecl.Identifier.Text))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Rule, classDecl.Identifier.GetLocation(), classDecl.Identifier.Text));
    }

    private static bool IsDomainType(string typeName)
    {
        var upper = typeName.ToUpperInvariant();
        return upper.Contains("CONFIG")
            || upper.Contains("PLAN")
            || upper.Contains("REQUEST")
            || upper.Contains("RESULT")
            || upper.Contains("MODE");
    }
}
