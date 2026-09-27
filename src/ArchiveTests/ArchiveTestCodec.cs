namespace Zipper.ArchiveTests;

/// <summary>Maps the recipe's payload codec to ZIP header fields and mutation labels.</summary>
internal static class ArchiveTestCodec
{
    internal const ushort Stored = 0;
    internal const ushort Deflate = 8;
    internal const ushort Deflate64 = 9;
    internal const ushort Bzip2 = 12;
    internal const ushort Ppmd = 98;

    internal static ushort WireCode(string payloadCodec) => payloadCodec switch
    {
        "stored" => Stored,
        "deflate" => Deflate,
        "deflate64" => Deflate64,
        "bzip2" => Bzip2,
        _ => throw new InvalidOperationException($"Unknown Archive Test recipe payload codec \"{payloadCodec}\"."),
    };

    internal static ushort VersionNeeded(ushort wireCode) => wireCode switch
    {
        Stored => 10,
        Deflate => 20,
        Deflate64 => 21,
        Bzip2 => 46,
        _ => throw new InvalidOperationException($"Unknown Archive Test method code {wireCode}."),
    };

    internal static string DisplayName(ushort wireCode) => wireCode switch
    {
        Stored => "Stored",
        Deflate => "Deflate",
        Deflate64 => "Deflate64",
        Bzip2 => "BZip2",
        Ppmd => "PPMd",
        _ => throw new InvalidOperationException($"Unknown Archive Test method code {wireCode}."),
    };
}
