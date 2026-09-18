using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

using ICSharpCode.SharpZipLib.BZip2;

using Xunit;
using Zipper.ArchiveTests;

namespace Zipper.Tests;

public class ArchiveFixtureBuilderTests
{
    private const string FrozenEmptyArchiveHex = "504b0506000000000000000000000000000000000000";
    private const string FrozenEmptyArchiveSha256 = "8739c76e681f900923b900c9df0ef75cf421d39cabb54650c4b9ad19b6a76d85";

    private static byte[] ReadEntryContent(byte[] archiveBytes, string entryName)
    {
        using var archive = new ZipArchive(new MemoryStream(archiveBytes), ZipArchiveMode.Read);
        var entry = archive.GetEntry(entryName) ?? throw new InvalidOperationException($"entry '{entryName}' not found");
        using var stream = entry.Open();
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    [Fact]
    public void BuildControl_ValidEmpty_ProducesEocdOnlyArchive()
    {
        var artifact = ArchiveFixtureBuilder.BuildControl("valid-empty", 42, CancellationToken.None);

        Assert.Equal(Convert.FromHexString(FrozenEmptyArchiveHex), artifact.ArchiveBytes);
        Assert.Equal(FrozenEmptyArchiveSha256, artifact.ArchiveSha256);
        Assert.Empty(artifact.Entries);
        Assert.Equal(0, artifact.Layout.EntryCount);
        Assert.Equal(0, artifact.Layout.EocdOffset);
        Assert.Equal(22, artifact.Layout.TotalBytes);
    }

    [Fact]
    public void BuildControl_ValidZeroEntry_ProducesEmptyEntryList()
    {
        var artifact = ArchiveFixtureBuilder.BuildControl("valid-zero-entry", 42, CancellationToken.None);

        // A zero-entry ZipArchive is also the EOCD-only Archive; the distinct Case Key
        // (not the bytes) keeps the Fixture IDs distinct.
        Assert.Equal(22, artifact.ArchiveBytes.Length);
        Assert.Equal(0, artifact.Layout.EntryCount);
        Assert.Empty(artifact.Entries);
    }

    [Fact]
    public void BuildControl_ValidStored_EmitsStoredMethodAndKnownContent()
    {
        var artifact = ArchiveFixtureBuilder.BuildControl("valid-stored", 42, CancellationToken.None);

        Assert.Equal(2, artifact.Layout.EntryCount);
        var first = artifact.Layout.Entries[0];
        Assert.Equal((ushort)0, first.Method);
        Assert.Equal("a.txt", first.Name);

        // Independently read the payload back through .NET ZipArchive and compare with the recipe expectation.
        Assert.Equal(artifact.Entries[0].Content, ReadEntryContent(artifact.ArchiveBytes, "a.txt"));
        Assert.Equal(artifact.Entries[1].Content, ReadEntryContent(artifact.ArchiveBytes, "b.bin"));
        Assert.Equal(100, artifact.Entries[0].Content.Length);
    }

    [Fact]
    public void BuildControl_ValidDeflate_EmitsDeflateMethodAndKnownContent()
    {
        var artifact = ArchiveFixtureBuilder.BuildControl("valid-deflate", 42, CancellationToken.None);

        var entry = artifact.Layout.Entries[0];
        Assert.Equal((ushort)8, entry.Method);
        Assert.Equal("doc.txt", entry.Name);
        Assert.Equal(artifact.Entries[0].Content, ReadEntryContent(artifact.ArchiveBytes, "doc.txt"));
    }

    [Fact]
    public void BuildControl_ValidDirectories_KeepsDirectoryAndFileEntries()
    {
        var artifact = ArchiveFixtureBuilder.BuildControl("valid-directories", 42, CancellationToken.None);

        Assert.Equal(3, artifact.Layout.EntryCount);
        Assert.Equal("dir/", artifact.Layout.Entries[0].Name);
        Assert.True(artifact.Entries[0].IsDirectory);
        Assert.Empty(ReadEntryContent(artifact.ArchiveBytes, "dir/"));
        Assert.Equal("dir/file.txt", artifact.Layout.Entries[1].Name);
        Assert.Equal(artifact.Entries[1].Content, ReadEntryContent(artifact.ArchiveBytes, "dir/file.txt"));
        Assert.Equal("top.txt", artifact.Layout.Entries[2].Name);
    }

    [Fact]
    public void BuildControl_SameSeed_ProducesIdenticalBytes()
    {
        var first = ArchiveFixtureBuilder.BuildControl("valid-deflate", 42, CancellationToken.None);
        var second = ArchiveFixtureBuilder.BuildControl("valid-deflate", 42, CancellationToken.None);

        Assert.Equal(first.ArchiveBytes, second.ArchiveBytes);
        Assert.Equal(first.ArchiveSha256, second.ArchiveSha256);
    }

    [Fact]
    public void BuildControl_DifferentSeed_ProducesDifferentBytes()
    {
        var first = ArchiveFixtureBuilder.BuildControl("valid-deflate", 42, CancellationToken.None);
        var second = ArchiveFixtureBuilder.BuildControl("valid-deflate", 43, CancellationToken.None);

        Assert.NotEqual(first.ArchiveBytes, second.ArchiveBytes);
        Assert.NotEqual(first.Entries[0].Content, second.Entries[0].Content);
    }

    [Fact]
    public void Build_UnknownRecipeMethod_ThrowsDescriptively()
    {
        var definition = new ArchiveTestCaseDefinition(
            "unknown-method", 1, 1, "valid", ["smoke"],
            new ArchiveTestRecipe([ArchiveTestRecipeEntry.File("x.bin", 10, "shrink-wrap")]));

        var error = Assert.Throws<InvalidOperationException>(
            () => ArchiveFixtureBuilder.Build(definition, 42, CancellationToken.None));

        Assert.Contains("shrink-wrap", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("valid-zip64-descriptor-signature", true)]
    [InlineData("valid-zip64-descriptor-no-signature", false)]
    public void Build_HandBuiltZip64Descriptor_EmitsDescriptorFormsWithSentinels(string caseKey, bool withSignature)
    {
        var definition = ArchiveTestCatalog.GetCase(caseKey);
        var first = ArchiveFixtureBuilder.Build(definition, 42, CancellationToken.None);
        var second = ArchiveFixtureBuilder.Build(definition, 42, CancellationToken.None);
        Assert.Equal(first.ArchiveBytes, second.ArchiveBytes);

        var entry = Assert.Single(first.Layout.Entries);
        Assert.Equal((ushort)8, entry.Method);
        Assert.True(entry.HasDataDescriptor);
        Assert.Equal(first.Entries[0].Content, ReadEntryContent(first.ArchiveBytes, "zd.txt"));

        // Zip64 sentinels in both headers; the descriptor carries the truth.
        var descriptorOffset = (int)entry.DataDescriptorOffset;
        if (withSignature)
        {
            Assert.Equal(0x08074b50u, BinaryPrimitives.ReadUInt32LittleEndian(first.ArchiveBytes.AsSpan(descriptorOffset, 4)));
        }

        var sizesAt = descriptorOffset + (withSignature ? 4 : 0);
        Assert.Equal(ArchiveFixtureBuilder.Crc32(first.Entries[0].Content), BinaryPrimitives.ReadUInt32LittleEndian(first.ArchiveBytes.AsSpan(sizesAt, 4)));
        Assert.Equal(0xFFFFFFFFu, BinaryPrimitives.ReadUInt32LittleEndian(first.ArchiveBytes.AsSpan((int)entry.LocalHeaderOffset + 18, 4)));
        Assert.Equal(0xFFFFFFFFu, BinaryPrimitives.ReadUInt32LittleEndian(first.ArchiveBytes.AsSpan((int)entry.CentralDirectoryOffset + 20, 4)));
    }

    [Fact]
    public void Build_Zip64DescriptorForms_DifferOnlyBySignature()
    {
        // The two forms differ only by the 4-byte descriptor signature; each
        // control carries its own case-keyed content of the same length.
        var sig = ArchiveFixtureBuilder.Build(ArchiveTestCatalog.GetCase("valid-zip64-descriptor-signature"), 42, CancellationToken.None);
        var nosig = ArchiveFixtureBuilder.Build(ArchiveTestCatalog.GetCase("valid-zip64-descriptor-no-signature"), 42, CancellationToken.None);
        Assert.Equal(sig.ArchiveBytes.Length, nosig.ArchiveBytes.Length + 4);
        Assert.Equal(sig.Entries[0].Content.Length, nosig.Entries[0].Content.Length);
    }

    [Fact]
    public void Build_UnicodePathClean_EmitsMatchingUnicodeName()
    {
        var definition = ArchiveTestCatalog.GetCase("valid-unicode-path");
        var first = ArchiveFixtureBuilder.Build(definition, 42, CancellationToken.None);
        var second = ArchiveFixtureBuilder.Build(definition, 42, CancellationToken.None);
        Assert.Equal(first.ArchiveBytes, second.ArchiveBytes);

        var entry = Assert.Single(first.Layout.Entries);
        Assert.Equal("safe.txt", entry.Name);
        Assert.NotNull(entry.UnicodePathNameHex);
        Assert.Equal(entry.NameHex, entry.UnicodePathNameHex);
        Assert.Equal(first.Entries[0].Content, ReadEntryContent(first.ArchiveBytes, "safe.txt"));
    }

    [Fact]
    public void Build_HandBuiltBzip2_EmitsMethodTwelveWithDecodablePayload()
    {
        var definition = new ArchiveTestCaseDefinition(
            "coded-bzip2", 1, 1, "valid", ["smoke"],
            new ArchiveTestRecipe([ArchiveTestRecipeEntry.File("doc.bin", 400, "bzip2")]));
        var artifact = ArchiveFixtureBuilder.Build(definition, 42, CancellationToken.None);

        var entry = Assert.Single(artifact.Layout.Entries);
        Assert.Equal((ushort)12, entry.Method);
        Assert.False(entry.HasDataDescriptor);
        Assert.Equal(400u, entry.UncompressedSize);
        Assert.Equal(artifact.Entries[0].Content.Length, (int)entry.UncompressedSize);
        Assert.Equal(ArchiveFixtureBuilder.Crc32(artifact.Entries[0].Content), entry.Crc32);
        Assert.Equal(artifact.Layout.CentralDirectoryOffset, entry.DataOffset + entry.CompressedSize);
        Assert.Equal((ushort)46, BinaryPrimitives.ReadUInt16LittleEndian(artifact.ArchiveBytes.AsSpan((int)entry.LocalHeaderOffset + 4)));
        Assert.Equal((ushort)46, BinaryPrimitives.ReadUInt16LittleEndian(artifact.ArchiveBytes.AsSpan((int)entry.CentralDirectoryOffset + 6)));

        using var source = new MemoryStream(artifact.ArchiveBytes, (int)entry.DataOffset, (int)entry.CompressedSize, writable: false);
        using var bz2 = new BZip2InputStream(source);
        using var inflated = new MemoryStream();
        bz2.CopyTo(inflated);
        Assert.Equal(artifact.Entries[0].Content, inflated.ToArray());
    }

    [Fact]
    public void Build_HandBuiltDeflate64_EmitsMethodNineAsDeflateSubset()
    {
        var definition = new ArchiveTestCaseDefinition(
            "coded-deflate64", 1, 1, "valid", ["smoke"],
            new ArchiveTestRecipe([ArchiveTestRecipeEntry.File("doc.txt", 400, "deflate64")]));
        var artifact = ArchiveFixtureBuilder.Build(definition, 42, CancellationToken.None);

        var entry = Assert.Single(artifact.Layout.Entries);
        Assert.Equal((ushort)9, entry.Method);
        Assert.False(entry.HasDataDescriptor);
        Assert.Equal(400u, entry.UncompressedSize);
        Assert.Equal(ArchiveFixtureBuilder.Crc32(artifact.Entries[0].Content), entry.Crc32);
        Assert.Equal(artifact.Layout.CentralDirectoryOffset, entry.DataOffset + entry.CompressedSize);
        Assert.Equal((ushort)21, BinaryPrimitives.ReadUInt16LittleEndian(artifact.ArchiveBytes.AsSpan((int)entry.LocalHeaderOffset + 4)));
        Assert.Equal((ushort)21, BinaryPrimitives.ReadUInt16LittleEndian(artifact.ArchiveBytes.AsSpan((int)entry.CentralDirectoryOffset + 6)));

        // Deflate-subset stream: any Deflate decoder — hence any Deflate64 one —
        // restores the content.
        Assert.Equal(artifact.Entries[0].Content, ReadEntryContent(artifact.ArchiveBytes, "doc.txt"));
    }

    [Fact]
    public void Build_HandBuiltMixedMethods_KeepsPerEntryCodecs()
    {
        var definition = new ArchiveTestCaseDefinition(
            "coded-mixed", 1, 1, "valid", ["smoke"],
            new ArchiveTestRecipe(
            [
                ArchiveTestRecipeEntry.File("a.txt", 100, "stored"),
                ArchiveTestRecipeEntry.File("b.bin", 400, "deflate"),
                ArchiveTestRecipeEntry.File("c.bz2", 400, "bzip2"),
            ]));
        var artifact = ArchiveFixtureBuilder.Build(definition, 42, CancellationToken.None);

        Assert.Equal(3, artifact.Layout.EntryCount);
        Assert.Equal([(ushort)0, (ushort)8, (ushort)12], artifact.Layout.Entries.Select(e => e.Method).ToList());
        Assert.All(artifact.Layout.Entries, e => Assert.False(e.HasDataDescriptor));
        Assert.Equal(
            [(ushort)10, (ushort)20, (ushort)46],
            artifact.Layout.Entries.Select(e => BinaryPrimitives.ReadUInt16LittleEndian(artifact.ArchiveBytes.AsSpan((int)e.LocalHeaderOffset + 4))).ToList());

        Assert.Equal(artifact.Entries[0].Content, ReadEntryContent(artifact.ArchiveBytes, "a.txt"));
        Assert.Equal(artifact.Entries[1].Content, ReadEntryContent(artifact.ArchiveBytes, "b.bin"));

        var bz2Entry = artifact.Layout.Entries[2];
        using var source = new MemoryStream(artifact.ArchiveBytes, (int)bz2Entry.DataOffset, (int)bz2Entry.CompressedSize, writable: false);
        using var bz2 = new BZip2InputStream(source);
        using var inflated = new MemoryStream();
        bz2.CopyTo(inflated);
        Assert.Equal(artifact.Entries[2].Content, inflated.ToArray());
    }

    [Fact]
    public void Build_HandBuiltCoded_SameSeed_ProducesIdenticalBytes()
    {
        var definition = new ArchiveTestCaseDefinition(
            "coded-determinism", 1, 1, "valid", ["smoke"],
            new ArchiveTestRecipe([ArchiveTestRecipeEntry.File("doc.bin", 400, "bzip2")]));
        var first = ArchiveFixtureBuilder.Build(definition, 42, CancellationToken.None);
        var second = ArchiveFixtureBuilder.Build(definition, 42, CancellationToken.None);

        Assert.Equal(first.ArchiveBytes, second.ArchiveBytes);
        Assert.Equal(first.ArchiveSha256, second.ArchiveSha256);
    }

    [Fact]
    public void BuildControl_ValidStored_ProducesFrozenBytes()
    {
        // The stored control never enters the hand-built path, so its bytes are a
        // golden: any routing change that moves it breaks this hash. Deflate has no
        // frozen golden (codec bytes vary across runtimes, ticket #846).
        var artifact = ArchiveFixtureBuilder.BuildControl("valid-stored", 42, CancellationToken.None);

        Assert.Equal("275c4c4da2722c54625b2175a78898eb27c20e28878ac3948e9854c7fa3dc9a5", artifact.ArchiveSha256);
    }

    [Fact]
    public void BuildControl_UnknownCaseKey_ThrowsNamingKnownKeys()
    {
        var error = Assert.Throws<KeyNotFoundException>(
            () => ArchiveFixtureBuilder.BuildControl("valid-unknown", 42, CancellationToken.None));

        Assert.Contains("valid-empty", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_SignatureLikePayloadBytes_DoNotConfuseLayout()
    {
        var signatureBytes = Encoding.ASCII.GetBytes("PK\x03\x04PK\x01\x02PK\x05\x06PK\x07\x08");
        var recipe = new ArchiveTestRecipe(
        [
            new ArchiveTestRecipeEntry("sig.bin", 64, "stored", IsDirectory: false, PayloadPrefix: signatureBytes),
        ]);
        var definition = new ArchiveTestCaseDefinition("valid-signature-like", 1, 1, "valid", ["smoke"], recipe);

        var artifact = ArchiveFixtureBuilder.Build(definition, 42, CancellationToken.None);

        var entry = artifact.Layout.Entries[0];
        Assert.Equal((ushort)0, entry.Method);
        Assert.Equal(entry.LocalHeaderOffset + 30 + entry.LocalNameLength + entry.LocalExtraLength, entry.DataOffset);

        // The embedded signatures live in payload bytes; the reader must still locate the
        // real central directory and read the exact content back.
        var content = ReadEntryContent(artifact.ArchiveBytes, "sig.bin");
        Assert.True(content.AsSpan().StartsWith(signatureBytes), "payload must start with the embedded signatures");
        Assert.Equal(artifact.Entries[0].Content, content);
        Assert.Equal(artifact.Layout.TotalBytes, artifact.Layout.EocdOffset + 22);
    }

    [Fact]
    public void Build_EntryCountOverBudget_ThrowsDescriptively()
    {
        var entries = Enumerable.Range(0, ArchiveTestCaseSemantics.MaxEntries + 1)
            .Select(i => ArchiveTestRecipeEntry.File($"f{i:D3}.txt", 1, "stored"))
            .ToList();
        var definition = new ArchiveTestCaseDefinition("over-count", 1, 1, "valid", ["smoke"], new ArchiveTestRecipe(entries));

        var error = Assert.Throws<InvalidDataException>(
            () => ArchiveFixtureBuilder.Build(definition, 42, CancellationToken.None));

        Assert.Contains("entry budget", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_ExactEntryBudget_Succeeds()
    {
        var entries = Enumerable.Range(0, ArchiveTestCaseSemantics.MaxEntries)
            .Select(i => ArchiveTestRecipeEntry.File($"f{i:D3}.txt", 1, "stored"))
            .ToList();
        var definition = new ArchiveTestCaseDefinition("exact-count", 1, 1, "valid", ["smoke"], new ArchiveTestRecipe(entries));

        var artifact = ArchiveFixtureBuilder.Build(definition, 42, CancellationToken.None);

        Assert.Equal(ArchiveTestCaseSemantics.MaxEntries, artifact.Layout.EntryCount);
    }

    [Fact]
    public void Build_ExpandedBudgetOverLimit_ThrowsDescriptively()
    {
        var entry = ArchiveTestRecipeEntry.File("big.bin", (int)ArchiveTestCaseSemantics.MaxExpandedBytesBudget + 1, "stored");
        var definition = new ArchiveTestCaseDefinition("over-expanded", 1, 1, "valid", ["smoke"], new ArchiveTestRecipe([entry]));

        var error = Assert.Throws<InvalidDataException>(
            () => ArchiveFixtureBuilder.Build(definition, 42, CancellationToken.None));

        Assert.Contains("expanded budget", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_PhysicalBudgetOverLimit_ThrowsDescriptively()
    {
        var entry = ArchiveTestRecipeEntry.File("big.bin", (int)ArchiveTestCaseSemantics.MaxArchivePhysicalBytes + 1, "stored");
        var definition = new ArchiveTestCaseDefinition("over-physical", 1, 1, "valid", ["smoke"], new ArchiveTestRecipe([entry]));

        var error = Assert.Throws<InvalidDataException>(
            () => ArchiveFixtureBuilder.Build(definition, 42, CancellationToken.None));

        Assert.Contains("budget", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_ZeroLengthDeflateFile_WritesEmptyEntryWithCrcZero()
    {
        var definition = new ArchiveTestCaseDefinition(
            "zero-file", 1, 1, "valid", ["smoke"],
            new ArchiveTestRecipe([ArchiveTestRecipeEntry.File("empty.txt", 0, "deflate")]));
        var artifact = ArchiveFixtureBuilder.Build(definition, 42, CancellationToken.None);

        var entry = Assert.Single(artifact.Layout.Entries);
        // The standard writer downgrades a zero-length deflate request to the stored
        // method; assert the emitted method, never the requested level.
        Assert.Equal((ushort)0, entry.Method);
        Assert.Equal(0u, entry.Crc32);
        Assert.Equal(0u, entry.UncompressedSize);
        Assert.Empty(ReadEntryContent(artifact.ArchiveBytes, "empty.txt"));
    }

    [Fact]
    public void Build_DepthOverLimit_ThrowsDescriptively()
    {
        var definition = new ArchiveTestCaseDefinition(
            "deep", 1, 1, "valid", ["smoke"],
            new ArchiveTestRecipe([ArchiveTestRecipeEntry.File("a/b/c.txt", 1, "stored")]));

        var error = Assert.Throws<InvalidDataException>(
            () => ArchiveFixtureBuilder.Build(definition, 42, CancellationToken.None));

        Assert.Contains("depth-2", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_PreCancelledToken_ThrowsOperationCanceled()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => ArchiveFixtureBuilder.BuildControl("valid-stored", 42, cancelled.Token));
    }

    [Fact]
    public void Build_AllSuiteSelection_DoesNotChangeCaseBytes()
    {
        // Suite selection is not an argument to Build; every suite that contains a case
        // must yield the same artifact for the same seed.
        var listed = ArchiveTestCatalog.ListSuite(ArchiveTestCatalog.AllSuites);
        var stored = Assert.Single(listed, c => c.CaseKey == "valid-stored");

        Assert.Equal(
            ArchiveFixtureBuilder.BuildControl("valid-stored", 7, CancellationToken.None).ArchiveBytes,
            ArchiveFixtureBuilder.Build(stored, 7, CancellationToken.None).ArchiveBytes);
    }
}
