using System.Globalization;
using Zipper.Config;

namespace Zipper.Cli.Modules;

/// <summary>Owns the output flags (--type/--types/--count/--output-path/--folders/--with-text/--distribution/--encoding/--target-zip-size/--include-load-file): parse, validate, and build OutputConfig.</summary>
public sealed class OutputModule : CliModule
{
    private string? _fileType;
    private string? _fileTypes;
    private long? _count;
    private string? _outputPath;
    private int _folders = 1;
    private bool _withText;
    private string _encoding = "UTF-8";
    private bool _isEncodingExplicit;
    private string _distribution = "proportional";
    private string? _targetZipSize;
    private bool _includeLoadFile;

    public override IReadOnlyCollection<string> OwnedFlags { get; } = new[]
    {
        "--type", "--types", "--count", "--output-path", "--folders", "--with-text",
        "--distribution", "--encoding", "--target-zip-size", "--include-load-file",
    };

    public override bool TakesValue(string flag) => flag is not "--with-text" and not "--include-load-file";

    public override bool TryApply(string flag, string? value)
    {
        switch (flag)
        {
            case "--type":
                if (value is null)
                {
                    Console.Error.WriteLine("Error: --type requires a value.");
                    return false;
                }
                _fileType = value;
                return true;
            case "--types":
                if (value is null)
                {
                    Console.Error.WriteLine("Error: --types requires a value.");
                    return false;
                }
                _fileTypes = value;
                return true;
            case "--count":
                if (value is null)
                {
                    Console.Error.WriteLine("Error: --count requires a value.");
                    return false;
                }
                if (long.TryParse(value, CultureInfo.InvariantCulture, out var count))
                {
                    _count = count;
                    return true;
                }
                Console.Error.WriteLine($"Error: Invalid value for --count: '{value}'");
                return false;
            case "--output-path":
                if (value is null)
                {
                    Console.Error.WriteLine("Error: --output-path requires a value.");
                    return false;
                }
                _outputPath = value;
                return true;
            case "--folders":
                if (value is null)
                {
                    Console.Error.WriteLine("Error: --folders requires a value.");
                    return false;
                }
                if (int.TryParse(value, CultureInfo.InvariantCulture, out var folders))
                {
                    _folders = folders;
                    return true;
                }
                Console.Error.WriteLine($"Error: Invalid value for --folders: '{value}'");
                return false;
            case "--with-text":
                _withText = true;
                return true;
            case "--encoding":
                if (value is null)
                {
                    Console.Error.WriteLine("Error: --encoding requires a value.");
                    return false;
                }
                _encoding = value;
                _isEncodingExplicit = true;
                return true;
            case "--distribution":
                if (value is null)
                {
                    Console.Error.WriteLine("Error: --distribution requires a value.");
                    return false;
                }
                _distribution = value;
                return true;
            case "--target-zip-size":
                if (value is null)
                {
                    Console.Error.WriteLine("Error: --target-zip-size requires a value.");
                    return false;
                }
                _targetZipSize = value;
                return true;
            case "--include-load-file":
                _includeLoadFile = true;
                return true;
            default:
                Console.Error.WriteLine($"Error: Unknown argument or unconsumed value '{flag}'");
                return false;
        }
    }

    // Sibling-channel + test-facing raw state. CrossCuttingRules and other modules read these getters.
    public string? FileType => _fileType;
    public string? FileTypes => _fileTypes;
    public long? Count => _count;
    public string? TargetZipSize => _targetZipSize;
    public string Encoding => _encoding;
    public bool IsEncodingExplicit => _isEncodingExplicit;
    public string Distribution => _distribution;
    public bool IncludeLoadFile => _includeLoadFile;
    public int Folders => _folders;
    public bool WithText => _withText;

