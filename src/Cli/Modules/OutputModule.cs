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
    private string? _compression;

    public override IReadOnlyCollection<string> OwnedFlags { get; } = new[]
    {
        "--type", "--types", "--count", "--output-path", "--folders", "--with-text",
        "--distribution", "--encoding", "--target-zip-size", "--include-load-file",
        "--compression",
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
            case "--compression":
                if (value is null)
                {
                    Console.Error.WriteLine("Error: --compression requires a value.");
                    return false;
                }
                _compression = value;
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

    /// <summary>The raw --output-path value as typed: the Archive Test workflow resolves
    /// it through PathValidator itself (REQ-208) instead of TryBuild, which requires a
    /// full OutputConfig.</summary>
    public string? RawOutputPath => _outputPath;

    public string? TargetZipSize => _targetZipSize;
    public string Encoding => _encoding;
    public bool IsEncodingExplicit => _isEncodingExplicit;
    public string Distribution => _distribution;
    public bool IncludeLoadFile => _includeLoadFile;
    public int Folders => _folders;
    public bool WithText => _withText;
    public string? Compression => _compression;

    internal bool TryBuild(
        IReadOnlyList<SourceInput.SourceRecord>? sourceRecords,
        out OutputConfig config,
        bool allowZeroCount = false)
    {
        // Check order: CrossCuttingRules (--type/--count gates) runs before this TryBuild,
        // which owns count bounds, path, known type, folders, target-zip-size,
        // --type x --types conflicts, ratio syntax, encoding, and distribution.
        if (!this.TryValidateCount(sourceRecords, allowZeroCount)
            || !this.TryResolveOutputPath(out var outputPath)
            || !this.TryValidateFileTypeAndFolders()
            || !this.TryResolveTargetZipSize(out var parsedSize)
            || !this.TryParseFileTypeRatios(out var parsedRatios)
            || !this.TryValidateEncodingAndDistribution()
            || !this.TryResolveCompressionMethod(out var compressionMethod))
        {
            config = default!;
            return false;
        }

        var (fileType, fileTypeRatios, fileTypePlan) = this.ResolveFileType(parsedRatios);

        config = new OutputConfig
        {
            OutputPath = outputPath,
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
            CompressionMethod = compressionMethod,
        };
        return true;
    }

    private bool TryValidateCount(
        IReadOnlyList<SourceInput.SourceRecord>? sourceRecords,
        bool allowZeroCount)
    {
        if (this._count is < 0 || this._count == 0 && (!allowZeroCount || sourceRecords is not null))
        {
            Console.Error.WriteLine("Error: --count must be a positive number.");
            return false;
        }

        if (this._count > int.MaxValue - 1)
        {
            Console.Error.WriteLine($"Error: --count must not exceed {int.MaxValue - 1}.");
            return false;
        }

        return true;
    }

    private bool TryResolveOutputPath(out string resolvedPath)
    {
        resolvedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(this._outputPath))
        {
            Console.Error.WriteLine("Error: Output path is required.");
            return false;
        }

        var resolved = PathValidator.ResolveSecurePath(this._outputPath, Directory.GetCurrentDirectory());
        if (resolved is null)
        {
            return false;
        }

        resolvedPath = resolved.FullName;
        return true;
    }

    private bool TryValidateFileTypeAndFolders()
    {
        if (!string.IsNullOrEmpty(this._fileType) && !FileGeneratorFactory.IsKnownType(this._fileType))
        {
            Console.Error.WriteLine($"Error: Unsupported file type '{this._fileType}'. Supported types: pdf, jpg, tiff, eml, docx, xlsx.");
            return false;
        }

        if (this._folders < 1 || this._folders > 100)
        {
            Console.Error.WriteLine("Error: Number of folders must be between 1 and 100.");
            return false;
        }

        return true;
    }

    private bool TryResolveTargetZipSize(out long? parsedSize)
    {
        parsedSize = null;
        if (string.IsNullOrEmpty(this._targetZipSize))
        {
            return true;
        }

        parsedSize = ArgumentHelpers.ParseSize(this._targetZipSize);
        if (parsedSize is null)
        {
            Console.Error.WriteLine("Error: Invalid format for --target-zip-size. Use KB, MB, GB, etc. (e.g., 500MB, 10GB).");
            return false;
        }

        if (parsedSize.Value <= 0)
        {
            Console.Error.WriteLine("Error: --target-zip-size must be positive.");
            return false;
        }

        return true;
    }

    private bool TryParseFileTypeRatios(out IReadOnlyList<FileTypeRatio>? parsedRatios)
    {
        parsedRatios = null;
        if (!string.IsNullOrEmpty(this._fileType) && this._fileTypes is not null)
        {
            Console.Error.WriteLine("Error: --type and --types cannot be used together. Use --types for a File Type mix.");
            return false;
        }

        if (this._fileTypes is not null
            && !FileTypeRatioParser.TryParse(this._fileTypes, out parsedRatios, out var ratioError))
        {
            Console.Error.WriteLine($"Error: {ratioError}");
            return false;
        }

        return true;
    }

    private bool TryValidateEncodingAndDistribution()
    {
        if (!string.IsNullOrEmpty(this._encoding) && ArgumentHelpers.GetEncodingFromName(this._encoding) is null)
        {
            Console.Error.WriteLine($"Error: Invalid encoding '{this._encoding}'. Supported values are UTF-8, UTF-16, ANSI.");
            return false;
        }

        if (!string.IsNullOrEmpty(this._distribution)
            && ArgumentHelpers.GetDistributionFromName(this._distribution) is null)
        {
            Console.Error.WriteLine($"Error: Invalid distribution '{this._distribution}'. Supported values are proportional, gaussian, exponential.");
            return false;
        }

        return true;
    }

    private (string FileType, IReadOnlyList<FileTypeRatio>? Ratios, FileTypePlan? Plan) ResolveFileType(
        IReadOnlyList<FileTypeRatio>? parsedRatios)
    {
        // Single-ratio mix collapses to a single File Type (parity with the old RequestBuilder assembly).
        var fileType = (this._fileType ?? "pdf").ToLowerInvariant();
        if (parsedRatios is { Count: 1 })
        {
            fileType = parsedRatios[0].Type;
            return (fileType, null, null);
        }

        if (parsedRatios is null)
        {
            return (fileType, null, null);
        }

        return (
            parsedRatios[0].Type,
            parsedRatios,
            this._count == 0 ? null : new FileTypePlan(parsedRatios, this._count!.Value));
    }

    private bool TryResolveCompressionMethod(out ZipCompressionMethod compressionMethod)
    {
        compressionMethod = ZipCompressionMethod.Deflate;
        if (string.IsNullOrEmpty(this._compression))
        {
            return true;
        }

        var parsedCompression = ArgumentHelpers.GetCompressionMethodFromName(this._compression);
        if (parsedCompression is null)
        {
            Console.Error.WriteLine($"Error: Invalid compression method '{this._compression}'. Supported values are store, deflate, deflate64, bzip2.");
            return false;
        }

        if (parsedCompression is ZipCompressionMethod.Deflate64 or ZipCompressionMethod.BZip2)
        {
            Console.Error.WriteLine($"Error: Compression method '{this._compression}' is not yet supported. Supported values: store, deflate.");
            return false;
        }

        compressionMethod = parsedCompression.Value;
        return true;
    }
}
