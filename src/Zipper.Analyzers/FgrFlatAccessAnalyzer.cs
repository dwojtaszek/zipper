using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Zipper.Analyzers;

/// <summary>
/// Diagnostic analyzer that warns when code accesses a flat pass-through property on
/// <c>FileGenerationRequest</c> instead of going directly to the appropriate sub-config.
/// </summary>
/// <remarks>
/// Diagnostic ID: <c>FGR_FLAT_ACCESS</c> (Info / non-fatal).
/// Severity will be escalated to Error in phase F4 (#213) once all 106 call sites
/// are rewritten by phase F3 (#212).
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class FgrFlatAccessAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The diagnostic identifier emitted by this analyzer.</summary>
    public const string DiagnosticId = "FGR_FLAT_ACCESS";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: "Use sub-config access instead of flat pass-through on FileGenerationRequest",
        messageFormat: "Access '{0}' via '{1}.{0}' instead of the flat pass-through on FileGenerationRequest. This property will be removed in a follow-up.",
        category: "Design",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "The flat pass-through properties on FileGenerationRequest delegate to sub-configs and are scheduled for removal. Access sub-configs directly.");

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    // Maps each flat property name to the name of the sub-config it delegates to.
    // Single source of truth for both property detection and sub-config resolution.
    private static readonly ImmutableDictionary<string, string> SubConfigMap =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // Output sub-config (8)
            ["OutputPath"] = "Output",
            ["FileCount"] = "Output",
            ["FileType"] = "Output",
            ["Folders"] = "Output",
            ["Concurrency"] = "Output",
            ["WithText"] = "Output",
            ["TargetZipSize"] = "Output",
            ["IncludeLoadFile"] = "Output",

            // Metadata sub-config (7)
            ["WithMetadata"] = "Metadata",
            ["ColumnProfile"] = "Metadata",
            ["Seed"] = "Metadata",
            ["DateFormatOverride"] = "Metadata",
            ["EmptyPercentageOverride"] = "Metadata",
            ["CustodianCountOverride"] = "Metadata",
            ["WithFamilies"] = "Metadata",

            // LoadFile sub-config (5)
            ["LoadFileFormat"] = "LoadFile",
            ["LoadFileFormats"] = "LoadFile",
            ["Encoding"] = "LoadFile",
            ["Distribution"] = "LoadFile",
            ["AttachmentRate"] = "LoadFile",

            // Delimiters sub-config (6)
            ["ColumnDelimiter"] = "Delimiters",
            ["QuoteDelimiter"] = "Delimiters",
            ["NewlineDelimiter"] = "Delimiters",
            ["MultiValueDelimiter"] = "Delimiters",
            ["NestedValueDelimiter"] = "Delimiters",
            ["EndOfLine"] = "Delimiters",

            // Bates sub-config (1)
            ["BatesConfig"] = "Bates",

            // Tiff sub-config (1)
            ["TiffPageRange"] = "Tiff",

            // Chaos sub-config (4)
            ["ChaosMode"] = "Chaos",
            ["ChaosAmount"] = "Chaos",
            ["ChaosTypes"] = "Chaos",
            ["ChaosScenario"] = "Chaos",

            // Production sub-config (3)
            ["ProductionSet"] = "Production",
            ["ProductionZip"] = "Production",
            ["VolumeSize"] = "Production",
        }.ToImmutableDictionary();

    /// <summary>
    /// All 35 flat pass-through property names on <c>FileGenerationRequest</c>.
    /// Derived from <see cref="SubConfigMap"/> keys — single source of truth.
    /// Exposed internally so tests can drive fixture-style coverage over every name.
    /// </summary>
    internal static readonly ImmutableHashSet<string> FlatPropertyNames =
        SubConfigMap.Keys.ToImmutableHashSet(StringComparer.Ordinal);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeMemberAccess, SyntaxKind.SimpleMemberAccessExpression);
    }

    private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
    {
        var memberAccess = (MemberAccessExpressionSyntax)context.Node;
        var propertyName = memberAccess.Name.Identifier.Text;

        if (!SubConfigMap.TryGetValue(propertyName, out var subConfig))
        {
            return;
        }

        // Suppress diagnostics that originate inside FileGenerationRequest.cs itself
        // (the property accessor bodies access sub-configs, not the flat pass-throughs,
        // but we suppress the whole file defensively as specified in the issue).
        var filePath = context.Node.SyntaxTree.FilePath;
        if (filePath.EndsWith("FileGenerationRequest.cs", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Only report if the receiver type is exactly FileGenerationRequest.
        var symbol = context.SemanticModel.GetSymbolInfo(memberAccess).Symbol as IPropertySymbol;
        if (symbol is null)
        {
            return;
        }

        if (symbol.ContainingType?.Name != "FileGenerationRequest")
        {
            return;
        }

        var diagnostic = Diagnostic.Create(Rule, memberAccess.GetLocation(), propertyName, subConfig);
        context.ReportDiagnostic(diagnostic);
    }
}
