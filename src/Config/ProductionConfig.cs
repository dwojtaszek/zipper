namespace Zipper.Config;

public enum RollingBatesMode
{
    Continuous,
    Restart,
}

/// <summary>
/// How Source Record relative paths map into a Production Set tree when Source-Driven
/// Generation is combined with --production-set.
/// </summary>
public enum SourcePathMode
{
    /// <summary>Production placement wins: Native Files are Bates-named under NATIVES/VOL###/; the source path only feeds Load File Metadata.</summary>
    Bates,

    /// <summary>Source subdirectories are preserved under the Volume; the file itself is Bates-named (NATIVES/VOL###/&lt;source-dirs&gt;/&lt;bates&gt;.&lt;ext&gt;).</summary>
    PreserveSubdirs,

    /// <summary>Native Files are placed at ORIGINALS/&lt;source relative path&gt; with the original filename; TEXT/IMAGES stay Volume-rooted and Bates-named.</summary>
    Originals,
}

public record ProductionConfig
{
    public bool ProductionSet { get; init; }

    public bool ProductionZip { get; init; }

    public int VolumeSize { get; init; } = 5000;
    public bool SupplementalProduction { get; init; }
    public IReadOnlyList<string> PriorManifests { get; init; } = Array.Empty<string>();
    public string SupplementalGapPolicy { get; init; } = "reject";

    public string? ProductionId { get; init; }

    public int RollingCount { get; init; } = 1;

    public RollingBatesMode RollingBatesMode { get; init; } = RollingBatesMode.Continuous;

    public bool RedactedProduction { get; init; }

    public string WithheldNativePolicy { get; init; } = "keep-native";

    public SourcePathMode SourcePathMode { get; init; } = SourcePathMode.Bates;
}


