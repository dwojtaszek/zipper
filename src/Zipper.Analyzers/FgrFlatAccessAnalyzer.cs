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
/// Diagnostic ID: <c>FGR_FLAT_ACCESS</c> (Error / build-breaking).
/// Severity escalated to Error in F4 (#213); flat pass-throughs have been deleted.
/// This rule now acts as a forever-guard preventing re-introduction.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class FgrFlatAccessAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The diagnostic identifier emitted by this analyzer.</summary>
    public const string DiagnosticId = "FGR_FLAT_ACCESS";

    // Sub-config type names — defined as constants to avoid string literal repetition (S1192).
    private const string OutputSub = "Output";
    private const string MetadataSub = "Metadata";
    private const string LoadFileSub = "LoadFile";
    private const string DelimitersSub = "Delimiters";
    private const string ChaosSub = "Chaos";
    private const string BatesSub = "Bates";
    private const string TiffSub = "Tiff";
    private const string ProductionSub = "Production";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: "Use sub-config access instead of flat pass-through on FileGenerationRequest",
        messageFormat: "Access '{0}' via '{1}.{0}' instead of a flat pass-through on FileGenerationRequest. Flat pass-throughs have been removed (see #213).",
        category: "Design",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The flat pass-through properties on FileGenerationRequest were removed in #213. Access sub-configs directly. Adding a new flat pass-through will break the build.");

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    // Maps each flat property name to the name of the sub-config it delegates to.
    // Single source of truth for both property detection and sub-config resolution.
    private static readonly ImmutableDictionary<string, string> SubConfigMap =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // Output sub-config (8)
            ["OutputPath"] = OutputSub,
            ["FileCount"] = OutputSub,
            ["FileType"] = OutputSub,
            ["Folders"] = OutputSub,
            ["Concurrency"] = OutputSub,
            ["WithText"] = OutputSub,
            ["TargetZipSize"] = OutputSub,
            ["IncludeLoadFile"] = OutputSub,

            // Metadata sub-config (7)
            ["WithMetadata"] = MetadataSub,
            ["ColumnProfile"] = MetadataSub,
            ["Seed"] = MetadataSub,
            ["DateFormatOverride"] = MetadataSub,
            ["EmptyPercentageOverride"] = MetadataSub,
            ["CustodianCountOverride"] = MetadataSub,
            ["WithFamilies"] = MetadataSub,

            // LoadFile sub-config (5)
            ["LoadFileFormat"] = LoadFileSub,
            ["LoadFileFormats"] = LoadFileSub,
            ["Encoding"] = LoadFileSub,
            ["Distribution"] = LoadFileSub,
            ["AttachmentRate"] = LoadFileSub,

            // Delimiters sub-config (6)
            ["ColumnDelimiter"] = DelimitersSub,
            ["QuoteDelimiter"] = DelimitersSub,
            ["NewlineDelimiter"] = DelimitersSub,
            ["MultiValueDelimiter"] = DelimitersSub,
            ["NestedValueDelimiter"] = DelimitersSub,
            ["EndOfLine"] = DelimitersSub,

            // Bates sub-config (1)
            ["BatesConfig"] = BatesSub,

            // Tiff sub-config (1)
            ["TiffPageRange"] = TiffSub,

            // Chaos sub-config (4)
            ["ChaosMode"] = ChaosSub,
            ["ChaosAmount"] = ChaosSub,
            ["ChaosTypes"] = ChaosSub,
            ["ChaosScenario"] = ChaosSub,

            // Production sub-config (3)
            ["ProductionSet"] = ProductionSub,
            ["ProductionZip"] = ProductionSub,
            ["VolumeSize"] = ProductionSub,
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
