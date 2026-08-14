using System.Globalization;
using Zipper.Config;

namespace Zipper.Cli.Modules;

/// <summary>Owns --input-csv, --directory-template: parse, validate, and read Source Records.</summary>
public sealed class SourceInputModule : CliModule
{
    private string? _inputCsv;
    private string? _directoryTemplate;

    public override IReadOnlyCollection<string> OwnedFlags { get; } = new[] { "--input-csv", "--directory-template" };

    public override bool TryApply(string flag, string? value)
    {
        switch (flag)
        {
            case "--input-csv":
                if (value is null)
                {
                    Console.Error.WriteLine("Error: --input-csv requires a value.");
                    return false;
                }
                _inputCsv = value;
                return true;
            case "--directory-template":
                if (value is null)
                {
                    Console.Error.WriteLine("Error: --directory-template requires a value.");
                    return false;
                }
                _directoryTemplate = value;
                return true;
            default:
                Console.Error.WriteLine($"Error: Unknown argument or unconsumed value '{flag}'");
                return false;
        }
    }

    // Transitional (Phase 3): test-facing raw state so CliParserTests/RequestBuilderTests can
    // assert module ownership; ParsedArguments deletes its InputCsv/DirectoryTemplate fields and these move too.
    public bool HasSourceInput => !string.IsNullOrEmpty(_inputCsv) || !string.IsNullOrEmpty(_directoryTemplate);
    public string? InputCsv => _inputCsv;
    public string? DirectoryTemplate => _directoryTemplate;

    internal bool TryBuild(long? declaredCount, bool productionSet, BatesNumberConfig? bates, out IReadOnlyList<SourceInput.SourceRecord>? sourceRecords)
    {
        if (!HasSourceInput)
        {
            sourceRecords = null;
            return true;
        }

        var hasCsv = !string.IsNullOrEmpty(_inputCsv);
        var hasDirectory = !string.IsNullOrEmpty(_directoryTemplate);

        if (hasCsv && hasDirectory)
        {
            Console.Error.WriteLine("Error: --input-csv and --directory-template cannot be used together.");
            sourceRecords = null;
            return false;
        }

        var sourcePath = hasCsv ? _inputCsv! : _directoryTemplate!;
        if (!PathValidator.IsPathSafe(sourcePath, Directory.GetCurrentDirectory()))
        {
            Console.Error.WriteLine($"Error: Path traversal detected in source input path '{sourcePath}'. Source input must reside within working directory.");
            sourceRecords = null;
            return false;
        }

        var exists = hasCsv ? File.Exists(sourcePath) : Directory.Exists(sourcePath);
        if (!exists)
        {
            Console.Error.WriteLine(hasCsv
                ? $"Error: Source CSV '{sourcePath}' does not exist."
                : $"Error: Directory template '{sourcePath}' does not exist.");
            sourceRecords = null;
            return false;
        }

        IReadOnlyList<SourceInput.SourceRecord> rows;
        var readOk = hasCsv
            ? SourceInput.SourceCsvReader.TryRead(_inputCsv!, out rows, out var readError)
            : SourceInput.DirectoryTemplateReader.TryRead(_directoryTemplate!, out rows, out readError);
        if (!readOk)
        {
            Console.Error.WriteLine($"Error: {readError}");
            sourceRecords = null;
            return false;
        }

        if (declaredCount.HasValue && declaredCount.Value != rows.Count)
        {
            Console.Error.WriteLine($"Error: --count ({declaredCount.Value}) does not match the Source Record count ({rows.Count}). Align --count with the source input or omit it.");
            sourceRecords = null;
            return false;
        }

        if (!productionSet && rows.Any(r => r.BatesNumber is not null) && bates is null)
        {
            Console.Error.WriteLine("Error: the source 'BatesNumber' column requires --bates-prefix so the Bates column is emitted.");
            sourceRecords = null;
            return false;
        }

        if (productionSet && rows.Any(r => r.BatesNumber is not null))
        {
            Console.Error.WriteLine("Error: the source 'BatesNumber' column cannot be used with --production-set. Production Set Bates Numbers come from the configured Bates sequence so Volume ranges in the Production Manifest stay exact.");
            sourceRecords = null;
            return false;
        }

        // Explicit identity overrides must stay clear of sequence-generated values: an
        // override equal to a generated fallback produces duplicate Load File identities
        // and OPT image paths.
        var identityCollision = FindGeneratedIdentityCollision(rows, bates);
        if (identityCollision is not null)
        {
            Console.Error.WriteLine($"Error: {identityCollision}");
            sourceRecords = null;
            return false;
        }

        sourceRecords = rows;
        return true;
    }

    // An explicit override collides with a generated identity only when it equals a value the
    // run would actually generate, compared case-insensitively (consistent with duplicate
    // detection and Windows path semantics): DOC{index:D8} for Control Numbers and the
    // configured Bates sequence values for Bates Numbers.
    internal static string? FindGeneratedIdentityCollision(IReadOnlyList<SourceInput.SourceRecord> rows, BatesNumberConfig? bates)
    {
        foreach (var row in rows)
        {
            var control = row.ControlNumber;
            if (control is not null
                && control.Length == 11
                && control.StartsWith("DOC", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(control.AsSpan(3), NumberStyles.None, CultureInfo.InvariantCulture, out var controlIndex)
                && controlIndex >= 1
                && controlIndex <= rows.Count
                && string.Equals(control, "DOC" + controlIndex.ToString("D8", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase))
            {
                return $"source ControlNumber '{control}' collides with the generated Control Number for row {controlIndex}. Choose an override outside the generated identity space.";
            }
        }

        if (bates is not null)
        {
            var prefix = bates.Prefix;
            var start = bates.Start;
            var digits = bates.Digits;
            foreach (var row in rows)
            {
                var batesValue = row.BatesNumber;
                if (batesValue is null || !batesValue.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var numericPart = batesValue.AsSpan(prefix.Length);
                if (!long.TryParse(numericPart, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
                {
                    continue;
                }

                var sequenceIndex = number - start;
                if (sequenceIndex < 0 || sequenceIndex >= rows.Count)
                {
                    continue;
                }

                var generated = prefix + number.ToString($"D{digits}", CultureInfo.InvariantCulture);
                if (string.Equals(batesValue, generated, StringComparison.OrdinalIgnoreCase))
                {
                    return $"source BatesNumber '{batesValue}' collides with the generated Bates sequence value for row {sequenceIndex + 1}. Choose an override outside the generated identity space.";
                }
            }
        }

        return null;
    }
}
