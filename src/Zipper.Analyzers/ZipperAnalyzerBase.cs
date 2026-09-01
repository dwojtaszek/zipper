using Microsoft.CodeAnalysis.Diagnostics;

namespace Zipper.Analyzers;

public abstract class ZipperAnalyzerBase : DiagnosticAnalyzer
{
    public override void Initialize(AnalysisContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        RegisterActions(context);
    }

    protected abstract void RegisterActions(AnalysisContext context);
}
