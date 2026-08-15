using System.Globalization;
using Zipper.Config;
using Zipper.Profiles;

namespace Zipper.Cli.Modules;

/// <summary>Owns the metadata flags (profile, seed, dates, families, collection metadata, attachment rate): parse, validate, and build MetadataConfig.</summary>
public sealed class MetadataModule : CliModule
{
    private bool _withMetadata;
    private bool _withCollectionMetadata;
    private bool _withFamilies;
    private string? _columnProfile;
    private int? _seed;
    private string? _dateFormat;
    private int? _emptyPercentage;
    private int? _custodianCount;
    private int _attachmentRate;

    public override IReadOnlyCollection<string> OwnedFlags { get; } = new[]
    {
        "--with-metadata", "--with-collection-metadata", "--with-families",
        "--column-profile", "--seed", "--date-format", "--empty-percentage", "--custodian-count",
        "--attachment-rate",
    };

    public override bool TakesValue(string flag) => flag is not "--with-metadata" and not "--with-collection-metadata" and not "--with-families";

    public override bool TryApply(string flag, string? value)
    {
        switch (flag)
        {
            case "--with-metadata": _withMetadata = true; return true;
            case "--with-collection-metadata": _withCollectionMetadata = true; return true;
            case "--with-families": _withFamilies = true; return true;
            case "--column-profile": _columnProfile = value; return true;
            case "--date-format": _dateFormat = value; return true;
            case "--seed":
                if (value is null) { Console.Error.WriteLine("Error: --seed requires a value."); return false; }
                if (int.TryParse(value, CultureInfo.InvariantCulture, out var seed)) { _seed = seed; return true; }
                Console.Error.WriteLine($"Error: Invalid value for --seed: '{value}'");
                return false;
            case "--empty-percentage":
                if (value is null) { Console.Error.WriteLine("Error: --empty-percentage requires a value."); return false; }
                if (int.TryParse(value, CultureInfo.InvariantCulture, out var emptyPct)) { _emptyPercentage = emptyPct; return true; }
                Console.Error.WriteLine($"Error: Invalid value for --empty-percentage: '{value}'");
                return false;
            case "--custodian-count":
                if (value is null) { Console.Error.WriteLine("Error: --custodian-count requires a value."); return false; }
                if (int.TryParse(value, CultureInfo.InvariantCulture, out var custCount)) { _custodianCount = custCount; return true; }
                Console.Error.WriteLine($"Error: Invalid value for --custodian-count: '{value}'");
                return false;
            case "--attachment-rate":
                if (value is null) { Console.Error.WriteLine("Error: --attachment-rate requires a value."); return false; }
                if (int.TryParse(value, CultureInfo.InvariantCulture, out var attachmentRate)) { _attachmentRate = attachmentRate; return true; }
                Console.Error.WriteLine($"Error: Invalid value for --attachment-rate: '{value}'");
                return false;
            default:
                Console.Error.WriteLine($"Error: Unknown argument or unconsumed value '{flag}'");
                return false;
        }
    }

    public bool HasColumnProfile => !string.IsNullOrEmpty(_columnProfile);

    // Sibling-channel + test-facing raw state. CrossCuttingRules and other modules read these getters.
    public bool WithMetadata => _withMetadata;
    public bool WithCollectionMetadata => _withCollectionMetadata;
    public bool WithFamilies => _withFamilies;
    public int AttachmentRate => _attachmentRate;
    public string? ColumnProfile => _columnProfile;
    public int? Seed => _seed;
    public string? DateFormat => _dateFormat;
    public int? EmptyPercentage => _emptyPercentage;
    public int? CustodianCount => _custodianCount;

    public bool TryBuild(bool includesEml, bool hasSourceInput, out MetadataConfig config)
    {
        // The eml-participation and source-input flags are owned by OutputModule and
        // SourceInputModule respectively, so they are passed in as parameters.
        if (_attachmentRate < 0 || _attachmentRate > 100)
        {
            Console.Error.WriteLine("Error: Attachment rate must be between 0 and 100.");
            config = default!;
            return false;
        }

        if (_emptyPercentage.HasValue && (_emptyPercentage.Value < 0 || _emptyPercentage.Value > 100))
        {
            Console.Error.WriteLine("Error: Empty percentage must be between 0 and 100.");
            config = default!;
            return false;
        }

        if (_custodianCount.HasValue && (_custodianCount.Value < 1 || _custodianCount.Value > 1000))
        {
            Console.Error.WriteLine("Error: Custodian count must be between 1 and 1000.");
            config = default!;
            return false;
        }

        // Source-driven rows are not read yet at validation time, so the eml-participation
        // warning is skipped for source input and applied per record during generation.
        if (_withFamilies && !hasSourceInput && (!includesEml || _attachmentRate <= 0))
        {
            Console.Error.WriteLine("Warning: --with-families is only meaningful when --type eml (or eml participates in --types) and --attachment-rate > 0 are specified.");
        }

        if (HasColumnProfile && !ColumnProfileLoader.IsBuiltInProfile(_columnProfile!))
        {
            if (!PathValidator.IsPathSafe(_columnProfile!, Directory.GetCurrentDirectory()))
            {
                Console.Error.WriteLine($"Error: Path traversal detected in column profile path '{_columnProfile}'. Profile file must reside within working directory.");
                config = default!;
                return false;
            }

            if (!File.Exists(_columnProfile))
            {
                Console.Error.WriteLine($"Error: Column profile '{_columnProfile}' is not a valid built-in profile or file path.\n       Built-in profiles: {string.Join(", ", BuiltInProfiles.ProfileNames)}");
                config = default!;
                return false;
            }
        }

        var withMetadata = _withMetadata;
        if (withMetadata && HasColumnProfile)
        {
            Console.Error.WriteLine("Warning: --column-profile takes precedence over --with-metadata. --with-metadata will be ignored.");
            withMetadata = false;
        }

        ColumnProfile? profile = null;
        if (HasColumnProfile)
        {
            try
            {
                profile = ColumnProfileLoader.Load(_columnProfile!);
            }
            catch (InvalidOperationException ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                config = default!;
                return false;
            }

            if (profile is null)
            {
                Console.Error.WriteLine($"Warning: Failed to load column profile '{_columnProfile}'.");
            }
            else if (_withCollectionMetadata)
            {
                profile = BuiltInProfiles.MergeWithCollectionMetadata(profile);
            }
        }

        config = new MetadataConfig
        {
            WithMetadata = withMetadata,
            ColumnProfile = profile,
            Seed = _seed,
            DateFormatOverride = _dateFormat,
            EmptyPercentageOverride = _emptyPercentage,
            CustodianCountOverride = _custodianCount,
            WithFamilies = _withFamilies,
            WithCollectionMetadata = _withCollectionMetadata,
        };
        return true;
    }
}
