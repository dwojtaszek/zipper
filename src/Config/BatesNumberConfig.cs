namespace Zipper.Config;

public record BatesNumberConfig
{
    public string Prefix { get; init; } = "DOC";
    public long Start { get; init; } = 1;
    public int Digits { get; init; } = 8;
    public long Increment { get; init; } = 1;
    public IReadOnlyList<string>? Prefixes { get; init; }
    public IReadOnlyList<long>? Starts { get; init; }

    /// <summary>
    /// Opt-in page-level suffixes (REQ-239): OPT page records and page images for every document
    /// carry <c>_NNN</c>, including one-page documents. Document-level Bates Numbers are unchanged.
    /// </summary>
    public bool PageSuffix { get; init; }
}

