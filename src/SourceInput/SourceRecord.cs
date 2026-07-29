namespace Zipper.SourceInput;

/// <summary>
/// One row of Source-Driven Generation input: the record identity, output-relative path,
/// and File Type that a generated Native File and its Load File record must reflect.
/// Native File bytes are never taken from the source; placeholders are generated internally.
/// </summary>
internal sealed record SourceRecord
{
    /// <summary>Gets the output-relative path, normalized to '/' separators, no leading or trailing separator.</summary>
    public required string RelativePath { get; init; }

    /// <summary>Gets the File Type (lowercased, no leading dot).</summary>
    public required string FileType { get; init; }

    /// <summary>Gets the Control Number override, or null to use the default DOC{index} identity.</summary>
    public string? ControlNumber { get; init; }

    /// <summary>Gets the Bates Number override, or null to use the configured Bates sequence.</summary>
    public string? BatesNumber { get; init; }

    /// <summary>Gets extra source columns to map into Load File Metadata through a Column Profile.</summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }

    /// <summary>
    /// Converts this row into a <see cref="FileWorkItem"/> for the generation pipeline.
    /// The folder portion of the relative path becomes <see cref="FileWorkItem.FolderName"/>
    /// (empty for root-level entries) so sibling outputs (extracted text, Attachments) stay
    /// next to the Native File.
    /// </summary>
    internal FileWorkItem ToWorkItem(long index)
    {
        var slash = this.RelativePath.LastIndexOf('/');
        return new FileWorkItem
        {
            Index = index,
            FolderNumber = 1,
            FolderName = slash >= 0 ? this.RelativePath[..slash] : string.Empty,
            FileName = slash >= 0 ? this.RelativePath[(slash + 1)..] : this.RelativePath,
            FilePathInZip = this.RelativePath,
            FileType = this.FileType,
            ControlNumberOverride = this.ControlNumber,
            BatesNumberOverride = this.BatesNumber,
            SourceMetadata = this.Metadata,
        };
    }
}
