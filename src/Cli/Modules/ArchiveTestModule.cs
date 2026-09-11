namespace Zipper.Cli.Modules;

/// <summary>
/// Owns the Archive Test workflow flags (--archive-test-suite / --archive-test-cases):
/// parse and raw state only. The typed request is built by
/// <see cref="Zipper.ArchiveTests.ArchiveTestCliWorkflow"/> from raw module values —
/// never via OutputModule.TryBuild or MetadataModule.TryBuild, which impose Native
/// File requirements (ADR-0008: no FileGenerationRequest fields, no fourth
/// IGenerationMode).
/// </summary>
public sealed class ArchiveTestModule : CliModule
{
    private string? _suite;
    private string? _caseList;

    public override IReadOnlyCollection<string> OwnedFlags { get; } = ["--archive-test-suite", "--archive-test-cases"];

    public override bool TryApply(string flag, string? value)
    {
        switch (flag)
        {
            case "--archive-test-suite":
                if (value is null)
                {
                    Console.Error.WriteLine("Error: --archive-test-suite requires a value.");
                    return false;
                }
                _suite = value;
                return true;
            case "--archive-test-cases":
                if (value is null)
                {
                    Console.Error.WriteLine("Error: --archive-test-cases requires a value.");
                    return false;
                }
                _caseList = value;
                return true;
            default:
                Console.Error.WriteLine($"Error: Unknown argument or unconsumed value '{flag}'");
                return false;
        }
    }

    /// <summary>The raw suite value as typed; suite names are case-insensitive at use.</summary>
    public string? RawSuite => _suite;

    /// <summary>The raw comma-separated Case Key list as typed.</summary>
    public string? RawCases => _caseList;

    /// <summary>True when either Archive Test flag was supplied (a case list without a
    /// suite is a rejected combination, so it still enters the workflow).</summary>
    public bool IsRequested => _suite is not null || _caseList is not null;
}