    internal bool TryBuild(IReadOnlyList<SourceInput.SourceRecord>? sourceRecords, out OutputConfig config)
    {
        // Check order: CrossCuttingRules (--type/--count gates) runs before this TryBuild,
        // which owns count bounds, path, known type, folders, target-zip-size,
        // --type x --types conflicts, ratio syntax, encoding, and distribution.
        if (_count is <= 0)
        {
            Console.Error.WriteLine("Error: --count must be a positive number.");
            config = default!;
            return false;
        }

        if (_count > int.MaxValue - 1)
        {
            Console.Error.WriteLine($"Error: --count must not exceed {int.MaxValue - 1}.");
            config = default!;
            return false;
        }

        if (string.IsNullOrWhiteSpace(_outputPath))
        {
            Console.Error.WriteLine("Error: Output path is required.");
            config = default!;
            return false;
        }

        var resolved = PathValidator.ResolveSecurePath(_outputPath, Directory.GetCurrentDirectory());
        if (resolved is null)
        {
            config = default!;
            return false;
        }

        if (!string.IsNullOrEmpty(_fileType) && !FileGeneratorFactory.IsKnownType(_fileType))
        {
            Console.Error.WriteLine($"Error: Unsupported file type '{_fileType}'. Supported types: pdf, jpg, tiff, eml, docx, xlsx.");
            config = default!;
            return false;
        }

        if (_folders < 1 || _folders > 100)
        {
            Console.Error.WriteLine("Error: Number of folders must be between 1 and 100.");
            config = default!;
            return false;
        }

        long? parsedSize = null;
        if (!string.IsNullOrEmpty(_targetZipSize))
        {
            parsedSize = ArgumentHelpers.ParseSize(_targetZipSize);
            if (parsedSize is null)
            {
                Console.Error.WriteLine("Error: Invalid format for --target-zip-size. Use KB, MB, GB, etc. (e.g., 500MB, 10GB).");
                config = default!;
                return false;
            }
            if (parsedSize.Value <= 0)
            {
                Console.Error.WriteLine("Error: --target-zip-size must be positive.");
                config = default!;
                return false;
            }
        }

        if (!string.IsNullOrEmpty(_fileType) && _fileTypes is not null)
        {
            Console.Error.WriteLine("Error: --type and --types cannot be used together. Use --types for a File Type mix.");
            config = default!;
            return false;
        }

        IReadOnlyList<FileTypeRatio>? parsedRatios = null;
        if (_fileTypes is not null && !FileTypeRatioParser.TryParse(_fileTypes, out parsedRatios, out var ratioError))
        {
            Console.Error.WriteLine($"Error: {ratioError}");
            config = default!;
            return false;
        }

        if (!string.IsNullOrEmpty(_encoding) && ArgumentHelpers.GetEncodingFromName(_encoding) is null)
        {
            Console.Error.WriteLine($"Error: Invalid encoding '{_encoding}'. Supported values are UTF-8, UTF-16, ANSI.");
            config = default!;
            return false;
        }

        if (!string.IsNullOrEmpty(_distribution) && ArgumentHelpers.GetDistributionFromName(_distribution) is null)
        {
            Console.Error.WriteLine($"Error: Invalid distribution '{_distribution}'. Supported values are proportional, gaussian, exponential.");
            config = default!;
            return false;
        }

        // Single-ratio mix collapses to a single File Type (parity with the old RequestBuilder assembly).
        var fileType = (_fileType ?? "pdf").ToLowerInvariant();
        IReadOnlyList<FileTypeRatio>? fileTypeRatios = null;
        FileTypePlan? fileTypePlan = null;
        if (parsedRatios is { Count: 1 })
        {
            fileType = parsedRatios[0].Type;
        }
        else if (parsedRatios is not null)
        {
            fileTypeRatios = parsedRatios;
            fileTypePlan = new FileTypePlan(parsedRatios, _count!.Value);
            fileType = parsedRatios[0].Type;
        }

        config = new OutputConfig
        {
            OutputPath = resolved.FullName,
            FileCount = sourceRecords is not null ? sourceRecords.Count : _count!.Value,
            FileType = sourceRecords is not null ? sourceRecords[0].FileType : fileType,
            FileTypeRatios = fileTypeRatios,
            FileTypePlan = fileTypePlan,
            SourceFileTypes = sourceRecords is not null
                ? sourceRecords.Select(r => r.FileType).Distinct(StringComparer.Ordinal).OrderBy(t => t, StringComparer.Ordinal).ToList()
                : null,
            Folders = _folders,
            Concurrency = PerformanceConstants.DefaultConcurrency,
            WithText = _withText,
            TargetZipSize = parsedSize,
            IncludeLoadFile = _includeLoadFile,
        };
        return true;
    }
}
