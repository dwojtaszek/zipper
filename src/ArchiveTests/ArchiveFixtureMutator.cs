using System.Security.Cryptography;

namespace Zipper.ArchiveTests;

/// <summary>
/// The named mutations applied to valid control Archives to build malformed fixtures
/// (ticket #839). Target offsets always come from the control's captured layout, never
/// from a signature scan, and every coordinate is on the before-mutation basis. Malformed
/// output is produced by editing control bytes directly — it is never reopened with
/// <c>ZipArchiveMode.Update</c>, which may repair defects.
/// </summary>
internal static class ArchiveFixtureMutator
{
    internal static MutatedArchiveFixture Apply(ArchiveTestMutationKind kind, ArchiveFixtureArtifact control)
    {
        ArgumentNullException.ThrowIfNull(control);
        var before = control.ArchiveBytes;

        return kind switch
        {
            ArchiveTestMutationKind.CrcLocalMismatch
                or ArchiveTestMutationKind.CrcCentralMismatch
                or ArchiveTestMutationKind.CrcBothMismatch => FlipCrc(kind, control, before),
            ArchiveTestMutationKind.TruncatePayloadTail => Truncate(
                kind, "file-data", control.Layout.Entries[^1].DataOffset + 10, control.Layout.Entries.Count - 1,
                "Truncates the Archive inside the final entry's compressed payload. By construction this also removes every later structure (the remaining payload bytes, the central directory, and the EOCD); the fixture is a tail-truncation case, not an isolated payload corruption.",
                before),
            ArchiveTestMutationKind.TruncateCentralTail => Truncate(
                kind, "central-header", control.Layout.CentralDirectoryOffset + 8, 0,
                "Truncates the Archive inside a central directory entry, removing the rest of the central directory and the EOCD; no entry becomes fully described.",
                before),
            ArchiveTestMutationKind.TruncateEocd => Truncate(
                kind, "eocd", control.Layout.EocdOffset + 10, null,
                "Removes part of the EOCD record; the Archive keeps a complete entry and central directory but an incomplete EOCD.",
                before),
            ArchiveTestMutationKind.MissingEocd => Truncate(
                kind, "eocd", control.Layout.EocdOffset, null,
                "Removes the entire EOCD record; entry and central directory bytes remain, but the Archive has no end record at all.",
                before),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown Archive Test mutation kind."),
        };
    }

    private static MutatedArchiveFixture FlipCrc(ArchiveTestMutationKind kind, ArchiveFixtureArtifact control, byte[] before)
    {
        var entry = control.Layout.Entries[0];
        var localCrcOffset = entry.LocalHeaderOffset + 14;  // local header CRC-32 field
        var centralCrcOffset = entry.CentralDirectoryOffset + 16;  // central header CRC-32 field

        var after = (byte[])before.Clone();
        var mutations = new List<ArchiveTestMutation>();
        if (kind is ArchiveTestMutationKind.CrcLocalMismatch or ArchiveTestMutationKind.CrcBothMismatch)
        {
            after[localCrcOffset] ^= 0x01;
            mutations.Add(Record(
                ArchiveTestMutationKind.CrcLocalMismatch.ToCaseKey(), "local-header", localCrcOffset,
                "Flips the low bit of the first entry's CRC-32 in the local header only; the payload and all other structures are unchanged.",
                entry.Ordinal, before, after));
        }

        if (kind is ArchiveTestMutationKind.CrcCentralMismatch or ArchiveTestMutationKind.CrcBothMismatch)
        {
            after[centralCrcOffset] ^= 0x01;

            // In the crc-both-mismatch fixture both records describe the same control → final
            // transition (same Before/After hashes): parallel field views, not a chain.
            var centralExplanation = kind == ArchiveTestMutationKind.CrcCentralMismatch
                ? "Flips the low bit of the first entry's CRC-32 in the central header only; the payload and all other structures are unchanged."
                : "Flips the low bit of the first entry's CRC-32 in the central header, applied to the same before-mutation Archive as the local-header record (parallel field views of one transition, not a chain); the payload and all other structures are unchanged.";
            mutations.Add(Record(
                ArchiveTestMutationKind.CrcCentralMismatch.ToCaseKey(), "central-header", centralCrcOffset,
                centralExplanation, entry.Ordinal, before, after));
        }

        return Finalize(kind, before, after, mutations);
    }

    /// <summary>
    /// Records a single-byte field flip: the hex windows show the 4 bytes around the offset,
    /// with <paramref name="before"/>/<paramref name="after"/> hashes and sizes covering the
    /// whole Archive. The changed range itself is always offset..offset+1 (1 byte replaced).
    /// </summary>
    private static ArchiveTestMutation Record(
        string code, string structure, long offset, string explanation, int? ordinal, byte[] before, byte[] after)
    {
        return new ArchiveTestMutation(
            Code: code,
            Structure: structure,
            OffsetBasis: "before-mutation",
            Offset: offset,
            Explanation: explanation,
            Ordinal: ordinal,
            DeletedLength: 1,
            InsertedLength: 1,
            BeforeSize: before.Length,
            AfterSize: after.Length,
            BeforeSha256: Hash(before),
            AfterSha256: Hash(after),
            // Field windows may be short near the end of the Archive; truncations override
            // both hex fields below with the removed-tail semantics.
            BeforeHex: Hex(before, offset, Math.Min(4, before.Length - (int)offset)),
            AfterHex: Hex(after, offset, Math.Min(4, after.Length - (int)offset)),
            DeclaredValue: null);
    }

    private static MutatedArchiveFixture Truncate(
        ArchiveTestMutationKind kind, string structure, long offset, int? ordinal, string explanation, byte[] before)
    {
        var after = before[..(int)offset];

        // Unlike Record's field windows, the truncation BeforeHex is the removed content:
        // up to 32 of the deleted tail bytes, not an in-place window (which no longer exists).
        var mutation = Record(kind.ToCaseKey(), structure, offset, explanation, ordinal, before, after) with
        {
            DeletedLength = before.Length - offset,
            InsertedLength = 0,
            BeforeHex = Hex(before, offset, Math.Min(32, before.Length - (int)offset)),
            AfterHex = null,
        };

        return Finalize(kind, before, after, [mutation]);
    }

    private static MutatedArchiveFixture Finalize(
        ArchiveTestMutationKind kind, byte[] before, byte[] after, List<ArchiveTestMutation> mutations)
    {
        if (after.AsSpan().SequenceEqual(before))
        {
            throw new InvalidOperationException($"Archive Test mutation '{kind.ToCaseKey()}' produced no byte change.");
        }

        return new MutatedArchiveFixture(after, Hash(after), mutations);
    }

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static string Hex(byte[] bytes, long offset, int length) =>
        Convert.ToHexStringLower(bytes.AsSpan((int)offset, length));
}

/// <summary>
/// A control Archive after one named mutation: final bytes, final hash, and the mutation
/// records describing every changed range on the before-mutation coordinate system.
/// </summary>
internal sealed record MutatedArchiveFixture(
    byte[] ArchiveBytes,
    string ArchiveSha256,
    IReadOnlyList<ArchiveTestMutation> Mutations);

internal enum ArchiveTestMutationKind
{
    CrcLocalMismatch,
    CrcCentralMismatch,
    CrcBothMismatch,
    TruncatePayloadTail,
    TruncateCentralTail,
    TruncateEocd,
    MissingEocd,
}

internal static class ArchiveTestMutationKindExtensions
{
    /// <summary>The canonical Case Key of each named mutation: the malformed catalog entries
    /// and the JSON mutation codes both derive from this single mapping.</summary>
    internal static string ToCaseKey(this ArchiveTestMutationKind kind) => kind switch
    {
        ArchiveTestMutationKind.CrcLocalMismatch => "crc-local-mismatch",
        ArchiveTestMutationKind.CrcCentralMismatch => "crc-central-mismatch",
        ArchiveTestMutationKind.CrcBothMismatch => "crc-both-mismatch",
        ArchiveTestMutationKind.TruncatePayloadTail => "truncate-payload-tail",
        ArchiveTestMutationKind.TruncateCentralTail => "truncate-central-tail",
        ArchiveTestMutationKind.TruncateEocd => "truncate-eocd",
        ArchiveTestMutationKind.MissingEocd => "missing-eocd",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown Archive Test mutation kind."),
    };
}
